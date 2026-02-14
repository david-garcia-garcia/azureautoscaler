using poolautoscaler.licensing;

namespace poolautoscaler.tests
{
    public class LicenseServiceTests : IDisposable
    {
        private string? _originalLicenseEnv;

        public LicenseServiceTests()
        {
            _originalLicenseEnv = Environment.GetEnvironmentVariable("AUTOSCALER_LICENSE");
        }

        public void Dispose()
        {
            if (_originalLicenseEnv != null)
                Environment.SetEnvironmentVariable("AUTOSCALER_LICENSE", _originalLicenseEnv);
            else
                Environment.SetEnvironmentVariable("AUTOSCALER_LICENSE", null);
        }

        [Fact]
        public void GetLicenseInfo_WhenNoLicenseProvided_ShouldReturnRestrictedLicenseInfo()
        {
            Environment.SetEnvironmentVariable("AUTOSCALER_LICENSE", null);
            var service = new LicenseService();

            var info = service.GetLicenseInfo();

            Assert.NotNull(info);
            Assert.True(info.IsRestricted);
            Assert.False(info.IsValid);
            Assert.NotNull(info.License);
            Assert.Equal("Unlicensed", info.License.LicensedTo);
            Assert.NotNull(service.LastError);
        }

        [Fact]
        public void GetLicenseInfo_ReturnsSameResultWhenCalledMultipleTimes()
        {
            Environment.SetEnvironmentVariable("AUTOSCALER_LICENSE", null);
            var service = new LicenseService();

            var info1 = service.GetLicenseInfo();
            var info2 = service.GetLicenseInfo();

            Assert.Same(info1.License, info2.License);
            Assert.Equal(info1.IsRestricted, info2.IsRestricted);
        }
    }
}
