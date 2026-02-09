using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace poolautoscaler.resources
{
    /// <summary>
    /// Client for Azure DevOps APIs including the undocumented Commerce API for managing parallel jobs.
    /// </summary>
    public class AzureDevOpsClient
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger _logger;

        // Meter IDs for Azure DevOps billing (fixed platform-level constants, same for all organizations)
        // Source: VSTeam PowerShell module - https://www.powershellgallery.com/packages/VSTeam
        // See also: https://stackoverflow.com/questions/71822758/pricing-of-agents-and-parallel-jobs-in-azure-devops
        public const string MeterIdHostedPipeline = "4bad9897-8d87-43bb-80be-5e6e8fefa3de";
        public const string MeterIdPrivatePipeline = "f44a67f2-53ae-4044-bd58-1c8aca386b98";

        public AzureDevOpsClient(ILogger logger)
        {
            _httpClient = new HttpClient();
            _logger = logger;
        }

        public AzureDevOpsClient(HttpClient httpClient, ILogger logger)
        {
            _httpClient = httpClient;
            _logger = logger;
        }

        #region Helper Methods

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

        #endregion

        /// <summary>
        /// Gets the organization ID (GUID) from the organization name using the connectionData API.
        /// </summary>
        public async Task<string> GetOrganizationIdAsync(string organization, string pat, CancellationToken cancellationToken)
        {
            var url = $"https://dev.azure.com/{organization}/_apis/connectionData";
            var request = CreateRequestWithBasicAuth(HttpMethod.Get, url, pat);

            var response = await _httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            var connectionData = JsonSerializer.Deserialize<ConnectionDataResponse>(content);

            if (string.IsNullOrEmpty(connectionData?.InstanceId))
            {
                throw new Exception($"Failed to obtain organization ID for '{organization}' from Azure DevOps.");
            }

            _logger.LogInformation("Resolved organization '{Organization}' to ID '{OrganizationId}'", organization, connectionData.InstanceId);
            return connectionData.InstanceId;
        }

        /// <summary>
        /// Gets a billing token from a PAT using the WebPlatformAuth API.
        /// This token is required for Commerce API calls.
        /// </summary>
        public async Task<string> GetBillingTokenAsync(string organization, string pat, CancellationToken cancellationToken)
        {
            var url = $"https://dev.azure.com/{organization}/_apis/WebPlatformAuth/SessionToken?api-version=7.2-preview.1";
            var request = CreateRequestWithBasicAuth(HttpMethod.Post, url, pat);
            SetJsonContent(request, new { namedTokenId = "AzCommDeploymentProfile" });

            var response = await _httpClient.SendAsync(request, cancellationToken);
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
        public async Task<ParallelJobsInfo> GetParallelJobsAsync(string organizationId, string billingToken, CancellationToken cancellationToken)
        {
            var url = $"https://azdevopscommerce.dev.azure.com/{organizationId}/_apis/AzComm/MeterResource?api-version=7.2-preview.1";
            var request = CreateRequestWithBearerAuth(HttpMethod.Get, url, billingToken);

            var response = await _httpClient.SendAsync(request, cancellationToken);
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
        public async Task SetParallelJobsAsync(string organizationId, string billingToken, string meterId, int quantity, CancellationToken cancellationToken)
        {
            var url = $"https://azdevopscommerce.dev.azure.com/{organizationId}/_apis/AzComm/MeterResource?api-version=7.2-preview.1";
            var request = CreateRequestWithBearerAuth(HttpMethod.Patch, url, billingToken);
            SetJsonContent(request, new { meterId, purchaseQuantity = quantity });

            _logger.LogInformation("Setting parallel jobs: MeterId={MeterId}, Quantity={Quantity}", meterId, quantity);

            var response = await _httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
        }

        /// <summary>
        /// Gets the count of queued jobs (jobs without assignTime) from a specific agent pool.
        /// </summary>
        public async Task<QueuedJobsInfo> GetQueuedJobsAsync(string organization, string pat, int poolId, CancellationToken cancellationToken)
        {
            var url = $"https://dev.azure.com/{organization}/_apis/distributedtask/pools/{poolId}/jobrequests?api-version=6.0";
            var request = CreateRequestWithBasicAuth(HttpMethod.Get, url, pat);

            var response = await _httpClient.SendAsync(request, cancellationToken);
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
        public async Task<List<AgentPool>> GetAgentPoolsAsync(string organization, string pat, CancellationToken cancellationToken)
        {
            var url = $"https://dev.azure.com/{organization}/_apis/distributedtask/pools?api-version=6.0";
            var request = CreateRequestWithBasicAuth(HttpMethod.Get, url, pat);

            var response = await _httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            var poolsResponse = JsonSerializer.Deserialize<AgentPoolsResponse>(content);

            return poolsResponse?.Value ?? new List<AgentPool>();
        }

        /// <summary>
        /// Gets aggregated queued jobs count across all pools of a specific type.
        /// </summary>
        /// <param name="organization">Organization name</param>
        /// <param name="pat">Personal access token</param>
        /// <param name="hostedOnly">If true, only count hosted (managed) pools. If false, only count self-hosted pools.</param>
        /// <param name="cancellationToken">Cancellation token</param>
        public async Task<QueuedJobsInfo> GetAggregatedQueuedJobsAsync(string organization, string pat, bool hostedOnly, CancellationToken cancellationToken)
        {
            var pools = await GetAgentPoolsAsync(organization, pat, cancellationToken);
            var result = new QueuedJobsInfo();

            // Filter pools by type
            var filteredPools = pools.Where(p => p.IsHosted == hostedOnly).ToList();

            _logger.LogDebug("Found {Count} {Type} pools to query for queued jobs",
                filteredPools.Count,
                hostedOnly ? "hosted" : "self-hosted");

            foreach (var pool in filteredPools)
            {
                try
                {
                    var poolJobs = await GetQueuedJobsAsync(organization, pat, pool.Id, cancellationToken);
                    result.QueuedJobs += poolJobs.QueuedJobs;
                    result.RunningJobs += poolJobs.RunningJobs;

                    if (poolJobs.QueuedJobs > 0 || poolJobs.RunningJobs > 0)
                    {
                        _logger.LogTrace("Pool '{PoolName}' (ID: {PoolId}): Queued={Queued}, Running={Running}",
                            pool.Name, pool.Id, poolJobs.QueuedJobs, poolJobs.RunningJobs);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to get queued jobs for pool '{PoolName}' (ID: {PoolId})", pool.Name, pool.Id);
                }
            }

            _logger.LogTrace("Total {Type} jobs - Queued: {Queued}, Running: {Running}",
                hostedOnly ? "hosted" : "self-hosted",
                result.QueuedJobs,
                result.RunningJobs);

            return result;
        }

        #region Response DTOs

        public class ConnectionDataResponse
        {
            [JsonPropertyName("instanceId")]
            public string? InstanceId { get; set; }
        }

        public class SessionTokenResponse
        {
            [JsonPropertyName("token")]
            public string? Token { get; set; }
        }

        public class MeterResourceResponse
        {
            [JsonPropertyName("value")]
            public List<MeterResource>? Value { get; set; }
        }

        public class MeterResource
        {
            [JsonPropertyName("meterId")]
            public string? MeterId { get; set; }

            // These come as decimal values from the API (e.g., 1.0), so we use double
            [JsonPropertyName("purchaseQuantity")]
            public double? PurchaseQuantity { get; set; }

            [JsonPropertyName("includedQuantity")]
            public double? IncludedQuantity { get; set; }
        }

        public class JobRequestsResponse
        {
            [JsonPropertyName("value")]
            public List<JobRequest>? Value { get; set; }
        }

        public class JobRequest
        {
            [JsonPropertyName("assignTime")]
            public DateTime? AssignTime { get; set; }

            [JsonPropertyName("result")]
            public object? Result { get; set; }
        }

        public class AgentPoolsResponse
        {
            [JsonPropertyName("value")]
            public List<AgentPool>? Value { get; set; }
        }

        public class AgentPool
        {
            [JsonPropertyName("id")]
            public int Id { get; set; }

            [JsonPropertyName("name")]
            public string? Name { get; set; }

            [JsonPropertyName("isHosted")]
            public bool IsHosted { get; set; }

            [JsonPropertyName("poolType")]
            public string? PoolType { get; set; }
        }

        #endregion
    }

    public class ParallelJobsInfo
    {
        public int HostedParallelJobs { get; set; }
        public int PrivateParallelJobs { get; set; }
    }

    public class QueuedJobsInfo
    {
        public int QueuedJobs { get; set; }
        public int RunningJobs { get; set; }
    }
}
