using Azure.Core;
using Azure.ResourceManager;
using Azure.ResourceManager.Storage;
using Microsoft.Extensions.Logging;
using System.Text.Json.Serialization;

namespace poolautoscaler.resources
{
    public class StorageFileShareResourceState : ResourceState
    {
        public class StorageFileShareState
        {
            public int? ShareQuotaGb { get; set; }

            public long? ShareUsageBytes { get; set; }
        }

        public StorageFileShareState RequestedStorageFileShareState { get; set; }
        public StorageFileShareState ExistingStorageFileShareState { get; set; }

        public override object ExistingStateRaw => this.ExistingStorageFileShareState;

        public override object RequestedStateRaw => this.RequestedStorageFileShareState;

        public StorageFileShareResourceState(string id, ILogger logger, Resource resourceConfiguration) : base(id, logger, resourceConfiguration)
        {
            if (!ResourceStateFactory.FileShare.IsMatch(id))
            {
                throw new ArgumentException("Invalid File Share resource ID", nameof(id));
            }
        }

        protected override async Task InternalRefreshAsync(ArmClient client, TokenCredential credential, CancellationToken cancellationToken)
        {
            this.Resource = await client.GetFileShareResource(new ResourceIdentifier(this.ResourceId)).GetAsync(cancellationToken: cancellationToken);

            var fileShare = (FileShareResource)this.Resource;

            if (fileShare.Data.ProvisionedBandwidthMibps != null || fileShare.Data.ProvisionedIops != null)
            {
                throw new Exception("Current implementation only supports provisioning model V1. V2 detected.");
            }

            this.ExistingStorageFileShareState = new StorageFileShareState()
            {
                ShareQuotaGb = fileShare.Data.ShareQuota,
                ShareUsageBytes = fileShare.Data.ShareUsageBytes
            };

            this.RequestedStorageFileShareState = new StorageFileShareState();
        }

        public void SetProvisionedStorage(double provisionedStorage)
        {
            if (this.RequestedStorageFileShareState.ShareQuotaGb.HasValue && this.RequestedStorageFileShareState.ShareQuotaGb > provisionedStorage)
            {
                Logger.LogDebug("Provisioned storage request not considered because an already higher request exists.");
                return;
            }

            this.RequestedStorageFileShareState.ShareQuotaGb = (int)provisionedStorage;
        }

        public void SetThroughput(double targetThroughputMbps)
        {
            int requiredQuota = StorageFileShareResourceStateHelper.GetQuotaFromThroughput(targetThroughputMbps);
            
            Logger.LogDebug("Setting storage quota to {requiredQuota} GB to achieve {ThroughputMiBps} MiB/s throughput.", requiredQuota, targetThroughputMbps);

            // Always take the higher value between existing request and required quota for throughput
            this.SetProvisionedStorage(requiredQuota);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
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
                    // Increments of 10GB
                    patch.ShareQuotaGb =
                        (int)(Math.Ceiling((decimal)this.RequestedStorageFileShareState.ShareQuotaGb / 10) * 10);
                }
            }
            else
            {
                patch.ShareQuotaGb = this.ExistingStorageFileShareState.ShareQuotaGb;
            }

            // Minimum 100 per share
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

        /// <summary>
        /// 
        /// </summary>
        /// <param name="operation"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentException"></exception>
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

            await fileShare.UpdateAsync(patch, cancellationToken);
        }
    }
}