namespace Maque.Core;

public sealed class GameRecordRepository
{
    private readonly string _root;

    public GameRecordRepository(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        _root = Path.GetFullPath(root);
    }

    public string Root => _root;

    public async Task<StoredGameRecord> StoreAsync(
        GameRecordDocument document,
        ReadOnlyMemory<byte> rawRecord,
        CancellationToken cancellationToken = default)
    {
        ValidateGameId(document.GameId);

        var recordDirectory = Path.Combine(_root, "records");
        var rawDirectory = Path.Combine(_root, "raw", document.Source.Platform);
        Directory.CreateDirectory(recordDirectory);
        Directory.CreateDirectory(rawDirectory);

        var jsonPath = Path.Combine(recordDirectory, $"{document.GameId}.json");
        var rawPath = Path.Combine(rawDirectory, $"{document.GameId}.bin");
        var jsonTemporaryPath = jsonPath + ".tmp";
        var rawTemporaryPath = rawPath + ".tmp";

        try
        {
            await File.WriteAllBytesAsync(rawTemporaryPath, rawRecord.ToArray(), cancellationToken).ConfigureAwait(false);
            await File.WriteAllBytesAsync(jsonTemporaryPath, GameRecordJson.Serialize(document), cancellationToken).ConfigureAwait(false);
            File.Move(rawTemporaryPath, rawPath, true);
            File.Move(jsonTemporaryPath, jsonPath, true);
        }
        finally
        {
            TryDeleteTemporaryFile(rawTemporaryPath);
            TryDeleteTemporaryFile(jsonTemporaryPath);
        }

        return new StoredGameRecord(jsonPath, rawPath);
    }

    private static void ValidateGameId(string gameId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameId);
        if (gameId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || gameId.Contains(Path.DirectorySeparatorChar)
            || gameId.Contains(Path.AltDirectorySeparatorChar))
        {
            throw new ArgumentException("牌谱编号不能用作安全文件名。", nameof(gameId));
        }
    }

    private static void TryDeleteTemporaryFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // A failed cleanup must not hide the original storage error.
        }
    }
}

public sealed record StoredGameRecord(string JsonPath, string RawPath);
