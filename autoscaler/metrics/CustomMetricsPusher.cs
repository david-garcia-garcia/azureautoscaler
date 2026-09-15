using System.Text;
using System.Text.Json;
using Azure.Core;
using Azure.ResourceManager;
using Microsoft.Extensions.Logging;
using poolautoscaler.configuration;
using poolautoscaler.metrics.Dto;
using poolautoscaler.resourcemanagement;

namespace poolautoscaler.metrics
{
    /// <summary>
    /// Pushes custom metrics to Azure Monitor for consumption (dashboards, alerts).
    /// Uses the Azure Monitor custom metrics REST API.
    /// </summary>
    /// <remarks>
    /// Requires the identity (managed identity or service principal) to have
    /// <c>Monitoring Metrics Publisher</c> role on the target resource(s). 403 Forbidden means the identity
    /// lacks this role—assign it at the VMSS, resource group, or subscription scope.
    /// See https://learn.microsoft.com/en-us/azure/azure-monitor/metrics/metrics-store-custom-rest-api.
    /// </remarks>
    internal sealed class CustomMetricsPusher
    {
        /// <summary>Scope for Azure Monitor (use .default for token requests).</summary>
        private const string MonitoringScope = "https://monitoring.azure.com/.default";
        private const string DefaultMetricNamespace = "Custom Autoscaler";

        private readonly TokenCredential credential;
        private readonly ArmClient armClient;
        private readonly ILogger logger;
        private readonly HttpClient httpClient;
        private readonly IResourceLocationResolver resourceLocationResolver;
        private readonly string? defaultCustomMetricsNamespace;
        private readonly SqlQuerySessionFactory sqlQuerySessionFactory;

        /// <summary>Per (resourceState.ResourceId, metricName) last push time.</summary>
        private readonly Dictionary<string, DateTime> lastPushTimes = new();

        /// <summary>Initializes a new instance of the <see cref="CustomMetricsPusher"/> class.</summary>
        /// <param name="credential">The token credential for Azure Monitor.</param>
        /// <param name="armClient">The ARM client.</param>
        /// <param name="logger">The logger.</param>
        /// <param name="resourceLocationResolver">Resolves resource IDs to region for the metrics endpoint.</param>
        /// <param name="defaultCustomMetricsNamespace">Optional global default namespace for custom metrics.</param>
        /// <param name="sqlQuerySessionFactory">SQL Query session factory. Tests inject a factory with an in-memory reader.</param>
        /// <param name="metricsHttpHandler">Optional HTTP handler for the Azure Monitor POST. Tests capture requests here.</param>
        public CustomMetricsPusher(
            TokenCredential credential,
            ArmClient armClient,
            ILogger logger,
            IResourceLocationResolver resourceLocationResolver,
            string? defaultCustomMetricsNamespace = null,
            SqlQuerySessionFactory? sqlQuerySessionFactory = null,
            HttpMessageHandler? metricsHttpHandler = null)
        {
            this.credential = credential;
            this.armClient = armClient;
            this.logger = logger;
            this.httpClient = metricsHttpHandler == null
                ? new HttpClient { Timeout = TimeSpan.FromSeconds(30) }
                : new HttpClient(metricsHttpHandler) { Timeout = TimeSpan.FromSeconds(30) };
            this.resourceLocationResolver = resourceLocationResolver;
            this.defaultCustomMetricsNamespace = defaultCustomMetricsNamespace;
            this.sqlQuerySessionFactory = sqlQuerySessionFactory ?? new SqlQuerySessionFactory();
        }

        /// <summary>
        /// Pushes custom metrics for a resource if configured and due.
        /// Call after Refresh; only pushes when each metric's Frequency has elapsed.
        /// </summary>
        /// <param name="state">The resource state.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A task that completes when the push is done.</returns>
        public async Task PushIfDueAsync(
            ResourceState state,
            CancellationToken cancellationToken)
        {
            if (state.Configuration.CustomMetrics == null || !state.Configuration.CustomMetrics.Any())
            {
                return;
            }

            CustomMetricDataContext? context = null;
            var metricIndex = 0;
            foreach (var metric in state.Configuration.CustomMetrics)
            {
                var hasQuery = !string.IsNullOrWhiteSpace(metric.Query);
                var key = hasQuery
                    ? $"{state.AzureResourceId}|query|{metricIndex}"
                    : $"{state.Resource?.Id}|{metric.Name}";
                metricIndex++;

                if (this.lastPushTimes.TryGetValue(key, out var last) &&
                    (DateTime.UtcNow - last) < metric.FrequencyParsed)
                {
                    continue;
                }

                try
                {
                    if (hasQuery)
                    {
                        await this.PushQueryMetricAsync(state, metric, key, cancellationToken);
                        continue;
                    }

                    context ??= await state.BuildCustomMetricDataContextAsync(
                        this.armClient,
                        this.credential,
                        cancellationToken);
                    if (context == null)
                    {
                        continue;
                    }

                    var value = EvaluateExpression(metric, context);
                    if (value == null)
                    {
                        continue;
                    }

                    var numericValue = ConvertToDouble(value);
                    await this.PushNamedMetricAsync(state, metric, CustomMetricSeries.FromScalar(metric.Name, numericValue), cancellationToken);
                    this.lastPushTimes[key] = DateTime.UtcNow;
                }
                catch (Exception ex)
                {
                    var failedName = hasQuery ? $"query[{metricIndex - 1}]" : metric.Name;
                    state.Logger.LogError(
                        ex,
                        "Failed to push custom metric {Name} on {ResourceId}: {Message}",
                        failedName,
                        state.AzureResourceId,
                        ex.Message);
                }
            }
        }

        /// <summary>
        /// Runs one Query, then POSTs each mapped series. Empty or all-non-numeric results skip publication.
        /// </summary>
        /// <param name="state">Refreshed resource state.</param>
        /// <param name="metric">Query CustomMetrics row.</param>
        /// <param name="dueKey">Due-key for this Query row.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A task that completes when the Query group is published or skipped.</returns>
        private async Task PushQueryMetricAsync(
            ResourceState state,
            CustomMetricConfig metric,
            string dueKey,
            CancellationToken cancellationToken)
        {
            var columns = await this.sqlQuerySessionFactory.ReadFirstRowAsync(
                state,
                this.credential,
                metric.Query!,
                metric.QueryTimeoutParsed,
                cancellationToken,
                metric.QueryConnection);
            var mapped = SqlQueryMetricSeriesMapper.Map(columns);
            if (mapped.SkippedMetricNames.Count > 0)
            {
                state.Logger.LogDebug(
                    "Skipping Query series on {ResourceId}: {Skipped}",
                    state.AzureResourceId,
                    string.Join(", ", mapped.SkippedMetricNames));
            }

            if (mapped.Series.Count == 0)
            {
                this.lastPushTimes[dueKey] = DateTime.UtcNow;
                if (mapped.SkippedMetricNames.Count == 0)
                {
                    state.Logger.LogDebug("Skipping Query custom metric on {ResourceId}: empty or non-numeric first row", state.AzureResourceId);
                }

                return;
            }

            foreach (var series in mapped.Series)
            {
                await this.PushNamedMetricAsync(state, metric, series, cancellationToken);
            }

            this.lastPushTimes[dueKey] = DateTime.UtcNow;
        }

        /// <summary>Resolves publish ResourceId, region, and namespace, then POSTs one custom metric series.</summary>
        /// <param name="state">Resource state used for placeholder replacement.</param>
        /// <param name="metric">CustomMetrics row (namespace and ResourceId).</param>
        /// <param name="series">Azure Monitor series bag (name, min, max, sum, count).</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A task that completes when the POST succeeds.</returns>
        private async Task PushNamedMetricAsync(
            ResourceState state,
            CustomMetricConfig metric,
            CustomMetricSeries series,
            CancellationToken cancellationToken)
        {
            var resourceId = state.ReplaceResourceParts(metric.ResourceId);

            if (string.IsNullOrEmpty(resourceId))
            {
                resourceId = state.Resource.Id;
            }

            var region = await this.resourceLocationResolver.GetRegionAsync(this.armClient, resourceId, cancellationToken);
            if (string.IsNullOrEmpty(region))
            {
                state.Logger.LogError("Skipping custom metric {Name}: could not resolve region for resource {ResourceId}", series.Name, resourceId);
                return;
            }

            var metricNamespace =
                !string.IsNullOrWhiteSpace(metric.Namespace) ? metric.Namespace :
                !string.IsNullOrWhiteSpace(this.defaultCustomMetricsNamespace) ? this.defaultCustomMetricsNamespace :
                DefaultMetricNamespace;

            await this.PushMetricAsync(resourceId, series, metricNamespace, region, cancellationToken);
            state.Logger.LogDebug(
                "Pushed custom metric {Name} min={Min} max={Max} sum={Sum} count={Count} in namespace {metricNamespace} to {ResourceId}",
                series.Name,
                series.Min,
                series.Max,
                series.Sum,
                series.Count,
                metricNamespace,
                resourceId);
        }

        private static object EvaluateExpression(CustomMetricConfig metric, CustomMetricDataContext context)
        {
            if (metric.DataExpressionDelegate == null)
            {
                return null;
            }

            return metric.DataExpressionDelegate.Invoke(context);
        }

        private static double ConvertToDouble(object value)
        {
            return value switch
            {
                null => 0,
                int i => i,
                long l => l,
                float f => f,
                decimal d => (double)d,
                double d => d,
                _ => double.TryParse(value.ToString(), out var parsed) ? parsed : 0
            };
        }

        private async Task PushMetricAsync(
            string resourceId,
            CustomMetricSeries series,
            string metricNamespace,
            string region,
            CancellationToken cancellationToken)
        {
            var token = await this.credential.GetTokenAsync(
                new TokenRequestContext(new[] { MonitoringScope }),
                cancellationToken);

            var url = this.BuildMetricsUrl(resourceId, region);
            var body = this.BuildMetricsBody(series, metricNamespace);

            var json = JsonSerializer.Serialize(body, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token.Token);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await this.httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new HttpRequestException(
                    $"Custom metric push returned {(int)response.StatusCode} {response.ReasonPhrase} for {resourceId}. {responseBody}");
            }
        }

        private string BuildMetricsUrl(string resourceId, string region)
        {
            // Format per Azure custom metrics REST API (see e.g. miztiik/custom-metrics-to-azure-monitor):
            // https://{region}.monitoring.azure.com/{resourceId}/metrics  (POST, no api-version)
            var normalized = resourceId.TrimStart('/');
            return $"https://{region}.monitoring.azure.com/{normalized}/metrics";
        }

        private object BuildMetricsBody(CustomMetricSeries series, string metricNamespace)
        {
            var time = DateTime.UtcNow.ToString("o");

            // Body format per working samples (e.g. miztiik/custom-metrics-to-azure-monitor): time + data.baseData only.
            return new
            {
                time,
                data = new
                {
                    baseData = new
                    {
                        metric = series.Name,
                        @namespace = metricNamespace,
                        dimNames = Array.Empty<string>(),
                        series = new[]
                        {
                            new
                            {
                                dimValues = Array.Empty<string>(),
                                min = series.Min,
                                max = series.Max,
                                sum = series.Sum,
                                count = series.Count
                            }
                        }
                    }
                }
            };
        }
    }
}
