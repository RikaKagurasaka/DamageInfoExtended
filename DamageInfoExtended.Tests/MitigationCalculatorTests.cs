using DamageInfoPlugin;
using Xunit;

namespace DamageInfoExtended.Tests;

public sealed class MitigationCalculatorTests
{
    [Fact]
    public void CombinesTargetAndSourceReductionsMultiplicatively()
    {
        var result = MitigationCalculator.Calculate(
            DamageType.Physical,
            [new MitigationStatus(1191, "Rampart", 100)],
            [new MitigationStatus(1193, "Reprisal", 200)],
            includeSourceDebuffs: true);

        Assert.Equal(0.28f, result.Reduction, precision: 3);
        Assert.Collection(
            result.Contributions,
            contribution => Assert.Equal("Rampart", contribution.Name),
            contribution => Assert.Equal("Reprisal", contribution.Name));
    }

    [Theory]
    [InlineData(DamageType.Physical, 0.28f)] // Rampart + Feint (10%).
    [InlineData(DamageType.Magical, 0.24f)]  // Rampart + Feint (5%).
    public void UsesTheCorrectTypedPortionOfSourceDebuffs(DamageType damageType, float expected)
    {
        var result = MitigationCalculator.Calculate(
            damageType,
            [new MitigationStatus(1191, "Rampart", 100)],
            [new MitigationStatus(1195, "Feint", 200)],
            includeSourceDebuffs: true);

        Assert.Equal(expected, result.Reduction, precision: 3);
    }

    [Fact]
    public void DoesNotApplyTypedMitigationToUnknownDamage()
    {
        var result = MitigationCalculator.Calculate(
            DamageType.Unique,
            [new MitigationStatus(1191, "Rampart", 100)],
            [new MitigationStatus(1195, "Feint", 200)],
            includeSourceDebuffs: true);

        Assert.Equal(0.20f, result.Reduction, precision: 3);
        Assert.Single(result.Contributions);
        Assert.Equal("Rampart", result.Contributions[0].Name);
    }

    [Fact]
    public void IgnoresExactDuplicateStatusInstances()
    {
        var result = MitigationCalculator.Calculate(
            DamageType.Physical,
            [
                new MitigationStatus(1191, "Rampart", 100),
                new MitigationStatus(1191, "Rampart", 100),
            ],
            Array.Empty<MitigationStatus>(),
            includeSourceDebuffs: true);

        Assert.Equal(0.20f, result.Reduction, precision: 3);
        Assert.Single(result.Contributions);
    }

    [Fact]
    public void KeepsSameStatusFromDifferentSourcesSeparate()
    {
        var result = MitigationCalculator.Calculate(
            DamageType.Physical,
            [
                new MitigationStatus(299, "Sacred Soil", 100),
                new MitigationStatus(299, "Sacred Soil", 101),
            ],
            Array.Empty<MitigationStatus>(),
            includeSourceDebuffs: true);

        Assert.Equal(0.19f, result.Reduction, precision: 3);
        Assert.Equal(2, result.Contributions.Count);
    }

    [Fact]
    public void CanExcludeSourceDebuffs()
    {
        var result = MitigationCalculator.Calculate(
            DamageType.Magical,
            Array.Empty<MitigationStatus>(),
            [new MitigationStatus(1203, "Addle", 200)],
            includeSourceDebuffs: false);

        Assert.False(result.HasKnownReduction);
        Assert.Empty(result.Contributions);
    }

    [Theory]
    [InlineData(3829, "Guardian")]
    [InlineData(3832, "Damnation")]
    [InlineData(3835, "Shadowed Vigil")]
    [InlineData(3838, "Great Nebula")]
    public void RecognisesDawntrailLevel100TankMitigationsByStatusId(uint statusId, string name)
    {
        var result = MitigationCalculator.Calculate(
            DamageType.Physical,
            [new MitigationStatus(statusId, "localized status name", 100)],
            Array.Empty<MitigationStatus>(),
            includeSourceDebuffs: false);

        Assert.Equal(0.40f, result.Reduction, precision: 3);
        var contribution = Assert.Single(result.Contributions);
        Assert.Equal(name, contribution.Name);
    }
}
