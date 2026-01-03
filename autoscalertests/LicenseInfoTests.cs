using poolautoscaler.licensing;

namespace poolautoscaler.tests
{
    public class LicenseInfoTests
    {
        [Fact]
        public void IsRestricted_WhenLicenseIsInvalid_ShouldReturnTrue()
        {
            // Arrange
            var license = new License
            {
                LicensedTo = "Test",
                ExpirationDate = DateTime.UtcNow.AddDays(30),
                MaxResources = 10
            };
            var licenseInfo = new LicenseInfo(license, isValid: false, isExpired: false);

            // Act
            var result = licenseInfo.IsRestricted;

            // Assert
            Assert.True(result);
        }

        [Fact]
        public void IsRestricted_WhenLicenseIsExpired_ShouldReturnTrue()
        {
            // Arrange
            var license = new License
            {
                LicensedTo = "Test",
                ExpirationDate = DateTime.UtcNow.AddDays(-1),
                MaxResources = 10
            };
            var licenseInfo = new LicenseInfo(license, isValid: true, isExpired: true);

            // Act
            var result = licenseInfo.IsRestricted;

            // Assert
            Assert.True(result);
        }

        [Fact]
        public void IsRestricted_WhenLicenseIsValidAndNotExpired_ShouldReturnFalse()
        {
            // Arrange
            var license = new License
            {
                LicensedTo = "Test",
                ExpirationDate = DateTime.UtcNow.AddDays(30),
                MaxResources = 10
            };
            var licenseInfo = new LicenseInfo(license, isValid: true, isExpired: false);

            // Act
            var result = licenseInfo.IsRestricted;

            // Assert
            Assert.False(result);
        }

        [Fact]
        public void Reason_WhenLicenseIsInvalid_ShouldReturnInvalid()
        {
            // Arrange
            var license = new License
            {
                LicensedTo = "Test",
                ExpirationDate = DateTime.UtcNow.AddDays(30),
                MaxResources = 10
            };
            var licenseInfo = new LicenseInfo(license, isValid: false, isExpired: false);

            // Act
            var result = licenseInfo.Reason;

            // Assert
            Assert.Equal("invalid", result);
        }

        [Fact]
        public void Reason_WhenLicenseIsExpired_ShouldReturnExpired()
        {
            // Arrange
            var license = new License
            {
                LicensedTo = "Test",
                ExpirationDate = DateTime.UtcNow.AddDays(-1),
                MaxResources = 10
            };
            var licenseInfo = new LicenseInfo(license, isValid: true, isExpired: true);

            // Act
            var result = licenseInfo.Reason;

            // Assert
            Assert.Equal("expired", result);
        }

        [Fact]
        public void Reason_WhenLicenseIsInvalidAndExpired_ShouldReturnInvalid()
        {
            // Arrange
            var license = new License
            {
                LicensedTo = "Test",
                ExpirationDate = DateTime.UtcNow.AddDays(-1),
                MaxResources = 10
            };
            var licenseInfo = new LicenseInfo(license, isValid: false, isExpired: true);

            // Act
            var result = licenseInfo.Reason;

            // Assert
            // Invalid takes precedence over expired
            Assert.Equal("invalid", result);
        }
    }
}
