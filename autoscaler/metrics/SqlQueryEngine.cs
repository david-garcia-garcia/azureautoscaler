namespace poolautoscaler.metrics
{
    /// <summary>SQL engine used to open a Query custom-metric session.</summary>
    internal enum SqlQueryEngine
    {
        /// <summary>Azure SQL Database or Elastic Pool (Microsoft.Data.SqlClient).</summary>
        AzureSql,

        /// <summary>PostgreSQL Flexible Server (Npgsql).</summary>
        PostgreSql,

        /// <summary>MySQL Flexible Server (MySqlConnector).</summary>
        MySql,
    }
}
