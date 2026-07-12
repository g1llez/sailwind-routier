using System.Globalization;
using System.Text.RegularExpressions;

namespace Routier
{
    internal static class CargoDims
    {
        internal static float ParseVolumeCuft(string sizeDescription)
        {
            if (string.IsNullOrWhiteSpace(sizeDescription))
                return 1f;

            var text = sizeDescription.Trim().ToLowerInvariant();
            var m = Regex.Match(text, @"([\d.]+)\s*(?:ft³|ft3|cu\.?\s*ft|cubic\s*ft)");
            if (m.Success && float.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && v > 0f)
                return v;

            m = Regex.Match(text, @"^([\d.]+)\s*(?:ft|')?\s*$");
            if (m.Success && float.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out v) && v > 0f)
                return v;

            var nums = Regex.Matches(text, @"[\d.]+");
            if (nums.Count >= 3 &&
                float.TryParse(nums[0].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var a) &&
                float.TryParse(nums[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var b) &&
                float.TryParse(nums[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var c))
                return a * b * c;

            if (nums.Count == 1 &&
                float.TryParse(nums[0].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out v) && v > 0f)
                return v;

            return 1f;
        }
    }
}
