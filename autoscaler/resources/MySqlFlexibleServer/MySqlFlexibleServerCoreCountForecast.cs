using Azure.Core;
using Azure.Monitor.Query;
using Azure.Monitor.Query.Models;
using Azure.ResourceManager;
using Microsoft.Extensions.Logging;
using poolautoscaler.configuration;
using poolautoscaler.metrics;
using poolautoscaler.resources.MySqlFlexibleServer.Dto;
using poolautoscaler.utils;

namespace poolautoscaler.resources.MySqlFlexibleServer
{
    /**
     * #########################################################
     * Esto es solo una prueba de concepto de como podría ser el algoritmo de
     * predicción de una métrica de un recurso. Esta muy harcodeado para el caso MySQL
     * pero es fácil abstraerlo para cualquier métrica numérica
     * #########################################################
     */
    public class MySqlFlexibleServerCoreCountForecast
    {
        private ILogger Logger { get; set; }

        private string ResourceId { get; set; }

        private MySqlFlexibleServerResourceState Resource { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="MySqlFlexibleServerCoreCountForecast"/> class.
        /// </summary>
        /// <param name="resourceId">Resource ID.</param>
        /// <param name="logger">Logger.</param>
        /// <param name="resource">MySQL flexible server resource state.</param>
        public MySqlFlexibleServerCoreCountForecast(
            string resourceId,
            ILogger logger,
            MySqlFlexibleServerResourceState resource)
        {
            this.Logger = logger;
            this.ResourceId = resourceId;
            this.Resource = resource;
        }

        /// <summary>Computes a custom core count forecast for the resource.</summary>
        /// <param name="client">The ARM client.</param>
        /// <param name="credential">The token credential.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <param name="setting">The scaling configuration.</param>
        /// <returns>The capacity forecast.</returns>
        public async Task<CapacityForecast> CustomCoreCountForecast(
            ArmClient client,
            TokenCredential credential,
            CancellationToken cancellationToken,
            ScalingConfiguration setting)
        {
            // ##########################
            // Parametros para el gobierno del algoritmo
            // ##########################

            // Cuantos días previos analizar para el análisis.
            // En el caso de MySQL flexible server no hay histórico de capacidad efectiva
            // por lo que tiramos de snapshots y máximo hay 16 días en Azure.
            TimeSpan timeRange = TimeSpan.FromDays(16);

            // Granularidad de las ventanas de análisis
            TimeSpan granularity = TimeSpan.FromMinutes(60);

            // El overhead que queremos tener en el pronóstico por encima
            // del consumo real
            double cpuOverheadRequired = 0.3;

            // ##########################
            // END
            // ##########################

            var metricsClient = new MetricsQueryClient(credential);
            var eval = new MetricEvaluation(this.Logger);

            var cpuHistoryResult = await eval.RetrieveHistoryRaw(
                metricsClient,
                this.ResourceId,
                "cpu_percent",
                timeRange,
                granularity,
                cancellationToken,
                null,
                null,
                new List<MetricAggregationType>() { MetricAggregationType.Average, MetricAggregationType.Maximum },

                // Hacer que las métricas empiecen en horas cerradas
                new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, DateTime.UtcNow.Day, DateTime.UtcNow.Hour, 0, 0));
            var cpuHistory = cpuHistoryResult.Values;

            // This is super important in burstable series, because consuming these credits can lead
            // to a total hault. Returns a value between 0 and 1 representing consumed percentage.
            var cpuCreditsRemainingResult = await eval.RetrieveHistoryRaw(
                metricsClient,
                this.ResourceId,
                "cpu_credits_remaining",
                timeRange,
                granularity,
                cancellationToken,
                null,
                null,
                new List<MetricAggregationType>() { MetricAggregationType.Minimum, MetricAggregationType.Maximum, MetricAggregationType.Average },

                // Hacer que las métricas empiecen en horas cerradas
                new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, DateTime.UtcNow.Day, DateTime.UtcNow.Hour, 0, 0));
            var cpuCreditsRemaining = cpuCreditsRemainingResult.Values;

            var cpuCreditsConsumedResult = await eval.RetrieveHistoryRaw(
                metricsClient,
                this.ResourceId,
                "cpu_credits_consumed",
                timeRange,
                granularity,
                cancellationToken,
                null,
                null,
                new List<MetricAggregationType>() { MetricAggregationType.Average },

                // Hacer que las métricas empiecen en horas cerradas
                new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, DateTime.UtcNow.Day, DateTime.UtcNow.Hour, 0, 0));
            var cpuCreditsConsumed = cpuCreditsConsumedResult.Values;

            var now = DateTimeOffset.UtcNow;
            var startDate = now.AddDays(-timeRange.Days);

            List<CapacityWindow> windows = new List<CapacityWindow>();

            // For each day in our analysis period
            for (var date = startDate; date <= now; date = date.AddDays(1))
            {
                // Check if this time window applies to this day
                if (!this.IsTimeWindowApplicable(setting.TimeWindow, date))
                {
                    continue;
                }

                var (startHour, endHour) = this.GetTimeWindowHours(setting.TimeWindow, date);

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

                    // Find the closes metric within 15min of margin
                    var cpuAverage = cpuHistory
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

                    var cpuMax = cpuHistory
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

                    var remainingCredits = cpuCreditsRemaining
                        .Select((i) =>
                            new
                            {
                                metric = i,
                                distance = Math.Abs((i.TimeStamp - pointInTime).TotalMinutes)
                            })
                        .Where((i) => i.distance < 15)
                        .OrderBy((i) => i.distance)
                        .FirstOrDefault()?
                        .metric;

                    // Esta métrica es el ratio de consumo POR MINUTO de créditos
                    var consumedCreditsRate = cpuCreditsConsumed
                        .Select((i) =>
                            new
                            {
                                metric = i,
                                distance = Math.Abs((i.TimeStamp - pointInTime).TotalMinutes)
                            })
                        .Where((i) => i.distance < 15)
                        .OrderBy((i) => i.distance)
                        .FirstOrDefault()?
                        .metric.Average;

                    if (cpuAverage == null)
                    {
                        continue;
                    }

                    try
                    {
                        // We could actually have multiple SKU in the time windows, taking the smallest will yield the most conservative approach.
                        var effectiveSku = this.Resource.GetEffectiveSkuAtPointInTime(pointInTime, useSmallestSku: true);
                        var effectiveCoreCount = MySqlFlexibleServerResourceStateHelper.GetCoreCountFromSkuName(effectiveSku);

                        var hourConsumedCredits2 = consumedCreditsRate * granularity.TotalMinutes;

                        // The most difficult part is to deal with resource saturation, there is no data to predict real usage...

                        // This is not reliable, because a SKU change will affect this and it also includes re-credited quota. Keeping here for the record.
                        var hourConsumedCredits = remainingCredits.Maximum - remainingCredits.Minimum;
                        var minimumCoreCount = 0;
                        bool cpuThrottled = cpuAverage.Value > 90 && cpuMax > 95;

                        if (BurstableVmInfo.IsBurstableSeries(effectiveSku))
                        {
                            var burstableBaseline = BurstableVmInfo.GetBurstableBaselinePerformance(effectiveSku);
                            var burstableMaxCredits = BurstableVmInfo.GetMaxCreditsPerVmSize(effectiveSku);

                            // This part is the most ficticious... we need to account for the fact that when we run out of burstable
                            // credits we "should have" used more CPU than what was available (which would be stuck at the base line), but there
                            // is no way to tell what would have been used (it's something above the baseline, but we don't know how much)

                            // Detecting throttling is very complex... you might run out of credits, yet still run at 100%, so it's a combination
                            // of how close you are to the baseline + how little credits are there left.

                            var throttlingFactorUsage = Math.Exp(-(cpuAverage.Value - burstableBaseline) / 4);
                            var throttlingFactorCredits = Math.Exp(-remainingCredits.Average.Value / 7);

                            if (throttlingFactorUsage > 0.5 && throttlingFactorCredits > 0.5)
                            {
                                // Force Adjust to be on the safe side, but the throttling factor should actually take care of this
                                minimumCoreCount = effectiveCoreCount + 1;

                                var cpuCompensation = (100 - burstableBaseline) * throttlingFactorCredits;
                                cpuAverage = cpuCompensation + cpuAverage;
                                cpuThrottled = true;
                            }
                        }

                        // We cannot know what CPU usage was really... above 100%.
                        if (cpuAverage.Value > 90 || cpuThrottled)
                        {
                            cpuAverage = cpuAverage * 1.25;
                        }

                        var cpuUsage = cpuAverage.Value / 100;

                        var usedCores = (cpuUsage * effectiveCoreCount);

                        var recommendedCoreCount = Math.Ceiling((decimal)(usedCores) * (decimal)(1 + cpuOverheadRequired));
                        if (recommendedCoreCount < minimumCoreCount)
                        {
                            recommendedCoreCount = minimumCoreCount;
                        }

                        window.CapacitySamples.Add(new CapacitySample
                        {
                            UsedMillicores = (cpuAverage / 100) * effectiveCoreCount * 1000,
                            RecommendedMillicores = (cpuAverage / 100) * effectiveCoreCount * 1000 * (1 + cpuOverheadRequired),
                            EffectiveAverageUsage = cpuUsage,
                            EffectiveValue = effectiveCoreCount,
                            RecommendedValue = (double)recommendedCoreCount,
                            Timestamp = pointInTime,
                        });
                    }
                    catch (InvalidOperationException ex)
                    {
                        // Log that we don't have history for this point in time
                        this.Logger.LogTrace(
                            "Unable to forecast for timestamp {Timestamp}: {Message}",
                            pointInTime,
                            ex.Message);
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
                    this.Logger.LogDebug($"Insufficient data to forecast load for day of week {d}");
                    continue;
                }

                var forecastedCapacityValue =
                    compatibleWindows
                        .Select((i) => i.MaxRecommendedCapacity)
                        .Max();

                // Esto nos da el PICO de consumo en una hora (la de más faena)
                var maxConsumedMillicoresPerHour = (int)compatibleWindows
                    .Select((i) => i.CapacitySamples.Max((i) => i.RecommendedMillicores))
                    .Max();

                var averageConsumedMillicoresPerHour = (int)compatibleWindows
                    .Select((i) => i.CapacitySamples.Average((i) => i.RecommendedMillicores))
                    .Max();

                // Let's find what SKU matches minimum this number of CPU requests, which we will need to adjust to ensure (in case of being in burstable series)
                var minSku = MySqlFlexibleServerResourceStateHelper.GetMinimumSkuThatSatisfiesCoreCount((int)Math.Ceiling((decimal)forecastedCapacityValue.Value), "Burstable", averageConsumedMillicoresPerHour);

                f.ForecastString.Add(d, minSku);

                this.Logger.LogDebug("Forecasted required SKU for {0} is {1}", d, minSku);
            }

            return f;
        }

        private double CalculateAffinity(DayOfWeek date1, DayOfWeek date2)
        {
            // If it's not in the time window at all, no affinity
            bool isCurrentWeekday = this.IsWeekday(date1);
            bool isSampleWeekday = this.IsWeekday(date2);

            // Exact day match has highest affinity
            if (date1 == date2)
            {
                return 1.0;
            }

            // Weekday to weekday or weekend to weekend has medium affinity
            if (isCurrentWeekday && isSampleWeekday)
            {
                return 0.3;
            }

            if (!isCurrentWeekday && !isSampleWeekday)
            {
                return 0.3;
            }

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
            if (window.Days == "All")
            {
                return true;
            }

            if (window.Days == "Weekday" && date.DayOfWeek >= DayOfWeek.Monday && date.DayOfWeek <= DayOfWeek.Friday)
            {
                return true;
            }

            if (window.Days == "Weekend" && (date.DayOfWeek == DayOfWeek.Saturday || date.DayOfWeek == DayOfWeek.Sunday))
            {
                return true;
            }

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
                     TimeZoneInfo.ConvertTimeToUtc(new DateTime(date.Year, date.Month, date.Day, window.EndTimeParsed.Value.Hours, window.EndTimeParsed.Value.Minutes, 0, 0), window.TimeZoneParsed));
            }
            else
            {
                return (
                    TimeZoneInfo.ConvertTimeToUtc(new DateTime(date.Year, date.Month, date.Day, window.StartTimeParsed.Value.Hours, window.StartTimeParsed.Value.Minutes, 0, 0).AddDays(-1), window.TimeZoneParsed),
                       TimeZoneInfo.ConvertTimeToUtc(new DateTime(date.Year, date.Month, date.Day, window.EndTimeParsed.Value.Hours, window.EndTimeParsed.Value.Minutes, 0, 0), window.TimeZoneParsed));
            }
        }
    }
}
