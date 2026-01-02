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

        public LicenseValidator()
        {
            // Load public key from embedded resource
            _publicKey = LoadPublicKey();
        }

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
            catch
            {
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
                
                using var rsa = RSA.Create();
                rsa.ImportFromPem(publicKeyPem);
                return new RsaSecurityKey(rsa);
            }

            // Fallback: create a dummy key (this should not happen in production)
            using var dummyRsa = RSA.Create();
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
