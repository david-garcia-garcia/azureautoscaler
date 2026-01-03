using poolautoscaler.licensing;

namespace poolautoscaler.tests
{
    public class LicenseValidatorTests : IDisposable
    {
        private string? _originalLicenseEnv;

        public LicenseValidatorTests()
        {
            // Save original environment variable
            _originalLicenseEnv = Environment.GetEnvironmentVariable("AUTOSCALER_LICENSE");
        }

        public void Dispose()
        {
            // Restore original environment variable
            if (_originalLicenseEnv != null)
            {
                Environment.SetEnvironmentVariable("AUTOSCALER_LICENSE", _originalLicenseEnv);
            }
            else
            {
                Environment.SetEnvironmentVariable("AUTOSCALER_LICENSE", null);
            }
        }

        [Fact]
        public void ValidateAndLoadLicense_WhenNoLicenseProvided_ShouldReturnExpiredDefaultLicense()
        {
            // Arrange
            Environment.SetEnvironmentVariable("AUTOSCALER_LICENSE", null);
            var validator = new LicenseValidator();

            // Act
            var license = validator.ValidateAndLoadLicense();

            // Assert
            Assert.NotNull(license);
            Assert.Equal("Unlicensed", license.LicensedTo);
            Assert.Equal(DateTime.MinValue, license.ExpirationDate);
            Assert.Equal(2, license.MaxResources);
            Assert.False(validator.IsValid);
            Assert.NotNull(validator.LastError);
            Assert.Contains("AUTOSCALER_LICENSE", validator.LastError);
        }

        [Fact]
        public void ValidateAndLoadLicense_WhenEmptyLicenseProvided_ShouldReturnExpiredDefaultLicense()
        {
            // Arrange
            Environment.SetEnvironmentVariable("AUTOSCALER_LICENSE", "");
            var validator = new LicenseValidator();

            // Act
            var license = validator.ValidateAndLoadLicense();

            // Assert
            Assert.NotNull(license);
            Assert.Equal("Unlicensed", license.LicensedTo);
            Assert.False(validator.IsValid);
            Assert.NotNull(validator.LastError);
        }

        [Fact]
        public void ValidateAndLoadLicense_WhenJwtSignedWithWrongKey_ShouldReturnExpiredDefaultLicense()
        {
            // Arrange
            // Create a JWT signed with a different key than the embedded public key
            // This will cause signature validation to fail
            // Using a malformed JWT that will fail validation
            var invalidJwt = "eyJhbGciOiJSUzI1NiIsInR5cCI6IkpXVCJ9.eyJsaWNlbnNlZFRvIjoiVGVzdCBDb21wYW55IiwiZXhwIjoxNzM1NzY4MDAwLCJtYXhSZXNvdXJjZXMiOjEwfQ.invalid-signature";
            Environment.SetEnvironmentVariable("AUTOSCALER_LICENSE", invalidJwt);
            var validator = new LicenseValidator();

            // Act
            var license = validator.ValidateAndLoadLicense();

            // Assert - Validation should fail due to signature mismatch
            Assert.NotNull(license);
            Assert.Equal("Unlicensed", license.LicensedTo);
            Assert.False(validator.IsValid);
            Assert.NotNull(validator.LastError);
        }

        [Fact]
        public void ValidateAndLoadLicense_WhenInvalidJwtFormat_ShouldReturnExpiredDefaultLicense()
        {
            // Arrange
            Environment.SetEnvironmentVariable("AUTOSCALER_LICENSE", "not.a.valid.jwt");
            var validator = new LicenseValidator();

            // Act
            var license = validator.ValidateAndLoadLicense();

            // Assert
            Assert.NotNull(license);
            Assert.Equal("Unlicensed", license.LicensedTo);
            Assert.False(validator.IsValid);
            Assert.NotNull(validator.LastError);
        }

        [Fact]
        public void ValidateAndLoadLicense_WhenMalformedJwt_ShouldReturnExpiredDefaultLicense()
        {
            // Arrange
            Environment.SetEnvironmentVariable("AUTOSCALER_LICENSE", "eyJhbGciOiJSUzI1NiIsInR5cCI6IkpXVCJ9.invalid.signature");
            var validator = new LicenseValidator();

            // Act
            var license = validator.ValidateAndLoadLicense();

            // Assert
            Assert.NotNull(license);
            Assert.False(validator.IsValid);
            Assert.NotNull(validator.LastError);
        }

        [Fact]
        public void IsExpired_WhenLicenseIsExpired_ShouldReturnTrue()
        {
            // Arrange
            var validator = new LicenseValidator();
            var expiredLicense = new License
            {
                LicensedTo = "Test",
                ExpirationDate = DateTime.UtcNow.AddDays(-1),
                MaxResources = 10
            };

            // Act
            var result = validator.IsExpired(expiredLicense);

            // Assert
            Assert.True(result);
        }

        [Fact]
        public void IsExpired_WhenLicenseIsNotExpired_ShouldReturnFalse()
        {
            // Arrange
            var validator = new LicenseValidator();
            var validLicense = new License
            {
                LicensedTo = "Test",
                ExpirationDate = DateTime.UtcNow.AddDays(30),
                MaxResources = 10
            };

            // Act
            var result = validator.IsExpired(validLicense);

            // Assert
            Assert.False(result);
        }

        [Fact]
        public void ValidateAndLoadLicense_ShouldCacheResult()
        {
            // Arrange
            Environment.SetEnvironmentVariable("AUTOSCALER_LICENSE", null);
            var validator = new LicenseValidator();

            // Act
            var license1 = validator.ValidateAndLoadLicense();
            var license2 = validator.ValidateAndLoadLicense();

            // Assert
            Assert.Same(license1, license2);
        }

        [Fact]
        public void CreateExpiredDefaultLicense_ShouldReturnExpiredLicense()
        {
            // Arrange
            var validator = new LicenseValidator();

            // Act
            var license = validator.CreateExpiredDefaultLicense();

            // Assert
            Assert.NotNull(license);
            Assert.Equal("Unlicensed", license.LicensedTo);
            Assert.Equal(DateTime.MinValue, license.ExpirationDate);
            Assert.Equal(2, license.MaxResources);
        }

        [Fact]
        public void LastError_WhenNoValidationAttempted_ShouldReturnNull()
        {
            // Arrange
            var validator = new LicenseValidator();

            // Act
            var error = validator.LastError;

            // Assert
            Assert.Null(error);
        }

        [Fact]
        public void LastError_AfterFailedValidation_ShouldContainErrorMessage()
        {
            // Arrange
            Environment.SetEnvironmentVariable("AUTOSCALER_LICENSE", "invalid.token");
            var validator = new LicenseValidator();

            // Act
            validator.ValidateAndLoadLicense();
            var error = validator.LastError;

            // Assert
            Assert.NotNull(error);
            Assert.NotEmpty(error);
        }
    }
}
