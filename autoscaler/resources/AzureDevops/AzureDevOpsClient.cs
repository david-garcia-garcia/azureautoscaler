using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using poolautoscaler.resources.AzureDevops.Dto;

namespace poolautoscaler.resources.AzureDevops
{
    /// <summary>
    /// Client for Azure DevOps APIs including the undocumented Commerce API for managing parallel jobs.
    /// </summary>
    public class AzureDevOpsClient
    {
        /// <summary>
        /// Meter ID for hosted (MS-hosted) pipeline parallel jobs billing.
        /// </summary>
        public const string MeterIdHostedPipeline = "4bad9897-8d87-43bb-80be-5e6e8fefa3de";

        /// <summary>
        /// Meter ID for private (self-hosted) pipeline parallel jobs billing.
        /// </summary>
        public const string MeterIdPrivatePipeline = "f44a67f2-53ae-4044-bd58-1c8aca386b98";

        private readonly HttpClient httpClient;
        private readonly ILogger logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="AzureDevOpsClient"/> class.
        /// </summary>
        /// <param name="logger">The logger.</param>
        public AzureDevOpsClient(ILogger logger)
        {
            this.httpClient = new HttpClient();
            this.logger = logger;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="AzureDevOpsClient"/> class.
        /// </summary>
        /// <param name="httpClient">The HTTP client to use.</param>
        /// <param name="logger">The logger.</param>
        public AzureDevOpsClient(HttpClient httpClient, ILogger logger)
        {
            this.httpClient = httpClient;
            this.logger = logger;
        }

        /// <summary>
        /// Gets the organization ID (GUID) from the organization name using the connectionData API.
        /// </summary>
        /// <param name="organization">The organization name.</param>
        /// <param name="pat">The personal access token.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Organization GUID.</returns>
        public async Task<string> GetOrganizationIdAsync(string organization, string pat, CancellationToken cancellationToken)
        {
            var url = $"https://dev.azure.com/{organization}/_apis/connectionData";
            var request = CreateRequestWithBasicAuth(HttpMethod.Get, url, pat);

            var response = await this.httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            var connectionData = JsonSerializer.Deserialize<ConnectionDataResponse>(content);

            if (string.IsNullOrEmpty(connectionData?.InstanceId))
            {
                throw new Exception($"Failed to obtain organization ID for '{organization}' from Azure DevOps.");
            }

            this.logger.LogInformation("Resolved organization '{Organization}' to ID '{OrganizationId}'", organization, connectionData.InstanceId);
            return connectionData.InstanceId;
        }

        /// <summary>
        /// Gets a billing token from a PAT using the WebPlatformAuth API.
        /// This token is required for Commerce API calls.
        /// </summary>
        /// <param name="organization">The organization name.</param>
        /// <param name="pat">The personal access token.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Billing token string.</returns>
        public async Task<string> GetBillingTokenAsync(string organization, string pat, CancellationToken cancellationToken)
        {
            var url = $"https://dev.azure.com/{organization}/_apis/WebPlatformAuth/SessionToken?api-version=7.2-preview.1";
            var request = CreateRequestWithBasicAuth(HttpMethod.Post, url, pat);
            SetJsonContent(request, new { namedTokenId = "AzCommDeploymentProfile" });

            var response = await this.httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            var tokenResponse = JsonSerializer.Deserialize<SessionTokenResponse>(content);

            if (string.IsNullOrEmpty(tokenResponse?.Token))
            {
                throw new Exception("Failed to obtain billing token from Azure DevOps.");
            }

            return tokenResponse.Token;
        }

        /// <summary>
        /// Gets the current parallel jobs configuration from the Commerce API.
        /// </summary>
        /// <param name="organizationId">The organization GUID.</param>
        /// <param name="billingToken">The billing token.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Current hosted and private parallel job counts.</returns>
        public async Task<ParallelJobsInfo> GetParallelJobsAsync(string organizationId, string billingToken, CancellationToken cancellationToken)
        {
            var url = $"https://azdevopscommerce.dev.azure.com/{organizationId}/_apis/AzComm/MeterResource?api-version=7.2-preview.1";
            var request = CreateRequestWithBearerAuth(HttpMethod.Get, url, billingToken);

            var response = await this.httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            var meterResources = JsonSerializer.Deserialize<MeterResourceResponse>(content);

            var result = new ParallelJobsInfo();

            if (meterResources?.Value != null)
            {
                foreach (var meter in meterResources.Value)
                {
                    if (meter.MeterId == MeterIdHostedPipeline)
                    {
                        result.HostedParallelJobs = (int)(meter.PurchaseQuantity ?? meter.IncludedQuantity ?? 0);
                    }
                    else if (meter.MeterId == MeterIdPrivatePipeline)
                    {
                        result.PrivateParallelJobs = (int)(meter.PurchaseQuantity ?? meter.IncludedQuantity ?? 0);
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Sets the number of parallel jobs using the Commerce API.
        /// </summary>
        /// <param name="organizationId">The organization GUID.</param>
        /// <param name="billingToken">The billing token.</param>
        /// <param name="meterId">The meter ID (hosted or private pipeline).</param>
        /// <param name="quantity">The new parallel job count.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A task that completes when the update is done.</returns>
        public async Task SetParallelJobsAsync(string organizationId, string billingToken, string meterId, int quantity, CancellationToken cancellationToken)
        {
            var url = $"https://azdevopscommerce.dev.azure.com/{organizationId}/_apis/AzComm/MeterResource?api-version=7.2-preview.1";
            var request = CreateRequestWithBearerAuth(HttpMethod.Patch, url, billingToken);
            SetJsonContent(request, new { meterId, purchaseQuantity = quantity });

            this.logger.LogInformation("Setting parallel jobs: MeterId={MeterId}, Quantity={Quantity}", meterId, quantity);

            var response = await this.httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
        }

        /// <summary>
        /// Gets the count of queued jobs (jobs without assignTime) from a specific agent pool.
        /// </summary>
        /// <param name="organization">The organization name.</param>
        /// <param name="pat">The personal access token.</param>
        /// <param name="poolId">The agent pool ID.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Queued and running job counts.</returns>
        public async Task<QueuedJobsInfo> GetQueuedJobsAsync(string organization, string pat, int poolId, CancellationToken cancellationToken)
        {
            var url = $"https://dev.azure.com/{organization}/_apis/distributedtask/pools/{poolId}/jobrequests?api-version=6.0";
            var request = CreateRequestWithBasicAuth(HttpMethod.Get, url, pat);

            var response = await this.httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            var jobRequests = JsonSerializer.Deserialize<JobRequestsResponse>(content);

            var result = new QueuedJobsInfo();

            if (jobRequests?.Value != null)
            {
                foreach (var job in jobRequests.Value)
                {
                    if (job.AssignTime == null && job.Result == null)
                    {
                        result.QueuedJobs++;
                    }
                    else if (job.Result == null)
                    {
                        result.RunningJobs++;
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Gets all agent pools in the organization.
        /// </summary>
        /// <param name="organization">The organization name.</param>
        /// <param name="pat">The personal access token.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>List of agent pools.</returns>
        public async Task<List<AgentPool>> GetAgentPoolsAsync(string organization, string pat, CancellationToken cancellationToken)
        {
            var url = $"https://dev.azure.com/{organization}/_apis/distributedtask/pools?api-version=6.0";
            var request = CreateRequestWithBasicAuth(HttpMethod.Get, url, pat);

            var response = await this.httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            var poolsResponse = JsonSerializer.Deserialize<AgentPoolsResponse>(content);

            return poolsResponse?.Value ?? new List<AgentPool>();
        }

        /// <summary>
        /// Gets aggregated queued jobs count across all pools of a specific type.
        /// </summary>
        /// <param name="organization">Organization name.</param>
        /// <param name="pat">Personal access token.</param>
        /// <param name="hostedOnly">If true, only count hosted (managed) pools. If false, only count self-hosted pools.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Aggregated queued and running job counts.</returns>
        public async Task<QueuedJobsInfo> GetAggregatedQueuedJobsAsync(string organization, string pat, bool hostedOnly, CancellationToken cancellationToken)
        {
            var pools = await this.GetAgentPoolsAsync(organization, pat, cancellationToken);
            var result = new QueuedJobsInfo();

            // Filter pools by type
            var filteredPools = pools.Where(p => p.IsHosted == hostedOnly).ToList();

            this.logger.LogDebug(
                "Found {Count} {Type} pools to query for queued jobs",
                filteredPools.Count,
                hostedOnly ? "hosted" : "self-hosted");

            foreach (var pool in filteredPools)
            {
                try
                {
                    var poolJobs = await this.GetQueuedJobsAsync(organization, pat, pool.Id, cancellationToken);
                    result.QueuedJobs += poolJobs.QueuedJobs;
                    result.RunningJobs += poolJobs.RunningJobs;

                    if (poolJobs.QueuedJobs > 0 || poolJobs.RunningJobs > 0)
                    {
                        this.logger.LogTrace(
                            "Pool '{PoolName}' (ID: {PoolId}): Queued={Queued}, Running={Running}",
                            pool.Name,
                            pool.Id,
                            poolJobs.QueuedJobs,
                            poolJobs.RunningJobs);
                    }
                }
                catch (Exception ex)
                {
                    this.logger.LogWarning(ex, "Failed to get queued jobs for pool '{PoolName}' (ID: {PoolId})", pool.Name, pool.Id);
                }
            }

            this.logger.LogTrace(
                "Total {Type} jobs - Queued: {Queued}, Running: {Running}",
                hostedOnly ? "hosted" : "self-hosted",
                result.QueuedJobs,
                result.RunningJobs);

            return result;
        }

        /// <summary>
        /// Creates an HTTP request with Basic authentication using a PAT.
        /// </summary>
        private static HttpRequestMessage CreateRequestWithBasicAuth(HttpMethod method, string url, string pat)
        {
            var request = new HttpRequestMessage(method, url);
            request.Headers.Authorization = new AuthenticationHeaderValue(
                "Basic",
                Convert.ToBase64String(Encoding.ASCII.GetBytes($"user:{pat}")));
            return request;
        }

        /// <summary>
        /// Creates an HTTP request with Bearer token authentication.
        /// </summary>
        private static HttpRequestMessage CreateRequestWithBearerAuth(HttpMethod method, string url, string bearerToken)
        {
            var request = new HttpRequestMessage(method, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
            return request;
        }

        /// <summary>
        /// Sets JSON content on an HTTP request.
        /// </summary>
        private static void SetJsonContent(HttpRequestMessage request, object content)
        {
            request.Content = new StringContent(
                JsonSerializer.Serialize(content),
                Encoding.UTF8,
                "application/json");
        }
    }
}
