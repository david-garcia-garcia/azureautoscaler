using poolautoscaler.licensing;

namespace poolautoscaler.tests
{
    /// <summary>Tests for the Generate() (license generation) path of <see cref="LicenseService"/>.</summary>
    public class LicenseServiceGenerationTests : IDisposable
    {
        private string? tempKeyPath;

        public void Dispose()
        {
            if (this.tempKeyPath != null && File.Exists(this.tempKeyPath))
            {
                File.Delete(this.tempKeyPath);
            }
        }

        [Fact]
        public void Generate_WhenPrivateKeyFileNotFound_ReturnsFailure()
        {
            var service = new LicenseService();
            var result = service.Generate(
                "nonexistent.pem",
                "Acme Corp",
                "2025-12-31T23:59:59Z",
                "10");

            Assert.False(result.Success);
            Assert.Null(result.Jwt);
            Assert.NotNull(result.ErrorMessage);
            Assert.Contains("not found", result.ErrorMessage);
        }

        [Fact]
        public void Generate_WhenExpirationDateInvalid_ReturnsFailure()
        {
            this.tempKeyPath = Path.GetTempFileName();
            try
            {
                File.WriteAllText(this.tempKeyPath, "dummy");
                var service = new LicenseService();
                var result = service.Generate(
                    this.tempKeyPath,
                    "Acme Corp",
                    "not-a-date",
                    "10");

                Assert.False(result.Success);
                Assert.Null(result.Jwt);
                Assert.NotNull(result.ErrorMessage);
                Assert.Contains("Invalid expiration date", result.ErrorMessage);
            }
            finally
            {
                if (this.tempKeyPath != null && File.Exists(this.tempKeyPath))
                {
                    File.Delete(this.tempKeyPath);
                }

                this.tempKeyPath = null;
            }
        }

        [Fact]
        public void Generate_WhenMaxResourcesInvalid_ReturnsFailure()
        {
            this.tempKeyPath = Path.GetTempFileName();
            try
            {
                File.WriteAllText(this.tempKeyPath, "dummy");
                var service = new LicenseService();
                var result = service.Generate(
                    this.tempKeyPath,
                    "Acme Corp",
                    "2025-12-31T23:59:59Z",
                    "zero");

                Assert.False(result.Success);
                Assert.Null(result.Jwt);
                Assert.NotNull(result.ErrorMessage);
                Assert.Contains("positive integer", result.ErrorMessage);
            }
            finally
            {
                if (this.tempKeyPath != null && File.Exists(this.tempKeyPath))
                {
                    File.Delete(this.tempKeyPath);
                }

                this.tempKeyPath = null;
            }
        }

        [Fact]
        public void Generate_WhenMaxResourcesZero_ReturnsFailure()
        {
            this.tempKeyPath = Path.GetTempFileName();
            try
            {
                File.WriteAllText(this.tempKeyPath, "dummy");
                var service = new LicenseService();
                var result = service.Generate(
                    this.tempKeyPath,
                    "Acme Corp",
                    "2025-12-31T23:59:59Z",
                    "0");

                Assert.False(result.Success);
                Assert.NotNull(result.ErrorMessage);
            }
            finally
            {
                if (this.tempKeyPath != null && File.Exists(this.tempKeyPath))
                {
                    File.Delete(this.tempKeyPath);
                }

                this.tempKeyPath = null;
            }
        }
    }
}
