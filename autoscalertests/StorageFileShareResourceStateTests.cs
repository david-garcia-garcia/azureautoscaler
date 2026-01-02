using Microsoft.Extensions.Logging;
using Moq;
using poolautoscaler.resources;

namespace poolautoscaler.tests
{
    public class StorageFileShareResourceStateTests
    {
        private readonly Mock<ILogger> _loggerMock;
        private readonly Resource _config;
        private readonly string _resourceId;

        public StorageFileShareResourceStateTests()
        {
            _loggerMock = new Mock<ILogger>();
            _resourceId = "/subscriptions/test/resourceGroups/test/providers/Microsoft.Storage/storageAccounts/test/fileServices/default/shares/test";
            _config = new Resource { };
        }

        [Fact]
        public void SetThroughput_WhenNoExistingRequest_ShouldSetQuotaBasedOnThroughput()
        {
            // Arrange
            var state = new StorageFileShareResourceState(_resourceId, _loggerMock.Object, _config);

            state.ExistingStorageFileShareState = new StorageFileShareResourceState.StorageFileShareState
            {
                ShareQuotaGb = 100,
                ShareUsageBytes = (long)50 * 1024 * 1024 * 1024 // 50GB usage
            };

            state.RequestedStorageFileShareState = new StorageFileShareResourceState.StorageFileShareState();

            // Act
            state.SetThroughput(130); // Request 130 MiB/s throughput (needs more than 100GB)

            // Assert
            var patch = state.PreparePatch();
            Assert.True(patch.HasChanges);
            Assert.False(patch.Disruptive); // Storage changes are not disruptive
            var patchData = (StorageFileShareResourceState.StorageFileShareState)patch.PatchData;
            
            // Verify that the quota was set to support the requested throughput
            var expectedQuota = StorageFileShareResourceStateHelper.GetQuotaFromThroughput(130);
            Assert.Equal(expectedQuota, patchData.ShareQuotaGb);
        }

        [Fact]
        public void SetThroughput_WhenHigherStorageAlreadyRequested_ShouldKeepHigherStorage()
        {
            // Arrange
            var state = new StorageFileShareResourceState(_resourceId, _loggerMock.Object, _config);

            state.ExistingStorageFileShareState = new StorageFileShareResourceState.StorageFileShareState
            {
                ShareQuotaGb = 100,
                ShareUsageBytes = (long)50 * 1024 * 1024 * 1024 // 50GB usage
            };

            state.RequestedStorageFileShareState = new StorageFileShareResourceState.StorageFileShareState
            {
                ShareQuotaGb = 500 // Already requested 500GB
            };

            // Act
            state.SetThroughput(140); // Request 140 MiB/s (which would need less than 500GB since 500GB provides 150 MiB/s)

            // Assert
            var patch = state.PreparePatch();
            Assert.True(patch.HasChanges);
            var patchData = (StorageFileShareResourceState.StorageFileShareState)patch.PatchData;
            Assert.Equal(500, patchData.ShareQuotaGb); // Should keep the higher storage value
        }

        [Fact]
        public void SetThroughput_WhenLowerStorageAlreadyRequested_ShouldUpgradeToSupportThroughput()
        {
            // Arrange
            var state = new StorageFileShareResourceState(_resourceId, _loggerMock.Object, _config);

            state.ExistingStorageFileShareState = new StorageFileShareResourceState.StorageFileShareState
            {
                ShareQuotaGb = 100,
                ShareUsageBytes = (long)50 * 1024 * 1024 * 1024 // 50GB usage
            };

            state.RequestedStorageFileShareState = new StorageFileShareResourceState.StorageFileShareState
            {
                ShareQuotaGb = 150 // Already requested 150GB
            };

            // Act
            state.SetThroughput(110); // Request 110 MiB/s (150GB provides 115 MiB/s, so should keep 150GB)

            // Assert
            var patch = state.PreparePatch();
            Assert.True(patch.HasChanges);
            var patchData = (StorageFileShareResourceState.StorageFileShareState)patch.PatchData;
            
            Assert.Equal(150, patchData.ShareQuotaGb); // Should keep existing higher storage since it already supports the throughput
        }

        [Fact]
        public void SetProvisionedStorage_WhenNoExistingRequest_ShouldSetQuota()
        {
            // Arrange
            var state = new StorageFileShareResourceState(_resourceId, _loggerMock.Object, _config);

            state.ExistingStorageFileShareState = new StorageFileShareResourceState.StorageFileShareState
            {
                ShareQuotaGb = 100,
                ShareUsageBytes = (long)50 * 1024 * 1024 * 1024 // 50GB usage
            };

            state.RequestedStorageFileShareState = new StorageFileShareResourceState.StorageFileShareState();

            // Act
            state.SetProvisionedStorage(150); // Request 150GB

            // Assert
            var patch = state.PreparePatch();
            Assert.True(patch.HasChanges);
            Assert.False(patch.Disruptive); // Storage changes are not disruptive
            var patchData = (StorageFileShareResourceState.StorageFileShareState)patch.PatchData;
            Assert.Equal(150, patchData.ShareQuotaGb);
        }

        [Fact]
        public void SetProvisionedStorage_ShouldRoundToNearestTenGB()
        {
            // Arrange
            var state = new StorageFileShareResourceState(_resourceId, _loggerMock.Object, _config);

            state.ExistingStorageFileShareState = new StorageFileShareResourceState.StorageFileShareState
            {
                ShareQuotaGb = 100,
                ShareUsageBytes = (long)50 * 1024 * 1024 * 1024 // 50GB usage
            };

            state.RequestedStorageFileShareState = new StorageFileShareResourceState.StorageFileShareState();

            // Act
            state.SetProvisionedStorage(155); // Request 155GB, should round to 160GB

            // Assert
            var patch = state.PreparePatch();
            Assert.True(patch.HasChanges);
            var patchData = (StorageFileShareResourceState.StorageFileShareState)patch.PatchData;
            Assert.Equal(160, patchData.ShareQuotaGb); // Should round up to nearest 10
        }


        [Fact]
        public void SetProvisionedStorage_ShouldNotAllowSettingValueBelowCurrentUsageQuota()
        {
            // Arrange
            var state = new StorageFileShareResourceState(_resourceId, _loggerMock.Object, _config);

            state.ExistingStorageFileShareState = new StorageFileShareResourceState.StorageFileShareState
            {
                ShareQuotaGb = 100,
                ShareUsageBytes = (long)75 * 1024 * 1024 * 1024 // 50GB usage
            };

            state.RequestedStorageFileShareState = new StorageFileShareResourceState.StorageFileShareState();

            // Act
            state.SetProvisionedStorage(50); // Request 155GB, should round to 160GB

            // Assert
            var patch = state.PreparePatch();
            Assert.False(patch.HasChanges);
            var patchData = (StorageFileShareResourceState.StorageFileShareState)patch.PatchData;
            Assert.Equal(100, patchData.ShareQuotaGb); // Should round up to nearest 10
        }

        [Fact]
        public void SetProvisionedStorage_WhenLowerThanExistingRequest_ShouldKeepHigherValue()
        {
            // Arrange
            var state = new StorageFileShareResourceState(_resourceId, _loggerMock.Object, _config);

            state.ExistingStorageFileShareState = new StorageFileShareResourceState.StorageFileShareState
            {
                ShareQuotaGb = 100,
                ShareUsageBytes = (long)50 * 1024 * 1024 * 1024 // 50GB usage
            };

            state.RequestedStorageFileShareState = new StorageFileShareResourceState.StorageFileShareState
            {
                ShareQuotaGb = 200 // Already requested 200GB
            };

            // Act
            state.SetProvisionedStorage(150); // Try to request 150GB

            // Assert
            var patch = state.PreparePatch();
            Assert.True(patch.HasChanges);
            var patchData = (StorageFileShareResourceState.StorageFileShareState)patch.PatchData;
            Assert.Equal(200, patchData.ShareQuotaGb); // Should keep the higher value
        }

        [Fact]
        public void PreparePatch_WithNoChanges_ShouldReturnNoChanges()
        {
            // Arrange
            var state = new StorageFileShareResourceState(_resourceId, _loggerMock.Object, _config);

            state.ExistingStorageFileShareState = new StorageFileShareResourceState.StorageFileShareState
            {
                ShareQuotaGb = 100,
                ShareUsageBytes = (long)50 * 1024 * 1024 * 1024 // 50GB usage
            };

            state.RequestedStorageFileShareState = new StorageFileShareResourceState.StorageFileShareState();

            // Assert
            var patch = state.PreparePatch();
            Assert.False(patch.HasChanges);
            Assert.False(patch.Disruptive);
            var patchData = (StorageFileShareResourceState.StorageFileShareState)patch.PatchData;
            Assert.Equal(100, patchData.ShareQuotaGb);
        }

        [Fact]
        public void PreparePatch_ShouldAlwaysRoundToTenGB()
        {
            // Arrange
            var state = new StorageFileShareResourceState(_resourceId, _loggerMock.Object, _config);

            state.ExistingStorageFileShareState = new StorageFileShareResourceState.StorageFileShareState
            {
                ShareQuotaGb = 100,
                ShareUsageBytes = (long)50 * 1024 * 1024 * 1024 // 50GB usage
            };

            state.RequestedStorageFileShareState = new StorageFileShareResourceState.StorageFileShareState();

            // Test multiple values
            int[] testValues = new[] { 123, 127, 132, 138 };
            int[] expectedValues = new[] { 130, 130, 140, 140 };

            for (int i = 0; i < testValues.Length; i++)
            {
                // Act
                state.SetProvisionedStorage(testValues[i]);

                // Assert
                var patch = state.PreparePatch();
                var patchData = (StorageFileShareResourceState.StorageFileShareState)patch.PatchData;
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
                new StorageFileShareResourceState(invalidResourceId, _loggerMock.Object, _config));
        }


    }

    public class StorageFileShareResourceStateHelperTests
    {
        [Fact]
        public void GetThroughputFromQuota_WithRealAzureValues_ShouldMatchPortalData()
        {
            // Test real Azure File Share scaling data from portal
            Assert.Equal(110, StorageFileShareResourceStateHelper.GetThroughputFromQuota(100));  // 100 GiB = 110 MiB/s
            Assert.Equal(118, StorageFileShareResourceStateHelper.GetThroughputFromQuota(175));  // 175 GiB = 118 MiB/s
            Assert.Equal(119, StorageFileShareResourceStateHelper.GetThroughputFromQuota(180));  // 180 GiB = 119 MiB/s
            Assert.Equal(119, StorageFileShareResourceStateHelper.GetThroughputFromQuota(181));  // 181 GiB = 119 MiB/s
            Assert.Equal(120, StorageFileShareResourceStateHelper.GetThroughputFromQuota(185));  // 185 GiB = 120 MiB/s
            Assert.Equal(120, StorageFileShareResourceStateHelper.GetThroughputFromQuota(200));  // 200 GiB = 120 MiB/s
            Assert.Equal(127, StorageFileShareResourceStateHelper.GetThroughputFromQuota(265));  // 265 GiB = 127 MiB/s
            Assert.Equal(150, StorageFileShareResourceStateHelper.GetThroughputFromQuota(500));  // 500 GiB = 150 MiB/s  
            Assert.Equal(200, StorageFileShareResourceStateHelper.GetThroughputFromQuota(1000)); // 1000 GiB = 200 MiB/s
            Assert.Equal(250, StorageFileShareResourceStateHelper.GetThroughputFromQuota(1500)); // 1500 GiB = 250 MiB/s
            Assert.Equal(400, StorageFileShareResourceStateHelper.GetThroughputFromQuota(3000));
            Assert.Equal(280, StorageFileShareResourceStateHelper.GetThroughputFromQuota(1800));
            Assert.Equal(213, StorageFileShareResourceStateHelper.GetThroughputFromQuota(1125));
        }

        [Fact]
        public void GetThroughputFromQuota_WithRealAzureDataPoints_ShouldMatchExactly()
        {
            // Test all known real Azure values from portal
            Assert.Equal(118, StorageFileShareResourceStateHelper.GetThroughputFromQuota(175));  // Real Azure: 175 GiB = 118 MiB/s
            Assert.Equal(119, StorageFileShareResourceStateHelper.GetThroughputFromQuota(180));  // Real Azure: 180 GiB = 119 MiB/s
            Assert.Equal(119, StorageFileShareResourceStateHelper.GetThroughputFromQuota(181));  // Real Azure: 181 GiB = 119 MiB/s
            Assert.Equal(120, StorageFileShareResourceStateHelper.GetThroughputFromQuota(185));  // Real Azure: 185 GiB = 120 MiB/s
            Assert.Equal(127, StorageFileShareResourceStateHelper.GetThroughputFromQuota(265));  // Real Azure: 265 GiB = 127 MiB/s
        }

        [Fact]
        public void GetThroughputFromQuota_WithVeryLargeQuota_ShouldFollowFormula()
        {
            // Test throughput for very large shares follows Azure's official formula
            Assert.Equal(1100, StorageFileShareResourceStateHelper.GetThroughputFromQuota(10000)); // 100 + CEILING(400) + CEILING(600) = 1100
        }

        [Fact]
        public void GetQuotaFromThroughput_WithLowThroughput_ShouldReturnMinimumQuota()
        {
            var quota = StorageFileShareResourceStateHelper.GetQuotaFromThroughput(70);
            Assert.True(quota >= 100); // Should be at least minimum quota
            
            // Verify that the returned quota actually supports the requested throughput
            var actualThroughput = StorageFileShareResourceStateHelper.GetThroughputFromQuota(quota);
            Assert.True(actualThroughput >= 70);
        }

        [Fact]
        public void GetQuotaFromThroughput_WithHighThroughput_ShouldReturnAppropriateQuota()
        {
            var quota = StorageFileShareResourceStateHelper.GetQuotaFromThroughput(200);
            
            // Verify that the returned quota actually supports the requested throughput
            var actualThroughput = StorageFileShareResourceStateHelper.GetThroughputFromQuota(quota);
            Assert.True(actualThroughput >= 200);
            
            // Verify it's not unnecessarily high
            if (quota > 100)
            {
                var lowerQuota = quota - 10;
                var lowerThroughput = StorageFileShareResourceStateHelper.GetThroughputFromQuota(lowerQuota);
                Assert.True(lowerThroughput < 200); // Lower quota should not meet the requirement
            }
        }

        [Fact]
        public void GetQuotaFromThroughput_WithMaxThroughput_ShouldReturnReasonableQuota()
        {
            var quota = StorageFileShareResourceStateHelper.GetQuotaFromThroughput(300);
            
            // Verify that the returned quota actually supports the maximum throughput
            var actualThroughput = StorageFileShareResourceStateHelper.GetThroughputFromQuota(quota);
            Assert.Equal(300, actualThroughput);
        }

        [Fact]
        public void ThroughputQuotaRoundTrip_ShouldBeConsistent()
        {
            // Test that converting throughput to quota and back gives consistent results
            double[] testThroughputs = { 70, 85, 100, 150, 200, 250, 300 };

            foreach (var targetThroughput in testThroughputs)
            {
                var quota = StorageFileShareResourceStateHelper.GetQuotaFromThroughput(targetThroughput);
                var actualThroughput = StorageFileShareResourceStateHelper.GetThroughputFromQuota(quota);
                
                // The actual throughput should be at least the target throughput
                Assert.True(actualThroughput >= targetThroughput, 
                    $"Target: {targetThroughput}, Quota: {quota}, Actual: {actualThroughput}");
            }
        }

        [Fact]
        public void GetQuotaFromThroughput_WithCustomIncrement_ShouldUseSpecifiedIncrement()
        {
            // Test with different increment values
            var targetThroughput = 150.0;
            
            // Test with 1 GB increment (more precise)
            var quotaFine = StorageFileShareResourceStateHelper.GetQuotaFromThroughput(targetThroughput, 1);
            var throughputFine = StorageFileShareResourceStateHelper.GetThroughputFromQuota(quotaFine);
            
            // Test with 50 GB increment (coarser)
            var quotaCoarse = StorageFileShareResourceStateHelper.GetQuotaFromThroughput(targetThroughput, 50);
            var throughputCoarse = StorageFileShareResourceStateHelper.GetThroughputFromQuota(quotaCoarse);
            
            // Both should meet the target throughput
            Assert.True(throughputFine >= targetThroughput, 
                $"Fine increment - Target: {targetThroughput}, Quota: {quotaFine}, Actual: {throughputFine}");
            Assert.True(throughputCoarse >= targetThroughput, 
                $"Coarse increment - Target: {targetThroughput}, Quota: {quotaCoarse}, Actual: {throughputCoarse}");
            
            // Fine increment should find a smaller or equal quota
            Assert.True(quotaFine <= quotaCoarse, 
                $"Fine increment quota ({quotaFine}) should be <= coarse increment quota ({quotaCoarse})");
        }

        [Fact]
        public void GetQuotaFromThroughput_InverseRelationship_ShouldBeAccurate()
        {
            // Test the inverse relationship with small increment for high precision
            double[] testThroughputs = { 110, 120, 130, 150, 180, 200 };
            
            foreach (var targetThroughput in testThroughputs)
            {
                // Use 1 GB increment for precise testing
                var quota = StorageFileShareResourceStateHelper.GetQuotaFromThroughput(targetThroughput, 1);
                var actualThroughput = StorageFileShareResourceStateHelper.GetThroughputFromQuota(quota);
                
                // The actual throughput should be at least the target
                Assert.True(actualThroughput >= targetThroughput, 
                    $"Target: {targetThroughput}, Quota: {quota}, Actual: {actualThroughput}");
                
                // The previous quota (if applicable) should NOT meet the requirement
                if (quota > 100)
                {
                    var lowerQuota = quota - 1;
                    var lowerThroughput = StorageFileShareResourceStateHelper.GetThroughputFromQuota(lowerQuota);
                    Assert.True(lowerThroughput < targetThroughput, 
                        $"Lower quota ({lowerQuota}) throughput ({lowerThroughput}) should be < target ({targetThroughput})");
                }
            }
        }

        [Fact]
        public void GetQuotaFromThroughput_DefaultIncrement_ShouldBe10GB()
        {
            // Test that the default increment is 10 GB
            var targetThroughput = 125.0;
            
            var quotaDefault = StorageFileShareResourceStateHelper.GetQuotaFromThroughput(targetThroughput);
            var quotaExplicit = StorageFileShareResourceStateHelper.GetQuotaFromThroughput(targetThroughput, 10);
            
            // Both should return the same result
            Assert.Equal(quotaDefault, quotaExplicit);
            
            // The result should be a multiple of 10 (since we start at 100 and increment by 10)
            Assert.True((quotaDefault - 100) % 10 == 0, 
                $"Quota {quotaDefault} should be 100 + multiple of 10");
        }

        [Theory]
        [InlineData(1)]
        [InlineData(5)]
        [InlineData(10)]
        [InlineData(25)]
        [InlineData(100)]
        public void GetQuotaFromThroughput_WithDifferentIncrements_ShouldFindValidQuota(int increment)
        {
            // Test that different increments all find valid quotas
            var targetThroughput = 140.0;
            
            var quota = StorageFileShareResourceStateHelper.GetQuotaFromThroughput(targetThroughput, increment);
            var actualThroughput = StorageFileShareResourceStateHelper.GetThroughputFromQuota(quota);
            
            // Should meet the target throughput
            Assert.True(actualThroughput >= targetThroughput, 
                $"Increment {increment}: Target: {targetThroughput}, Quota: {quota}, Actual: {actualThroughput}");
            
            // Quota should be >= 100 (minimum)
            Assert.True(quota >= 100, $"Quota {quota} should be >= 100");
            
            // Quota should be valid for the increment (100 + n * increment)
            Assert.True((quota - 100) % increment == 0, 
                $"Quota {quota} should be 100 + multiple of {increment}");
        }
    }
}