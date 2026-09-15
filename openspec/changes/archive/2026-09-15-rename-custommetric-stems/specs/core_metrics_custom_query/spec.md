## MODIFIED Requirements

### Requirement: Query XOR DataExpression on CustomMetrics
A CustomMetrics YAML entry (bound to `PublishedMetricConfig` in code) SHALL supply exactly one value source: `Query` or `DataExpression`. The product SHALL NOT add a `SyntheticMetrics` configuration type. `Name` SHALL be required only when the value source is `DataExpression`. A `Query` entry SHALL NOT require `Name`. Startup validation SHALL reject a CustomMetrics entry that has both sources, neither source, or a blank `Query`/`DataExpression`. Startup validation SHALL reject `Query` on any resource that is not Azure SQL Database, Azure SQL Elastic Pool, PostgreSQL Flexible Server, or MySQL Flexible Server.

#### Scenario: DataExpression-only row
- **WHEN** a CustomMetrics entry has `DataExpression` and no `Query`
- **THEN** startup validation succeeds and `PublishedMetricsPusher` evaluates the expression when due

#### Scenario: Query-only row
- **WHEN** a CustomMetrics entry has `Query` and no `DataExpression`
- **THEN** startup validation succeeds and `PublishedMetricsPusher` runs the query when due

### Requirement: QUERY publish path
QUERY results SHALL be published only through the existing CustomMetrics push path (`PublishedMetricsPusher`). This change SHALL NOT implement SQL `GatherScalingCustomMetric()` for in-process gather of `custom_*` scaling metric names. Existing Azure Monitor scaling metrics SHALL keep working.

#### Scenario: Query does not implement SQL gather hook
- **WHEN** a scaling Metric name starts with `custom_` on a SQL resource
- **THEN** gather uses `GatherScalingCustomMetric` which remains unimplemented on SQL types

#### Scenario: Query publishes through CustomMetrics YAML
- **WHEN** a SQL resource has a due Query CustomMetrics entry that returns numeric columns
- **THEN** `PublishedMetricsPusher` POSTs one series per numeric column
