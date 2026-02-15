using Microsoft.Extensions.Logging;
using Moq;
using poolautoscaler.configuration;
using poolautoscaler.resources.StorageFileShare;
using poolautoscaler.resources.StorageFileShare.Dto;

namespace poolautoscaler.tests
{
    public class StorageFileShareResourceStateTests
    {
        private readonly Mock<ILogger> loggerMock;
        private readonly Resource config;
        private readonly string resourceId;

        public StorageFileShareResourceStateTests()
        {
            this.loggerMock = new Mock<ILogger>();
            this.resourceId = "/subscriptions/test/resourceGroups/test/providers/Microsoft.Storage/storageAccounts/test/fileServices/default/shares/test";
            this.config = new Resource { };
        }

        [Fact]
        public void SetThroughput_WhenNoExistingRequest_ShouldSetQuotaBasedOnThroughput()
        {
            // Arrange
            var state = new StorageFileShareResourceState(this.resourceId, this.loggerMock.Object, this.config);

            state.ExistingStorageFileShareState = new StorageFileShareState
            {
                ShareQuotaGb = 100,
                ShareUsageBytes = (long)50 * 1024 * 1024 * 1024 // 50GB usage
            };

            state.RequestedStorageFileShareState = new StorageFileShareState();

            // Act
            state.SetThroughput(130); // Request 130 MiB/s throughput (needs more than 100GB)

            // Assert
            var patch = state.PreparePatch();
            Assert.True(patch.HasChanges);
            Assert.False(patch.Disruptive); // Storage changes are not disruptive
            var patchData = (StorageFileShareState)patch.PatchData;

            // Verify that the quota was set to support the requested throughput
            var expectedQuota = StorageFileShareResourceStateHelper.GetQuotaFromThroughput(130);
            Assert.Equal(expectedQuota, patchData.ShareQuotaGb);
        }

        [Fact]
        public void SetThroughput_WhenHigherStorageAlreadyRequested_ShouldKeepHigherStorage()
        {
            // Arrange
            var state = new StorageFileShareResourceState(this.resourceId, this.loggerMock.Object, this.config);

            state.ExistingStorageFileShareState = new StorageFileShareState
            {
                ShareQuotaGb = 100,
                ShareUsageBytes = (long)50 * 1024 * 1024 * 1024 // 50GB usage
            };

            state.RequestedStorageFileShareState = new StorageFileShareState
            {
                ShareQuotaGb = 500 // Already requested 500GB
            };

            // Act
            state.SetThroughput(140); // Request 140 MiB/s (which would need less than 500GB since 500GB provides 150 MiB/s)

            // Assert
            var patch = state.PreparePatch();
            Assert.True(patch.HasChanges);
            var patchData = (StorageFileShareState)patch.PatchData;
            Assert.Equal(500, patchData.ShareQuotaGb); // Should keep the higher storage value
        }

        [Fact]
        public void SetThroughput_WhenLowerStorageAlreadyRequested_ShouldUpgradeToSupportThroughput()
        {
            // Arrange
            var state = new StorageFileShareResourceState(this.resourceId, this.loggerMock.Object, this.config);

            state.ExistingStorageFileShareState = new StorageFileShareState
            {
                ShareQuotaGb = 100,
                ShareUsageBytes = (long)50 * 1024 * 1024 * 1024 // 50GB usage
            };

            state.RequestedStorageFileShareState = new StorageFileShareState
            {
                ShareQuotaGb = 150 // Already requested 150GB
            };

            // Act
            state.SetThroughput(110); // Request 110 MiB/s (150GB provides 115 MiB/s, so should keep 150GB)

            // Assert
            var patch = state.PreparePatch();
            Assert.True(patch.HasChanges);
            var patchData = (StorageFileShareState)patch.PatchData;

            Assert.Equal(150, patchData.ShareQuotaGb); // Should keep existing higher storage since it already supports the throughput
        }

        [Fact]
        public void SetProvisionedStorage_WhenNoExistingRequest_ShouldSetQuota()
        {
            // Arrange
            var state = new StorageFileShareResourceState(this.resourceId, this.loggerMock.Object, this.config);

            state.ExistingStorageFileShareState = new StorageFileShareState
            {
                ShareQuotaGb = 100,
                ShareUsageBytes = (long)50 * 1024 * 1024 * 1024 // 50GB usage
            };

            state.RequestedStorageFileShareState = new StorageFileShareState();

            // Act
            state.SetProvisionedStorage(150); // Request 150GB

            // Assert
            var patch = state.PreparePatch();
            Assert.True(patch.HasChanges);
            Assert.False(patch.Disruptive); // Storage changes are not disruptive
            var patchData = (StorageFileShareState)patch.PatchData;
            Assert.Equal(150, patchData.ShareQuotaGb);
        }

        [Fact]
        public void SetProvisionedStorage_ShouldRoundToNearestTenGB()
        {
            // Arrange
            var state = new StorageFileShareResourceState(this.resourceId, this.loggerMock.Object, this.config);

            state.ExistingStorageFileShareState = new StorageFileShareState
            {
                ShareQuotaGb = 100,
                ShareUsageBytes = (long)50 * 1024 * 1024 * 1024 // 50GB usage
            };

            state.RequestedStorageFileShareState = new StorageFileShareState();

            // Act
            state.SetProvisionedStorage(155); // Request 155GB, should round to 160GB

            // Assert
            var patch = state.PreparePatch();
            Assert.True(patch.HasChanges);
            var patchData = (StorageFileShareState)patch.PatchData;
            Assert.Equal(160, patchData.ShareQuotaGb); // Should round up to nearest 10
        }

        [Fact]
        public void SetProvisionedStorage_ShouldNotAllowSettingValueBelowCurrentUsageQuota()
        {
            // Arrange
            var state = new StorageFileShareResourceState(this.resourceId, this.loggerMock.Object, this.config);

            state.ExistingStorageFileShareState = new StorageFileShareState
            {
                ShareQuotaGb = 100,
                ShareUsageBytes = (long)75 * 1024 * 1024 * 1024 // 50GB usage
            };

            state.RequestedStorageFileShareState = new StorageFileShareState();

            // Act
            state.SetProvisionedStorage(50); // Request 155GB, should round to 160GB

            // Assert
            var patch = state.PreparePatch();
            Assert.False(patch.HasChanges);
            var patchData = (StorageFileShareState)patch.PatchData;
            Assert.Equal(100, patchData.ShareQuotaGb); // Should round up to nearest 10
        }

        [Fact]
        public void SetProvisionedStorage_WhenLowerThanExistingRequest_ShouldKeepHigherValue()
        {
            // Arrange
            var state = new StorageFileShareResourceState(this.resourceId, this.loggerMock.Object, this.config);

            state.ExistingStorageFileShareState = new StorageFileShareState
            {
                ShareQuotaGb = 100,
                ShareUsageBytes = (long)50 * 1024 * 1024 * 1024 // 50GB usage
            };

            state.RequestedStorageFileShareState = new StorageFileShareState
            {
                ShareQuotaGb = 200 // Already requested 200GB
            };

            // Act
            state.SetProvisionedStorage(150); // Try to request 150GB

            // Assert
            var patch = state.PreparePatch();
            Assert.True(patch.HasChanges);
            var patchData = (StorageFileShareState)patch.PatchData;
            Assert.Equal(200, patchData.ShareQuotaGb); // Should keep the higher value
        }

        [Fact]
        public void PreparePatch_WithNoChanges_ShouldReturnNoChanges()
        {
            // Arrange
            var state = new StorageFileShareResourceState(this.resourceId, this.loggerMock.Object, this.config);

            state.ExistingStorageFileShareState = new StorageFileShareState
            {
                ShareQuotaGb = 100,
                ShareUsageBytes = (long)50 * 1024 * 1024 * 1024 // 50GB usage
            };

            state.RequestedStorageFileShareState = new StorageFileShareState();

            // Assert
            var patch = state.PreparePatch();
            Assert.False(patch.HasChanges);
            Assert.False(patch.Disruptive);
            var patchData = (StorageFileShareState)patch.PatchData;
            Assert.Equal(100, patchData.ShareQuotaGb);
        }

        [Fact]
        public void PreparePatch_ShouldAlwaysRoundToTenGB()
        {
            // Arrange
            var state = new StorageFileShareResourceState(this.resourceId, this.loggerMock.Object, this.config);

            state.ExistingStorageFileShareState = new StorageFileShareState
            {
                ShareQuotaGb = 100,
                ShareUsageBytes = (long)50 * 1024 * 1024 * 1024 // 50GB usage
            };

            state.RequestedStorageFileShareState = new StorageFileShareState();

            // Test multiple values
            int[] testValues = new[] { 123, 127, 132, 138 };
            int[] expectedValues = new[] { 130, 130, 140, 140 };

            for (int i = 0; i < testValues.Length; i++)
            {
                // Act
                state.SetProvisionedStorage(testValues[i]);

                // Assert
                var patch = state.PreparePatch();
                var patchData = (StorageFileShareState)patch.PatchData;
                Assert.Equal(expectedValues[i], patchData.ShareQuotaGb);
            }
        }

        [Fact]
        public void Constructor_WithInvalidResourceId_ShouldThrowArgumentException()
        {
            // Arrange
            var invalidResourceId = "/subscriptions/test/resourceGroups/test/providers/Microsoft.Compute/virtualMachines/test";

            // Act & Assert
            Assert.Throws<ArgumentException>(() =>
                new StorageFileShareResourceState(invalidResourceId, this.loggerMock.Object, this.config));
        }
    }
}
