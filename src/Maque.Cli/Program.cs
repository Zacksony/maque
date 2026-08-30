using System.Text;
using Maque.Core;
using Maque.Majsoul;

Console.OutputEncoding = Encoding.UTF8;

try
{
    return await RunAsync(args);
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("操作已取消。");
    return 130;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"失败：{exception.Message}");
    return 1;
}

static async Task<int> RunAsync(string[] args)
{
    if (args.Length == 0 || args[0] is "--help" or "-h" or "help")
    {
        PrintUsage();
        return args.Length == 0 ? 2 : 0;
    }

    if (!string.Equals(args[0], "import", StringComparison.OrdinalIgnoreCase))
    {
        throw new ArgumentException($"未知命令：{args[0]}");
    }

    var parsed = ImportArguments.Parse(args[1..]);
    var resourceVersion = parsed.ResourceVersion
        ?? Environment.GetEnvironmentVariable("MAJSOUL_RESOURCE_VERSION")
        ?? "0.16.273";
    var packageVersion = parsed.PackageVersion
        ?? Environment.GetEnvironmentVariable("MAJSOUL_PACKAGE_VERSION")
        ?? "4.0.46";

    MajsoulAuthentication? authentication = null;
    var account = parsed.Account ?? Environment.GetEnvironmentVariable("MAJSOUL_ACCOUNT");
    if (parsed.PromptLogin && string.IsNullOrWhiteSpace(account))
    {
        if (Console.IsInputRedirected)
        {
            throw new InvalidOperationException("无法从重定向输入安全读取账号。请使用 --account。 ");
        }

        Console.Error.Write("雀魂登录账号：");
        account = Console.ReadLine();
    }
    if (!string.IsNullOrWhiteSpace(account))
    {
        var password = Environment.GetEnvironmentVariable("MAJSOUL_PASSWORD");
        if (string.IsNullOrEmpty(password))
        {
            password = ReadPassword($"雀魂账号 {account} 的密码：");
        }

        authentication = new MajsoulAuthentication(account, password);
    }

    using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(90) };
    var client = new MajsoulClient(httpClient, new MajsoulClientOptions
    {
        ResourceVersion = resourceVersion,
        PackageVersion = packageVersion
    });
    var converter = new MajsoulRecordConverter();
    var repository = new GameRecordRepository(parsed.Repository);
    var failures = 0;

    foreach (var link in parsed.Links)
    {
        try
        {
            Console.WriteLine($"读取：{link}");
            var fetched = await client.FetchAsync(link, authentication);
            var document = converter.Convert(fetched, parsed.Player, DateTimeOffset.UtcNow);
            var stored = await repository.StoreAsync(document, fetched.RecordData);

            if (!string.IsNullOrWhiteSpace(parsed.Player)
                && !document.Players.Any(player => player.IsFocusPlayer))
            {
                Console.Error.WriteLine($"警告：牌谱 {document.GameId} 中未找到玩家“{parsed.Player}”。");
            }

            Console.WriteLine(
                $"已保存：{document.GameId}，{document.Players.Count}名玩家，" +
                $"{document.Rounds.Count}局，{Path.GetRelativePath(Environment.CurrentDirectory, stored.JsonPath)}");
        }
        catch (Exception exception)
        {
            failures++;
            Console.Error.WriteLine($"导入失败：{link}\n  {exception.Message}");
        }
    }

    return failures == 0 ? 0 : 1;
}

static string ReadPassword(string prompt)
{
    if (Console.IsInputRedirected)
    {
        throw new InvalidOperationException(
            "无法在重定向输入中安全读取密码。请设置 MAJSOUL_PASSWORD 环境变量。 ");
    }

    Console.Error.Write(prompt);
    var buffer = new StringBuilder();
    while (true)
    {
        var key = Console.ReadKey(intercept: true);
        if (key.Key == ConsoleKey.Enter)
        {
            Console.Error.WriteLine();
            break;
        }

        if (key.Key == ConsoleKey.Backspace)
        {
            if (buffer.Length > 0)
            {
                buffer.Length--;
            }

            continue;
        }

        if (!char.IsControl(key.KeyChar))
        {
            buffer.Append(key.KeyChar);
        }
    }

    if (buffer.Length == 0)
    {
        throw new InvalidOperationException("密码不能为空。 ");
    }

    return buffer.ToString();
}

static void PrintUsage()
{
    Console.WriteLine(
        """
        Maque 雀魂牌谱导入器 (.NET 10)

        用法：
          dotnet run --project src/Maque.Cli -- import [选项] <牌谱链接> [更多链接...]

        选项：
          --player <昵称>             标记需要重点复盘的玩家
          --login                     在本机交互输入登录账号和密码
          --account <登录账号>        雀魂国服登录账号；密码将在本机隐藏输入
          --repository <目录>         数据仓库目录，默认 data
          --resource-version <版本>   Unity 资源版本，默认 0.16.273
          --package-version <版本>    WebGL 包版本，默认 4.0.46

        自动化环境变量：
          MAJSOUL_ACCOUNT, MAJSOUL_PASSWORD,
          MAJSOUL_RESOURCE_VERSION, MAJSOUL_PACKAGE_VERSION

        程序不会把账号或密码写入牌谱、配置或日志。
        """);
}

internal sealed record ImportArguments(
    string? Player,
    string? Account,
    bool PromptLogin,
    string Repository,
    string? ResourceVersion,
    string? PackageVersion,
    IReadOnlyList<string> Links)
{
    public static ImportArguments Parse(string[] args)
    {
        string? player = null;
        string? account = null;
        var promptLogin = false;
        string repository = "data";
        string? resourceVersion = null;
        string? packageVersion = null;
        var links = new List<string>();

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            switch (argument)
            {
                case "--player":
                    player = ReadValue(args, ref index, argument);
                    break;
                case "--account":
                    account = ReadValue(args, ref index, argument);
                    break;
                case "--login":
                    promptLogin = true;
                    break;
                case "--repository":
                    repository = ReadValue(args, ref index, argument);
                    break;
                case "--resource-version":
                    resourceVersion = ReadValue(args, ref index, argument);
                    break;
                case "--package-version":
                    packageVersion = ReadValue(args, ref index, argument);
                    break;
                default:
                    if (argument.StartsWith("-", StringComparison.Ordinal))
                    {
                        throw new ArgumentException($"未知选项：{argument}");
                    }

                    links.Add(argument);
                    break;
            }
        }

        if (links.Count == 0)
        {
            throw new ArgumentException("至少需要一个雀魂牌谱链接。 ");
        }

        return new ImportArguments(player, account, promptLogin, repository, resourceVersion, packageVersion, links);
    }

    private static string ReadValue(string[] args, ref int index, string option)
    {
        if (++index >= args.Length || string.IsNullOrWhiteSpace(args[index]))
        {
            throw new ArgumentException($"选项 {option} 缺少值。 ");
        }

        return args[index];
    }
}
