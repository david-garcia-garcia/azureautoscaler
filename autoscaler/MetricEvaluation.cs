using Azure.Monitor.Query;
using Azure.Monitor.Query.Models;
using Microsoft.Extensions.Logging;
using poolautoscaler.strategies;

namespace poolautoscaler
{
    internal class MetricEvaluation
    {
        private ILogger Logger;

        public MetricEvaluation(ILogger logger)
        {
            this.Logger = logger;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="client"></param>
        /// <param name="resourceId"></param>
        /// <param name="metricName"></param>
        /// <param name="timeRange"></param>
        /// <param name="granularity"></param>
        /// <param name="cancellationToken"></param>
        /// <param name="splitName"></param>
        /// <param name="splitValue"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentException"></exception>
        public async Task<List<MetricEvalDtoResultValue>> RetrieveHistory(
            MetricsQueryClient client,
            string resourceId,
            string metricName,
            TimeSpan timeRange,
            TimeSpan granularity,
            CancellationToken cancellationToken,
            string splitName,
            string splitValue,
            IList<MetricAggregationType> aggregations = null
            )
        {
            var timeSeries = await this.RetrieveHistoryRaw(client, resourceId, metricName, timeRange, granularity, cancellationToken, splitName, splitValue, aggregations);
            return timeSeries;
        }

        public async Task<List<MetricEvalDtoResultValue>> RetrieveHistoryRaw(
            MetricsQueryClient client,
            string resourceId,
            string metricName,
            TimeSpan timeRange,
            TimeSpan granularity,
            CancellationToken cancellationToken,
            string splitName,
            string splitValue,
            IList<MetricAggregationType> aggregations = null,
            DateTime? now = null
            )
        {
            aggregations = aggregations ?? new List<MetricAggregationType>() { MetricAggregationType.Average };

            if (timeRange.TotalMinutes < 1)
            {
                throw new ArgumentException("RetrieveHistory: Must be greater than one.", nameof(timeRange));
            }

            if (granularity.TotalMinutes < 1)
            {
                throw new ArgumentException("RetrieveHistory: Must be greater than one.", nameof(granularity));
            }

            if (granularity > timeRange)
            {
                throw new Exception("RetrieveHistory: Granularity must be greater than time range.");
            }

            now = now ?? DateTime.UtcNow;

            // Define the time range in the query options
            var queryOptions = new MetricsQueryOptions
            {
                TimeRange = new QueryTimeRange(
                    new DateTimeOffset(now.Value.AddMinutes(-(timeRange.TotalMinutes + 10)), TimeSpan.Zero),
                    new DateTimeOffset(now.Value, TimeSpan.Zero)),
                Granularity = granularity,
                Aggregations = { }
            };

            foreach (var metricAggregationType in aggregations)
            {
                queryOptions.Aggregations.Add(metricAggregationType);
            }

            // Add dimension if split name is provided
            if (!string.IsNullOrEmpty(splitName))
            {
                queryOptions.Filter = $@"{splitName} eq '{splitValue}'";
            }

            this.Logger.LogTrace("Querying last {0} minutes of {1} data from {2} to {3}",
                timeRange.TotalMinutes,
                metricName,
                queryOptions.TimeRange?.Start?.ToString("s"),
                queryOptions.TimeRange?.End?.ToString("s"));

            // Query the metrics for the specified resource
            var metricsResponse = await client.QueryResourceAsync(
                resourceId,
                new[] { metricName },
                queryOptions,
                cancellationToken);

            // Extract the metric values
            var metric = metricsResponse.Value.Metrics.FirstOrDefault();
            if (metric == null || !metric.TimeSeries.Any())
            {
                this.Logger.LogWarning("No metrics data found for metric {0}.", metricName);
                return [];
            }

            // Calculate the average DTU consumption
            var timeSeries = metric.TimeSeries.FirstOrDefault();
            if (timeSeries == null || !timeSeries.Values.Any())
            {
                this.Logger.LogWarning("No time series found for metric {0}.", metricName);
                return null;
            }

            return timeSeries.Values.Select((i) => new MetricEvalDtoResultValue()
            {
                Average = i.Average,
                Maximum = i.Maximum,
                Minimum = i.Minimum,
                Count = i.Count,
                TimeStamp = i.TimeStamp,
                Total = i.Total
            }).ToList();
        }
    }
}
