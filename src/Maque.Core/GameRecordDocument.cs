namespace Maque.Core;

public sealed record GameRecordDocument
{
    public string Format { get; init; } = "maque-universal-paipu";
    public int FormatVersion { get; init; } = 1;
    public required string GameId { get; init; }
    public required SourceRecord Source { get; init; }
    public DateTimeOffset? StartedAt { get; init; }
    public DateTimeOffset? EndedAt { get; init; }
    public required GameRules Rules { get; init; }
    public required IReadOnlyList<PlayerRecord> Players { get; init; }
    public required IReadOnlyList<RoundRecord> Rounds { get; init; }
    public IReadOnlyList<GameEvent> UnmappedEvents { get; init; } = [];
}

public sealed record SourceRecord
{
    public required string Platform { get; init; }
    public required string RecordId { get; init; }
    public required string PublicId { get; init; }
    public required string OriginalLink { get; init; }
    public required DateTimeOffset FetchedAt { get; init; }
    public required string ProtocolVersion { get; init; }
    public required string ClientVersion { get; init; }
    public required string RawPath { get; init; }
    public required string RawSha256 { get; init; }
    public string? FocusPlayerNickname { get; init; }
}

public sealed record GameRules
{
    public required int PlayerCount { get; init; }
    public required string RoundType { get; init; }
    public required int SourceCategory { get; init; }
    public required int SourceMode { get; init; }
    public required int SourceModeId { get; init; }
    public required int SourceStandardRule { get; init; }
    public int? InitialPoints { get; init; }
    public bool? CanEndBelowZero { get; init; }
}

public sealed record PlayerRecord
{
    public required int Seat { get; init; }
    public required string Nickname { get; init; }
    public long? SourceAccountId { get; init; }
    public int? LevelId { get; init; }
    public int? LevelScore { get; init; }
    public int? FinalPoints { get; init; }
    public double? FinalScore { get; init; }
    public int? GradingScoreDelta { get; init; }
    public bool IsFocusPlayer { get; init; }
}

public sealed record RoundRecord
{
    public required int Index { get; init; }
    public required string Wind { get; init; }
    public required int HandNumber { get; init; }
    public required int DealerSeat { get; init; }
    public required int Honba { get; init; }
    public required int RiichiSticks { get; init; }
    public required IReadOnlyList<int> InitialScores { get; init; }
    public required IReadOnlyDictionary<int, IReadOnlyList<string>> InitialHands { get; init; }
    public required IReadOnlyList<string> DoraIndicators { get; init; }
    public int? InitialTilesLeft { get; init; }
    public required IReadOnlyList<GameEvent> Events { get; init; }
}

public sealed record GameEvent
{
    public required int Sequence { get; init; }
    public required string Type { get; init; }
    public required string SourceAction { get; init; }
    public int? Seat { get; init; }
    public int? FromSeat { get; init; }
    public string? Tile { get; init; }
    public IReadOnlyList<string>? Tiles { get; init; }
    public IReadOnlyList<int>? FromSeats { get; init; }
    public bool? IsTsumogiri { get; init; }
    public bool? IsRiichi { get; init; }
    public bool? IsDoubleRiichi { get; init; }
    public bool? RiichiAccepted { get; init; }
    public int? SourceType { get; init; }
    public string? MeldType { get; init; }
    public int? TilesLeft { get; init; }
    public IReadOnlyList<string>? DoraIndicators { get; init; }
    public IReadOnlyList<int>? Scores { get; init; }
    public IReadOnlyList<int>? ScoreDeltas { get; init; }
    public IReadOnlyList<WinRecord>? Wins { get; init; }
    public IReadOnlyList<DrawPlayerRecord>? DrawPlayers { get; init; }
    public bool? GameEnded { get; init; }
    public string? RawPayloadBase64 { get; init; }
}

public sealed record WinRecord
{
    public required int Seat { get; init; }
    public required bool IsTsumo { get; init; }
    public required bool IsDealer { get; init; }
    public required bool IsRiichi { get; init; }
    public required bool IsYakuman { get; init; }
    public required string WinningTile { get; init; }
    public required IReadOnlyList<string> Hand { get; init; }
    public required IReadOnlyList<string> Melds { get; init; }
    public required IReadOnlyList<string> DoraIndicators { get; init; }
    public required IReadOnlyList<string> UraDoraIndicators { get; init; }
    public required int Han { get; init; }
    public required int Fu { get; init; }
    public required int RonPoints { get; init; }
    public required int DealerTsumoPayment { get; init; }
    public required int NonDealerTsumoPayment { get; init; }
    public required int TotalPoints { get; init; }
    public required IReadOnlyList<YakuRecord> Yaku { get; init; }
}

public sealed record YakuRecord
{
    public required int SourceId { get; init; }
    public required string Name { get; init; }
    public required int Han { get; init; }
}

public sealed record DrawPlayerRecord
{
    public required int Seat { get; init; }
    public required bool IsTenpai { get; init; }
    public required IReadOnlyList<string> RevealedHand { get; init; }
    public int? Score { get; init; }
}
