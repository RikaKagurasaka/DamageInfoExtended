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

    [Theory]
    [InlineData(2708, "Aquaveil", 0.15f)]
    [InlineData(2717, "Exaltation", 0.10f)]
    [InlineData(2711, "Expedient", 0.10f)]
    [InlineData(2619, "Taurochole", 0.10f)]
    [InlineData(1219, "Confession", 0.10f)]
    public void RecognisesHealerMitigationsByStatusId(uint statusId, string name, float expectedReduction)
    {
        var result = MitigationCalculator.Calculate(
            DamageType.Magical,
            [new MitigationStatus(statusId, "localized status name", 100)],
            Array.Empty<MitigationStatus>(),
            includeSourceDebuffs: false);

        Assert.Equal(expectedReduction, result.Reduction, precision: 3);
        Assert.Equal(name, Assert.Single(result.Contributions).Name);
    }

    [Theory]
    [InlineData(74, "Sentinel", 0.30f)]
    [InlineData(728, "Sheltron", 0.15f)]
    [InlineData(2674, "Holy Sheltron", 0.15f)]
    [InlineData(735, "Raw Intuition", 0.10f)]
    [InlineData(2678, "Bloodwhetting", 0.10f)]
    [InlineData(1858, "Nascent Glint", 0.10f)]
    [InlineData(2679, "Stem the Flow", 0.10f)]
    [InlineData(89, "Vengeance", 0.30f)]
    [InlineData(747, "Shadow Wall", 0.30f)]
    [InlineData(2682, "Oblation", 0.10f)]
    [InlineData(1832, "Camouflage", 0.10f)]
    [InlineData(1834, "Nebula", 0.30f)]
    [InlineData(1840, "Heart of Stone", 0.15f)]
    [InlineData(2683, "Heart of Corundum", 0.15f)]
    [InlineData(2684, "Clarity of Corundum", 0.15f)]
    [InlineData(317, "Fey Illumination", 0.05f)]
    [InlineData(1875, "Seraphic Illumination", 0.05f)]
    [InlineData(3896, "Sun Sign", 0.10f)]
    [InlineData(830, "The Bole", 0.10f)]
    [InlineData(3003, "Holos", 0.10f)]
    [InlineData(1179, "Riddle of Earth", 0.20f)]
    [InlineData(1232, "Third Eye", 0.10f)]
    [InlineData(1197, "Tactician", 0.15f)]
    [InlineData(2707, "Magick Barrier", 0.10f)]
    public void RecognisesAdditionalJobMitigationsByStatusId(uint statusId, string name, float expectedReduction)
    {
        var damageType = name is "Fey Illumination" or "Seraphic Illumination" or "Magick Barrier"
            ? DamageType.Magical
            : DamageType.Physical;
        var result = MitigationCalculator.Calculate(
            damageType,
            [new MitigationStatus(statusId, "localized status name", 100)],
            Array.Empty<MitigationStatus>(),
            includeSourceDebuffs: false);

        Assert.Equal(expectedReduction, result.Reduction, precision: 3);
        Assert.Equal(name, Assert.Single(result.Contributions).Name);
    }
}
