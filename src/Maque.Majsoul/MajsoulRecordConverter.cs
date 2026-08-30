using System.Security.Cryptography;
using Google.Protobuf;
using Maque.Core;
using Maque.Majsoul.Protocol;

namespace Maque.Majsoul;

public sealed class MajsoulRecordConverter
{
    public GameRecordDocument Convert(
        MajsoulFetchResult fetched,
        string? focusPlayerNickname,
        DateTimeOffset fetchedAt)
    {
        ArgumentNullException.ThrowIfNull(fetched);

        var gameDetails = ParseGameDetails(fetched.RecordData);
        var rounds = new List<RoundRecord>();
        var preRoundEvents = new List<GameEvent>();
        RoundBuilder? currentRound = null;

        foreach (var sourceAction in ReadSourceActions(gameDetails))
        {
            if (sourceAction.Wrapper.Name == ".lq.RecordNewRound")
            {
                if (currentRound is not null)
                {
                    rounds.Add(currentRound.Build());
                }

                var newRound = RecordNewRound.Parser.ParseFrom(sourceAction.Wrapper.Data);
                currentRound = new RoundBuilder(rounds.Count, newRound);
                continue;
            }

            var mapped = MapAction(sourceAction.Wrapper, currentRound?.NextSequence ?? preRoundEvents.Count);
            if (mapped.Count == 0)
            {
                continue;
            }

            if (currentRound is null)
            {
                preRoundEvents.AddRange(mapped);
            }
            else
            {
                currentRound.AddRange(mapped);
            }
        }

        if (currentRound is not null)
        {
            rounds.Add(currentRound.Build());
        }

        var header = fetched.Header;
        var players = MapPlayers(header, focusPlayerNickname);
        var playerCount = header.Result?.Players.Count > 0
            ? header.Result.Players.Count
            : players.Count;
        var rawSha256 = System.Convert.ToHexString(SHA256.HashData(fetched.RecordData)).ToLowerInvariant();
        var gameId = string.IsNullOrWhiteSpace(header.Uuid) ? fetched.Link.RecordId : header.Uuid;

        return new GameRecordDocument
        {
            GameId = gameId,
            Source = new SourceRecord
            {
                Platform = "majsoul",
                RecordId = fetched.Link.RecordId,
                PublicId = fetched.Link.PublicId,
                OriginalLink = fetched.Link.Original,
                FetchedAt = fetchedAt,
                ProtocolVersion = fetched.ProtocolVersion,
                ClientVersion = fetched.ClientVersion,
                RawPath = $"raw/majsoul/{gameId}.bin",
                RawSha256 = rawSha256,
                FocusPlayerNickname = focusPlayerNickname
            },
            StartedAt = FromUnixSeconds(header.StartTime),
            EndedAt = FromUnixSeconds(header.EndTime),
            Rules = new GameRules
            {
                PlayerCount = playerCount,
                RoundType = header.Config?.Mode?.Mode switch
                {
                    1 => "east",
                    2 => "south",
                    _ => "unknown"
                },
                SourceCategory = (int)(header.Config?.Category ?? 0),
                SourceMode = (int)(header.Config?.Mode?.Mode ?? 0),
                SourceModeId = (int)(header.Config?.Meta?.ModeId ?? 0),
                SourceStandardRule = (int)header.StandardRule,
                InitialPoints = header.Config?.Mode?.DetailRule?.InitPoint is > 0
                    ? (int)header.Config.Mode.DetailRule.InitPoint
                    : null,
                CanEndBelowZero = header.Config?.Mode?.DetailRule is null
                    ? null
                    : header.Config.Mode.DetailRule.CanJifei
            },
            Players = players,
            Rounds = rounds,
            UnmappedEvents = preRoundEvents
        };
    }

    private static GameDetailRecords ParseGameDetails(byte[] rawRecord)
    {
        try
        {
            var outer = Wrapper.Parser.ParseFrom(rawRecord);
            if (!string.IsNullOrWhiteSpace(outer.Name) && outer.Data.Length > 0)
            {
                return GameDetailRecords.Parser.ParseFrom(outer.Data);
            }
        }
        catch (InvalidProtocolBufferException)
        {
            // Some data_url responses contain GameDetailRecords without Wrapper.
        }

        try
        {
            return GameDetailRecords.Parser.ParseFrom(rawRecord);
        }
        catch (InvalidProtocolBufferException exception)
        {
            throw new MajsoulProtocolException("雀魂牌谱正文不是受支持的 GameDetailRecords 数据。", exception);
        }
    }

    private static IEnumerable<SourceAction> ReadSourceActions(GameDetailRecords details)
    {
        foreach (var record in details.Records)
        {
            Wrapper wrapper;
            try
            {
                wrapper = Wrapper.Parser.ParseFrom(record);
            }
            catch (InvalidProtocolBufferException)
            {
                wrapper = new Wrapper
                {
                    Name = ".lq.UnknownRecord",
                    Data = record
                };
            }

            yield return new SourceAction(wrapper);
        }

        foreach (var action in details.Actions)
        {
            if (action.Result.Length == 0)
            {
                continue;
            }

            Wrapper wrapper;
            try
            {
                wrapper = Wrapper.Parser.ParseFrom(action.Result);
            }
            catch (InvalidProtocolBufferException)
            {
                wrapper = new Wrapper
                {
                    Name = $".lq.UnknownGameAction{action.Type}",
                    Data = action.Result
                };
            }

            yield return new SourceAction(wrapper);
        }
    }

    private static IReadOnlyList<PlayerRecord> MapPlayers(RecordGame header, string? focusPlayerNickname)
    {
        var results = header.Result?.Players.ToDictionary(player => (int)player.Seat) ?? [];
        return header.Accounts
            .Concat(header.Robots)
            .GroupBy(account => account.Seat)
            .Select(group => group.First())
            .OrderBy(account => account.Seat)
            .Select(account =>
            {
                results.TryGetValue((int)account.Seat, out var result);
                return new PlayerRecord
                {
                    Seat = (int)account.Seat,
                    Nickname = account.Nickname,
                    SourceAccountId = account.AccountId,
                    LevelId = account.Level?.Id is > 0 ? (int)account.Level.Id : null,
                    LevelScore = account.Level is null ? null : (int)account.Level.Score,
                    FinalPoints = result is null ? null : result.PartPoint1,
                    FinalScore = result is null ? null : result.TotalPoint / 1000d,
                    GradingScoreDelta = result is null ? null : result.GradingScore,
                    IsFocusPlayer = !string.IsNullOrWhiteSpace(focusPlayerNickname)
                        && string.Equals(account.Nickname, focusPlayerNickname, StringComparison.OrdinalIgnoreCase)
                };
            })
            .ToArray();
    }

    private static List<GameEvent> MapAction(Wrapper wrapper, int sequence)
    {
        var events = new List<GameEvent>();
        switch (wrapper.Name)
        {
            case ".lq.RecordDealTile":
                {
                    var value = RecordDealTile.Parser.ParseFrom(wrapper.Data);
                    AddRiichiAccepted(events, value.Liqi, wrapper.Name, ref sequence);
                    events.Add(new GameEvent
                    {
                        Sequence = sequence,
                        Type = "draw",
                        SourceAction = wrapper.Name,
                        Seat = (int)value.Seat,
                        Tile = value.Tile,
                        TilesLeft = (int)value.LeftTileCount,
                        DoraIndicators = NullIfEmpty(value.Doras)
                    });
                    break;
                }
            case ".lq.RecordDiscardTile":
                {
                    var value = RecordDiscardTile.Parser.ParseFrom(wrapper.Data);
                    events.Add(new GameEvent
                    {
                        Sequence = sequence,
                        Type = "discard",
                        SourceAction = wrapper.Name,
                        Seat = (int)value.Seat,
                        Tile = value.Tile,
                        IsTsumogiri = value.Moqie,
                        IsRiichi = value.IsLiqi || value.IsWliqi,
                        IsDoubleRiichi = value.IsWliqi,
                        DoraIndicators = NullIfEmpty(value.Doras)
                    });
                    break;
                }
            case ".lq.RecordChiPengGang":
                {
                    var value = RecordChiPengGang.Parser.ParseFrom(wrapper.Data);
                    AddRiichiAccepted(events, value.Liqi, wrapper.Name, ref sequence);
                    events.Add(new GameEvent
                    {
                        Sequence = sequence,
                        Type = "call",
                        SourceAction = wrapper.Name,
                        Seat = (int)value.Seat,
                        Tiles = value.Tiles.ToArray(),
                        FromSeats = value.Froms.Select(seat => (int)seat).ToArray(),
                        SourceType = (int)value.Type,
                        MeldType = value.Type switch
                        {
                            0 => "chi",
                            1 => "pon",
                            2 => "daiminkan",
                            _ => "unknown"
                        },
                        Scores = NullIfEmpty(value.Scores)
                    });
                    break;
                }
            case ".lq.RecordAnGangAddGang":
                {
                    var value = RecordAnGangAddGang.Parser.ParseFrom(wrapper.Data);
                    events.Add(new GameEvent
                    {
                        Sequence = sequence,
                        Type = "kan",
                        SourceAction = wrapper.Name,
                        Seat = (int)value.Seat,
                        Tile = value.Tiles,
                        SourceType = (int)value.Type,
                        MeldType = value.Type switch
                        {
                            2 => "kakan",
                            3 => "ankan",
                            _ => "unknown"
                        },
                        DoraIndicators = NullIfEmpty(value.Doras)
                    });
                    break;
                }
            case ".lq.RecordBaBei":
                {
                    var value = RecordBaBei.Parser.ParseFrom(wrapper.Data);
                    events.Add(new GameEvent
                    {
                        Sequence = sequence,
                        Type = "north",
                        SourceAction = wrapper.Name,
                        Seat = (int)value.Seat,
                        Tile = "4z",
                        IsTsumogiri = value.Moqie,
                        DoraIndicators = NullIfEmpty(value.Doras)
                    });
                    break;
                }
            case ".lq.RecordHule":
                {
                    var value = RecordHule.Parser.ParseFrom(wrapper.Data);
                    events.Add(new GameEvent
                    {
                        Sequence = sequence,
                        Type = "win",
                        SourceAction = wrapper.Name,
                        Scores = NullIfEmpty(value.Scores),
                        ScoreDeltas = NullIfEmpty(value.DeltaScores),
                        DoraIndicators = NullIfEmpty(value.Doras),
                        GameEnded = value.Gameend is not null,
                        Wins = value.Hules.Select(MapWin).ToArray()
                    });
                    break;
                }
            case ".lq.RecordNoTile":
                {
                    var value = RecordNoTile.Parser.ParseFrom(wrapper.Data);
                    var scoreBySeat = value.Scores.ToDictionary(item => (int)item.Seat);
                    var drawPlayers = value.Players.Select((player, seat) =>
                    {
                        scoreBySeat.TryGetValue(seat, out var score);
                        return new DrawPlayerRecord
                        {
                            Seat = seat,
                            IsTenpai = player.Tingpai,
                            RevealedHand = player.Hand.ToArray(),
                            Score = score is null ? null : (int)score.Score
                        };
                    }).ToArray();
                    events.Add(new GameEvent
                    {
                        Sequence = sequence,
                        Type = value.Liujumanguan ? "nagashi-mangan" : "exhaustive-draw",
                        SourceAction = wrapper.Name,
                        DrawPlayers = drawPlayers,
                        GameEnded = value.Gameend,
                        Scores = value.Scores.Count == 0
                            ? null
                            : value.Scores.OrderBy(item => item.Seat).Select(item => (int)item.Score).ToArray()
                    });
                    break;
                }
            case ".lq.RecordLiuJu":
                {
                    var value = RecordLiuJu.Parser.ParseFrom(wrapper.Data);
                    events.Add(new GameEvent
                    {
                        Sequence = sequence,
                        Type = "abortive-draw",
                        SourceAction = wrapper.Name,
                        Seat = (int)value.Seat,
                        SourceType = (int)value.Type,
                        Tiles = NullIfEmpty(value.Tiles),
                        Scores = value.Gameend is null ? null : value.Gameend.Scores.ToArray(),
                        GameEnded = value.Gameend is not null
                    });
                    break;
                }
            case ".lq.RecordMJStart":
                break;
            default:
                events.Add(new GameEvent
                {
                    Sequence = sequence,
                    Type = "unknown",
                    SourceAction = wrapper.Name,
                    RawPayloadBase64 = System.Convert.ToBase64String(wrapper.Data.Span)
                });
                break;
        }

        return events;
    }

    private static WinRecord MapWin(HuleInfo value) => new()
    {
        Seat = (int)value.Seat,
        IsTsumo = value.Zimo,
        IsDealer = value.Qinjia,
        IsRiichi = value.Liqi,
        IsYakuman = value.Yiman,
        WinningTile = value.HuTile,
        Hand = value.Hand.ToArray(),
        Melds = value.Ming.ToArray(),
        DoraIndicators = value.Doras.ToArray(),
        UraDoraIndicators = value.LiDoras.ToArray(),
        Han = (int)value.Count,
        Fu = (int)value.Fu,
        RonPoints = (int)value.PointRong,
        DealerTsumoPayment = (int)value.PointZimoQin,
        NonDealerTsumoPayment = (int)value.PointZimoXian,
        TotalPoints = (int)value.PointSum,
        Yaku = value.Fans.Select(fan => new YakuRecord
        {
            SourceId = (int)fan.Id,
            Name = fan.Name,
            Han = (int)fan.Val
        }).ToArray()
    };

    private static void AddRiichiAccepted(
        ICollection<GameEvent> events,
        LiQiSuccess? value,
        string sourceAction,
        ref int sequence)
    {
        if (value is null)
        {
            return;
        }

        events.Add(new GameEvent
        {
            Sequence = sequence++,
            Type = "riichi-accepted",
            SourceAction = sourceAction,
            Seat = (int)value.Seat,
            RiichiAccepted = !value.Failed,
            Scores = [value.Score]
        });
    }

    private static DateTimeOffset? FromUnixSeconds(uint value) =>
        value == 0 ? null : DateTimeOffset.FromUnixTimeSeconds(value);

    private static IReadOnlyList<T>? NullIfEmpty<T>(IEnumerable<T> source)
    {
        var result = source.ToArray();
        return result.Length == 0 ? null : result;
    }

    private sealed record SourceAction(Wrapper Wrapper);

    private sealed class RoundBuilder
    {
        private readonly List<GameEvent> _events = [];
        private readonly RecordNewRound _source;
        private readonly int _index;

        public RoundBuilder(int index, RecordNewRound source)
        {
            _index = index;
            _source = source;
        }

        public int NextSequence => _events.Count;

        public void AddRange(IEnumerable<GameEvent> values)
        {
            foreach (var value in values)
            {
                _events.Add(value with { Sequence = _events.Count });
            }
        }

        public RoundRecord Build()
        {
            var doras = _source.Doras.Count > 0
                ? _source.Doras.ToArray()
                : string.IsNullOrWhiteSpace(_source.Dora) ? [] : [_source.Dora];

            return new RoundRecord
            {
                Index = _index,
                Wind = _source.Chang switch
                {
                    0 => "east",
                    1 => "south",
                    2 => "west",
                    3 => "north",
                    _ => $"unknown-{_source.Chang}"
                },
                HandNumber = (int)_source.Ju + 1,
                DealerSeat = (int)_source.Ju,
                Honba = (int)_source.Ben,
                RiichiSticks = (int)_source.Liqibang,
                InitialScores = _source.Scores.ToArray(),
                InitialHands = new Dictionary<int, IReadOnlyList<string>>
                {
                    [0] = _source.Tiles0.ToArray(),
                    [1] = _source.Tiles1.ToArray(),
                    [2] = _source.Tiles2.ToArray(),
                    [3] = _source.Tiles3.ToArray()
                }.Where(pair => pair.Value.Count > 0).ToDictionary(pair => pair.Key, pair => pair.Value),
                DoraIndicators = doras,
                InitialTilesLeft = _source.LeftTileCount > 0 ? (int)_source.LeftTileCount : null,
                Events = _events.ToArray()
            };
        }
    }
}
