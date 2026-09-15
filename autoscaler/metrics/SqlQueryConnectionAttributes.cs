using Microsoft.Data.SqlClient;

namespace poolautoscaler.metrics
{
    /// <summary>Merges operator QueryConnection extras onto an app-owned SQL session string.</summary>
    internal static class SqlQueryConnectionAttributes
    {
        /// <summary>Keys that would steal host, catalog, or credentials from the implied session.</summary>
        private static readonly HashSet<string> ReservedKeys = new(StringComparer.OrdinalIgnoreCase)
        {
            "Server",
            "Data Source",
            "DataSource",
            "Address",
            "Addr",
            "Network Address",
            "Initial Catalog",
            "Database",
            "Host",
            "User ID",
            "UID",
            "User",
            "UserID",
            "Username",
            "Password",
            "Pwd",
            "Authentication",
            "Access Token",
            "AccessToken",
        };

        /// <summary>Rejects extras that would replace host, catalog, or credentials.</summary>
        /// <param name="connectionAttributes">Operator QueryConnection map, or null.</param>
        public static void RejectReserved(IReadOnlyDictionary<string, string>? connectionAttributes)
        {
            if (connectionAttributes == null)
            {
                return;
            }

            foreach (var key in connectionAttributes.Keys)
            {
                if (string.IsNullOrWhiteSpace(key) || ReservedKeys.Contains(key.Trim()))
                {
                    throw new Exception(
                        $"QueryConnection key '{key}' is reserved. Host, catalog, and credentials come from the resource and TokenCredential.");
                }
            }
        }

        /// <summary>Requires QueryConnection.ApplicationIntent to be ReadOnly or ReadWrite (Azure SQL Query rows).</summary>
        /// <param name="connectionAttributes">Operator QueryConnection map.</param>
        public static void RequireAzureSqlApplicationIntent(IReadOnlyDictionary<string, string>? connectionAttributes)
        {
            if (!TryReadAttribute(connectionAttributes, "ApplicationIntent", out var applicationIntent)
                || (!string.Equals(applicationIntent, "ReadOnly", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(applicationIntent, "ReadWrite", StringComparison.OrdinalIgnoreCase)))
            {
                throw new Exception(
                    "Azure SQL Query requires QueryConnection.ApplicationIntent set to ReadOnly or ReadWrite.");
            }
        }

        /// <summary>Builds an Azure SQL string. ApplicationIntent comes only from QueryConnection — the app does not inject it.</summary>
        /// <param name="host">Logical-server FQDN.</param>
        /// <param name="catalog">Database name or master.</param>
        /// <param name="connectionAttributes">Operator extras; must include ApplicationIntent for Azure SQL Query.</param>
        /// <returns>Driver connection string without a password.</returns>
        public static string BuildAzureSql(string host, string catalog, IReadOnlyDictionary<string, string>? connectionAttributes)
        {
            RejectReserved(connectionAttributes);
            var builder = new SqlConnectionStringBuilder
            {
                DataSource = $"tcp:{host},1433",
                InitialCatalog = catalog,
                Encrypt = true,
                TrustServerCertificate = false,
            };

            ApplyExtras(builder, connectionAttributes);
            return builder.ConnectionString;
        }

        /// <summary>Appends non-reserved extras to a PostgreSQL or MySQL base string.</summary>
        /// <param name="connectionString">App-owned OSS connection string.</param>
        /// <param name="connectionAttributes">Operator extras, or null.</param>
        /// <returns>Base string plus extras.</returns>
        public static string MergeOss(string connectionString, IReadOnlyDictionary<string, string>? connectionAttributes)
        {
            RejectReserved(connectionAttributes);
            if (connectionAttributes == null || connectionAttributes.Count == 0)
            {
                return connectionString;
            }

            var merged = connectionString;
            foreach (var attribute in connectionAttributes)
            {
                if (string.IsNullOrWhiteSpace(attribute.Key))
                {
                    continue;
                }

                merged += $";{attribute.Key.Trim()}={attribute.Value}";
            }

            return merged;
        }

        /// <summary>Reads one operator attribute by name, ignoring key casing.</summary>
        /// <param name="connectionAttributes">Operator extras, or null.</param>
        /// <param name="attributeName">Connection-string key.</param>
        /// <param name="attributeValue">Trimmed value when the method returns true.</param>
        /// <returns>True when the key is present and the value is not blank.</returns>
        private static bool TryReadAttribute(
            IReadOnlyDictionary<string, string>? connectionAttributes,
            string attributeName,
            out string attributeValue)
        {
            attributeValue = string.Empty;
            if (connectionAttributes == null)
            {
                return false;
            }

            foreach (var attribute in connectionAttributes)
            {
                if (!string.Equals(attribute.Key, attributeName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(attribute.Value))
                {
                    return false;
                }

                attributeValue = attribute.Value.Trim();
                return true;
            }

            return false;
        }

        /// <summary>Copies operator extras onto the Azure SQL builder.</summary>
        /// <param name="builder">App-owned Azure SQL builder.</param>
        /// <param name="connectionAttributes">Operator extras, or null.</param>
        private static void ApplyExtras(SqlConnectionStringBuilder builder, IReadOnlyDictionary<string, string>? connectionAttributes)
        {
            if (connectionAttributes == null)
            {
                return;
            }

            foreach (var attribute in connectionAttributes)
            {
                if (string.IsNullOrWhiteSpace(attribute.Key))
                {
                    continue;
                }

                builder[attribute.Key.Trim()] = attribute.Value;
            }
        }
    }
}
