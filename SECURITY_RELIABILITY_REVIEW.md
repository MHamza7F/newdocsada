# SCADA reliability and security review

Date: 2026-10-10

## Scope and safety

Focused source-level repairs to Modbus reliability, authentication, controller permissions, dynamic-page failure handling, and shared navigation. Existing uncommitted changes were preserved. No live application, database, Firebase service, or physical gateway was started or modified for verification. No commits were made. This is not certification that the entire system is vulnerability-free or crash-free.

## Completed repairs

- TCP polling no longer marks a reachable gateway offline merely because a scan made its last-seen timestamp stale.
- Empty-gateway probe failures now respect their 10-second cadence rather than repeating each 200-ms worker cycle.
- Polling skips scan-owned endpoint leases instead of blocking unrelated gateways.
- Cancelled, malformed, timed-out and partially consumed Modbus responses invalidate contaminated TCP sessions; shutdown cancellation does not count as a sensor failure.
- Scanner nullable paths are guarded; unused report state was removed.
- JWT validation shares issuance configuration, pins HS256, validates issuer/audience, and rejects missing, short or known public fallback signing keys.
- Refresh rotation binds user/JWT identity, consumes tokens atomically, respects lockout/revocation and preserves the original session deadline.
- Isolated bounded server-side Web sessions replace process-global user tokens. Refresh uses the API origin and a fresh request for one retry.
- Authorization no longer trusts caller-supplied role headers. Previously uncovered controller actions now require defined action permissions.
- Login/logout validate antiforgery tokens; logout is POST. Production cookies require HTTPS and detailed authentication errors are not exposed.
- All website Firebase registrations, calls and fallbacks were removed. Devices, readings, history, tanks, sites, audit, users/roles and firmware metadata now use local API/SQLite paths. History uses actual range-filtered API statistics, not fabricated min/max or identifiers.
- Added authorized local readings and firmware metadata controllers. Firmware OTA returns an explicit unsupported result; metadata is not a binary upload and has no fabricated checksum.
- Users, audit and multi-site pages display recoverable load errors rather than terminating the Blazor circuit. Failed protected loads clear stale data; user and role loads are independent.
- Shared sidebar collapse, mobile drawer state, navigation dismissal, focus containment/restoration, reduced motion and responsive spacing were repaired.

## Final verification

Executed serially against the final code:

```powershell
dotnet test "tests/ScadaEngine.Tests/ScadaEngine.Tests.csproj" --no-restore --verbosity minimal
dotnet test "tests/Auth.Security.Tests/Auth.Security.Tests.csproj" --no-restore --verbosity minimal
dotnet build "src/scada_demo_test.API/scada_demo_test.API.csproj" --no-restore --configuration Release --verbosity minimal
dotnet build "src/scada_demo_test.Web/scada_demo_test.Web.csproj" --no-restore --configuration Release --verbosity minimal
node --check "src/scada_demo_test.Web/wwwroot/js/sidebar.js"
git diff --check
```

- Engine suite: **45 passed**, none failed/skipped.
- Security/local-API suite: **75 passed**, none failed/skipped.
- API and Web Release builds: **0 warnings, 0 errors** each.
- JavaScript syntax and whitespace checks passed. Git emitted line-ending conversion notices, not whitespace failures.
- Security tests include in-memory framework/service tests and source-contract coverage; they are not a hosted browser penetration test.
- Modbus tests use loopback TCP, not physical serial equipment. UI agent also ran mocked-DOM drawer behavior checks.

## Deployment requirements and unresolved risks

1. **JWT configuration:** provide a unique high-entropy `Jwt:Key` of at least 32 UTF-8 bytes through private configuration, e.g. `Jwt__Key`. Never commit it. Actual private configuration was not inspected. API startup intentionally fails closed if the key is unsafe.
2. **Production HTTPS:** Web authentication and antiforgery cookies are Secure outside Development. Verify TLS and reverse-proxy settings. Plain HTTP remains available in Development only for these cookie flows.
3. **Machine telemetry authentication remains unresolved:** existing push firmware supplies no credential. Source IP and external ID are attribution, not authentication. Agree on device provisioning/credential support or an authenticated ingress boundary before changing this protocol; imposing user JWTs would break current devices.
4. **MAUI excluded:** no MAUI/mobile files changed. Shared Domain Firebase service/package remains solely for compatibility with unchanged MAUI consumers; the website no longer registers or calls it. No remote Firebase data was deleted or copied to SQLite.
5. **Token lifecycle:** database refresh tokens remain plaintext; existing access JWTs can remain valid until expiry. Immediate permission/security-stamp revocation and refresh-token-family reuse detection are not implemented.
6. **Session storage:** in-memory sessions require sign-in after restart and shared storage for multi-instance deployments. Offline logout cannot guarantee remote refresh-token revocation.
7. **Role compatibility:** action permissions, not tab visibility alone, now govern covered API actions. Tank actions reuse Devices permissions. Loading the role catalog requires Roles.Manage.
8. **Performance:** fixes avoid evidenced retry/lease stalls, but no measured production throughput/latency improvement is claimed. Physical Modbus reliability also depends on baud/parity, wiring, duplicate addresses, bridge behavior and network loss.
9. **Firmware limitations:** release metadata persists locally; real binary storage, signature verification and hardware OTA are not implemented. Unsupported rollout no longer reports fake success. Website read failures surface explicitly, and affected page initialization/refresh paths now provide recoverable errors.

## Required live acceptance checks

- Use a test deployment and separately provisioned credentials; do not expose the current telemetry endpoint publicly without an authenticated boundary.
- Verify login, remember-me, refresh, cross-user isolation and POST logout in the actual browser. Tampered/missing antiforgery tokens must fail without changing authentication.
- Exercise least-privilege users across users/roles, devices/sensors, sites, reports/exports and alerts. Confirm permission denial never writes to Firebase.
- Observe actual gateway/sensor readings long enough to include the intermittent dropout interval. Run a scan concurrently; confirm other gateways continue polling and no false offline flicker occurs. Record timestamped latency/failure counts without credentials.
- Verify intentional gateway disconnection still becomes offline; do not hide real stale readings to make the UI look healthy.
- Check sidebar at 320, 768 and 769 pixels, desktop collapse with open groups, keyboard-only navigation, route changes and both themes.

Backend decision completed: existing local API/SQLite is authoritative for the website. Next live checks require device credential/ingress provisioning, real gateway observation and browser acceptance testing. Shared MAUI dependencies are intentionally outside this change.
