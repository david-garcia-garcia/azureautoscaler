Developer review: in progress — 2026-09-15T09:06:37.1870566Z

IssueKey: 2026-09-15-sql-query-synthetic-metrics
JobName: 2026-09-15-sql-query-synthetic-metrics

## What this changes
**Operators.** Can declare `Query` on SQL `CustomMetrics` (XOR `DataExpression`). Connection uses the process identity plus `ApplicationIntent=ReadOnly`. Docs require a SQL user (`CREATE USER FROM EXTERNAL PROVIDER`) and `GRANT VIEW DATABASE STATE`.

**Admin users.** None.

**Developers.** `CustomMetricConfig.Query` + `SqlQuerySessionFactory` publish each first-row numeric column through `CustomMetricsPusher`. Four SQL types only. Implied catalogs: ResourceId database / `master` / `postgres` / `mysql`.

**End users.** None.

## Motivation
On `1.x` CustomMetrics only evaluates DataExpression; SQL CustomMetric throws; there is no SQL client. Operators cannot publish portal-mirror DTU/CPU/memory/data I/O from a QUERY. Without this apply, dashboards stay on native Monitor series that lag or miss replicas.

```mermaid
flowchart LR
  Q[CustomMetrics Query] --> F[SqlQuerySessionFactory]
  F --> P[CustomMetricsPusher]
  P --> AM[Azure Monitor]
```

## Merge readiness
Implement complete; local tests passed. Code review is next. 4 workflow phases remain.

Priority: P2 — operator and dashboard parity pain with partial native-metric workarounds today.

Reviewed head: 7a1fbdd
Owner decision: Required. See Explore Decisions.

## Review scores
| Measure | Result | What it means |
| --- | --- | --- |
| Overall readiness | 6/6 | Local tests passed; review and archive remain |
| CI proof | N/A | prHost local |
| Local tests proof | 6 | passed — 429 tests |
| Review resolution | N/A | No OPEN PR |

## Verification
| Check | Result | Evidence |
| --- | --- | --- |
| Branch | 2026-09-15-sql-query-synthetic-metrics not pushed | git |
| OpenSpec | sql-query-custom-metrics | tasks all [x] |
| Pull request | none | prHost local |
| CI | N/A | prHost local |
| Local tests | passed | `dotnet test autoscalertests/poolautoscaler.tests.csproj` 429 passed |
| PR comments | no comments | comments none |

## Specs
- [core_metrics_custom_query](openspec/changes/sql-query-custom-metrics/proposal.md) — added

## Deviations from the ask
- taken: ticket “synthetic metrics that use a QUERY” → extend `CustomMetrics` with exclusive Query — `autoscaler/configuration/CustomMetricConfig.cs` — avoid a parallel SyntheticMetrics tree. Requester: confirmed.

## Follow-up issues
- [ ] [Rename `CustomMetric()` vs `CustomMetrics` stems](knowledge/debt/2026-09-15-rename-custommetric-vs-custommetrics.md) — `CustomMetric()` and `CustomMetrics` share a stem but are two jobs.
- [ ] [Rename live spec ids to 4-part](knowledge/debt/2026-09-15-rename-live-spec-ids-to-4-part.md) — live `openspec/specs/` ids are kebab-case, not 4-part.

## How this fits together
Local ticket → branch `2026-09-15-sql-query-synthetic-metrics` → Query apply landed → no remote PR or push.

## Explore Decisions
| Question | Rank | Decision | By |
| --- | --- | --- | --- |
| Do QUERY metrics feed scaling (`Metrics` / `custom_*`), push-only (`CustomMetrics`), or both? | additive asked | assumed — push-only via `CustomMetrics`. Do not wire QUERY into SQL `CustomMetric()`. | explore |

## Before merge
- [ ] Code review seven axes
- [ ] Archive change after impact

## Findings
None.

## Axis review
None.

## Agent review details

### Review metrics
| Metric | Value | Why it matters |
| --- | --- | --- |
| Specs in this PR | 1 added / 0 modified | core_metrics_custom_query |
| Open reviewer comments walked | 0 open | No PR inventory |
| Reviewed head | 7a1fbdd | Implement bus commit |

### Stored data model
None.

### Technical review
Best possible solution: Query XOR DataExpression on CustomMetrics, one session factory, first-row numeric columns.

Do we have a high-confidence way to reproduce? Yes — 429 local tests including Query XOR, catalogs, pusher columns, CustomMetric still throws.

Is this the best way to solve the issue? Yes versus 1.x.

### Evidence
What I checked:
- handoff.yaml localTests: passed
- tasks.md all [x]
- `dotnet test` 429 passed (implement worker)

### Rank-up moves
None.
