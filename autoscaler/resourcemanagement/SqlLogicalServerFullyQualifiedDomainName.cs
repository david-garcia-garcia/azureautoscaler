using Azure.Core;
using Azure.ResourceManager;
using Azure.ResourceManager.Sql;

namespace poolautoscaler.resourcemanagement
{
    /// <summary>Reads the logical-server FQDN from ARM for a SQL Database or Elastic Pool child resource.</summary>
    internal static class SqlLogicalServerFullyQualifiedDomainName
    {
        /// <summary>Gets FullyQualifiedDomainName from the parent SQL server of <paramref name="sqlChildResourceId"/>.</summary>
        /// <param name="client">ARM client used for the server GET.</param>
        /// <param name="sqlChildResourceId">Database or elastic-pool ARM id whose parent is the logical server.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Server FQDN, or null when the child has no parent.</returns>
        public static async Task<string?> ReadAsync(
            ArmClient client,
            ResourceIdentifier sqlChildResourceId,
            CancellationToken cancellationToken)
        {
            var parentServerId = sqlChildResourceId.Parent;
            if (parentServerId == null)
            {
                return null;
            }

            var server = await client.GetSqlServerResource(parentServerId).GetAsync(cancellationToken: cancellationToken);
            return server.Value.Data.FullyQualifiedDomainName;
        }
    }
}
