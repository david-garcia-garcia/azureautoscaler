# Devdocs impact
change: sql-query-custom-metrics

## Units
- Query CustomMetrics — pattern — `autoscaler/configuration/CustomMetricConfig.cs`, `autoscaler/metrics/SqlQuerySessionFactory.cs`
- CustomMetricsPusher Query path — subsystem — `autoscaler/metrics/CustomMetricsPusher.cs`

## Findings
- [x] missing-packet  Query CustomMetrics — no packet; produced `core_metrics_custom_query.md`
- [x] missing-packet  CustomMetricsPusher Query path — folded into the same packet (one publication job)
