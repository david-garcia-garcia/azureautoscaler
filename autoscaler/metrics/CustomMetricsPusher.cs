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

        /// <summary>Per (resourceState.ResourceId, metricName) last push time.</summary>
        private readonly Dictionary<string, DateTime> lastPushTimes = new();

        /// <summary>Initializes a new instance of the <see cref="CustomMetricsPusher"/> class.</summary>
        /// <param name="credential">The token credential for Azure Monitor.</param>
        /// <param name="armClient">The ARM client.</param>
        /// <param name="logger">The logger.</param>
        /// <param name="resourceLocationResolver">Resolves resource IDs to region for the metrics endpoint.</param>
        /// <param name="defaultCustomMetricsNamespace">Optional global default namespace for custom metrics.</param>
        public CustomMetricsPusher(
            TokenCredential credential,
            ArmClient armClient,
            ILogger logger,
            IResourceLocationResolver resourceLocationResolver,
            string? defaultCustomMetricsNamespace = null)
        {
            this.credential = credential;
            this.armClient = armClient;
            this.logger = logger;
            this.httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            this.resourceLocationResolver = resourceLocationResolver;
            this.defaultCustomMetricsNamespace = defaultCustomMetricsNamespace;
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

            var context = await state.BuildCustomMetricDataContextAsync(
                this.armClient,
                this.credential,
                cancellationToken);

            if (context == null)
            {
                return;
            }

            foreach (var metric in state.Configuration.CustomMetrics)
            {
                var key = $"{state.Resource?.Id}|{metric.Name}";
                if (this.lastPushTimes.TryGetValue(key, out var last) &&
                    (DateTime.UtcNow - last) < metric.FrequencyParsed)
                {
                    continue;
                }

                try
                {
                    var value = EvaluateExpression(metric, context);
                    if (value == null)
                    {
                        continue;
                    }

                    var numericValue = ConvertToDouble(value);

                    var resourceId = state.ReplaceResourceParts(metric.ResourceId);

                    if (string.IsNullOrEmpty(resourceId))
                    {
                        resourceId = state.Resource.Id;
                    }

                    var region = await this.resourceLocationResolver.GetRegionAsync(this.armClient, resourceId, cancellationToken);
                    if (string.IsNullOrEmpty(region))
                    {
                        state.Logger.LogError("Skipping custom metric {Name}: could not resolve region for resource {ResourceId}", metric.Name, resourceId);
                        continue;
                    }

                    var metricNamespace =
                        !string.IsNullOrWhiteSpace(metric.Namespace) ? metric.Namespace :
                        !string.IsNullOrWhiteSpace(this.defaultCustomMetricsNamespace) ? this.defaultCustomMetricsNamespace :
                        DefaultMetricNamespace;

                    await this.PushMetricAsync(resourceId, metric.Name, metricNamespace, numericValue, region, cancellationToken);
                    this.lastPushTimes[key] = DateTime.UtcNow;
                    state.Logger.LogDebug("Pushed custom metric {Name}={Value} in namespace {metricNamespace} to {ResourceId}", metric.Name, numericValue, metricNamespace, resourceId);
                }
                catch (Exception ex)
                {
                    state.Logger.LogError(ex, "Failed to push custom metric {Name}: {Message}", metric.Name, ex.Message);
                }
            }/**/
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
            string metricName,
            string metricNamespace,
            double value,
            string region,
            CancellationToken cancellationToken)
        {
            var token = await this.credential.GetTokenAsync(
                new TokenRequestContext(new[] { MonitoringScope }),
                cancellationToken);

            var url = this.BuildMetricsUrl(resourceId, region);
            var body = this.BuildMetricsBody(metricName, metricNamespace, value);

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

        private object BuildMetricsBody(string metricName, string metricNamespace, double value)
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
                        metric = metricName,
                        @namespace = metricNamespace,
                        dimNames = Array.Empty<string>(),
                        series = new[]
                        {
                            new
                            {
                                dimValues = Array.Empty<string>(),
                                min = value,
                                max = value,
                                sum = value,
                                count = 1
                            }
                        }
                    }
                }
            };
        }
    }
}
