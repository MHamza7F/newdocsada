# Real infrastructure tab and industrial theme

## Result
- New `/infrastructure` route, available as standalone **View Gateway and Sensors** next to Live Monitoring, not hidden in a collapsed group.
- Uses existing local API inventory and the shared SignalR telemetry stream. No demo records, random readings, simulated clocks or fabricated trends.
- Device inventory refreshes every five seconds. Real registered NORVI/gateway records and active sensors are shown; deleted selections are cleared on refresh.
- Device/sensor searches, type/status/category filters, grouped cards, sticky back navigation and sensor detail/history are included.
- Sensor cards require connected telemetry, API online status, actual fresh samples and configured metric keys. Stale/disconnected samples do not appear as live values. Freshness currently uses the shared 60-second window.
- Add device uses the existing gateway configuration flow; the action requires Devices.Add. The page requires Devices tab and Devices.View permissions.
- Hardware photos copied from the supplied design reference. Reference simulation JavaScript was not copied.
- Site-wide navy/teal theme adapts shared cards, controls, tables, navigation and modals for light/dark mode. Saved user choice wins over OS preference; otherwise the theme follows the OS.
- Shared live telemetry now exposes detached snapshots and uses actual source timestamps, rejecting non-finite, out-of-order and far-future readings.

## Verification and boundaries
- Web Release build passed with zero warnings/errors.
- Existing security/local API tests: 75 passed. Engine tests: 45 passed. These suites do not establish visual correctness of the new page.
- `node tests/theme-regression.cjs` exercises saved/system preferences, pre-paint selection and subsequent preference changes.
- No MAUI/mobile edits, real database writes, physical hardware operations or source-demo changes were performed for this feature.
- Live browser/physical gateway acceptance remains required: add NORVI/gateway through existing workflow; confirm it appears within a refresh interval; open its sensors and compare real readings with the original monitoring page; unplug/reconnect and check stale suppression.
- Visually review all existing pages and modals in both themes at desktop and mobile widths. CSS/build checks are not a pixel-perfect or accessibility audit.
- Category grouping is inferred from driver metadata/name because the current DTO has no explicit category field; unknown sensors are Other.
- Sensor history is displayed as actual timestamp/value samples, not the reference's simulated sparkline. Existing acquisition and Modbus settings are unchanged.

## Navigation/content follow-up
- Fixed missing `scada_demo_test.Web.styles.css` host link: the new page's scoped content styling now loads.
- Added versioned stylesheet/theme script URLs so changed assets invalidate browser caches.
- Applied additional content panel/table/form/modal palette corrections and theme-aware destructive action colors.
- Fixed sidebar sign-out to use the protected POST form, matching the existing account endpoint.
- Source-contract checks verify standalone navigation, isolated CSS loading and real API/sensor snapshot bindings; six theme preference scenarios pass.
- Web Release build to isolated temporary output passes with zero errors/warnings. The running Web process locks normal build output; stop/restart that process to serve the new UI. It was not forcibly stopped.
- No browser visual acceptance or hardware tests were run for this follow-up; MAUI/mobile remains unchanged.
