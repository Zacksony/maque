using System.Text.RegularExpressions;

namespace Maque.Majsoul;

public sealed partial record MajsoulRecordLink(
    string Original,
    string PublicId,
    string RecordId,
    string? ViewpointToken)
{
    public static MajsoulRecordLink Parse(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        var trimmed = value.Trim();
        var publicId = TryGetPaipuQueryValue(trimmed) ?? trimmed;
        publicId = Uri.UnescapeDataString(publicId).Trim();

        var separator = publicId.IndexOf('_');
        var recordId = separator < 0 ? publicId : publicId[..separator];
        var viewpointToken = separator < 0 ? null : publicId[(separator + 1)..];

        if (!RecordIdPattern().IsMatch(recordId))
        {
            throw new FormatException($"无法从输入中识别雀魂牌谱编号：{value}");
        }

        return new MajsoulRecordLink(trimmed, publicId, recordId, viewpointToken);
    }

    private static string? TryGetPaipuQueryValue(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            return null;
        }

        foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            if (parts.Length == 2 && string.Equals(Uri.UnescapeDataString(parts[0]), "paipu", StringComparison.OrdinalIgnoreCase))
            {
                return Uri.UnescapeDataString(parts[1]);
            }
        }

        return null;
    }

    [GeneratedRegex(@"^\d{6}-[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$", RegexOptions.CultureInvariant)]
    private static partial Regex RecordIdPattern();
}
