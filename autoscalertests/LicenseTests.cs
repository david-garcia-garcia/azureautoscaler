using poolautoscaler.licensing;

namespace poolautoscaler.tests
{
    /// <summary>Tests for <see cref="License"/> model, including <see cref="License.IsSubscriptionAllowed"/>.</summary>
    public class LicenseTests
    {
        [Fact]
        public void IsSubscriptionAllowed_WhenAllowedSubscriptionIdsIsNull_ReturnsTrueForAnySubscription()
        {
            var license = new License
            {
                LicensedTo = "Test",
                MaxResources = 10,
                AllowedSubscriptionIds = null
            };

            Assert.True(license.IsSubscriptionAllowed("sub-1"));
            Assert.True(license.IsSubscriptionAllowed("sub-2"));
            Assert.True(license.IsSubscriptionAllowed(null));
            Assert.True(license.IsSubscriptionAllowed(string.Empty));
        }

        [Fact]
        public void IsSubscriptionAllowed_WhenAllowedSubscriptionIdsIsEmpty_ReturnsTrueForAnySubscription()
        {
            var license = new License
            {
                LicensedTo = "Test",
                MaxResources = 10,
                AllowedSubscriptionIds = new List<string>()
            };

            Assert.True(license.IsSubscriptionAllowed("sub-1"));
            Assert.True(license.IsSubscriptionAllowed("sub-2"));
            Assert.True(license.IsSubscriptionAllowed(null));
            Assert.True(license.IsSubscriptionAllowed(string.Empty));
        }

        [Fact]
        public void IsSubscriptionAllowed_WhenAllowedSubscriptionIdsIsPopulated_ReturnsTrueOnlyForListedIds()
        {
            var license = new License
            {
                LicensedTo = "Test",
                MaxResources = 10,
                AllowedSubscriptionIds = new List<string> { "sub-1", "sub-2" }
            };

            Assert.True(license.IsSubscriptionAllowed("sub-1"));
            Assert.True(license.IsSubscriptionAllowed("sub-2"));
            Assert.False(license.IsSubscriptionAllowed("sub-3"));
            Assert.False(license.IsSubscriptionAllowed("sub-other"));
        }

        [Fact]
        public void IsSubscriptionAllowed_WhenAllowedSubscriptionIdsIsPopulated_ReturnsFalseForNullSubscriptionId()
        {
            var license = new License
            {
                LicensedTo = "Test",
                MaxResources = 10,
                AllowedSubscriptionIds = new List<string> { "sub-1" }
            };

            Assert.False(license.IsSubscriptionAllowed(null));
        }

        [Fact]
        public void IsSubscriptionAllowed_WhenAllowedSubscriptionIdsIsPopulated_ReturnsFalseForEmptySubscriptionId()
        {
            var license = new License
            {
                LicensedTo = "Test",
                MaxResources = 10,
                AllowedSubscriptionIds = new List<string> { "sub-1" }
            };

            Assert.False(license.IsSubscriptionAllowed(string.Empty));
        }

        [Fact]
        public void IsSubscriptionAllowed_WhenAllowedSubscriptionIdsHasSingleId_ReturnsTrueOnlyForThatId()
        {
            var license = new License
            {
                LicensedTo = "Test",
                MaxResources = 10,
                AllowedSubscriptionIds = new List<string> { "allowed-sub-id" }
            };

            Assert.True(license.IsSubscriptionAllowed("allowed-sub-id"));
            Assert.False(license.IsSubscriptionAllowed("allowed-sub-id "));
            Assert.False(license.IsSubscriptionAllowed("other-sub-id"));
        }
    }
}
