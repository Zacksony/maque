using Maque.Core;

namespace Maque.Tests;

public sealed class GameRecordRepositoryTests
{
    [Fact]
    public async Task StoreAsync_WritesJsonAndRawRecord()
    {
        var root = Path.Combine(Path.GetTempPath(), $"maque-tests-{Guid.NewGuid():N}");
        try
        {
            var document = new GameRecordDocument
            {
                GameId = "260826-826cd976-c7b5-4ef5-8c80-3fbf91f95a0b",
                Source = new SourceRecord
                {
                    Platform = "majsoul",
                    RecordId = "260826-826cd976-c7b5-4ef5-8c80-3fbf91f95a0b",
                    PublicId = "260826-826cd976-c7b5-4ef5-8c80-3fbf91f95a0b_a1",
                    OriginalLink = "https://game.maj-soul.com/1/?paipu=test",
                    FetchedAt = DateTimeOffset.UnixEpoch,
                    ProtocolVersion = "test",
                    ClientVersion = "test",
                    RawPath = "raw/majsoul/test.bin",
                    RawSha256 = "abc"
                },
                Rules = new GameRules
                {
                    PlayerCount = 4,
                    RoundType = "south",
                    SourceCategory = 2,
                    SourceMode = 2,
                    SourceModeId = 12,
                    SourceStandardRule = 1
                },
                Players = [],
                Rounds = []
            };

            var stored = await new GameRecordRepository(root).StoreAsync(document, new byte[] { 1, 2, 3 });

            Assert.True(File.Exists(stored.JsonPath));
            Assert.Equal(new byte[] { 1, 2, 3 }, await File.ReadAllBytesAsync(stored.RawPath));
            var loaded = GameRecordJson.Deserialize(await File.ReadAllBytesAsync(stored.JsonPath));
            Assert.Equal(document.GameId, loaded?.GameId);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
