using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace DamageInfoPlugin;

internal static partial class IncomingFlyTextFormatter
{
    /// <summary>
    /// Known native flytext forms emitted for a block/parry reduction.  The
    /// supported client languages use either a signed, percentage-first form
    /// (Chinese) or a label-first form (English, Japanese, German, French).
    /// An unrecognised subtitle is retained exactly as provided by the game.
    /// </summary>
    [GeneratedRegex(@"[（(]\s*(?:(?:-\s*)?(?<percent>\d+(?:[\.,]\d+)?)\s*%\s*(?:招架|格挡|受け流し|ブロック)|(?:Parried|Blocked|Pariert|Geblockt|Parade|Blocage|Paré|Parée|Bloqué|Bloquée|受け流し|ブロック)\s*(?<percentAfterLabel>\d+(?:[\.,]\d+)?)\s*%)\s*[）)]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BlockOrParryRegex();

    internal static bool TryExtractBlockOrParry(string? text, out float reduction, out string textWithoutModifier)
    {
        reduction = 0;
        textWithoutModifier = text ?? string.Empty;
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        var match = BlockOrParryRegex().Match(text);
        var percentText = match.Groups["percent"].Success
            ? match.Groups["percent"].Value
            : match.Groups["percentAfterLabel"].Value;
        if (!match.Success ||
            !float.TryParse(percentText.Replace(',', '.'), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var percent) ||
            percent is <= 0 or > 100)
        {
            return false;
        }

        reduction = percent / 100f;
        textWithoutModifier = text.Remove(match.Index, match.Length).Trim();
        return true;
    }

    internal static float CombineReductions(float first, float second)
        => 1f - ((1f - Math.Clamp(first, 0f, 1f)) * (1f - Math.Clamp(second, 0f, 1f)));

    /// <summary>
    /// Formats the calculated rate as a compact integer percentage. Truncate
    /// rather than round so the displayed rate never exceeds the calculation.
    /// </summary>
    internal static string BuildSourceSuffix(float reduction)
    {
        if (reduction <= 0)
            return string.Empty;

        return $" -{MathF.Truncate(reduction * 100):0}%";
    }
}
