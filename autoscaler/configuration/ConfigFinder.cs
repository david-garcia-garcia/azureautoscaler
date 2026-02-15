using System.Globalization;

namespace poolautoscaler.configuration
{
    /// <summary>Checks if a scaling configuration is active for a given date/time.</summary>
    public class ConfigFinder
    {
        /// <summary>Returns true if the configuration's time window includes the given UTC time.</summary>
        /// <param name="config">The scaling configuration.</param>
        /// <param name="dateTimeUtc">The UTC date/time to check.</param>
        /// <returns>True if the configuration is active at the given time.</returns>
        public bool SettingIsActive(ScalingConfiguration config, DateTime dateTimeUtc)
        {
            TimeZoneInfo timeZone = TimeZoneInfo.FindSystemTimeZoneById(config.TimeWindow.TimeZone);
            DateTime dateTimeInTimeZone = TimeZoneInfo.ConvertTimeFromUtc(dateTimeUtc, timeZone);

            if (this.CheckTimeWindowMatchesDateDay(config.TimeWindow, dateTimeInTimeZone)
                && this.CheckTimeWindowMatchesDateMonth(config.TimeWindow, dateTimeInTimeZone)
                && this.CheckTimeWindowMatchesTime(config.TimeWindow, dateTimeInTimeZone.TimeOfDay))
            {
                return true;
            }

            return false;
        }

        /// <summary>Returns true if the time-of-day falls within the window's start/end.</summary>
        /// <param name="window">The time window configuration.</param>
        /// <param name="timeOfDay">The time of day to check.</param>
        /// <returns>True if the time is within the window.</returns>
        public bool CheckTimeWindowMatchesTime(TimeWindow window, TimeSpan timeOfDay)
        {
            if (window.EndTimeParsed > window.StartTimeParsed)
            {
                if (window.StartTimeParsed <= timeOfDay && window.EndTimeParsed >= timeOfDay)
                {
                    return true;
                }
            }
            else
            {
                if (window.StartTimeParsed <= timeOfDay || window.EndTimeParsed >= timeOfDay)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Returns true if the date's month matches the window's months.</summary>
        /// <param name="window">The time window configuration.</param>
        /// <param name="currentDate">The date to check.</param>
        /// <returns>True if the month matches.</returns>
        public bool CheckTimeWindowMatchesDateMonth(TimeWindow window, DateTime currentDate)
        {
            var timeWindowMonths = window.Months;

            if (timeWindowMonths == "All" || string.IsNullOrWhiteSpace(timeWindowMonths))
            {
                return true;
            }
            else if (int.TryParse(timeWindowMonths, out var parsedMonth)) // Add this condition to check if the timeWindowDays matches the pattern dd/MM
            {
                if (parsedMonth == currentDate.Month)
                {
                    return true;
                }
            }
            else if (timeWindowMonths.Contains(","))
            {
                var days = timeWindowMonths.Split(',').Select((i) => i.Trim());
                return days.Contains(currentDate.ToString("MMMM", CultureInfo.CreateSpecificCulture("en")));
            }

            throw new ArgumentException($@"Invalid month {timeWindowMonths}");
        }

        /// <summary>Returns true if the date's day/weekday matches the window's days.</summary>
        /// <param name="window">The time window configuration.</param>
        /// <param name="currentDate">The date to check.</param>
        /// <returns>True if the day matches.</returns>
        public bool CheckTimeWindowMatchesDateDay(TimeWindow window, DateTime currentDate)
        {
            var timeWindowDays = window.Days;

            if (timeWindowDays == "All" || string.IsNullOrWhiteSpace(timeWindowDays))
            {
                return true;
            }
            else if (timeWindowDays == "Weekday")
            {
                return currentDate.DayOfWeek >= DayOfWeek.Monday && currentDate.DayOfWeek <= DayOfWeek.Friday;
            }
            else if (timeWindowDays == "Weekend")
            {
                return currentDate.DayOfWeek == DayOfWeek.Saturday || currentDate.DayOfWeek == DayOfWeek.Sunday;
            }
            else if (int.TryParse(timeWindowDays, out var parsedDay)) // Add this condition to check if the timeWindowDays matches the pattern dd/MM
            {
                if (parsedDay == currentDate.Day)
                {
                    return true;
                }
            }
            else if (timeWindowDays.Contains(","))
            {
                var days = timeWindowDays.Split(',').Select((i) => i.Trim()); ;
                return days.Contains(currentDate.DayOfWeek.ToString());
            }

            throw new ArgumentException($@"Invalid day of week {timeWindowDays}");
        }
    }
}
