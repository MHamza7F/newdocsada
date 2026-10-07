# DB SWITCH: Supabase (PostgreSQL) → Local SQL Server (LocalDB)

Switched on **2026-09-22** per user request. All Supabase/Postgres code is
COMMENTED OUT (not deleted) so it can be reverted. Original Postgres migrations
are backed up at `backup_postgres_migrations/`.

## New database
- Instance: `(localdb)\MSSQLLocalDB`
- Database name: `scada_db_iiot`
- Connection string (in `src/scada_demo_test.API/appsettings.json`):
  `Server=(localdb)\\MSSQLLocalDB;Database=scada_db_iiot;Trusted_Connection=True;TrustServerCertificate=True;`
- Tables are created by a fresh EF migration `InitialSqlServer` + `MigrateAsync`
  at API startup (`DynamicSchemaInitializer` + `IdentitySeeder` also run).

## Files changed
1. `src/scada_demo_test.Infrastructure/scada_demo_test.Infrastructure.csproj`
   - COMmented out `Npgsql.EntityFrameworkCore.PostgreSQL` package.
   - Added `Microsoft.EntityFrameworkCore.SqlServer`.
2. `src/scada_demo_test.API/appsettings.json`
   - Supabase connection string commented out (kept as reference).
   - Added LocalDB connection string.
3. `src/scada_demo_test.API/Program.cs`
   - `UseNpgsql(...)` commented out → `UseSqlServer(...)`.
   - Startup baseline SQL (`information_schema` / `CREATE TABLE __EFMigrationsHistory`
     / `ON CONFLICT`) rewritten for SQL Server.
4. `src/scada_demo_test.Infrastructure/Resilience/EfResilience.cs`
   - `NpgsqlException` handling commented → `SqlException` (Microsoft.Data.SqlClient).
5. `src/scada_demo_test.Infrastructure/Persistence/SensorTelemetryRepository.cs`
   - `NpgsqlParameter` → `SqlParameter`.
   - `bigserial/timestamptz/now()/LIMIT/CREATE IF NOT EXISTS/ADD COLUMN IF NOT EXISTS`
     → SQL Server equivalents (`IDENTITY(1,1)`, `datetime2`, `SYSUTCDATETIME()`,
     `TOP`, `OBJECT_ID(...) IS NULL`, `COL_LENGTH(...) IS NULL`).
6. `src/scada_demo_test.Infrastructure/Persistence/DynamicSchemaInitializer.cs`
   - Postgres DDL (`uuid`, `boolean`, `text`, `double precision`, `timestamptz`,
     `CREATE TABLE IF NOT EXISTS`, `DO $$...$$`) → SQL Server types/DLL
     (`uniqueidentifier`, `bit`, `nvarchar(max)`, `float`, `datetime2`, guarded
     `CREATE`/`ALTER`).
7. `src/scada_demo_test.Infrastructure/Migrations/`
   - Removed old `20260909105359_InitialPostgres*` + snapshot
     (backed up to `backup_postgres_migrations/`).
   - Added fresh `20260922112111_InitialSqlServer` migration generated with
     `dotnet ef migrations add InitialSqlServer`.
8. `DB_LOCAL_SWITCH_CHANGELOG.md` (this file).
9. `src/scada_demo_test.Infrastructure/Resilience/EfResilience.cs` (LOCAL TUNING,
   2026-09-22): the Supabase-era retry policy (4 retries, exponential backoff
   200ms*2^n + jitter → up to ~3s stall per DB hiccup, tuned for a REMOTE cloud
   DB) was replaced with a local-friendly policy: **3 quick retries at a fixed
   100ms**. The Modbus poller inserts inside a sequential per-sensor loop, so one
   long backoff was also pushing back the OTHER sensors on the same gateway. The
   Supabase-era policy is commented out above the class for revert. Polling still
   never fails (transient SQL errors are still retried), reading feels ~instant on
   LocalDB.

## Verified LIVE (2026-09-22, all green)
- API + Web build: **0 warnings / 0 errors**.
- `MigrateAsync` created all 23 tables on LocalDB `scada_db_iiot`
  (`__EFMigrationsHistory` stamped `20260922112111_InitialSqlServer`).
- Seeder data present: 1 admin user, 3 sites, 4 storage tanks, 3 alert rules.
- Login `superadmin@alamiot.com` / `SuperAdmin@123` → 200 + JWT + permissions.
- `/api/devices` 200 (empty), `/api/sensors/libraries` 200 (3 drivers).
- Full create→telemetry→delete round-trip on LocalDB: dynamic telemetry table
  `telemetry_sensor_aosong_*` created with correct T-SQL schema (bigint IDENTITY,
  datetime2, float, nvarchar, smallint); poller inserted a `GATEWAY_OFFLINE` row
  (T-SQL insert path OK); count + from/to range queries OK; sensor+device delete
  dropped the table. DB left clean (0 devices, 0 telemetry tables).
- Web routes OK; login page shows `/images/logo.jpg` branding.
- Current listeners: **API :5080** (PID 7244 ov. proc), **Web :5150** (PID 10060
  ov. proc).
- AFTER LOCAL TUNING restart: **API :5080** (PID 16416, started with
  `--urls http://0.0.0.0:5080` — the raw exe does NOT read `launchSettings.json`,
  without the flag it defaulted to :5000 on the first WMI start), **Web :5150**
  (PID 14576). Login 200, devices/libraries/incidents/rules all 200. DB now has 1
  device re-created via the UI: `MY_USR` (USR-W610 @ 10.10.100.254:502, 2
  sensors) — poller marked it ONLINE (last-seen fresh), see AGENTS.md Known context.

## LOCAL TUNING for the reading delay (2026-09-22)
User: "local DB ke hisaab se thora delay hai — fix it properly, make sure polling
never fails, and comment out any Supabase-era delay settings for reading."
Findings + action:
- No delay/throttle/backoff keys exist in `appsettings.json` (grep over
  `src/**/appsettings*.json` → 0 matches). The ONLY Supabase-era "delay" on the
  read/write hot path was `EfResilience.Policy` (see file #9 above): it wrapped the
  telemetry insert with exponential backoff (200/400/800/1600ms + jitter) sized for
  a REMOTE cloud DB. When it fired it stalled the poller's sequential per-sensor
  loop AND pushed back the other sensors on the same gateway → perceived reading
  delay.
- `ModbusPollingHostedService` constants (`CycleDelay` 1s, `GatewayBackoff` 30s,
  watchdog ≥60s, `ReadBudgetMs = max(5000, TimeoutMs)`, sensor 3-strike) are
  HARDWARE / serial-bridge tuning, NOT DB-related — untouched (they are what keep
  polling failure-free).
- `ModbusTcpMaster` uses no artificial delay: reads are deadline-bounded, not
  sleeping.
- Action: EfResilience retries trimmed 4x-exponential → 3x-fixed-100ms.
  Verified live after restart: login 200, `/api/devices` 200 (MY_USR ONLINE,
  last-seen refreshing — poller alive), libraries/incidents/rules 200.

## To REVERT to Supabase
1. Restore commented lines in files listed above.
2. Restore `Migrations_Postgres_Backup/*` back into `Migrations/` and delete
   the `InitialSqlServer` migration.
3. Restore Supabase connection string in `appsettings.json`.
4. Re-add `Npgsql.EntityFrameworkCore.PostgreSQL` package; remove SqlServer package.
5. Rebuild API + Web.