using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace poolautoscaler.licensing
{
    /// <summary>
    /// Validates license JWT tokens and provides license information
    /// </summary>
    public class LicenseValidator
    {
        private readonly RsaSecurityKey _publicKey;
        private License? _cachedLicense;
        private bool _isValid;
        private string? _lastError;

        public LicenseValidator()
        {
            // Load public key from embedded resource
            _publicKey = LoadPublicKey();
        }

        /// <summary>
        /// Gets the last validation error message, if any
        /// </summary>
        public string? LastError => _lastError;

        /// <summary>
        /// Validates and loads a license from the JWT token in environment variable
        /// </summary>
        /// <returns>License object if valid, expired default license if invalid or not present</returns>
        public License? ValidateAndLoadLicense()
        {
            if (_cachedLicense != null)
            {
                return _cachedLicense;
            }

            var licenseJwt = Environment.GetEnvironmentVariable("AUTOSCALER_LICENSE");

            // If no license is provided, return expired default license
            if (string.IsNullOrEmpty(licenseJwt))
            {
                _lastError = "AUTOSCALER_LICENSE environment variable is not set or is empty";
                _cachedLicense = CreateExpiredDefaultLicense();
                _isValid = false;
                return _cachedLicense;
            }

            try
            {
                // Verify and decode JWT
                var tokenHandler = new JwtSecurityTokenHandler();
                var validationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = false,
                    ValidateAudience = false,
                    ValidateLifetime = false, // We'll check expiration manually
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = _publicKey,
                    ClockSkew = TimeSpan.Zero
                };

                var principal = tokenHandler.ValidateToken(licenseJwt, validationParameters, out SecurityToken validatedToken);
                
                if (validatedToken is not JwtSecurityToken jwtToken)
                {
                    _lastError = "Token validation succeeded but result is not a JWT token";
                    _cachedLicense = CreateExpiredDefaultLicense();
                    _isValid = false;
                    return _cachedLicense;
                }

                // Extract claims
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

                // Parse expiration date (JWT exp is Unix timestamp)
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

        /// <summary>
        /// Checks if the license is expired
        /// </summary>
        public bool IsExpired(License license)
        {
            return DateTime.UtcNow > license.ExpirationDate;
        }

        /// <summary>
        /// Gets whether the license signature is valid
        /// </summary>
        public bool IsValid => _isValid;

        private RsaSecurityKey LoadPublicKey()
        {
            // Try to load from embedded resource first
            var assembly = System.Reflection.Assembly.GetExecutingAssembly();
            var resourceName = "poolautoscaler.licensing.public_key.pem";

            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream != null)
            {
                using var reader = new StreamReader(stream);
                var publicKeyPem = reader.ReadToEnd();
                
                // Create RSA without disposing - RsaSecurityKey needs it for the lifetime of the validator
                // The RSA will be disposed when the validator is garbage collected
                var rsa = RSA.Create();
                rsa.ImportFromPem(publicKeyPem);
                return new RsaSecurityKey(rsa);
            }

            // Fallback: create a dummy key (this should not happen in production)
            var dummyRsa = RSA.Create();
            return new RsaSecurityKey(dummyRsa);
        }

        public License CreateExpiredDefaultLicense()
        {
            return new License
            {
                LicensedTo = "Unlicensed",
                ExpirationDate = DateTime.MinValue,
                MaxResources = 1
            };
        }
    }
}
