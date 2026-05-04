# Knowledge staging

Items below are unchecked until processed by `knowledge-archive`.

## Sources

- [ ] **C# DTO optional diagnostics & deferred logging (.NET 8)**
  - scope: internal
  - type: research
  - description: Patterns for attaching optional diagnostic/structured data to DTOs, single log at significant events, ILogger message templates vs multi-line strings, avoiding duplicate logs, separating raw evaluation data from table formatting.
  - local: knowledge/staging/internal/dotnet-dto-deferred-diagnostic-logging.md
  - urls: https://learn.microsoft.com/en-us/dotnet/core/extensions/logging, https://learn.microsoft.com/en-us/dotnet/core/extensions/high-performance-logging
  - from-topic: Research for C# .NET 8 — optional diagnostic text/structured data on DTOs for deferred logging on scale operations; string vs lines vs object; structured logging; deduplication; data vs presentation.

- [ ] **Background scale operation logging (ResourceProcessor)**
  - scope: internal
  - type: codebase
  - description: Where scale completion is logged, background Task.Run flow, scoped logger; duration formatting note for HH:mm:ss across multi-day spans.
  - local: knowledge/staging/internal/scale-operation-background-logging.md
  - from-topic: Include elapsed time (HH:mm:ss) on "Scale operation completed successfully" log line.
