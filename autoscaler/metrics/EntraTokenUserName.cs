using System.IdentityModel.Tokens.Jwt;

namespace poolautoscaler.metrics
{
    /// <summary>Reads the Entra display or app name from an access token when a SQL driver requires a user name.</summary>
    internal static class EntraTokenUserName
    {
        /// <summary>
        /// Returns the token identity name used as the PostgreSQL/MySQL login, or null when no claim is present.
        /// </summary>
        /// <param name="accessToken">Bearer token issued for the OSS RDBMS or Azure SQL scope.</param>
        /// <returns>preferred_username, upn, unique_name, or appid; null when none are set.</returns>
        public static string? TryRead(string accessToken)
        {
            if (string.IsNullOrWhiteSpace(accessToken))
            {
                return null;
            }

            var jwt = new JwtSecurityTokenHandler().ReadJwtToken(accessToken);
            return FirstClaim(jwt, "preferred_username")
                ?? FirstClaim(jwt, "upn")
                ?? FirstClaim(jwt, "unique_name")
                ?? FirstClaim(jwt, "appid");
        }

        /// <summary>Returns the first claim value for the given type, or null when missing or blank.</summary>
        /// <param name="jwt">Decoded access token.</param>
        /// <param name="claimType">Claim type to read.</param>
        /// <returns>Claim value, or null.</returns>
        private static string? FirstClaim(JwtSecurityToken jwt, string claimType)
        {
            var claim = jwt.Claims.FirstOrDefault(c => c.Type == claimType);
            return string.IsNullOrWhiteSpace(claim?.Value) ? null : claim.Value;
        }
    }
}
