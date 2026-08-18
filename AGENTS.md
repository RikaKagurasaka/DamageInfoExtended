# Damage Info Extended implementation notes

This is a GPL-3.0 derived work of `perchbirdd/DamageInfoPlugin`, retained at
upstream commit `4c7ed96796e9e6991d2b4a6c5c5e342ad1893bd3`. Preserve the
upstream GPL licence and copyright notices.

The mitigation calculator is adapted from the MIT-licensed Better Deaths
project. Keep its attribution in `THIRD_PARTY_NOTICES.md`; do not add it as a
runtime dependency.

Present mitigation through the original incoming-source subtitle as
`>[source] -28%`; do not reintroduce a standalone `MIT` label. A shield suffix
such as `+1234` requires a verified exact per-hit consumption value, not merely
the client-reported remaining shield percentage. The current scope deliberately
displays no shield quantity.

Native parenthesised block/parry text may be parsed and combined
multiplicatively only when it uses a recognised explicit percentage format:
Chinese `(-15% 招架)` / `(-15% 格挡)`, English `(Parried 15%)` / `(Blocked
20%)`, or the corresponding Japanese, German and French forms. Preserve an
unrecognised subtitle unchanged.

Keep the source and mitigation order configurable: default is `>[source] -28%`,
and the alternative is `-28% >[source]`.

Round every displayed mitigation rate to an integer percentage, whether it is
status-only or includes a recognised native block/parry rate. Use
half-away-from-zero rounding to avoid binary-float under-display of a nominal
whole-percent rate.

Build with `/Users/rika/.dotnet/dotnet`. Package testing must happen in a
running Dalamud/XIVLauncher instance on Windows after unit/build checks pass.
When correlation is uncertain, do not modify the native flytext.
