# Support QUERY-based custom synthetic metrics on all SQL backends (portal mirror metrics)

We need to support, in our synthetic metrics, for all SQL backends (PostgreSQL, Azure SQL Database, Azure Elastic Pool, MySQL) the ability to define custom synthetic metrics that use a QUERY to obtain the metric data.

The final purpose of this is to be able to push into the portal MIRROR metrics of what the portal already has for the main database: dtu, cpu, memory, data io (not logio because this is readonly).
