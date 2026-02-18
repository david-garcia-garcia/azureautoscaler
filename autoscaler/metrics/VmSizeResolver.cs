using System.Collections.Concurrent;
using Azure;
using Azure.ResourceManager;
using Azure.ResourceManager.Compute;
using Azure.ResourceManager.Resources;
using Microsoft.Extensions.Logging;

namespace poolautoscaler.metrics
{
    /// <summary>
    /// Resolves VM sizes from Azure API (Virtual Machine Sizes - List per location). Caches results by location.
    /// Loads data on first use when the caller provides an ARM client.
    /// </summary>
    public sealed class VmSizeResolver : IVmSizeResolver
    {
        private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(24);

        private readonly ConcurrentDictionary<string, CachedSizes> cache = new();
        private readonly ConcurrentDictionary<string, SemaphoreSlim> loadLocks = new();
        private readonly ILogger logger;

        /// <summary>Initializes a new instance of the <see cref="VmSizeResolver"/> class.</summary>
        /// <param name="logger">The logger.</param>
        public VmSizeResolver(ILogger logger)
        {
            this.logger = logger;
        }

        /// <inheritdoc />
        public long GetMemoryBytes(
            string subscriptionId,
            Azure.Core.AzureLocation location,
            string vmSize,
            ArmClient? client = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(vmSize))
            {
                return 0;
            }

            var key = $"{subscriptionId}|{location.Name}";
            this.EnsureLoadedSync(key, subscriptionId, location, client, cancellationToken);

            if (this.cache.TryGetValue(key, out var cached) &&
                cached.Sizes.TryGetValue(vmSize, out var entry))
            {
                return (long)entry.MemoryInMB * 1024 * 1024;
            }

            return 0;
        }

        /// <inheritdoc />
        public int GetCoreCount(
            string subscriptionId,
            Azure.Core.AzureLocation location,
            string vmSize,
            ArmClient? client = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(vmSize))
            {
                return 0;
            }

            var key = $"{subscriptionId}|{location.Name}";
            this.EnsureLoadedSync(key, subscriptionId, location, client, cancellationToken);

            if (this.cache.TryGetValue(key, out var cached) &&
                cached.Sizes.TryGetValue(vmSize, out var entry))
            {
                return entry.NumberOfCores;
            }

            return GetCoreCountFromSkuNameFallback(vmSize);
        }

        private void EnsureLoadedSync(
            string key,
            string subscriptionId,
            Azure.Core.AzureLocation location,
            ArmClient? client,
            CancellationToken cancellationToken)
        {
            if (client == null)
            {
                return;
            }

            if (this.cache.TryGetValue(key, out var existing) && existing.ExpiresAt > DateTimeOffset.UtcNow)
            {
                return;
            }

            var sem = this.loadLocks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
            sem.Wait(cancellationToken);
            try
            {
                if (this.cache.TryGetValue(key, out var cached) && cached.ExpiresAt > DateTimeOffset.UtcNow)
                {
                    return;
                }

                this.LoadAsync(client, key, subscriptionId, location, cancellationToken).GetAwaiter().GetResult();
            }
            finally
            {
                sem.Release();
            }
        }

        private async Task LoadAsync(
            ArmClient client,
            string key,
            string subscriptionId,
            Azure.Core.AzureLocation location,
            CancellationToken cancellationToken)
        {
            try
            {
                var subscription = await client.GetSubscriptionResource(
                    SubscriptionResource.CreateResourceIdentifier(subscriptionId)).GetAsync(cancellationToken);

                var sizes = new Dictionary<string, VmSizeEntry>(StringComparer.OrdinalIgnoreCase);
                foreach (var vmSize in subscription.Value.GetVirtualMachineSizes(location))
                {
                    sizes[vmSize.Name] = new VmSizeEntry
                    {
                        NumberOfCores = vmSize.NumberOfCores ?? 0,
                        MemoryInMB = vmSize.MemoryInMB ?? 0
                    };
                }

                this.cache[key] = new CachedSizes
                {
                    Sizes = sizes,
                    ExpiresAt = DateTimeOffset.UtcNow.Add(CacheTtl)
                };

                this.logger.LogDebug("Loaded {Count} VM sizes for location {Location}", sizes.Count, location.Name);
            }
            catch (RequestFailedException ex)
            {
                this.logger.LogWarning(ex, "Failed to load VM sizes for {Location}: {Message}", location.Name, ex.Message);
            }
        }

        private static int GetCoreCountFromSkuNameFallback(string vmSize)
        {
            var match = System.Text.RegularExpressions.Regex.Match(vmSize, @"[A-Z](\d+)");
            return match.Success ? int.Parse(match.Groups[1].Value) : 0;
        }

        private struct VmSizeEntry
        {
            public int NumberOfCores;
            public int MemoryInMB;
        }

        private class CachedSizes
        {
            public required Dictionary<string, VmSizeEntry> Sizes { get; init; }

            public DateTimeOffset ExpiresAt { get; set; }
        }
    }
}
