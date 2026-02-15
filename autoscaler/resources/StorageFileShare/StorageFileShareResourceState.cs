using Azure;
using Azure.Core;
using Azure.ResourceManager;
using Azure.ResourceManager.Storage;
using Microsoft.Extensions.Logging;
using poolautoscaler.configuration;
using poolautoscaler.resourcemanagement;
using poolautoscaler.resourcemanagement.Dto;
using poolautoscaler.resources.StorageFileShare.Dto;

namespace poolautoscaler.resources.StorageFileShare
{
    /// <summary>
    /// Resource state for a storage file share.
    /// </summary>
    public class StorageFileShareResourceState : ResourceState
    {
        /// <summary>Gets or sets the requested file share state (quota).</summary>
        public StorageFileShareState RequestedStorageFileShareState { get; set; }

        /// <summary>Gets or sets the current existing file share state from Azure.</summary>
        public StorageFileShareState ExistingStorageFileShareState { get; set; }

        /// <inheritdoc />
        public override object ExistingStateRaw => this.ExistingStorageFileShareState;

        /// <inheritdoc />
        public override object RequestedStateRaw => this.RequestedStorageFileShareState;

        /// <summary>
        /// Initializes a new instance of the <see cref="StorageFileShareResourceState"/> class.
        /// </summary>
        /// <param name="id">The file share resource ID.</param>
        /// <param name="logger">The logger.</param>
        /// <param name="resourceConfiguration">The resource configuration.</param>
        public StorageFileShareResourceState(string id, ILogger logger, Resource resourceConfiguration) : base(id, logger, resourceConfiguration)
        {
            if (!ResourceStateFactory.FileShare.IsMatch(id))
            {
                throw new ArgumentException("Invalid File Share resource ID", nameof(id));
            }
        }

        /// <summary>Sets the requested provisioned storage (share quota) in GB.</summary>
        /// <param name="provisionedStorage">Provisioned storage in GB.</param>
        public void SetProvisionedStorage(double provisionedStorage)
        {
            if (this.RequestedStorageFileShareState.ShareQuotaGb.HasValue && this.RequestedStorageFileShareState.ShareQuotaGb > provisionedStorage)
            {
                this.Logger.LogDebug("Provisioned storage request not considered because an already higher request exists.");
                return;
            }

            this.RequestedStorageFileShareState.ShareQuotaGb = (int)provisionedStorage;
        }

        /// <summary>Sets the requested throughput by computing the required quota (MiB/s).</summary>
        /// <param name="targetThroughputMbps">Target throughput in MiB/s.</param>
        public void SetThroughput(double targetThroughputMbps)
        {
            int requiredQuota = StorageFileShareResourceStateHelper.GetQuotaFromThroughput(targetThroughputMbps);

            this.Logger.LogDebug("Setting storage quota to {requiredQuota} GB to achieve {ThroughputMiBps} MiB/s throughput.", requiredQuota, targetThroughputMbps);

            this.SetProvisionedStorage(requiredQuota);
        }

        /// <inheritdoc />
        public override ResourcePatchOperation PreparePatch()
        {
            ResourcePatchOperation operation = new ResourcePatchOperation();

            var patch = new StorageFileShareState();

            if (this.RequestedStorageFileShareState.ShareQuotaGb != null)
            {
                if (this.RequestedStorageFileShareState.ShareQuotaGb <= (this.ExistingStorageFileShareState.ShareUsageBytes / 1024 / 1024 / 1024))
                {
                    this.Logger.LogWarning("Requested storage quota below resource usage.");
                    patch.ShareQuotaGb = this.ExistingStorageFileShareState.ShareQuotaGb;
                }
                else
                {
                    patch.ShareQuotaGb = (int)(Math.Ceiling((decimal)this.RequestedStorageFileShareState.ShareQuotaGb / 10) * 10);
                }
            }
            else
            {
                patch.ShareQuotaGb = this.ExistingStorageFileShareState.ShareQuotaGb;
            }

            if (patch.ShareQuotaGb < 100)
            {
                patch.ShareQuotaGb = 100;
            }

            bool hasChanges = patch.ShareQuotaGb != this.ExistingStorageFileShareState.ShareQuotaGb;

            operation.HasChanges = hasChanges;
            operation.PatchData = patch;
            operation.Disruptive = false;

            return operation;
        }

        /// <inheritdoc />
        public override async Task ApplyChanges(ResourcePatchOperation operation, CancellationToken cancellationToken)
        {
            var fileShare = (FileShareResource)this.Resource;

            if (!(operation.PatchData is StorageFileShareState internalPatch))
            {
                throw new ArgumentException();
            }

            if (DateTime.UtcNow < fileShare.Data.NextAllowedQuotaDowngradeOn
                && internalPatch.ShareQuotaGb < this.ExistingStorageFileShareState.ShareQuotaGb)
            {
                this.Logger.LogInformation("Storage quota downgrade not allowed for {0}", (fileShare.Data.NextAllowedQuotaDowngradeOn - DateTime.UtcNow).Value.ToString("g"));
                return;
            }

            FileShareData patch = new FileShareData();
            patch.ShareQuota = internalPatch.ShareQuotaGb;

            try
            {
                await fileShare.UpdateAsync(patch, cancellationToken);
            }
            catch (RequestFailedException e) when (e.ErrorCode == "ContainerQuotaDowngradeNotAllowed")
            {
                this.Logger.LogInformation("Storage quota downgrade not allowed at the time. You cannot downgrade quota if the last increase happened less than 24h ago.");
                this.Logger.LogInformation("Resource evaluation will be disabled for the next two hours.");
                this.DisabledUntil["Storage quota downgrade not allowed at the time"] = DateTime.UtcNow.AddHours(2);
            }
        }

        /// <inheritdoc />
        protected override string GetResourceIdForChangeHistory()
        {
            return null;
        }

        /// <inheritdoc />
        protected override async Task InternalRefreshAsync(ArmClient client, TokenCredential credential, CancellationToken cancellationToken)
        {
            this.Resource = await client.GetFileShareResource(new ResourceIdentifier(this.ResourceId)).GetAsync(cancellationToken: cancellationToken);

            var fileShare = (FileShareResource)this.Resource;

            if (fileShare.Data.ProvisionedBandwidthMibps != null || fileShare.Data.ProvisionedIops != null)
            {
                throw new Exception("Current implementation only supports provisioning model V1. V2 detected.");
            }

            this.ResourceTags.Clear();
            var fileShareName = fileShare.Data.Name;
            var storageAccountId = this.ReplaceResourceParts("/subscriptions/${subscriptionId}/resourceGroups/${resourceGroupName}/providers/Microsoft.Storage/storageAccounts/${storageAccountName}");
            var storageAccount = await client.GetStorageAccountResource(new ResourceIdentifier(storageAccountId)).GetAsync(cancellationToken: cancellationToken);

            if (storageAccount.Value.Data.Tags != null)
            {
                var prefix = $"{fileShareName}:";
                foreach (var tag in storageAccount.Value.Data.Tags)
                {
                    if (tag.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        var tagKey = tag.Key.Substring(prefix.Length);
                        this.ResourceTags[tagKey] = tag.Value;
                    }
                }
            }

            this.ExistingStorageFileShareState = new StorageFileShareState()
            {
                ShareQuotaGb = fileShare.Data.ShareQuota,
                ShareUsageBytes = fileShare.Data.ShareUsageBytes
            };

            this.RequestedStorageFileShareState = new StorageFileShareState();
        }
    }
}
