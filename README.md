[![Build status](https://github.com/RikaKagurasaka/DamageInfoExtended/actions/workflows/build.yml/badge.svg)](https://github.com/RikaKagurasaka/DamageInfoExtended/actions/workflows/build.yml)
[![Latest release](https://img.shields.io/github/v/release/RikaKagurasaka/DamageInfoExtended)](https://github.com/RikaKagurasaka/DamageInfoExtended/releases)

# Damage Info Extended

GPL-3.0 derivative of Damage Info for extra information in native XIV flytext.
It retains Damage Info's damage type/source features and appends a conservative
calculated mitigation suffix to a matched incoming source subtitle, for example
`>Enemy -28%`. A recognised native block/parry subtitle is folded into the
same percentage: Chinese `(-15% 招架)` / `(-15% 格挡)`, English `(Parried
15%)` / `(Blocked 20%)`, plus Japanese, German and French forms.

`/dmginfoext` opens the configuration window.

The percentage is the calculated rate of known active reductions, stacked
multiplicatively. It is not a claim of exact prevented damage: shields,
invulnerability, variable effects, server rounding, and unknown statuses are
excluded. A native block/parry rate is included only when it is explicitly
shown and recognised. If the action-effect to flytext correlation is missing
or ambiguous, the native flytext is left unchanged.

No shield quantity is displayed. The configuration can place mitigation before
the source instead, such as `-28% >Enemy`.

See [implementation status](docs/IMPLEMENTATION.md) for environment setup,
build/test commands, known limits, and runtime validation.

## Installation

After the first live-tested release is published, add this custom repository in
`/xlsettings` → Experimental → Custom Plugin Repositories:

`https://raw.githubusercontent.com/RikaKagurasaka/DalamudPlugins/main/pluginmaster.json`

Then install **Damage Info Extended** through `/xlplugins`. Until the live-game
test matrix is complete, this URL deliberately has no installable release.

The configuration window also provides the original Damage Info presentation
options. Physical, blunt, piercing, and slashing are combined into physical;
magic is magical; and darkness is a game type distinct from physical/magical.

## Purpose
The purpose of this plugin is to provide extra information in channels untapped by Square in FFXIV. FFXIV has always had support for coloring the flying text, but has never used it to display anything meaningful. In addition, there are a number of text setups that can provide extra information in the subtitle usually reserved for parries or blocks.

## Status

The mitigation extension has passed a compile check and pure calculation unit
tests, but still requires the live FFXIV test matrix before any release claim.

## Known issues
- From experience the plugin in its current state is quite reliable with damage type, however, it is for a general idea of damage type, rather than a 100% guarantee due to the way damage/flytext is linked.
- Some actions that are cast by enemies are not the same action that take effect on the player. For example, the boss may cast "Damaging Attack", that is a magic action. However, the damage that the player receives is a _different action_ called "Damaging Attack", that may be physical. The reason for this is unknown. It only affects cast bar accuracy. It cannot be fixed.
For licence and third-party attribution, see [LICENSE](LICENSE) and
[THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
