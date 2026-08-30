using Maque.Majsoul.Protocol;

namespace Maque.Majsoul;

public sealed record MajsoulFetchResult(
    MajsoulRecordLink Link,
    string ProtocolVersion,
    string ClientVersion,
    string Gateway,
    RecordGame Header,
    byte[] RecordData);
