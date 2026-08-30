namespace Maque.Majsoul;

public sealed record MajsoulClientOptions
{
    // Current Unity resource version observed on the official CN WebGL title screen.
    // Mahjong Soul does not expose it through the legacy /1/version.json endpoint.
    public string ResourceVersion { get; init; } = "0.16.273";
    public string PackageVersion { get; init; } = "4.0.46";
    public string UserAgent { get; init; } =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
        "(KHTML, like Gecko) Chrome/149.0.0.0 Safari/537.36";
}
