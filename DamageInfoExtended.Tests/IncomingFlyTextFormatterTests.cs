using DamageInfoPlugin;
using Xunit;

namespace DamageInfoExtended.Tests;

public sealed class IncomingFlyTextFormatterTests
{
    [Theory]
    [InlineData("(-15% 招架)", 0.15f)]
    [InlineData("伤害 (-20% 格挡)", 0.20f)]
    [InlineData("(Parried 15%)", 0.15f)]
    [InlineData("(Blocked 20%)", 0.20f)]
    [InlineData("（受け流し 15%）", 0.15f)]
    [InlineData("（ブロック 20%）", 0.20f)]
    [InlineData("(Pariert 15,5%)", 0.155f)]
    [InlineData("(Geblockt 20%)", 0.20f)]
    [InlineData("(Parade 15%)", 0.15f)]
    [InlineData("(Blocage 20%)", 0.20f)]
    [InlineData("(Paré 15%)", 0.15f)]
    public void ExtractsKnownNativeBlockOrParryModifier(string text, float expectedReduction)
    {
        var parsed = IncomingFlyTextFormatter.TryExtractBlockOrParry(text, out var reduction, out var remainingText);

        Assert.True(parsed);
        Assert.Equal(expectedReduction, reduction, precision: 3);
        Assert.DoesNotMatch(@"[（(].*%.*[）)]", remainingText);
    }

    [Fact]
    public void LeavesUnrecognisedSubtitleUntouched()
    {
        const string text = "(-15% Dodge)";

        var parsed = IncomingFlyTextFormatter.TryExtractBlockOrParry(text, out _, out var remainingText);

        Assert.False(parsed);
        Assert.Equal(text, remainingText);
    }

    [Fact]
    public void DisplaysAllMitigationAsRoundedIntegerPercent()
    {
        var combined = IncomingFlyTextFormatter.CombineReductions(0.28f, 0.15f);

        Assert.Equal(0.388f, combined, precision: 3);
        Assert.Equal(" -39%", IncomingFlyTextFormatter.BuildSourceSuffix(combined));
        Assert.Equal(" -16%", IncomingFlyTextFormatter.BuildSourceSuffix(0.159f));
        Assert.Equal(" -20%", IncomingFlyTextFormatter.BuildSourceSuffix(0.199999f));
    }
}
