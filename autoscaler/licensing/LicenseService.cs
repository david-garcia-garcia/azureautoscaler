using System.Diagnostics;
using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

namespace poolautoscaler.licensing
{
    /// <summary>
    /// Result of a license generation attempt.
    /// </summary>
    public sealed class LicenseGenerationResult
    {
        public bool Success { get; init; }
        public string? Jwt { get; init; }
        public string? ErrorMessage { get; init; }
    }

    /// <summary>
    /// Single service for license verification and generation. Validates the current
    /// license (from AUTOSCALER_LICENSE) and can generate new license JWTs for the CLI.
    /// </summary>
    public class LicenseService
    {
        private readonly RsaSecurityKey _publicKey;
        private License? _cachedLicense;
        private bool _isValid;
        private string? _lastError;

        public LicenseService()
        {
            _publicKey = LoadPublicKey();
        }

        // ---- Verification (runtime) ----

        /// <summary>
        /// Gets the last validation error message, if any.
        /// </summary>
        public string? LastError => _lastError;

        /// <summary>
        /// Validates the license from the environment and returns a <see cref="LicenseInfo"/>
        /// (always non-null; uses expired default license when missing or invalid).
        /// </summary>
        public LicenseInfo GetLicenseInfo()
        {
            var license = ValidateAndLoadLicense();
            var isExpired = license != null && IsExpired(license);
            return new LicenseInfo(
                license ?? CreateExpiredDefaultLicense(),
                _isValid,
                isExpired);
        }

        /// <summary>
        /// Checks if the license is expired.
        /// </summary>
        public static bool IsExpired(License license)
        {
            return DateTime.UtcNow > license.ExpirationDate;
        }

        /// <summary>
        /// Logs current license information and any validation/expiry warnings to the given logger.
        /// </summary>
        public void LogLicenseInfo(ILogger logger)
        {
            var info = GetLicenseInfo();
            var license = info.License;
            var now = DateTime.UtcNow;
            var timeUntilExpiration = license.ExpirationDate - now;
            var daysRemaining = (int)timeUntilExpiration.TotalDays;

            logger.LogInformation(
                "License: LicensedTo={LicensedTo}, DaysRemaining={DaysRemaining}, MaxResources={MaxResources}",
                license.LicensedTo,
                daysRemaining,
                license.MaxResources);

            if (info.IsRestricted)
            {
                if (info.IsExpired)
                    logger.LogWarning("License expired. Limited functionality enabled.");
                else if (!info.IsValid)
                    logger.LogWarning("License invalid. Limited functionality enabled.");

                if (!string.IsNullOrEmpty(LastError))
                    logger.LogWarning("License Validation Error: {Error}", LastError);
            }
        }

        private License? ValidateAndLoadLicense()
        {
            if (_cachedLicense != null)
                return _cachedLicense;

            if (Debugger.IsAttached)
            {
                var debugLicense = new License
                {
                    LicensedTo = "Debug Mode",
                    ExpirationDate = DateTime.UtcNow.AddHours(2),
                    MaxResources = 50
                };
                _cachedLicense = debugLicense;
                _isValid = true;
                _lastError = null;
                return debugLicense;
            }

            var licenseJwt = Environment.GetEnvironmentVariable("AUTOSCALER_LICENSE");
            if (string.IsNullOrEmpty(licenseJwt))
            {
                _lastError = "AUTOSCALER_LICENSE environment variable is not set or is empty";
                _cachedLicense = CreateExpiredDefaultLicense();
                _isValid = false;
                return _cachedLicense;
            }

            try
            {
                var tokenHandler = new JwtSecurityTokenHandler();
                var validationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = false,
                    ValidateAudience = false,
                    ValidateLifetime = false,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = _publicKey,
                    ClockSkew = TimeSpan.Zero
                };

                tokenHandler.ValidateToken(licenseJwt, validationParameters, out SecurityToken validatedToken);

                if (validatedToken is not JwtSecurityToken jwtToken)
                {
                    _lastError = "Token validation succeeded but result is not a JWT token";
                    _cachedLicense = CreateExpiredDefaultLicense();
                    _isValid = false;
                    return _cachedLicense;
                }

                var licensedTo = jwtToken.Claims.FirstOrDefault(c => c.Type == "licensedTo")?.Value ?? "Unknown";
                var expirationDateStr = jwtToken.Claims.FirstOrDefault(c => c.Type == "exp")?.Value;
                var maxResourcesStr = jwtToken.Claims.FirstOrDefault(c => c.Type == "maxResources")?.Value;

                if (string.IsNullOrEmpty(expirationDateStr) || string.IsNullOrEmpty(maxResourcesStr))
                {
                    _lastError = $"Missing required claims in JWT token. exp: {(string.IsNullOrEmpty(expirationDateStr) ? "missing" : "present")}, maxResources: {(string.IsNullOrEmpty(maxResourcesStr) ? "missing" : "present")}";
                    _cachedLicense = CreateExpiredDefaultLicense();
                    _isValid = false;
                    return _cachedLicense;
                }

                var expirationDate = DateTimeOffset.FromUnixTimeSeconds(long.Parse(expirationDateStr)).UtcDateTime;
                var maxResources = int.Parse(maxResourcesStr);

                var license = new License
                {
                    LicensedTo = licensedTo,
                    ExpirationDate = expirationDate,
                    MaxResources = maxResources
                };

                _cachedLicense = license;
                _isValid = true;
                return license;
            }
            catch (SecurityTokenSignatureKeyNotFoundException ex)
            {
                _lastError = $"JWT signature validation failed: {ex.Message}. The license token may have been signed with a different private key.";
                _cachedLicense = CreateExpiredDefaultLicense();
                _isValid = false;
                return _cachedLicense;
            }
            catch (SecurityTokenException ex)
            {
                _lastError = $"JWT token validation failed: {ex.Message}";
                _cachedLicense = CreateExpiredDefaultLicense();
                _isValid = false;
                return _cachedLicense;
            }
            catch (FormatException ex)
            {
                _lastError = $"JWT token format error: {ex.Message}. The token may be malformed or corrupted.";
                _cachedLicense = CreateExpiredDefaultLicense();
                _isValid = false;
                return _cachedLicense;
            }
            catch (Exception ex)
            {
                _lastError = $"Unexpected error validating license: {ex.GetType().Name}: {ex.Message}";
                _cachedLicense = CreateExpiredDefaultLicense();
                _isValid = false;
                return _cachedLicense;
            }
        }

        private static License CreateExpiredDefaultLicense()
        {
            return new License
            {
                LicensedTo = "Unlicensed",
                ExpirationDate = DateTime.MinValue,
                MaxResources = 2
            };
        }

        private static RsaSecurityKey LoadPublicKey()
        {
            var assembly = System.Reflection.Assembly.GetExecutingAssembly();
            var resourceName = "poolautoscaler.licensing.public_key.pem";

            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream != null)
            {
                using var reader = new StreamReader(stream);
                var publicKeyPem = reader.ReadToEnd();
                var rsa = RSA.Create();
                rsa.ImportFromPem(publicKeyPem);
                return new RsaSecurityKey(rsa);
            }

            var dummyRsa = RSA.Create();
            return new RsaSecurityKey(dummyRsa);
        }

        // ---- Generation (CLI) ----

        /// <summary>
        /// Generates a license JWT from the given arguments. Validates inputs and reads
        /// the private key file. Does not perform any console I/O.
        /// </summary>
        public LicenseGenerationResult Generate(
            string privateKeyPath,
            string licensedTo,
            string expirationDateStr,
            string maxResourcesStr)
        {
            if (!File.Exists(privateKeyPath))
            {
                return new LicenseGenerationResult
                {
                    Success = false,
                    ErrorMessage = $"Private key file not found: {privateKeyPath}"
                };
            }

            if (!TryParseExpirationDate(expirationDateStr, out var expirationDateOffset, out var dateError))
            {
                return new LicenseGenerationResult
                {
                    Success = false,
                    ErrorMessage = dateError
                };
            }

            if (!int.TryParse(maxResourcesStr, out var maxResources) || maxResources < 1)
            {
                return new LicenseGenerationResult
                {
                    Success = false,
                    ErrorMessage = $"maxResources must be a positive integer: {maxResourcesStr}"
                };
            }

            try
            {
                var privateKeyPem = File.ReadAllText(privateKeyPath);
                var jwt = GenerateJwt(licensedTo, expirationDateOffset, maxResources, privateKeyPem);

                return new LicenseGenerationResult
                {
                    Success = true,
                    Jwt = jwt
                };
            }
            catch (Exception ex)
            {
                return new LicenseGenerationResult
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        private static string GenerateJwt(
            string licensedTo,
            DateTimeOffset expirationDate,
            int maxResources,
            string privateKeyPem)
        {
            using var rsa = RSA.Create();
            rsa.ImportFromPem(privateKeyPem);
            var signingKey = new RsaSecurityKey(rsa);

            var claims = new[]
            {
                new Claim("licensedTo", licensedTo),
                new Claim("maxResources", maxResources.ToString())
            };

            var tokenHandler = new JwtSecurityTokenHandler();
            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(claims),
                Expires = expirationDate.UtcDateTime,
                SigningCredentials = new SigningCredentials(signingKey, SecurityAlgorithms.RsaSha256)
            };

            var token = tokenHandler.CreateToken(tokenDescriptor);
            return tokenHandler.WriteToken(token);
        }

        /// <summary>
        /// Parses expiration date string (ISO 8601, e.g. with Z suffix).
        /// </summary>
        internal static bool TryParseExpirationDate(
            string expirationDateStr,
            out DateTimeOffset expirationDateOffset,
            out string? errorMessage)
        {
            expirationDateOffset = default;
            errorMessage = null;

            try
            {
                if (expirationDateStr.EndsWith("Z", StringComparison.OrdinalIgnoreCase))
                {
                    expirationDateOffset = DateTimeOffset.ParseExact(
                        expirationDateStr,
                        "yyyy-MM-ddTHH:mm:ssZ",
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.RoundtripKind | DateTimeStyles.AssumeUniversal);
                }
                else
                {
                    expirationDateOffset = DateTimeOffset.Parse(
                        expirationDateStr,
                        null,
                        DateTimeStyles.RoundtripKind);
                }

                return true;
            }
            catch (FormatException)
            {
                errorMessage = $"Invalid expiration date format: {expirationDateStr}. Expected ISO 8601 (e.g., 2025-12-31T23:59:59Z)";
                return false;
            }
        }
    }
}
