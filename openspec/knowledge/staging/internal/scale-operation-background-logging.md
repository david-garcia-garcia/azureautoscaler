# Scale operation (background) logging

## Overview

- Scale applies run in a **fire-and-forget** `Task.Run` after patch preparation; informational logs use `resState.Logger` (scoped resource logger — explains prefix like `sqlsrvdatosetg_pools_sqlpool-default[0]` in output).

## Flow

1. `logger.LogInformation("Dispatching scale operation (background)")` (sync path).
2. Background task: `Starting scale operation (background)` → `await resState.ApplyChanges(patchOp, stoppingToken)` → `LastScale = getUtcNow()` → `Scale operation completed successfully` (or cancel/error branches).

## Key file

- `autoscaler/resourcemanagement/ResourceProcessor.cs` — background block ~lines 487–512.

## Implementation note (duration HH:mm:ss)

- Capture `var start = getUtcNow()` immediately before/after the start log (or before `ApplyChanges`).
- After `ApplyChanges`, compute `var elapsed = getUtcNow() - start`.
- For elapsed **duration** in `HH:mm:ss` where operations may exceed 24h, prefer total hours: `(int)elapsed.TotalHours`, `elapsed.Minutes`, `elapsedSeconds` — **not** `elapsed.Hours` alone (that is the 0–23 component within days).
- Use structured logging: e.g. template `"Scale operation completed successfully after {Duration}"` with formatted string, or pass `elapsed` and format in template per project conventions.
