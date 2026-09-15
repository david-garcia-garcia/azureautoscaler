## 1. ResourceState throttle

- [x] 1.1 Add `LastNoScalingConfigurationsMessageLogged` (or equivalent) on `ResourceState` beside `LastDisabledMessageLogged`.

## 2. ResourceProcessor loop ordering and disable

- [x] 2.1 Extract throttled disabled Information helper; call from `ProcessOneAsync` early return and post-refresh disable in `RunLoop`.
- [x] 2.2 In `ProcessOneAsync`, return before `RunLoop` when `IsDisabled()` is true.
- [x] 2.3 Reorder `RunLoop`: run refresh and push before enumerating scaling configurations; treat null like empty for scaling skip.
- [x] 2.4 Emit hourly Information when scaling dictionary is null or empty after refresh/push; keep Trace for inactive time-window case on non-empty dictionary.

## 3. Tests

- [x] 3.1 Add test with `ScalingConfigurations = null`: no throw, refresh runs, optional Information verify on mock logger.
- [x] 3.2 Add test: after synthetic unhandled-exception disable, second `ProcessOneAsync` within the hour skips refresh/scaling and does not repeat failure logging.
