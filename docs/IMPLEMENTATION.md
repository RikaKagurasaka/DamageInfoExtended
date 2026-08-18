# Implementation status

## Delivered in the first working slice

- The project is a GPL-3.0 derivative of Damage Info at upstream commit
  `4c7ed96796e9e6991d2b4a6c5c5e342ad1893bd3`.
- The existing ActionEffect → screen-log → `IFlyTextGui.FlyTextCreated`
  correlation path now snapshots local-player incoming-hit statuses and adds
  `-[rate]` to its matched incoming source subtitle.
- Action-effect capture uses the SDK-15
  `ActionEffectHandler.MemberFunctionPointers.Receive` member-function pointer,
  not Damage Info's legacy byte signature.
- Known reductions stack multiplicatively. Typed reductions only apply to a
  matching physical or magical packet type; unknown packet types use only
  all-damage reductions.
- Source-side Addle, Feint, Reprisal, and Dismantled are configurable and are
  evaluated from the attacker snapshot at packet time.
- Recognised native parenthesised block/parry suffixes are combined
  multiplicatively with status reduction and folded into one source suffix.
  Supported forms cover Chinese, English, Japanese, German and French; an
  unrecognised form is left intact. Every mitigation result is truncated to an
  integer percentage.
- A configuration option can place the mitigation suffix before the source
  instead of after it.
- The configuration window exposes the annotation, source-debuff inclusion,
  and opt-in diagnostic logging. Diagnostic entries contain IDs and packet
  values, not character names.

## Deliberate limits

- This is local-player incoming damage only. The plugin must not present a
  party-wide flytext feature it cannot correlate reliably.
- The shown figure is a **calculated active mitigation rate**, not measured
  damage prevented. The packet contains resolved damage.
- Shields, invulnerability, and variable/unknown effects are excluded from the
  status-derived percentage. A recognised native block/parry percentage is
  separately combined because it supplies an explicit per-hit rate. Do not
  display a shield amount without an exact, validated per-hit calculation; the
  current scope intentionally has no shield display.
- The catalogue is intentionally conservative. Rules with verified IDs work on
  every client language; inherited English-name fallbacks are interim coverage
  and need conversion to verified Status IDs before a stable public release.
- A missing or ambiguous correlation leaves the native flytext unchanged.

## Developer requirements

- .NET SDK 10, matching the current Dalamud SDK baseline.
- A Dalamud developer bundle, passed to MSBuild as `DalamudLibPath`. The local
  checkout uses an ignored `.dalamud/dev/` directory populated from the
  official developer bundle.
- A Windows FFXIV/XIVLauncher/Dalamud installation for runtime validation. The
  macOS source workspace can build and run the pure unit tests, but cannot
  prove live game ABI/correlation behaviour.

Build the plugin from this directory:

```sh
/Users/rika/.dotnet/dotnet build DamageInfoPlugin.sln --configuration Debug \
  -p:DalamudLibPath="$PWD/.dalamud/dev/"
```

Run calculator tests:

```sh
/Users/rika/.dotnet/dotnet test DamageInfoExtended.Tests/DamageInfoExtended.Tests.csproj
```

## Runtime validation still required

Enable **Incoming mitigation** and its diagnostic checkbox, then verify the
matrix in the root research document: single hit, same-frame multihit, DoT or
auto, AoE, physical/magical type, Addle/Feint/Reprisal, shields, block/parry,
and unknown type. Capture the diagnostic log if an annotation is missing or
incorrect; do not enable general Damage Info debug logging when privacy is a
concern because upstream general logging may include names.
