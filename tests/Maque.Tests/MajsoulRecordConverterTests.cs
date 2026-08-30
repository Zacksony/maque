using Google.Protobuf;
using Maque.Majsoul;
using Maque.Majsoul.Protocol;

namespace Maque.Tests;

public sealed class MajsoulRecordConverterTests
{
    [Fact]
    public void Convert_ProducesPlatformIndependentRoundsAndEvents()
    {
        var header = CreateHeader();
        var details = new GameDetailRecords { Version = 210715 };
        details.Records.Add(Wrap(".lq.RecordMJStart", ByteString.Empty));
        details.Records.Add(Wrap(".lq.RecordNewRound", new RecordNewRound
        {
            Chang = 0,
            Ju = 0,
            Ben = 0,
            Liqibang = 0,
            Dora = "3p",
            LeftTileCount = 69,
            Scores = { 25000, 25000, 25000, 25000 },
            Tiles0 = { "1m", "2m", "3m" },
            Tiles1 = { "4m", "5m", "6m" },
            Tiles2 = { "1p", "2p", "3p" },
            Tiles3 = { "1s", "2s", "3s" }
        }.ToByteString()));
        details.Records.Add(Wrap(".lq.RecordDealTile", new RecordDealTile
        {
            Seat = 0,
            Tile = "4m",
            LeftTileCount = 68
        }.ToByteString()));
        details.Records.Add(Wrap(".lq.RecordDiscardTile", new RecordDiscardTile
        {
            Seat = 0,
            Tile = "1m",
            Moqie = false,
            IsLiqi = true
        }.ToByteString()));
        details.Records.Add(Wrap(".lq.FutureRecord", ByteString.CopyFrom([1, 2, 3])));
        details.Records.Add(Wrap(".lq.RecordHule", new RecordHule
        {
            Scores = { 33000, 23000, 22000, 22000 },
            DeltaScores = { 8000, -2000, -3000, -3000 },
            Gameend = new GameEnd { Scores = { 33000, 23000, 22000, 22000 } },
            Hules =
            {
                new HuleInfo
                {
                    Seat = 0,
                    Zimo = true,
                    Qinjia = true,
                    HuTile = "4m",
                    Count = 3,
                    Fu = 30,
                    PointSum = 12000,
                    Hand = { "1m", "2m", "3m", "4m" },
                    Fans = { new FanInfo { Id = 1, Name = "立直", Val = 1 } }
                }
            }
        }.ToByteString()));

        var outer = new Wrapper
        {
            Name = ".lq.GameDetailRecords",
            Data = details.ToByteString()
        }.ToByteArray();
        var link = MajsoulRecordLink.Parse(
            "https://game.maj-soul.com/1/?paipu=260826-826cd976-c7b5-4ef5-8c80-3fbf91f95a0b_a21920067");
        var fetched = new MajsoulFetchResult(
            link,
            "v0.11.252.w",
            "WebGL_2022-0.16.273",
            "wss://route-5.maj-soul.com/gateway",
            header,
            outer);

        var result = new MajsoulRecordConverter().Convert(
            fetched,
            "Zackson",
            DateTimeOffset.Parse("2026-08-31T00:00:00Z"));

        Assert.Equal("maque-universal-paipu", result.Format);
        Assert.Equal(4, result.Rules.PlayerCount);
        Assert.Equal("south", result.Rules.RoundType);
        Assert.True(result.Players.Single(player => player.Nickname == "Zackson").IsFocusPlayer);
        var round = Assert.Single(result.Rounds);
        Assert.Equal("east", round.Wind);
        Assert.Equal("3p", Assert.Single(round.DoraIndicators));
        Assert.Collection(
            round.Events,
            item => Assert.Equal("draw", item.Type),
            item => Assert.True(item.IsRiichi),
            item =>
            {
                Assert.Equal("unknown", item.Type);
                Assert.Equal("AQID", item.RawPayloadBase64);
            },
            item =>
            {
                Assert.Equal("win", item.Type);
                Assert.Equal(12000, Assert.Single(item.Wins!).TotalPoints);
            });
    }

    private static RecordGame CreateHeader()
    {
        var header = new RecordGame
        {
            Uuid = "260826-826cd976-c7b5-4ef5-8c80-3fbf91f95a0b",
            StartTime = 1787733960,
            EndTime = 1787737560,
            StandardRule = 1,
            Config = new GameConfig
            {
                Category = 2,
                Mode = new GameMode
                {
                    Mode = 2,
                    DetailRule = new GameDetailRule { InitPoint = 25000, CanJifei = true }
                },
                Meta = new GameMetaData { ModeId = 12 }
            },
            Result = new GameEndResult()
        };

        for (uint seat = 0; seat < 4; seat++)
        {
            header.Accounts.Add(new AccountInfo
            {
                AccountId = 1000 + seat,
                Seat = seat,
                Nickname = seat == 0 ? "Zackson" : $"Player{seat}",
                Level = new AccountLevel { Id = 10101, Score = 237 }
            });
            header.Result.Players.Add(new PlayerResult
            {
                Seat = seat,
                TotalPoint = seat == 0 ? 33000 : 22000,
                PartPoint1 = seat == 0 ? 33000 : 22000,
                GradingScore = seat == 0 ? 50 : -10
            });
        }

        return header;
    }

    private static ByteString Wrap(string name, ByteString data) =>
        new Wrapper { Name = name, Data = data }.ToByteString();
}
