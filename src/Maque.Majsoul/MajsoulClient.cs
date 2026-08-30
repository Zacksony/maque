using System.Buffers.Binary;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Google.Protobuf;
using Maque.Majsoul.Protocol;

namespace Maque.Majsoul;

public sealed class MajsoulClient
{
    private const string GameOrigin = "https://game.maj-soul.com";
    private const int MaximumMessageBytes = 64 * 1024 * 1024;
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(12);
    private static readonly TimeSpan ResponseTimeout = TimeSpan.FromSeconds(25);
    private readonly HttpClient _httpClient;
    private readonly MajsoulClientOptions _options;

    public MajsoulClient(HttpClient httpClient, MajsoulClientOptions? options = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _options = options ?? new MajsoulClientOptions();
    }

    public Task<MajsoulFetchResult> FetchAsync(string link, CancellationToken cancellationToken = default) =>
        FetchAsync(link, null, cancellationToken);

    public async Task<MajsoulFetchResult> FetchAsync(
        string link,
        MajsoulAuthentication? authentication,
        CancellationToken cancellationToken = default)
    {
        var parsedLink = MajsoulRecordLink.Parse(link);
        var discovery = await DiscoverAsync(cancellationToken).ConfigureAwait(false);
        var failures = new List<Exception>();

        foreach (var gateway in discovery.Gateways)
        {
            try
            {
                return await FetchFromGatewayAsync(parsedLink, discovery, gateway, authentication, cancellationToken).ConfigureAwait(false);
            }
            catch (MajsoulProtocolException)
            {
                throw;
            }
            catch (Exception exception) when (exception is WebSocketException or HttpRequestException or IOException or OperationCanceledException)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }

                failures.Add(new IOException($"网关 {gateway.Uri} 请求失败：{exception.Message}", exception));
            }
        }

        throw new AggregateException("所有雀魂牌谱网关均请求失败。", failures);
    }

    private async Task<DiscoveryResult> DiscoverAsync(CancellationToken cancellationToken)
    {
        using var versionResponse = await _httpClient.GetAsync($"{GameOrigin}/1/version.json", cancellationToken).ConfigureAwait(false);
        versionResponse.EnsureSuccessStatusCode();
        await using var versionStream = await versionResponse.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var versionJson = await JsonDocument.ParseAsync(versionStream, cancellationToken: cancellationToken).ConfigureAwait(false);

        var version = versionJson.RootElement.GetProperty("version").GetString()
            ?? throw new MajsoulProtocolException("雀魂版本响应缺少 version。 ");
        var clientVersion = $"WebGL_2022-{_options.ResourceVersion}";

        var configUri = $"{GameOrigin}/1/v{version}/config.json";
        using var configResponse = await _httpClient.GetAsync(configUri, cancellationToken).ConfigureAwait(false);
        configResponse.EnsureSuccessStatusCode();
        await using var configStream = await configResponse.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var configJson = await JsonDocument.ParseAsync(configStream, cancellationToken: cancellationToken).ConfigureAwait(false);

        var gateways = await DiscoverGatewaysAsync(configJson.RootElement, version, cancellationToken).ConfigureAwait(false);
        if (gateways.Count == 0)
        {
            throw new MajsoulProtocolException("雀魂配置中未发现可用网关。 ");
        }

        return new DiscoveryResult(version, clientVersion, gateways);
    }

    private async Task<IReadOnlyList<GatewayEndpoint>> DiscoverGatewaysAsync(
        JsonElement root,
        string version,
        CancellationToken cancellationToken)
    {
        var routeServices = new List<Uri>();
        if (!root.TryGetProperty("ip", out var regions) || regions.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        foreach (var region in regions.EnumerateArray())
        {
            if (!region.TryGetProperty("gateways", out var gateways) || gateways.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var gateway in gateways.EnumerateArray())
            {
                if (!gateway.TryGetProperty("url", out var urlElement))
                {
                    continue;
                }

                var value = urlElement.GetString();
                if (!Uri.TryCreate(value, UriKind.Absolute, out var httpUri))
                {
                    continue;
                }

                routeServices.Add(httpUri);
            }
        }

        var failures = new List<Exception>();
        foreach (var routeService in routeServices)
        {
            try
            {
                var routeUri = new Uri(routeService, $"/api/clientgate/routes?platform=Web&version={Uri.EscapeDataString(version)}");
                using var routeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                routeCts.CancelAfter(TimeSpan.FromSeconds(10));
                using var response = await _httpClient.GetAsync(routeUri, routeCts.Token).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                await using var stream = await response.Content.ReadAsStreamAsync(routeCts.Token).ConfigureAwait(false);
                using var json = await JsonDocument.ParseAsync(stream, cancellationToken: routeCts.Token).ConfigureAwait(false);
                var routes = ParseRoutes(json.RootElement);
                if (routes.Count > 0)
                {
                    return routes;
                }
            }
            catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException or JsonException)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }

                failures.Add(exception);
            }
        }

        throw new AggregateException("无法从雀魂线路服务取得 WebSocket 网关。", failures);
    }

    private static IReadOnlyList<GatewayEndpoint> ParseRoutes(JsonElement root)
    {
        var result = new List<GatewayEndpoint>();
        if (!root.TryGetProperty("data", out var data)
            || !data.TryGetProperty("routes", out var routes)
            || routes.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        foreach (var route in routes.EnumerateArray())
        {
            var id = route.TryGetProperty("id", out var idElement) ? idElement.GetString() : null;
            var domain = route.TryGetProperty("domain", out var domainElement) ? domainElement.GetString() : null;
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(domain))
            {
                continue;
            }

            result.Add(new GatewayEndpoint(new Uri($"wss://{domain}/gateway"), id));
        }

        return result;
    }

    private async Task<MajsoulFetchResult> FetchFromGatewayAsync(
        MajsoulRecordLink link,
        DiscoveryResult discovery,
        GatewayEndpoint gateway,
        MajsoulAuthentication? authentication,
        CancellationToken cancellationToken)
    {
        using var socket = new ClientWebSocket();
        socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(15);
        socket.Options.SetRequestHeader("Origin", GameOrigin);

        using (var connectCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            connectCts.CancelAfter(ConnectTimeout);
            await socket.ConnectAsync(gateway.Uri, connectCts.Token).ConfigureAwait(false);
        }

        var routeRequest = new ReqRequestConnection
        {
            Type = 1,
            RouteId = gateway.RouteId,
            Timestamp = (ulong)DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            Platform = "Web"
        };
        var routeResponseBytes = await SendRequestAsync(
            socket,
            1,
            ".lq.Route.requestConnection",
            routeRequest.ToByteArray(),
            cancellationToken).ConfigureAwait(false);
        var routeResponse = ResRequestConnection.Parser.ParseFrom(routeResponseBytes);
        if (routeResponse.Error?.Code > 0)
        {
            throw new MajsoulProtocolException($"雀魂路由握手失败，错误码 {routeResponse.Error.Code}。 ");
        }

        ushort requestId = 2;
        if (authentication is not null)
        {
            requestId = await LoginAsync(socket, requestId, discovery.ClientVersion, authentication, cancellationToken)
                .ConfigureAwait(false);
        }

        var request = new ReqGameRecord
        {
            GameUuid = link.RecordId,
            ClientVersionString = discovery.ClientVersion
        };
        var responseBytes = await SendRequestAsync(
            socket,
            requestId,
            ".lq.Lobby.fetchGameRecord",
            request.ToByteArray(),
            cancellationToken).ConfigureAwait(false);
        var response = ResGameRecord.Parser.ParseFrom(responseBytes);
        if (response.Error?.Code > 0)
        {
            var hint = response.Error.Code == 1004 && authentication is null
                ? " 当前雀魂服务器要求先登录；请提供账号并在本机安全输入密码。"
                : string.Empty;
            throw new MajsoulProtocolException(
                $"雀魂拒绝牌谱请求，错误码 {response.Error.Code}，参数 {response.Error.JsonParam}。{hint}");
        }

        var recordData = response.Data.ToByteArray();
        if (recordData.Length == 0 && !string.IsNullOrWhiteSpace(response.DataUrl))
        {
            recordData = await _httpClient.GetByteArrayAsync(response.DataUrl, cancellationToken).ConfigureAwait(false);
        }

        if (recordData.Length == 0)
        {
            throw new MajsoulProtocolException("雀魂返回了空牌谱。 ");
        }

        return new MajsoulFetchResult(
            link,
            discovery.ProtocolVersion,
            discovery.ClientVersion,
            gateway.Uri.ToString(),
            response.Head ?? new RecordGame { Uuid = link.RecordId },
            recordData);
    }

    private async Task<ushort> LoginAsync(
        ClientWebSocket socket,
        ushort requestId,
        string clientVersion,
        MajsoulAuthentication authentication,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(authentication.Account);
        ArgumentException.ThrowIfNullOrWhiteSpace(authentication.Password);

        var key = Encoding.UTF8.GetBytes("lailai");
        var passwordBytes = Encoding.UTF8.GetBytes(authentication.Password);
        var passwordHash = System.Convert.ToHexString(HMACSHA256.HashData(key, passwordBytes)).ToLowerInvariant();
        CryptographicOperations.ZeroMemory(passwordBytes);

        var request = new ReqLogin
        {
            Account = authentication.Account,
            Password = passwordHash,
            Reconnect = false,
            Device = new ClientDeviceInfo
            {
                Platform = "pc",
                Hardware = "pc",
                Os = "windows",
                OsVersion = "win10",
                IsBrowser = true,
                Software = "Chrome",
                SalePlatform = "web",
                ScreenWidth = 2560,
                ScreenHeight = 1440,
                UserAgent = _options.UserAgent,
                ScreenType = 2
            },
            RandomKey = Guid.NewGuid().ToString(),
            ClientVersion = new ClientVersionInfo
            {
                Resource = _options.ResourceVersion,
                Package = _options.PackageVersion
            },
            GenAccessToken = true,
            ClientVersionString = clientVersion,
            Tag = "cn"
        };
        request.CurrencyPlatforms.Add([1, 2, 5, 6, 8, 10, 11]);

        var loginBytes = await SendRequestAsync(
            socket,
            requestId++,
            ".lq.Lobby.login",
            request.ToByteArray(),
            cancellationToken).ConfigureAwait(false);
        var login = ResLogin.Parser.ParseFrom(loginBytes);
        if (login.Error?.Code > 0 || string.IsNullOrWhiteSpace(login.AccessToken))
        {
            throw new MajsoulProtocolException(
                $"雀魂登录失败，错误码 {login.Error?.Code ?? 0}，参数 {login.Error?.JsonParam}。 ");
        }

        var loginSuccessBytes = await SendRequestAsync(
            socket,
            requestId++,
            ".lq.Lobby.loginSuccess",
            [],
            cancellationToken).ConfigureAwait(false);
        var loginSuccess = ResCommon.Parser.ParseFrom(loginSuccessBytes);
        if (loginSuccess.Error?.Code > 0)
        {
            throw new MajsoulProtocolException($"雀魂确认登录失败，错误码 {loginSuccess.Error.Code}。 ");
        }

        var beat = new ReqLoginBeat { Contract = "DF2vkXCnfeXp4WoGSBGNcJBufZiMN3UP" };
        var beatBytes = await SendRequestAsync(
            socket,
            requestId++,
            ".lq.Lobby.loginBeat",
            beat.ToByteArray(),
            cancellationToken).ConfigureAwait(false);
        var beatResponse = ResCommon.Parser.ParseFrom(beatBytes);
        if (beatResponse.Error?.Code > 0)
        {
            throw new MajsoulProtocolException($"雀魂登录心跳失败，错误码 {beatResponse.Error.Code}。 ");
        }

        return requestId;
    }

    private static async Task<ByteString> SendRequestAsync(
        ClientWebSocket socket,
        ushort requestId,
        string method,
        byte[] payload,
        CancellationToken cancellationToken)
    {
        var wrapper = new Wrapper
        {
            Name = method,
            Data = ByteString.CopyFrom(payload)
        };
        var wrappedBytes = wrapper.ToByteArray();
        var frame = new byte[wrappedBytes.Length + 3];
        frame[0] = 2;
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(1, 2), requestId);
        wrappedBytes.CopyTo(frame, 3);
        await socket.SendAsync(frame, WebSocketMessageType.Binary, true, cancellationToken).ConfigureAwait(false);

        using var responseCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        responseCts.CancelAfter(ResponseTimeout);
        while (socket.State == WebSocketState.Open)
        {
            var responseFrame = await ReceiveMessageAsync(socket, responseCts.Token).ConfigureAwait(false);
            if (responseFrame.Length < 3 || responseFrame[0] != 3)
            {
                continue;
            }

            var responseId = BinaryPrimitives.ReadUInt16LittleEndian(responseFrame.AsSpan(1, 2));
            if (responseId != requestId)
            {
                continue;
            }

            return Wrapper.Parser.ParseFrom(responseFrame.AsSpan(3).ToArray()).Data;
        }

        throw new WebSocketException("雀魂网关在返回请求结果前关闭了连接。 ");
    }

    private static async Task<byte[]> ReceiveMessageAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = new byte[16 * 1024];
        using var stream = new MemoryStream();
        while (true)
        {
            var result = await socket.ReceiveAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                throw new WebSocketException($"服务器关闭连接：{result.CloseStatus} {result.CloseStatusDescription}");
            }

            if (result.MessageType != WebSocketMessageType.Binary)
            {
                if (result.EndOfMessage)
                {
                    return [];
                }

                continue;
            }

            stream.Write(buffer, 0, result.Count);
            if (stream.Length > MaximumMessageBytes)
            {
                throw new MajsoulProtocolException($"雀魂响应超过安全上限 {MaximumMessageBytes} 字节。 ");
            }

            if (result.EndOfMessage)
            {
                return stream.ToArray();
            }
        }
    }

    private sealed record DiscoveryResult(
        string ProtocolVersion,
        string ClientVersion,
        IReadOnlyList<GatewayEndpoint> Gateways);

    private sealed record GatewayEndpoint(Uri Uri, string RouteId);
}
