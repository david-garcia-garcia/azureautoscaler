# C# / .NET 8 — optional diagnostics on DTOs for deferred logging

## Shape of attached data

- **Prefer structured records** (immutable DTOs, `IReadOnlyList<T>` of row/value types) for anything you might query, test, or re-log; keep primitive scalars and small POCOs — not pre-rendered prose.
- **`IReadOnlyList<string>`** only when lines are truly unstructured narrative; still risk presentation leaking into the domain.
- **Single optional `string`** is appropriate for one short human note; avoid stuffing tables or multi-section dumps into it.

## ILogger / multi-line

- Use **message templates with named placeholders** (`LogInformation("Scale {Action} for {ResourceId}", ...)`) so sinks (Application Insights, Serilog, OpenTelemetry) get **separate properties**; avoid `$"..."` for structured fields ([Logging in .NET](https://learn.microsoft.com/en-us/dotnet/core/extensions/logging)).
- **Multi-line in one message** is usually worse for indexing; either log **one event** with **structured properties** (JSON-serializable fragments, counts, key metrics) or attach a **single diagnostic blob property** your sink renders — not repeated `Environment.NewLine` tables in the template text.
- For hot paths, **`LoggerMessage` source generators** reduce allocation ([high-performance logging](https://learn.microsoft.com/en-us/dotnet/core/extensions/high-performance-logging)).

## Duplicate logging

- **Accumulate diagnostics on the in-memory DTO/state** during evaluation; emit **one log call** at the boundary (e.g. scale commit) that includes correlation IDs and the deferred payload — intermediate steps should not log the same facts unless levels differ (`Trace` vs `Information`).
- Optionally use **`ILogger.BeginScope`** with a small dictionary for correlation + deferred keys so one outer log carries context without repeating parameters.

## Data vs presentation

- Store **raw grids** as `IReadOnlyList<MetricEvaluationRow>` (or similar) with numeric/bool/enum fields; **format tables at log time** (or in a custom `ITextFormatter` / enricher) so DTOs stay presentation-free and tests assert on data, not string shape.

## References (external)

- https://learn.microsoft.com/en-us/dotnet/core/extensions/logging
- https://learn.microsoft.com/en-us/dotnet/core/extensions/high-performance-logging
