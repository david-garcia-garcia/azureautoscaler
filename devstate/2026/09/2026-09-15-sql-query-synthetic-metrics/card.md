Developer review: ready for review — 2026-09-15T09:14:53.3165619Z

IssueKey: 2026-09-15-sql-query-synthetic-metrics
JobName: 2026-09-15-sql-query-synthetic-metrics

## What this changes
**Operators.** Can add a `Query` entry under SQL `CustomMetrics` (XOR `DataExpression`). The app builds a read-only Entra session (Azure SQL: `ApplicationIntent=ReadOnly`; PostgreSQL/MySQL: TLS `VerifyFull`). Create the process identity as a SQL user and `GRANT VIEW DATABASE STATE`. Elastic Pool connects to `master` — `sys.dm_db_resource_stats` there measures master, not the pool.

**Admin users.** None.

**Developers.** `CustomMetricConfig.Query`, `SqlQuerySessionFactory`, first-row numeric columns via `CustomMetricsPusher`. Live spec `core_metrics_custom_query`. Usage packet `knowledge/devdocs/core_metrics_custom_query.md`. Archived change `openspec/changes/archive/2026-09-15-sql-query-custom-metrics`.

**End users.** None.

## Motivation
On `1.x` CustomMetrics only evaluates DataExpression and SQL CustomMetric throws. Operators cannot publish portal-mirror DTU, CPU, memory, and data I/O from SQL. Without this branch those series stay missing or diverge from the portal, especially on readable secondaries.

```mermaid
flowchart LR
  Q[Query CustomMetrics] --> F[SqlQuerySessionFactory]
  F --> P[CustomMetricsPusher]
  P --> AM[Azure Monitor custom namespace]
```

## Merge readiness
Local workflow complete. No remote PR (prHost local, no push). 432 tests passed.

Priority: P2 — operator and dashboard parity pain with partial native-metric workarounds today.

Reviewed head: e68cf57
Owner decision: Required. See Explore Decisions.

## Review scores
| Measure | Result | What it means |
| --- | --- | --- |
| Overall readiness | 6/6 | localTests passed; local card written |
| CI proof | N/A | prHost local |
| Local tests proof | 6 | passed — 432 tests |
| Review resolution | N/A | No OPEN PR |

## Verification
| Check | Result | Evidence |
| --- | --- | --- |
| Branch | 2026-09-15-sql-query-synthetic-metrics not pushed | git (human: no push) |
| OpenSpec | sql-query-custom-metrics archived | openspec/changes/archive/2026-09-15-sql-query-custom-metrics |
| Pull request | none | prHost local |
| CI | N/A | prHost local |
| Local tests | passed | `dotnet test` 432 passed |
| PR comments | no comments | comments none |

## Specs
- [core_metrics_custom_query](openspec/changes/archive/2026-09-15-sql-query-custom-metrics/proposal.md) — added

## Deviations from the ask
- taken: ticket “synthetic metrics that use a QUERY” → extend `CustomMetrics` with exclusive Query — `autoscaler/configuration/CustomMetricConfig.cs` — avoid a parallel SyntheticMetrics tree. Requester: confirmed.

## Follow-up issues
- [ ] [Rename `CustomMetric()` vs `CustomMetrics` stems](knowledge/debt/2026-09-15-rename-custommetric-vs-custommetrics.md) — `CustomMetric()` and `CustomMetrics` share a stem but are two jobs.
- [ ] [Rename live spec ids to 4-part](knowledge/debt/2026-09-15-rename-live-spec-ids-to-4-part.md) — live `openspec/specs/` ids are kebab-case, not 4-part. `validate-artifact-names` stays dirty on those folders.

## How this fits together
Local ticket → branch `2026-09-15-sql-query-synthetic-metrics` from `1.x` → apply + review + archive → durable card at `devstate/2026/09/2026-09-15-sql-query-synthetic-metrics/card.md`. No remote git.

## Explore Decisions
| Question | Rank | Decision | By |
| --- | --- | --- | --- |
| Do QUERY metrics feed scaling (`Metrics` / `custom_*`), push-only (`CustomMetrics`), or both? | additive asked | assumed — push-only via `CustomMetrics`. Do not wire QUERY into SQL `CustomMetric()`. | explore |

## Before merge
None.

## Findings
None.

## Axis review
`codereview_standards.md` — 5 total, 0 pending, 5 completed
`codereview_nitpicks.md` — 1 total, 0 pending, 1 completed
`codereview_spec.md` — 0 total, 0 pending, 0 completed
`codereview_security.md` — 1 total, 0 pending, 1 completed
`codereview_performance.md` — 0 total, 0 pending, 0 completed
`codereview_dead.md` — 0 total, 0 pending, 0 completed
`codereview_coverage.md` — 4 total, 0 pending, 2 completed, 2 skipped

## Agent review details

### Review metrics
| Metric | Value | Why it matters |
| --- | --- | --- |
| Specs in this PR | 1 added / 0 modified | core_metrics_custom_query |
| Open reviewer comments walked | 0 open | No PR inventory |
| Reviewed head | e68cf57 | Last product commit before this card Set |

### Stored data model
None.

### Technical review
Best possible solution: Query XOR DataExpression on CustomMetrics, implied catalogs, one session factory, first-row numeric publish.

Do we have a high-confidence way to reproduce? Yes — 432 local tests.

Is this the best way to solve the issue? Yes versus `1.x`.

### Evidence
What I checked:
- `dotnet test` 432 passed
- openspec archive `2026-09-15-sql-query-custom-metrics`
- validate-spec-map OK; validate-artifact-names dirty on legacy kebab specs only
- Pin `origin/1.x` a3f37c5

### Rank-up moves
None.
