using System.Text.RegularExpressions;

namespace poolautoscaler.utils
{
    public static class DurationParser
    {
        private static readonly Regex DurationRegex = new Regex(
            @"^(?<value>\d+)(?<unit>[smhd])$", RegexOptions.IgnoreCase);

        public static TimeSpan ParseDuration(string input)
        {
            var match = DurationRegex.Match(input.Trim());

            if (!match.Success)
                throw new FormatException($"Invalid time format: '{input}'");

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
