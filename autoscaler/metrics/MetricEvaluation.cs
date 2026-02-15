using Azure.Monitor.Query;
using Azure.Monitor.Query.Models;
using Microsoft.Extensions.Logging;
using poolautoscaler.metrics.Dto;

namespace poolautoscaler.metrics
{
    /// <summary>Retrieves and evaluates metric history from Azure Monitor.</summary>
    internal class MetricEvaluation
    {
        private ILogger Logger;

        /// <summary>Initializes a new instance of the <see cref="MetricEvaluation"/> class.</summary>
        /// <param name="logger">The logger instance.</param>
        public MetricEvaluation(ILogger logger)
        {
            this.Logger = logger;
        }

        /// <summary>Retrieves metric history from Azure Monitor for the given resource and metric.</summary>
        /// <param name="client">The metrics query client.</param>
        /// <param name="resourceId">The resource identifier.</param>
        /// <param name="metricName">The metric name.</param>
        /// <param name="timeRange">The time range to query.</param>
        /// <param name="granularity">The aggregation granularity.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <param name="splitName">Optional dimension name to filter by.</param>
        /// <param name="splitValue">Optional dimension value to filter by.</param>
        /// <param name="aggregations">Optional aggregation types.</param>
        /// <returns>The evaluation result containing metric data.</returns>
        /// <exception cref="ArgumentException">Thrown when arguments are invalid.</exception>
        public async Task<MetricEvalDtoResult> RetrieveHistory(
            MetricsQueryClient client,
            string resourceId,
            string metricName,
            TimeSpan timeRange,
            TimeSpan granularity,
            CancellationToken cancellationToken,
            string splitName,
            string splitValue,
            IList<MetricAggregationType> aggregations = null)
        {
            var result = await this.RetrieveHistoryRaw(client, resourceId, metricName, timeRange, granularity, cancellationToken, splitName, splitValue, aggregations);
            return result;
        }

        /// <summary>Fetches raw metric history from Azure Monitor.</summary>
        /// <param name="client">The metrics query client.</param>
        /// <param name="resourceId">The resource identifier.</param>
        /// <param name="metricName">The metric name.</param>
        /// <param name="timeRange">The time range to query.</param>
        /// <param name="granularity">The aggregation granularity.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <param name="splitName">Optional dimension name to filter by.</param>
        /// <param name="splitValue">Optional dimension value to filter by.</param>
        /// <param name="aggregations">Optional aggregation types.</param>
        /// <param name="now">Optional reference time (for testing).</param>
        /// <returns>Metric result with time series data.</returns>
        public async Task<MetricEvalDtoResult> RetrieveHistoryRaw(
            MetricsQueryClient client,
            string resourceId,
            string metricName,
            TimeSpan timeRange,
            TimeSpan granularity,
            CancellationToken cancellationToken,
            string splitName,
            string splitValue,
            IList<MetricAggregationType> aggregations = null,
            DateTime? now = null)
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

            this.Logger.LogTrace(
                "Querying last {0} minutes of {1} data from {2} to {3}",
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
                return new MetricEvalDtoResult
                {
                    Values = new List<MetricEvalDtoResultValue>(),
                    ExecutedAggregations = aggregations
                };
            }

            // Calculate the average DTU consumption
            var timeSeries = metric.TimeSeries.FirstOrDefault();
            if (timeSeries == null || !timeSeries.Values.Any())
            {
                this.Logger.LogWarning("No time series found for metric {0}.", metricName);
                return new MetricEvalDtoResult
                {
                    Values = new List<MetricEvalDtoResultValue>(),
                    ExecutedAggregations = aggregations
                };
            }

            var values = timeSeries.Values.Select((i) => new MetricEvalDtoResultValue()
            {
                Average = i.Average,
                Maximum = i.Maximum,
                Minimum = i.Minimum,
                Count = i.Count,
                TimeStamp = i.TimeStamp,
                Total = i.Total

                // Note: Default is populated in RetrieveHistory() based on executed aggregations
            }).ToList();

            return new MetricEvalDtoResult
            {
                Values = values,
                ExecutedAggregations = aggregations
            };
        }
    }
}
