using Azure.Core;
using Azure.ResourceManager;
using poolautoscaler.metrics.Dto;

namespace poolautoscaler.metrics
{
    /// <summary>
    /// Provides custom metric data context for pushing metrics to Azure Monitor.
    /// Resource types implement this to build the context passed to DataExpression.
    /// </summary>
    public interface ICustomMetricDataProvider
    {
        /// <summary>
        /// Builds the data context for custom metric expression evaluation.
        /// Populates Resource, ExistingState, ResourceParts, Helpers. Uses <see cref="IVmSizeResolver"/> and <see cref="IResourceLocationResolver"/> when available (resolved from the state's service provider).
        /// </summary>
        /// <param name="client">The ARM client.</param>
        /// <param name="credential">The token credential.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The data context for expression evaluation.</returns>
        Task<CustomMetricDataContext> BuildCustomMetricDataContextAsync(
            ArmClient client,
            TokenCredential credential,
            CancellationToken cancellationToken);
    }
}
