// Portions adapted from Better Deaths (https://github.com/nainaiowo/better-deaths),
// commit fa75e04b0674659bf5f21c2da4856de6d3b13870, under the MIT License.
// See ../THIRD_PARTY_NOTICES.md.

using System;
using System.Collections.Generic;
using System.Linq;

namespace DamageInfoPlugin;

internal enum MitigationScope
{
    All,
    Physical,
    Magical,
}

internal readonly record struct MitigationStatus(uint Id, string Name, uint SourceId);

internal sealed record MitigationContribution(
    uint StatusId,
    string Name,
    float Percent,
    MitigationScope Scope,
    bool IsSourceDebuff);

internal sealed record MitigationResult(float Reduction, IReadOnlyList<MitigationContribution> Contributions)
{
    public bool HasKnownReduction => Contributions.Count > 0 && Reduction > 0;

    public string DisplayPercent => $"{Reduction * 100:0.#}%";
}

internal sealed record MitigationRule(
    string Name,
    float Percent,
    MitigationScope Scope,
    bool IsSourceDebuff = false,
    uint StatusId = 0);

/// <summary>
/// Calculates the known, declared damage reductions active for a resolved hit.
/// It intentionally excludes shields, invulnerability, block/parry, and variable
/// mitigation strengths because none has a reliable fixed percentage here.
/// </summary>
internal static class MitigationCalculator
{
    // The direct reduction rules and multiplicative stacking are adapted from
    // Better Deaths. IDs are used where verified; names are a compatibility
    // fallback until the complete status-ID migration is finished.
    private static readonly MitigationRule[] Rules =
    [
        new("Rampart", 20, MitigationScope.All, StatusId: 1191),
        new("Sentinel", 30, MitigationScope.All),
        // Dawntrail level-100 tank actions. These status IDs are required for
        // non-English clients; name-only matching silently misses them.
        new("Guardian", 40, MitigationScope.All, StatusId: 3829),
        new("Sheltron", 15, MitigationScope.All),
        new("Holy Sheltron", 15, MitigationScope.All),
        new("Knight's Resolve", 15, MitigationScope.All, StatusId: 2675),
        new("Passage of Arms", 15, MitigationScope.All, StatusId: 1176),
        new("Raw Intuition", 10, MitigationScope.All),
        new("Bloodwhetting", 10, MitigationScope.All),
        new("Nascent Glint", 10, MitigationScope.All),
        new("Stem the Flow", 10, MitigationScope.All),
        new("Vengeance", 30, MitigationScope.All),
        new("Damnation", 40, MitigationScope.All, StatusId: 3832),
        new("Dark Mind", 10, MitigationScope.Physical, StatusId: 746),
        new("Dark Mind", 20, MitigationScope.Magical, StatusId: 746),
        new("Dark Missionary", 5, MitigationScope.Physical, StatusId: 1894),
        new("Dark Missionary", 10, MitigationScope.Magical, StatusId: 1894),
        new("Shadow Wall", 30, MitigationScope.All),
        new("Shadowed Vigil", 40, MitigationScope.All, StatusId: 3835),
        new("Oblation", 10, MitigationScope.All),
        new("Camouflage", 10, MitigationScope.All),
        new("Nebula", 30, MitigationScope.All),
        new("Great Nebula", 40, MitigationScope.All, StatusId: 3838),
        new("Heart of Stone", 15, MitigationScope.All),
        new("Heart of Corundum", 15, MitigationScope.All),
        new("Clarity of Corundum", 15, MitigationScope.All),
        new("Heart of Light", 5, MitigationScope.Physical, StatusId: 1839),
        new("Heart of Light", 10, MitigationScope.Magical, StatusId: 1839),
        new("Temperance", 10, MitigationScope.All, StatusId: 1873),
        new("Aquaveil", 15, MitigationScope.All, StatusId: 2708),
        new("Confession", 10, MitigationScope.All, StatusId: 1219),
        new("Sacred Soil", 10, MitigationScope.All, StatusId: 299),
        new("Sacred Soil", 10, MitigationScope.All, StatusId: 2638),
        // The mitigation portion of Expedient is exposed as Desperate Measures.
        new("Expedient", 10, MitigationScope.All, StatusId: 2711),
        new("Fey Illumination", 5, MitigationScope.Magical),
        new("Seraphic Illumination", 5, MitigationScope.Magical),
        new("Collective Unconscious", 10, MitigationScope.All, StatusId: 849),
        new("Wheel of Fortune", 10, MitigationScope.All, StatusId: 1206),
        new("Exaltation", 10, MitigationScope.All, StatusId: 2717),
        new("Sun Sign", 10, MitigationScope.All),
        new("The Bole", 10, MitigationScope.All),
        new("Kerachole", 10, MitigationScope.All, StatusId: 2618),
        new("Taurochole", 10, MitigationScope.All, StatusId: 2619),
        new("Holos", 10, MitigationScope.All),
        new("Riddle of Earth", 20, MitigationScope.All),
        new("Third Eye", 10, MitigationScope.All),
        new("Tengentsu", 10, MitigationScope.All, StatusId: 3853),
        new("Tengentsu's Foresight", 10, MitigationScope.All, StatusId: 3854),
        new("Troubadour", 15, MitigationScope.All, StatusId: 1934),
        new("Tactician", 15, MitigationScope.All),
        new("Shield Samba", 15, MitigationScope.All, StatusId: 1826),
        new("Magick Barrier", 10, MitigationScope.Magical),

        // Enemy-side damage-down effects.
        new("Reprisal", 10, MitigationScope.All, IsSourceDebuff: true, StatusId: 1193),
        new("Feint", 10, MitigationScope.Physical, IsSourceDebuff: true, StatusId: 1195),
        new("Feint", 5, MitigationScope.Magical, IsSourceDebuff: true, StatusId: 1195),
        new("Addle", 5, MitigationScope.Physical, IsSourceDebuff: true, StatusId: 1203),
        new("Addle", 10, MitigationScope.Magical, IsSourceDebuff: true, StatusId: 1203),
        new("Dismantled", 10, MitigationScope.All, IsSourceDebuff: true, StatusId: 860),
    ];

    internal static MitigationResult Calculate(
        DamageType damageType,
        IEnumerable<MitigationStatus> targetStatuses,
        IEnumerable<MitigationStatus> sourceStatuses,
        bool includeSourceDebuffs)
    {
        var contributions = new List<MitigationContribution>();
        AddApplicableRules(contributions, damageType, targetStatuses, sourceRules: false);
        if (includeSourceDebuffs)
        {
            AddApplicableRules(contributions, damageType, sourceStatuses, sourceRules: true);
        }

        var remaining = 1.0;
        foreach (var contribution in contributions)
        {
            remaining *= 1.0 - (Math.Clamp(contribution.Percent, 0f, 100f) / 100.0);
        }

        return new MitigationResult((float)(1.0 - remaining), contributions);
    }

    private static void AddApplicableRules(
        List<MitigationContribution> contributions,
        DamageType damageType,
        IEnumerable<MitigationStatus> statuses,
        bool sourceRules)
    {
        foreach (var status in statuses.Where(status => status.Id != 0).GroupBy(status => (status.Id, status.SourceId)).Select(group => group.First()))
        {
            foreach (var rule in Rules)
            {
                if (rule.IsSourceDebuff != sourceRules || !RuleMatches(rule, status) || !ScopeApplies(rule.Scope, damageType))
                {
                    continue;
                }

                contributions.Add(new MitigationContribution(
                    status.Id,
                    rule.Name,
                    rule.Percent,
                    rule.Scope,
                    rule.IsSourceDebuff));
            }
        }
    }

    private static bool RuleMatches(MitigationRule rule, MitigationStatus status)
        => rule.StatusId != 0
            ? rule.StatusId == status.Id
            : rule.Name.Equals(status.Name, StringComparison.OrdinalIgnoreCase);

    private static bool ScopeApplies(MitigationScope scope, DamageType damageType)
        => scope switch
        {
            MitigationScope.Physical => damageType == DamageType.Physical,
            MitigationScope.Magical => damageType == DamageType.Magical,
            _ => true,
        };
}
