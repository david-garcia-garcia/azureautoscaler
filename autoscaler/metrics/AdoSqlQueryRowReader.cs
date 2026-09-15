using Microsoft.Data.SqlClient;
using MySqlConnector;
using Npgsql;

namespace poolautoscaler.metrics
{
    /// <summary>Opens an ADO session with the engine driver and returns the first Query row.</summary>
    internal sealed class AdoSqlQueryRowReader : ISqlQueryRowReader
    {
        /// <inheritdoc />
        public async Task<IReadOnlyList<SqlQueryColumn>> ReadFirstRowAsync(
            SqlQueryConnectionPlan plan,
            string accessToken,
            string? entraUserName,
            string query,
            CancellationToken cancellationToken)
        {
            return plan.Engine switch
            {
                SqlQueryEngine.AzureSql => await this.ReadAzureSqlFirstRowAsync(plan, accessToken, query, cancellationToken),
                SqlQueryEngine.PostgreSql => await this.ReadPostgreSqlFirstRowAsync(plan, accessToken, entraUserName, query, cancellationToken),
                SqlQueryEngine.MySql => await this.ReadMySqlFirstRowAsync(plan, accessToken, entraUserName, query, cancellationToken),
                _ => throw new InvalidOperationException($"Unsupported SQL Query engine: {plan.Engine}."),
            };
        }

        /// <summary>Opens Azure SQL with AccessToken and ApplicationIntent already on the plan string.</summary>
        /// <param name="plan">Azure SQL connection plan.</param>
        /// <param name="accessToken">Token for https://database.windows.net/.default.</param>
        /// <param name="query">Operator SQL.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>First-row columns, or empty.</returns>
        private async Task<IReadOnlyList<SqlQueryColumn>> ReadAzureSqlFirstRowAsync(
            SqlQueryConnectionPlan plan,
            string accessToken,
            string query,
            CancellationToken cancellationToken)
        {
            await using var connection = new SqlConnection(plan.ConnectionString)
            {
                AccessToken = accessToken,
            };
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = query;
            command.CommandTimeout = (int)plan.CommandTimeout.TotalSeconds;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            return SqlQueryFirstRowMapper.ReadFirstRow(reader);
        }

        /// <summary>Opens PostgreSQL Flexible Server with the Entra token as the password.</summary>
        /// <param name="plan">PostgreSQL connection plan.</param>
        /// <param name="accessToken">Token for https://ossrdbms-aad.database.windows.net/.default.</param>
        /// <param name="entraUserName">Token display or app name required by Npgsql.</param>
        /// <param name="query">Operator SQL.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>First-row columns, or empty.</returns>
        private async Task<IReadOnlyList<SqlQueryColumn>> ReadPostgreSqlFirstRowAsync(
            SqlQueryConnectionPlan plan,
            string accessToken,
            string? entraUserName,
            string query,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(entraUserName))
            {
                throw new InvalidOperationException("PostgreSQL Query needs an Entra user name on the access token (preferred_username, upn, unique_name, or appid).");
            }

            var builder = new NpgsqlConnectionStringBuilder(plan.ConnectionString)
            {
                Username = entraUserName,
                Password = accessToken,
            };
            await using var connection = new NpgsqlConnection(builder.ConnectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = query;
            command.CommandTimeout = (int)plan.CommandTimeout.TotalSeconds;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            return SqlQueryFirstRowMapper.ReadFirstRow(reader);
        }

        /// <summary>Opens MySQL Flexible Server with the Entra token as the password.</summary>
        /// <param name="plan">MySQL connection plan.</param>
        /// <param name="accessToken">Token for https://ossrdbms-aad.database.windows.net/.default.</param>
        /// <param name="entraUserName">Token display or app name required by MySqlConnector.</param>
        /// <param name="query">Operator SQL.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>First-row columns, or empty.</returns>
        private async Task<IReadOnlyList<SqlQueryColumn>> ReadMySqlFirstRowAsync(
            SqlQueryConnectionPlan plan,
            string accessToken,
            string? entraUserName,
            string query,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(entraUserName))
            {
                throw new InvalidOperationException("MySQL Query needs an Entra user name on the access token (preferred_username, upn, unique_name, or appid).");
            }

            var builder = new MySqlConnectionStringBuilder(plan.ConnectionString)
            {
                UserID = entraUserName,
                Password = accessToken,
            };
            await using var connection = new MySqlConnection(builder.ConnectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = query;
            command.CommandTimeout = (int)plan.CommandTimeout.TotalSeconds;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            return SqlQueryFirstRowMapper.ReadFirstRow(reader);
        }
    }
}
