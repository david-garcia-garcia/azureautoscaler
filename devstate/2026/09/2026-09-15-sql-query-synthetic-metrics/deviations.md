# Deviations

- [ ] proposed  CustomMetrics + Query instead of a new SyntheticMetrics type
  Asked: ticket/source.md — “support, in our synthetic metrics … custom synthetic metrics that use a QUERY”
  Instead: extend existing `CustomMetrics` / `CustomMetricConfig` with an exclusive Query value source.
  Owner: `autoscaler/configuration/CustomMetricConfig.cs`
  Why: honouring “synthetic” as its own config tree would add a parallel publication unit next to `CustomMetrics`, whose job is already operator-defined numbers pushed to Azure Monitor.
  By: explore
  Requester: not asked
