using poolautoscaler.licensing;

namespace poolautoscaler.tests
{
    /// <summary>Tests for license verification (GetLicenseInfo, LastError, IsExpired) on <see cref="LicenseService"/>.</summary>
    public class LicenseValidatorTests : IDisposable
    {
        private string? originalLicenseEnv;

        public LicenseValidatorTests()
        {
            this.originalLicenseEnv = Environment.GetEnvironmentVariable("AUTOSCALER_LICENSE");
        }

        public void Dispose()
        {
            if (this.originalLicenseEnv != null)
            {
                Environment.SetEnvironmentVariable("AUTOSCALER_LICENSE", this.originalLicenseEnv);
            }
            else
            {
                Environment.SetEnvironmentVariable("AUTOSCALER_LICENSE", null);
            }
        }

        [Fact]
        public void ValidateAndLoadLicense_WhenNoLicenseProvided_ShouldReturnExpiredDefaultLicense()
        {
            Environment.SetEnvironmentVariable("AUTOSCALER_LICENSE", null);
            var service = new LicenseService();

            var info = service.GetLicenseInfo();

            Assert.NotNull(info.License);
            Assert.Equal("Unlicensed", info.License.LicensedTo);
            Assert.Equal(DateTime.MinValue, info.License.ExpirationDate);
            Assert.Equal(2, info.License.MaxResources);
            Assert.False(info.IsValid);
            Assert.NotNull(service.LastError);
            Assert.Contains("AUTOSCALER_LICENSE", service.LastError);
        }

        [Fact]
        public void ValidateAndLoadLicense_WhenEmptyLicenseProvided_ShouldReturnExpiredDefaultLicense()
        {
            Environment.SetEnvironmentVariable("AUTOSCALER_LICENSE", "");
            var service = new LicenseService();

            var info = service.GetLicenseInfo();

            Assert.NotNull(info.License);
            Assert.Equal("Unlicensed", info.License.LicensedTo);
            Assert.False(info.IsValid);
            Assert.NotNull(service.LastError);
        }

        [Fact]
        public void ValidateAndLoadLicense_WhenJwtSignedWithWrongKey_ShouldReturnExpiredDefaultLicense()
        {
            var invalidJwt = "eyJhbGciOiJSUzI1NiIsInR5cCI6IkpXVCJ9.eyJsaWNlbnNlZFRvIjoiVGVzdCBDb21wYW55IiwiZXhwIjoxNzM1NzY4MDAwLCJtYXhSZXNvdXJjZXMiOjEwfQ.invalid-signature";
            Environment.SetEnvironmentVariable("AUTOSCALER_LICENSE", invalidJwt);
            var service = new LicenseService();

            var info = service.GetLicenseInfo();

            Assert.NotNull(info.License);
            Assert.Equal("Unlicensed", info.License.LicensedTo);
            Assert.False(info.IsValid);
            Assert.NotNull(service.LastError);
        }

        [Fact]
        public void ValidateAndLoadLicense_WhenInvalidJwtFormat_ShouldReturnExpiredDefaultLicense()
        {
            Environment.SetEnvironmentVariable("AUTOSCALER_LICENSE", "not.a.valid.jwt");
            var service = new LicenseService();

            var info = service.GetLicenseInfo();

            Assert.NotNull(info.License);
            Assert.Equal("Unlicensed", info.License.LicensedTo);
            Assert.False(info.IsValid);
            Assert.NotNull(service.LastError);
        }

        [Fact]
        public void ValidateAndLoadLicense_WhenMalformedJwt_ShouldReturnExpiredDefaultLicense()
        {
            Environment.SetEnvironmentVariable("AUTOSCALER_LICENSE", "eyJhbGciOiJSUzI1NiIsInR5cCI6IkpXVCJ9.invalid.signature");
            var service = new LicenseService();

            var info = service.GetLicenseInfo();

            Assert.NotNull(info.License);
            Assert.False(info.IsValid);
            Assert.NotNull(service.LastError);
        }

        [Fact]
        public void IsExpired_WhenLicenseIsExpired_ShouldReturnTrue()
        {
            var expiredLicense = new License
            {
                LicensedTo = "Test",
                ExpirationDate = DateTime.UtcNow.AddDays(-1),
                MaxResources = 10
            };

            var result = LicenseService.IsExpired(expiredLicense);

            Assert.True(result);
        }

        [Fact]
        public void IsExpired_WhenLicenseIsNotExpired_ShouldReturnFalse()
        {
            var validLicense = new License
            {
                LicensedTo = "Test",
                ExpirationDate = DateTime.UtcNow.AddDays(30),
                MaxResources = 10
            };

            var result = LicenseService.IsExpired(validLicense);

            Assert.False(result);
        }

        [Fact]
        public void ValidateAndLoadLicense_ShouldCacheResult()
        {
            Environment.SetEnvironmentVariable("AUTOSCALER_LICENSE", null);
            var service = new LicenseService();

            var info1 = service.GetLicenseInfo();
            var info2 = service.GetLicenseInfo();

            Assert.Same(info1.License, info2.License);
        }

        [Fact]
        public void GetLicenseInfo_WhenNoLicense_ReturnsExpiredDefaultLicenseValues()
        {
            Environment.SetEnvironmentVariable("AUTOSCALER_LICENSE", null);
            var service = new LicenseService();

            var info = service.GetLicenseInfo();

            Assert.NotNull(info.License);
            Assert.Equal("Unlicensed", info.License.LicensedTo);
            Assert.Equal(DateTime.MinValue, info.License.ExpirationDate);
            Assert.Equal(2, info.License.MaxResources);
        }

        [Fact]
        public void LastError_WhenNoValidationAttempted_ShouldReturnNull()
        {
            var service = new LicenseService();

            var error = service.LastError;

            Assert.Null(error);
        }

        [Fact]
        public void LastError_AfterFailedValidation_ShouldContainErrorMessage()
        {
            Environment.SetEnvironmentVariable("AUTOSCALER_LICENSE", "invalid.token");
            var service = new LicenseService();

            service.GetLicenseInfo();
            var error = service.LastError;

            Assert.NotNull(error);
            Assert.NotEmpty(error);
        }
    }
}
