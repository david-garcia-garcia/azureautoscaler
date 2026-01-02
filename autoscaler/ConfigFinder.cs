using System.Globalization;

namespace poolautoscaler
{
    public class ConfigFinder
    {
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
