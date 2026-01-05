using Azure.Core;
using Azure.Monitor.Query;
using Azure.Monitor.Query.Models;
using Azure.ResourceManager;
using Microsoft.Extensions.Logging;
using poolautoscaler.utils;

namespace poolautoscaler.strategies
{
    /// <summary>
    /// Generalized forecast engine that can work with any dimension by using configurable metrics.
    /// Supports either percentage-based or absolute value-based forecasting.
    /// </summary>
    public class ForecastEngine
    {
        private ILogger Logger { get; set; }
        private string ResourceId { get; set; }

        /// <summary>
        /// Initializes a new instance of the ForecastEngine
        /// </summary>
        /// <param name="resourceId">The Azure resource ID</param>
        /// <param name="logger">Logger instance</param>
        public ForecastEngine(
            string resourceId,
            ILogger logger)
        {
            this.Logger = logger;
            this.ResourceId = resourceId;
        }

        /// <summary>
        /// Generates a capacity forecast based on historical metrics
        /// </summary>
        /// <param name="client">ARM client</param>
        /// <param name="credential">Token credential</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <param name="setting">Scaling configuration</param>
        /// <param name="rule">Scaling rule with forecast configuration</param>
        /// <returns>Capacity forecast</returns>
        public async Task<CapacityForecast> GenerateForecast(
            ArmClient client,
            TokenCredential credential,
            CancellationToken cancellationToken,
            ScalingConfiguration setting,
            ScalingRule rule)
        {
            // Validate forecast configuration
            ValidateForecastConfiguration(rule);

            // ##########################
            // Parameters for algorithm governance
            // ##########################

            // How many previous days to analyze
            // Default to 16 days (Azure snapshot limit for some resources)
            TimeSpan timeRange = TimeSpan.FromDays(16);

            // Granularity of analysis windows
            TimeSpan granularity = TimeSpan.FromMinutes(60);

            // Overhead we want to have in the forecast above actual consumption
            double overheadRequired = 0.3;

            // ##########################
            // END
            // ##########################

            var metricsClient = new MetricsQueryClient(credential);
            var eval = new MetricEvaluation(this.Logger);

            // Determine which metrics to use
            bool usePercentage = !string.IsNullOrEmpty(rule.MetricDimensionUsedPercentage);
            string usedMetricName = usePercentage ? rule.MetricDimensionUsedPercentage : rule.MetricDimensionUsedTotal;
            string totalMetricName = rule.MetricDimensionTotal;

            if (usePercentage && !string.IsNullOrEmpty(rule.MetricDimensionUsedTotal))
            {
                this.Logger.LogWarning(
                    "Both MetricDimensionUsedPercentage and MetricDimensionUsedTotal are specified for rule '{0}'. Using percentage metric '{1}' and ignoring '{2}'.",
                    rule.Id,
                    rule.MetricDimensionUsedPercentage,
                    rule.MetricDimensionUsedTotal);
            }

            // Retrieve total available value history
            var totalHistory = await eval.RetrieveHistoryRaw(
                metricsClient,
                this.ResourceId,
                totalMetricName,
                timeRange,
                granularity,
                cancellationToken,
                null,
                null,
                new List<MetricAggregationType>() { MetricAggregationType.Average, MetricAggregationType.Maximum },
                new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, DateTime.UtcNow.Day, DateTime.UtcNow.Hour, 0, 0)
            );

            // Retrieve usage history (percentage or absolute)
            var usedHistory = await eval.RetrieveHistoryRaw(
                metricsClient,
                this.ResourceId,
                usedMetricName,
                timeRange,
                granularity,
                cancellationToken,
                null,
                null,
                new List<MetricAggregationType>() { MetricAggregationType.Average, MetricAggregationType.Maximum },
                new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, DateTime.UtcNow.Day, DateTime.UtcNow.Hour, 0, 0)
            );

            var now = DateTimeOffset.UtcNow;
            var startDate = now.AddDays(-timeRange.Days);

            List<CapacityWindow> windows = new List<CapacityWindow>();

            // For each day in our analysis period
            for (var date = startDate; date <= now; date = date.AddDays(1))
            {
                // Check if this time window applies to this day
                if (!IsTimeWindowApplicable(setting.TimeWindow, date))
                    continue;

                var (startHour, endHour) = GetTimeWindowHours(setting.TimeWindow, date);

                CapacityWindow window = new CapacityWindow();
                window.CapacitySamples = new List<CapacitySample>();
                window.Day = date;
                window.StartTimestmap = startHour;
                window.Duration = (endHour - startHour);
                windows.Add(window);

                // Build as many slots as can fit in the window
                for (int x = 0; x < window.Duration.TotalMinutes; x = x + (int)granularity.TotalMinutes)
                {
                    var pointInTime = window.StartTimestmap.AddMinutes(x);

                    // Find the closest metric within 15min of margin
                    var totalAverage = totalHistory
                        .Select((i) =>
                            new
                            {
                                metric = i,
                                distance = Math.Abs((i.TimeStamp - pointInTime).TotalMinutes)
                            })
                        .Where((i) => i.distance < 15)
                        .OrderBy((i) => i.distance)
                        .FirstOrDefault()?
                        .metric
                        .Average;

                    var totalMax = totalHistory
                        .Select((i) =>
                            new
                            {
                                metric = i,
                                distance = Math.Abs((i.TimeStamp - pointInTime).TotalMinutes)
                            })
                        .Where((i) => i.distance < 15)
                        .OrderBy((i) => i.distance)
                        .FirstOrDefault()?
                        .metric
                        .Maximum;

                    var usedAverage = usedHistory
                        .Select((i) =>
                            new
                            {
                                metric = i,
                                distance = Math.Abs((i.TimeStamp - pointInTime).TotalMinutes)
                            })
                        .Where((i) => i.distance < 15)
                        .OrderBy((i) => i.distance)
                        .FirstOrDefault()?
                        .metric
                        .Average;

                    var usedMax = usedHistory
                        .Select((i) =>
                            new
                            {
                                metric = i,
                                distance = Math.Abs((i.TimeStamp - pointInTime).TotalMinutes)
                            })
                        .Where((i) => i.distance < 15)
                        .OrderBy((i) => i.distance)
                        .FirstOrDefault()?
                        .metric
                        .Maximum;

                    if (totalAverage == null || usedAverage == null)
                    {
                        continue;
                    }

                    try
                    {
                        // Calculate effective usage
                        double effectiveUsage;
                        double effectiveTotal;

                        if (usePercentage)
                        {
                            // Percentage is already 0-100, convert to 0-1
                            effectiveUsage = usedAverage.Value / 100.0;
                            effectiveTotal = totalAverage.Value;
                        }
                        else
                        {
                            // Absolute values: calculate percentage
                            effectiveTotal = totalAverage.Value;
                            effectiveUsage = effectiveTotal > 0 ? usedAverage.Value / effectiveTotal : 0;
                        }

                        // Apply overhead compensation for high usage
                        if (effectiveUsage > 0.90 || (usePercentage && usedAverage.Value > 90))
                        {
                            effectiveUsage = effectiveUsage * 1.25;
                        }

                        // Calculate recommended capacity
                        var usedCapacity = effectiveUsage * effectiveTotal;
                        var recommendedCapacity = Math.Ceiling(usedCapacity * (1 + overheadRequired));

                        // Ensure we don't go below minimum or above maximum if specified
                        if (!string.IsNullOrEmpty(rule.DimensionValueMin))
                        {
                            if (double.TryParse(rule.DimensionValueMin, out var minValue))
                            {
                                if (recommendedCapacity < minValue)
                                {
                                    recommendedCapacity = minValue;
                                }
                            }
                        }

                        if (!string.IsNullOrEmpty(rule.DimensionValueMax))
                        {
                            if (double.TryParse(rule.DimensionValueMax, out var maxValue))
                            {
                                if (recommendedCapacity > maxValue)
                                {
                                    recommendedCapacity = maxValue;
                                }
                            }
                        }

                        window.CapacitySamples.Add(new CapacitySample
                        {
                            UsedMillicores = usedCapacity * 1000, // Convert to millicores for consistency
                            RecommendedMillicores = recommendedCapacity * 1000,
                            EffectiveAverageUsage = effectiveUsage,
                            EffectiveValue = effectiveTotal,
                            RecommendedValue = recommendedCapacity,
                            Timestamp = pointInTime,
                        });
                    }
                    catch (Exception ex)
                    {
                        // Log that we don't have history for this point in time
                        this.Logger.LogTrace(
                            "Unable to forecast for timestamp {Timestamp}: {Message}",
                            pointInTime,
                            ex.Message
                        );
                        continue;
                    }
                }
            }

            // Let's print some statistics about the forecast
            this.Logger.LogDebug("Forecast for {0}", setting.Id);
            this.Logger.LogDebug("Analyzed {0} days of data", windows.Count);

            // Let's now digest metrics for each day
            foreach (var w in windows)
            {
                w.MaxEffectiveCapacity = w.CapacitySamples.OrderBy((i) => i.EffectiveValue).LastOrDefault()?.EffectiveValue;
                w.MaxRecommendedCapacity = w.CapacitySamples.OrderBy((i) => i.RecommendedValue).LastOrDefault()?.RecommendedValue;
                w.WindowCoveragePercent = (int)((w.CapacitySamples.Count * 100) / w.Duration.TotalHours);
                this.Logger.LogDebug("Window analysis {0} - {1}", w.Day, w.Day.DayOfWeek);
                this.Logger.LogDebug("   Duration {0}", w.Duration);
                this.Logger.LogDebug("   WindowStart UTC {0}", w.StartTimestmap);
                this.Logger.LogDebug("   WindowEnd UTC {0}", w.StartTimestmap.Add(w.Duration));
                this.Logger.LogDebug("   MaxEffectiveCapacity {0}", w.MaxEffectiveCapacity);
                this.Logger.LogDebug("   MaxRecommendedCapacity {0}", w.MaxRecommendedCapacity);
                this.Logger.LogDebug("   WindowCoveragePercent {0}", w.WindowCoveragePercent);
            }

            // Now produce a daily forecast for this scale setting
            var f = new CapacityForecast();
            f.ExpiresAt = DateTime.UtcNow.AddDays(2);
            f.ForecastString = new Dictionary<DayOfWeek, string>();
            f.Forecast = new Dictionary<DayOfWeek, double>();

            // We generate a forecast profile for every day of the week
            List<DayOfWeek> days = new List<DayOfWeek>()
            {
                DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday,
                DayOfWeek.Saturday, DayOfWeek.Sunday
            };

            foreach (var d in days)
            {
                // Add affinity to all windows
                foreach (var capacityWindow in windows)
                {
                    capacityWindow.Affinity = this.CalculateAffinity(d, capacityWindow.Day.DayOfWeek);
                }

                var compatibleWindows = windows.Where((i) => i.Affinity > 0.5).ToList();

                if (!compatibleWindows.Any((i) => i.CapacitySamples.Any()))
                {
                    f.ForecastString.Add(d, null);
                    f.Forecast.Add(d, 0);
                    this.Logger.LogDebug($"Insufficient data to forecast load for day of week {d}");
                    continue;
                }

                var forecastedCapacityValue =
                    compatibleWindows
                        .Select((i) => i.MaxRecommendedCapacity)
                        .Max();

                if (forecastedCapacityValue.HasValue && forecastedCapacityValue.Value > 0)
                {
                    f.Forecast.Add(d, forecastedCapacityValue.Value);
                    f.ForecastString.Add(d, forecastedCapacityValue.Value.ToString("0"));
                }
                else
                {
                    f.Forecast.Add(d, 0);
                    f.ForecastString.Add(d, null);
                }

                this.Logger.LogDebug("Forecasted required capacity for {0} is {1}", d, forecastedCapacityValue);
            }

            return f;
        }

        private void ValidateForecastConfiguration(ScalingRule rule)
        {
            if (string.IsNullOrEmpty(rule.MetricDimensionTotal))
            {
                throw new ArgumentException(
                    $"MetricDimensionTotal is required for Forecast strategy in rule '{rule.Id}'.");
            }

            if (string.IsNullOrEmpty(rule.MetricDimensionUsedPercentage) &&
                string.IsNullOrEmpty(rule.MetricDimensionUsedTotal))
            {
                throw new ArgumentException(
                    $"Either MetricDimensionUsedPercentage or MetricDimensionUsedTotal must be specified for Forecast strategy in rule '{rule.Id}'.");
            }
        }

        private double CalculateAffinity(DayOfWeek date1, DayOfWeek date2)
        {
            // If it's not in the time window at all, no affinity
            bool isCurrentWeekday = IsWeekday(date1);
            bool isSampleWeekday = IsWeekday(date2);

            // Exact day match has highest affinity
            if (date1 == date2)
                return 1.0;

            // Weekday to weekday or weekend to weekend has medium affinity
            if (isCurrentWeekday && isSampleWeekday)
                return 0.3;

            if (!isCurrentWeekday && !isSampleWeekday)
                return 0.3;

            // Different day types have no affinity
            return 0;
        }

        private bool IsWeekday(DayOfWeek day)
        {
            return day >= DayOfWeek.Monday && day <= DayOfWeek.Friday;
        }

        private bool IsTimeWindowApplicable(TimeWindow window, DateTimeOffset date)
        {
            // Check if this day falls within the time window configuration
            if (window.Days == "All") return true;
            if (window.Days == "Weekday" && date.DayOfWeek >= DayOfWeek.Monday && date.DayOfWeek <= DayOfWeek.Friday) return true;
            if (window.Days == "Weekend" && (date.DayOfWeek == DayOfWeek.Saturday || date.DayOfWeek == DayOfWeek.Sunday)) return true;

            if (window.Days.Contains(","))
            {
                var days = window.Days.Split(',').Select(d => d.Trim());
                return days.Contains(date.DayOfWeek.ToString());
            }

            return false;
        }

        private (DateTimeOffset StartHour, DateTimeOffset EndHour) GetTimeWindowHours(TimeWindow window, DateTimeOffset date)
        {
            if (window.EndTimeParsed > window.StartTimeParsed)
            {
                return (
                     TimeZoneInfo.ConvertTimeToUtc(new DateTime(date.Year, date.Month, date.Day, window.StartTimeParsed.Value.Hours, window.StartTimeParsed.Value.Minutes, 0, 0), window.TimeZoneParsed),
                     TimeZoneInfo.ConvertTimeToUtc(new DateTime(date.Year, date.Month, date.Day, window.EndTimeParsed.Value.Hours, window.EndTimeParsed.Value.Minutes, 0, 0), window.TimeZoneParsed)
                    );
            }
            else
            {
                return (
                    TimeZoneInfo.ConvertTimeToUtc(new DateTime(date.Year, date.Month, date.Day, window.StartTimeParsed.Value.Hours, window.StartTimeParsed.Value.Minutes, 0, 0).AddDays(-1), window.TimeZoneParsed),
                       TimeZoneInfo.ConvertTimeToUtc(new DateTime(date.Year, date.Month, date.Day, window.EndTimeParsed.Value.Hours, window.EndTimeParsed.Value.Minutes, 0, 0), window.TimeZoneParsed)
                );
            }
        }
    }

    public class CapacityWindow
    {
        public double? MaxRecommendedCapacity { get; set; }

        public double? MaxEffectiveCapacity { get; set; }

        public DateTimeOffset Day { get; set; }

        public List<CapacitySample> CapacitySamples { get; set; }

        public int WindowCoveragePercent { get; set; }

        public DateTimeOffset StartTimestmap { get; set; }

        public TimeSpan Duration { get; set; }

        public double Affinity { get; set; }
    }

    public class CapacitySample
    {
        public double? UsedMillicores { get; set; }

        public double? RecommendedMillicores { get; set; }

        public double? EffectiveAverageUsage { get; set; }

        public double? EffectiveValue { get; set; }

        public double? RecommendedValue { get; set; }

        public DateTimeOffset Timestamp { get; set; }
    }

    public class CapacityForecast
    {
        /// <summary>
        /// A capacity forecast for each day of the week (numeric values)
        /// </summary>
        public Dictionary<DayOfWeek, double> Forecast { get; set; }

        /// <summary>
        /// A capacity forecast for each day of the week (string values)
        /// </summary>
        public Dictionary<DayOfWeek, string> ForecastString { get; set; }

        public DateTime ExpiresAt { get; set; }
    }
}
