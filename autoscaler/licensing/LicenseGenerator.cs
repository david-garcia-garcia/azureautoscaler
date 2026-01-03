using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;

namespace poolautoscaler.licensing
{
    /// <summary>
    /// Helper class to generate license JWT tokens (for license generation service)
    /// </summary>
    public class LicenseGenerator
    {
        /// <summary>
        /// Generates a signed JWT license token
        /// </summary>
        /// <param name="licensedTo">Name of the licensee</param>
        /// <param name="expirationDate">Expiration date as DateTimeOffset</param>
        /// <param name="maxResources">Maximum number of resources</param>
        /// <param name="privateKeyPem">RSA private key in PEM format</param>
        /// <returns>JWT token string</returns>
        public static string GenerateLicense(
            string licensedTo,
            DateTimeOffset expirationDate,
            int maxResources,
            string privateKeyPem)
        {
            // Load private key
            using var rsa = RSA.Create();
            rsa.ImportFromPem(privateKeyPem);
            var signingKey = new RsaSecurityKey(rsa);

            // Create claims
            var claims = new[]
            {
                new Claim("licensedTo", licensedTo),
                new Claim("maxResources", maxResources.ToString())
            };

            // Create JWT
            var tokenHandler = new JwtSecurityTokenHandler();
            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(claims),
                Expires = expirationDate.UtcDateTime, // Set expiration via Expires property
                SigningCredentials = new SigningCredentials(signingKey, SecurityAlgorithms.RsaSha256)
            };

            var token = tokenHandler.CreateToken(tokenDescriptor);
            return tokenHandler.WriteToken(token);
        }
    }
}
