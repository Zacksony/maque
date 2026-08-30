using System.Text.Json;
using System.Text.Json.Serialization;

namespace Maque.Core;

public static class GameRecordJson
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static byte[] Serialize(GameRecordDocument document) =>
        JsonSerializer.SerializeToUtf8Bytes(document, Options);

    public static GameRecordDocument? Deserialize(ReadOnlySpan<byte> json) =>
        JsonSerializer.Deserialize<GameRecordDocument>(json, Options);
}
