using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace scada_demo_test.Infrastructure.Persistence;

// Idempotent, migration-free schema evolution for the IIoT platform work.
// Local SQL Server LocalDB does not support ADD COLUMN IF NOT EXISTS /
// CREATE TABLE IF NOT EXISTS, so every statement is guarded with the T-SQL
// equivalents (OBJECT_ID / COL_LENGTH / sys.indexes / sys.foreign_keys) and the
// existing database is upgraded in place every startup - no dotnet-ef migration
// files to generate, no risk of the column set getting out of sync.
//
// Only additive DDL (new columns / new defaults) lives here; it can safely run on
// every boot. It must run AFTER MigrateAsync() so pre-existing tables exist.
// Supabase-era Postgres DDL is preserved in DB_LOCAL_SWITCH_CHANGELOG.md.
public static class DynamicSchemaInitializer
{
    public static async Task EnsureSchemaAsync(IServiceProvider services, CancellationToken ct = default)
    {
        var db = services.GetRequiredService<MyDbContextDxy>();
        if (db.Database.IsSqlite())
        {
            await EnsureSqliteSchemaAsync(db, services, ct);
            return;
        }
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(DynamicSchemaInitializer));

        await db.Database.OpenConnectionAsync(ct);

        var statements = new[]
        {
            // ---- Device (gateway) communication model ----
            """IF COL_LENGTH(N'Devices', N'HardwareType') IS NULL ALTER TABLE [Devices] ADD [HardwareType] int NOT NULL DEFAULT 0;""",
            """IF COL_LENGTH(N'Devices', N'Port') IS NULL ALTER TABLE [Devices] ADD [Port] int NOT NULL DEFAULT 502;""",
            """IF COL_LENGTH(N'Devices', N'BaudRate') IS NULL ALTER TABLE [Devices] ADD [BaudRate] int NOT NULL DEFAULT 9600;""",
            """IF COL_LENGTH(N'Devices', N'Parity') IS NULL ALTER TABLE [Devices] ADD [Parity] int NOT NULL DEFAULT 0;""",
            """IF COL_LENGTH(N'Devices', N'StopBits') IS NULL ALTER TABLE [Devices] ADD [StopBits] int NOT NULL DEFAULT 1;""",
            """IF COL_LENGTH(N'Devices', N'TimeoutMs') IS NULL ALTER TABLE [Devices] ADD [TimeoutMs] int NOT NULL DEFAULT 2000;""",
            """IF COL_LENGTH(N'Devices', N'MaxSensorCapacity') IS NULL ALTER TABLE [Devices] ADD [MaxSensorCapacity] int NOT NULL DEFAULT 25;""",
            """IF COL_LENGTH(N'Devices', N'IsOnline') IS NULL ALTER TABLE [Devices] ADD [IsOnline] bit NOT NULL DEFAULT 0;""",
            // ProvisionedVia: which management tab registered the device ("Norvi"/"Gateway").
            // One-time backfill derives the origin from the hardware class.
            """
            IF COL_LENGTH(N'Devices', N'ProvisionedVia') IS NULL
            BEGIN
                ALTER TABLE [Devices] ADD [ProvisionedVia] nvarchar(64) NOT NULL DEFAULT N'Gateway';
                UPDATE [Devices] SET [ProvisionedVia] = CASE WHEN [HardwareType] = 0 THEN N'Norvi' ELSE N'Gateway' END;
            END;
            """,
            // Existing devices become "not yet confirmed" until the poller says otherwise.
            """UPDATE [Devices] SET [Status] = 1 WHERE [Status] IS NULL;""",

            // ---- Sensor (attached slave meter) configuration ----
            // Older cloud databases have no Sensors table at all (the ``InitialPostgres``
            // migration was never applied to them), so provision it here with the exact
            // EF model shape - this is a self-healing, idempotent boot-time upgrade and
            // never a dotnet-ef migration.
            """
            IF OBJECT_ID(N'Sensors', N'U') IS NULL
            CREATE TABLE [Sensors] (
                [Id] uniqueidentifier NOT NULL,
                [DeviceId] uniqueidentifier NOT NULL,
                [UniqueSensorId] nvarchar(450) NOT NULL,
                [Name] nvarchar(max) NOT NULL,
                [SensorType] nvarchar(max) NOT NULL,
                [SensorTypeKey] nvarchar(450) NOT NULL DEFAULT '',
                [SlaveAddress] int NOT NULL DEFAULT 1,
                [PollIntervalSeconds] int NOT NULL DEFAULT 5,
                [CalibrationMultiplier] float NOT NULL DEFAULT 1.0,
                [TelemetryTableName] nvarchar(max) NULL,
                [IsActive] bit NOT NULL DEFAULT 1,
                [IsOnline] bit NOT NULL DEFAULT 0,
                [MetricFields] nvarchar(max) NULL,
                [Config] nvarchar(max) NULL,
                [CreatedAt] datetime2 NOT NULL DEFAULT (SYSUTCDATETIME()),
                CONSTRAINT [PK_Sensors] PRIMARY KEY ([Id])
            );
            """,
            """IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Sensors_UniqueSensorId' AND object_id = OBJECT_ID(N'Sensors')) CREATE UNIQUE INDEX [IX_Sensors_UniqueSensorId] ON [Sensors] ([UniqueSensorId]);""",
            """IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Sensors_DeviceId' AND object_id = OBJECT_ID(N'Sensors')) CREATE INDEX [IX_Sensors_DeviceId] ON [Sensors] ([DeviceId]);""",
            """IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Sensors_DeviceId_SensorTypeKey' AND object_id = OBJECT_ID(N'Sensors')) CREATE INDEX [IX_Sensors_DeviceId_SensorTypeKey] ON [Sensors] ([DeviceId], [SensorTypeKey]);""",
            """
            IF OBJECT_ID(N'Sensors', N'U') IS NOT NULL
               AND NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_Sensors_Devices_DeviceId')
                ALTER TABLE [Sensors] ADD CONSTRAINT [FK_Sensors_Devices_DeviceId]
                    FOREIGN KEY ([DeviceId]) REFERENCES [Devices] ([Id]) ON DELETE CASCADE;
            """,
            """IF COL_LENGTH(N'Sensors', N'SensorTypeKey') IS NULL ALTER TABLE [Sensors] ADD [SensorTypeKey] nvarchar(450) NOT NULL DEFAULT '';""",
            """IF COL_LENGTH(N'Sensors', N'SlaveAddress') IS NULL ALTER TABLE [Sensors] ADD [SlaveAddress] int NOT NULL DEFAULT 1;""",
            """IF COL_LENGTH(N'Sensors', N'PollIntervalSeconds') IS NULL ALTER TABLE [Sensors] ADD [PollIntervalSeconds] int NOT NULL DEFAULT 5;""",
            """IF COL_LENGTH(N'Sensors', N'CalibrationMultiplier') IS NULL ALTER TABLE [Sensors] ADD [CalibrationMultiplier] float NOT NULL DEFAULT 1.0;""",
            """IF COL_LENGTH(N'Sensors', N'TelemetryTableName') IS NULL ALTER TABLE [Sensors] ADD [TelemetryTableName] nvarchar(max) NULL;""",
            """IF COL_LENGTH(N'Sensors', N'IsActive') IS NULL ALTER TABLE [Sensors] ADD [IsActive] bit NOT NULL DEFAULT 1;""",
            """IF COL_LENGTH(N'Sensors', N'IsOnline') IS NULL ALTER TABLE [Sensors] ADD [IsOnline] bit NOT NULL DEFAULT 0;"""
        };

        foreach (var sql in statements)
        {
            try
            {
                await db.Database.ExecuteSqlRawAsync(sql, ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Schema statement failed (continuing anyway): {Sql}", sql);
            }
        }

        await db.Database.CloseConnectionAsync();
    }

    // SQLite dialect of the same additive-only upgrade path. EnsureCreated only
    // builds the schema on a FRESH database, so an existing scada_db_iiot.sqlite
    // must get new columns via guarded ALTER TABLE here or every EF query that
    // projects the new property fails with "no such column".
    private static async Task EnsureSqliteSchemaAsync(MyDbContextDxy db, IServiceProvider services, CancellationToken ct)
    {
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(DynamicSchemaInitializer));

        await db.Database.OpenConnectionAsync(ct);
        try
        {
            var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var cmd = db.Database.GetDbConnection().CreateCommand();
            cmd.CommandText = "PRAGMA table_info('Devices');";
            using (var reader = await cmd.ExecuteReaderAsync(ct))
            {
                while (await reader.ReadAsync(ct))
                {
                    columns.Add(reader.GetString(1)); // column-name ordinal
                }
            }

            if (!columns.Contains("ProvisionedVia"))
            {
                // SQLite ALTER TABLE requires a constant default; backfill the real
                // origin from the hardware type (0 = NorviESP32, 1 = UsrW610).
                await db.Database.ExecuteSqlRawAsync(
                    "ALTER TABLE Devices ADD COLUMN ProvisionedVia TEXT NOT NULL DEFAULT 'Gateway';", ct);
                await db.Database.ExecuteSqlRawAsync(
                    "UPDATE Devices SET ProvisionedVia = CASE WHEN HardwareType = 0 THEN 'Norvi' ELSE 'Gateway' END;", ct);
                logger.LogInformation("SQLite schema upgrade: added Devices.ProvisionedVia with hardware-based backfill.");
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "SQLite schema statement failed (continuing anyway).");
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }
}
