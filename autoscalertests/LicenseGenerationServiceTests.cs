using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
                "2030-12-31T23:59:59Z",
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
                    "2030-12-31T23:59:59Z",
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
                    "2030-12-31T23:59:59Z",
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

        [Fact]
        public void Generate_WhenAllowedSubscriptionIdsProvided_IncludesClaimInJwt()
        {
            this.tempKeyPath = CreateTempRsaKey();
            try
            {
                var service = new LicenseService();
                var result = service.Generate(
                    this.tempKeyPath,
                    "Acme Corp",
                    "2030-12-31T23:59:59Z",
                    "10",
                    allowedSubscriptionIds: new List<string> { "sub-1", "sub-2" });

                Assert.True(result.Success, result.ErrorMessage ?? "Generate failed");
                Assert.NotNull(result.Jwt);
                var payload = DecodeJwtPayload(result.Jwt);
                Assert.True(payload.TryGetProperty("allowedSubscriptionIds", out var claim));
                var claimJson = claim.ValueKind == JsonValueKind.String ? claim.GetString() : claim.GetRawText();
                var ids = JsonSerializer.Deserialize<List<string>>(claimJson!);
                Assert.NotNull(ids);
                Assert.Equal(2, ids.Count);
                Assert.Contains("sub-1", ids);
                Assert.Contains("sub-2", ids);
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
        public void Generate_WhenAllowedSubscriptionIdsNull_DoesNotIncludeClaimInJwt()
        {
            this.tempKeyPath = CreateTempRsaKey();
            try
            {
                var service = new LicenseService();
                var result = service.Generate(
                    this.tempKeyPath,
                    "Acme Corp",
                    "2030-12-31T23:59:59Z",
                    "10",
                    allowedSubscriptionIds: null);

                Assert.True(result.Success, result.ErrorMessage ?? "Generate failed");
                Assert.NotNull(result.Jwt);
                var payload = DecodeJwtPayload(result.Jwt);
                Assert.False(payload.TryGetProperty("allowedSubscriptionIds", out _));
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
        public void Generate_WhenAllowedSubscriptionIdsEmpty_DoesNotIncludeClaimInJwt()
        {
            this.tempKeyPath = CreateTempRsaKey();
            try
            {
                var service = new LicenseService();
                var result = service.Generate(
                    this.tempKeyPath,
                    "Acme Corp",
                    "2030-12-31T23:59:59Z",
                    "10",
                    allowedSubscriptionIds: new List<string>());

                Assert.True(result.Success, result.ErrorMessage ?? "Generate failed");
                Assert.NotNull(result.Jwt);
                var payload = DecodeJwtPayload(result.Jwt);
                Assert.False(payload.TryGetProperty("allowedSubscriptionIds", out _));
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

        private static string CreateTempRsaKey()
        {
            using var rsa = RSA.Create(2048);
            var pem = rsa.ExportPkcs8PrivateKeyPem();
            var path = Path.GetTempFileName();
            File.WriteAllText(path, pem);
            return path;
        }

        private static JsonElement DecodeJwtPayload(string jwt)
        {
            var parts = jwt.Split('.');
            Assert.Equal(3, parts.Length);
            var payloadBase64 = parts[1];
            var base64 = payloadBase64.Replace('-', '+').Replace('_', '/');
            base64 = base64.PadRight(base64.Length + ((4 - (base64.Length % 4)) % 4), '=');
            var bytes = Convert.FromBase64String(base64);
            var json = Encoding.UTF8.GetString(bytes);
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.Clone();
        }
    }
}
