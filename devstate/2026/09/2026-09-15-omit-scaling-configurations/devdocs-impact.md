# Devdocs impact
change: omit-scaling-configurations

## Units
- Resource evaluation — subsystem — `autoscaler/resourcemanagement/ResourceProcessor.cs`, delta spec `core_resource_management_omit-scaling-configurations`
- Custom metrics on evaluation cycles — pattern — `CustomMetricsPusher.PushIfDueAsync` inside `RunLoop` (independent of scaling dictionary shape)

## Findings
- [x] missing-packet  Resource evaluation — no packet; catalog only lists `core` metrics domain

## Review
Verdict: in progress
