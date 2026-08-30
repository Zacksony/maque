using Maque.Majsoul;

namespace Maque.Tests;

public sealed class MajsoulRecordLinkTests
{
    [Fact]
    public void Parse_ExtractsRecordAndViewpointFromSharedLink()
    {
        var result = MajsoulRecordLink.Parse(
            "https://game.maj-soul.com/1/?paipu=260826-826cd976-c7b5-4ef5-8c80-3fbf91f95a0b_a21920067");

        Assert.Equal("260826-826cd976-c7b5-4ef5-8c80-3fbf91f95a0b", result.RecordId);
        Assert.Equal("a21920067", result.ViewpointToken);
        Assert.Equal("260826-826cd976-c7b5-4ef5-8c80-3fbf91f95a0b_a21920067", result.PublicId);
    }

    [Fact]
    public void Parse_AcceptsBareRecordId()
    {
        var result = MajsoulRecordLink.Parse("260823-be706326-22da-4f44-8a40-b0de261de110");

        Assert.Equal("260823-be706326-22da-4f44-8a40-b0de261de110", result.RecordId);
        Assert.Null(result.ViewpointToken);
    }

    [Fact]
    public void Parse_RejectsUnrelatedUrl()
    {
        Assert.Throws<FormatException>(() => MajsoulRecordLink.Parse("https://example.com/not-a-record"));
    }
}
