using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using scada_demo_test.Domain.Drivers;
using scada_demo_test.Domain.Interfaces;
using scada_demo_test.Infrastructure.Resilience;

namespace scada_demo_test.Infrastructure.Persistence;

// Streaming engine for the dynamically-created per-sensor telemetry tables.
// Every physical table name and column name is produced by the driver registry
// (TelemetryTableNaming / ISensorDriver), never by user input, and every write on
// the hot path goes through the shared Polly retry policy.
//
// Provider: local SQL Server LocalDB (T-SQL dialect) on Windows, with automatic
// SQLite dialect support when running in a non-Windows container environment.
public class SensorTelemetryRepository : ISensorTelemetryRepository
{
    private readonly MyDbContextDxy _db;
    public SensorTelemetryRepository(MyDbContextDxy db) => _db = db;

    public async Task EnsureTelemetryTableAsync(string tableName, ISensorDriver driver, SensorColumnSet columns, CancellationToken ct = default)
    {
        ValidateTable(tableName, driver);

        var columnDefs = new List<string>();
        if (_db.Database.IsSqlite())
        {
            if (columns.HasPrimary) columnDefs.Add($"\"{driver.PrimaryColumnName}\" REAL NOT NULL DEFAULT 0");
            if (columns.HasSecondary) columnDefs.Add($"\"{driver.SecondaryColumnName}\" REAL NOT NULL DEFAULT 0");

            var sqliteSql = $"""
                CREATE TABLE IF NOT EXISTS "{tableName}" (
                    "Id" INTEGER PRIMARY KEY AUTOINCREMENT,
                    "TimestampUTC" TEXT NOT NULL DEFAULT ( CURRENT_TIMESTAMP ),
                    "RawHexBuffer" TEXT NULL,
                    {string.Join(",\n                    ", columnDefs)},
                    "ConnectionStatus" INTEGER NOT NULL DEFAULT 1,
                    "ErrorCode" TEXT NULL
                );
                """;
            await EfResilience.RunAsync(() => _db.Database.ExecuteSqlRawAsync(sqliteSql, ct));
            return;
        }

        if (columns.HasPrimary) columnDefs.Add($"[{driver.PrimaryColumnName}] float NOT NULL DEFAULT 0");
        if (columns.HasSecondary) columnDefs.Add($"[{driver.SecondaryColumnName}] float NOT NULL DEFAULT 0");

        var sql = $"""
            IF OBJECT_ID(N'{tableName}', N'U') IS NULL
            CREATE TABLE [{tableName}] (
                [Id] bigint IDENTITY(1,1) NOT NULL PRIMARY KEY,
                [TimestampUTC] datetime2 NOT NULL DEFAULT (SYSUTCDATETIME()),
                [RawHexBuffer] nvarchar(max) NULL,
                {string.Join(",\n                ", columnDefs)},
                [ConnectionStatus] smallint NOT NULL DEFAULT 1,
                [ErrorCode] nvarchar(max) NULL
            );
            """;

        await EfResilience.RunAsync(() => _db.Database.ExecuteSqlRawAsync(sql, ct));

        if (columns.HasPrimary)
        {
            var addPrimary = $"""
                IF COL_LENGTH(N'{tableName}', N'{driver.PrimaryColumnName}') IS NULL
                    ALTER TABLE [{tableName}] ADD [{driver.PrimaryColumnName}] float NOT NULL DEFAULT 0;
                """;
            await EfResilience.RunAsync(() => _db.Database.ExecuteSqlRawAsync(addPrimary, ct));
        }
        if (columns.HasSecondary)
        {
            var addSecondary = $"""
                IF COL_LENGTH(N'{tableName}', N'{driver.SecondaryColumnName}') IS NULL
                    ALTER TABLE [{tableName}] ADD [{driver.SecondaryColumnName}] float NOT NULL DEFAULT 0;
                """;
            await EfResilience.RunAsync(() => _db.Database.ExecuteSqlRawAsync(addSecondary, ct));
        }
    }

    public async Task DropTableIfExistsAsync(string tableName, CancellationToken ct = default)
    {
        if (!TelemetryTableNaming.IsSafeTableName(tableName)) return;

        var sql = _db.Database.IsSqlite()
            ? $"""DROP TABLE IF EXISTS "{tableName}";"""
            : $"""DROP TABLE IF EXISTS [{tableName}];""";
        await EfResilience.RunAsync(() => _db.Database.ExecuteSqlRawAsync(sql, ct));
    }

    public async Task<bool> TableExistsAsync(string tableName, CancellationToken ct = default)
    {
        if (!TelemetryTableNaming.IsSafeTableName(tableName)) return false;

        var connection = _db.Database.GetDbConnection();
        await _db.Database.OpenConnectionAsync(ct);
        try
        {
            await using var cmd = connection.CreateCommand();
            if (_db.Database.IsSqlite())
            {
                cmd.CommandText = "SELECT COUNT(1) FROM sqlite_master WHERE type='table' AND name=@name;";
                var p = cmd.CreateParameter();
                p.ParameterName = "@name";
                p.Value = tableName;
                cmd.Parameters.Add(p);
                var res = await cmd.ExecuteScalarAsync(ct);
                return Convert.ToInt32(res) > 0;
            }
            else
            {
                cmd.CommandText = "SELECT CASE WHEN OBJECT_ID(@name, N'U') IS NOT NULL THEN 1 ELSE 0 END;";
                cmd.Parameters.Add(new SqlParameter("@name", tableName));
                var result = await cmd.ExecuteScalarAsync(ct);
                return result is int i && i == 1;
            }
        }
        finally
        {
            await _db.Database.CloseConnectionAsync();
        }
    }

    public async Task InsertAsync(
        string tableName,
        ISensorDriver driver,
        SensorColumnSet columns,
        byte[] rawPayload,
        double primaryValue,
        double secondaryValue,
        bool online,
        string? errorCode,
        CancellationToken ct = default)
    {
        ValidateTable(tableName, driver);

        if (_db.Database.IsSqlite())
        {
            var cols = new List<string> { "\"TimestampUTC\"", "\"RawHexBuffer\"" };
            var vals = new List<string> { "@ts", "@raw" };
            var connection = _db.Database.GetDbConnection();
            await _db.Database.OpenConnectionAsync(ct);
            try
            {
                await using var cmd = connection.CreateCommand();
                AddParam(cmd, "@ts", DateTime.UtcNow.ToString("O"));
                AddParam(cmd, "@raw", rawPayload is { Length: > 0 } ? Convert.ToHexString(rawPayload) : DBNull.Value);
                if (columns.HasPrimary)
                {
                    cols.Add($"\"{driver.PrimaryColumnName}\"");
                    vals.Add("@p");
                    AddParam(cmd, "@p", primaryValue);
                }
                if (columns.HasSecondary)
                {
                    cols.Add($"\"{driver.SecondaryColumnName}\"");
                    vals.Add("@s");
                    AddParam(cmd, "@s", secondaryValue);
                }
                cols.Add("\"ConnectionStatus\"");
                cols.Add("\"ErrorCode\"");
                vals.Add("@status");
                vals.Add("@error");
                AddParam(cmd, "@status", (short)(online ? 1 : 0));
                AddParam(cmd, "@error", (object?)errorCode ?? DBNull.Value);

                cmd.CommandText = $"INSERT INTO \"{tableName}\" ({string.Join(", ", cols)}) VALUES ({string.Join(", ", vals)});";
                await cmd.ExecuteNonQueryAsync(ct);
            }
            finally
            {
                await _db.Database.CloseConnectionAsync();
            }
            return;
        }

        var columnsToWrite = new List<string> { "[TimestampUTC]", "[RawHexBuffer]" };
        var valuePlaceholders = new List<string> { "SYSUTCDATETIME()", "@raw" };
        var parameters = new List<SqlParameter>
        {
            new("@raw", rawPayload is { Length: > 0 } ? Convert.ToHexString(rawPayload) : (object)DBNull.Value)
        };

        if (columns.HasPrimary)
        {
            columnsToWrite.Add($"[{driver.PrimaryColumnName}]");
            valuePlaceholders.Add("@p");
            parameters.Add(new SqlParameter("@p", primaryValue));
        }
        if (columns.HasSecondary)
        {
            columnsToWrite.Add($"[{driver.SecondaryColumnName}]");
            valuePlaceholders.Add("@s");
            parameters.Add(new SqlParameter("@s", secondaryValue));
        }

        columnsToWrite.Add("[ConnectionStatus]");
        columnsToWrite.Add("[ErrorCode]");
        valuePlaceholders.Add("@status");
        valuePlaceholders.Add("@error");
        parameters.Add(new SqlParameter("@status", (short)(online ? 1 : 0)));
        parameters.Add(new SqlParameter("@error", errorCode ?? (object)DBNull.Value));

        var sql = $"""
            INSERT INTO [{tableName}]
                ({string.Join(", ", columnsToWrite)})
            VALUES ({string.Join(", ", valuePlaceholders)});
            """;

        await EfResilience.RunAsync(() => _db.Database.ExecuteSqlRawAsync(sql, parameters.ToArray(), ct));
    }

    private static void AddParam(DbCommand cmd, string name, object value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value;
        cmd.Parameters.Add(p);
    }

    public async Task<SensorTelemetryReading?> GetLatestAsync(string tableName, ISensorDriver driver, SensorColumnSet columns, CancellationToken ct = default)
    {
        var rows = await QueryAsync(tableName, driver, columns, latestOnly: true, count: 1, null, null, ct);
        return rows.FirstOrDefault();
    }

    public async Task<IReadOnlyList<SensorTelemetryReading>> GetRecentAsync(
        string tableName, ISensorDriver driver, SensorColumnSet columns, int count = 20, CancellationToken ct = default)
        => await QueryAsync(tableName, driver, columns, latestOnly: false, count, null, null, ct);

    public async Task<IReadOnlyList<SensorTelemetryReading>> GetRangeAsync(
        string tableName, ISensorDriver driver, SensorColumnSet columns,
        DateTime fromUtc, DateTime toUtc, int limit = 1000, CancellationToken ct = default)
        => await QueryAsync(tableName, driver, columns, latestOnly: false, limit, fromUtc, toUtc, ct);

    private static void ValidateTable(string tableName, ISensorDriver driver)
    {
        if (!TelemetryTableNaming.IsSafeTableName(tableName))
        {
            throw new InvalidOperationException($"Telemetry table name '{tableName}' failed safety validation.");
        }
        if (string.IsNullOrWhiteSpace(driver.PrimaryColumnName) || string.IsNullOrWhiteSpace(driver.SecondaryColumnName))
        {
            throw new InvalidOperationException($"Driver '{driver.DriverKey}' has no primary/secondary value columns.");
        }
    }

    private async Task<List<SensorTelemetryReading>> QueryAsync(
        string tableName, ISensorDriver driver, SensorColumnSet columns, bool latestOnly, int count,
        DateTime? fromUtc, DateTime? toUtc, CancellationToken ct)
    {
        if (!TelemetryTableNaming.IsSafeTableName(tableName)) return new();
        if (!columns.HasAny) return new();

        var isSqlite = _db.Database.IsSqlite();
        var valueColumns = new List<string>();
        if (columns.HasPrimary) valueColumns.Add(isSqlite ? $"\"{driver.PrimaryColumnName}\"" : $"[{driver.PrimaryColumnName}]");
        if (columns.HasSecondary) valueColumns.Add(isSqlite ? $"\"{driver.SecondaryColumnName}\"" : $"[{driver.SecondaryColumnName}]");

        var selectColumns = isSqlite
            ? $"\"TimestampUTC\", \"RawHexBuffer\", {string.Join(", ", valueColumns)}, \"ConnectionStatus\", \"ErrorCode\""
            : $"[TimestampUTC], [RawHexBuffer], {string.Join(", ", valueColumns)}, [ConnectionStatus], [ErrorCode]";

        var hasRange = fromUtc.HasValue || toUtc.HasValue;
        string sql;
        if (isSqlite)
        {
            sql = latestOnly
                ? $"SELECT {selectColumns} FROM \"{tableName}\" WHERE \"ConnectionStatus\" = 1 ORDER BY \"TimestampUTC\" DESC LIMIT 1;"
                : $"SELECT {selectColumns} FROM \"{tableName}\" ORDER BY \"TimestampUTC\" DESC LIMIT @count;";
        }
        else
        {
            sql = latestOnly
                ? $"""
                   SELECT TOP 1 {selectColumns}
                   FROM [{tableName}]
                   WHERE [ConnectionStatus] = 1
                   ORDER BY [TimestampUTC] DESC;
                   """
                : hasRange
                    ? $"""
                       SELECT TOP (@count) {selectColumns}
                       FROM [{tableName}]
                       WHERE (@from IS NULL OR [TimestampUTC] >= @from)
                         AND (@to IS NULL OR [TimestampUTC] <= @to)
                       ORDER BY [TimestampUTC] DESC;
                       """
                    : $"""
                       SELECT TOP (@count) {selectColumns}
                       FROM [{tableName}]
                       ORDER BY [TimestampUTC] DESC;
                       """;
        }

        var result = new List<SensorTelemetryReading>();
        var connection = _db.Database.GetDbConnection();
        await _db.Database.OpenConnectionAsync(ct);
        try
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = sql;
            if (!latestOnly)
            {
                if (isSqlite)
                {
                    AddParam(cmd, "@count", count);
                }
                else
                {
                    cmd.Parameters.Add(new SqlParameter("@count", count));
                    if (hasRange)
                    {
                        cmd.Parameters.Add(new SqlParameter("@from", fromUtc.HasValue ? (object)fromUtc.Value : DBNull.Value));
                        cmd.Parameters.Add(new SqlParameter("@to", toUtc.HasValue ? (object)toUtc.Value : DBNull.Value));
                    }
                }
            }

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                result.Add(new SensorTelemetryReading
                {
                    TimestampUtc = DateTime.SpecifyKind(Convert.ToDateTime(reader["TimestampUTC"]), DateTimeKind.Utc),
                    RawHexBuffer = reader["RawHexBuffer"] as string,
                    PrimaryValue = columns.HasPrimary ? Convert.ToDouble(reader[driver.PrimaryColumnName]) : 0,
                    SecondaryValue = columns.HasSecondary ? Convert.ToDouble(reader[driver.SecondaryColumnName]) : 0,
                    ConnectionStatus = Convert.ToInt16(reader["ConnectionStatus"]),
                    ErrorCode = reader["ErrorCode"] as string
                });
            }
        }
        finally
        {
            await _db.Database.CloseConnectionAsync();
        }

        return result;
    }
}
