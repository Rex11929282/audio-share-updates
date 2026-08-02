using AudioShare.Lyrics;

namespace AudioShare.Core.Tests;

public sealed class LrcParserTests
{
    [Fact]
    public void Parse_OrdersTimedLinesAndSetsEndTimes()
    {
        var lines = LrcParser.Parse("[ar:歌手]\n[00:02.50]第二句\n[00:01.00]第一句");

        Assert.Collection(
            lines,
            first =>
            {
                Assert.Equal("第一句", first.Text);
                Assert.Equal(1000, first.StartTimeMilliseconds);
                Assert.Equal(2500, first.EndTimeMilliseconds);
            },
            second =>
            {
                Assert.Equal("第二句", second.Text);
                Assert.Equal(2500, second.StartTimeMilliseconds);
                Assert.Null(second.EndTimeMilliseconds);
            });
    }

    [Fact]
    public void Parse_IgnoresMetadataMalformedAndBlankRows()
    {
        Assert.Empty(LrcParser.Parse("[ti:標題]\n錯誤行\n[00:03.00]   "));
    }

    [Fact]
    public void Parse_ExpandsMultipleTimestampsOnOneRow()
    {
        var lines = LrcParser.Parse("[00:01.00][00:03.00]重複副歌");

        Assert.Equal([1000L, 3000L], lines.Select(line => line.StartTimeMilliseconds));
        Assert.All(lines, line => Assert.Equal("重複副歌", line.Text));
    }

    [Fact]
    public void Parse_NormalizesOneTwoAndThreeDigitFractions()
    {
        var lines = LrcParser.Parse("[00:01.5]一位\n[00:02.05]兩位\n[00:03.005]三位");

        Assert.Equal([1500L, 2050L, 3005L], lines.Select(line => line.StartTimeMilliseconds));
    }
}
