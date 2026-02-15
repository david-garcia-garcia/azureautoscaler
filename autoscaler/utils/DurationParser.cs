using System.Text.RegularExpressions;

namespace poolautoscaler.utils
{
    /// <summary>Parses duration strings (e.g. "4m", "1h").</summary>
    public static class DurationParser
    {
        private static readonly Regex DurationRegex = new Regex(
            @"^(?<value>\d+)(?<unit>[smhd])$", RegexOptions.IgnoreCase);

        /// <summary>Parses a duration string (value + unit: s, m, h, d).</summary>
        /// <param name="input">The duration string (e.g. "4m", "1h").</param>
        /// <returns>Parsed time span.</returns>
        public static TimeSpan ParseDuration(string input)
        {
            var match = DurationRegex.Match(input.Trim());

            if (!match.Success)
            {
                throw new FormatException($"Invalid time format: '{input}'");
            }

            int value = int.Parse(match.Groups["value"].Value);
            string unit = match.Groups["unit"].Value.ToLower();

            return unit switch
            {
                "s" => TimeSpan.FromSeconds(value),
                "m" => TimeSpan.FromMinutes(value),
                "h" => TimeSpan.FromHours(value),
                "d" => TimeSpan.FromDays(value),
                _ => throw new FormatException($"Unsupported time unit: '{unit}'"),
            };
        }
    }
}
