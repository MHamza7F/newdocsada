using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using scada_demo_test.Domain.Entities;
using scada_demo_test.Infrastructure.Identity;

namespace scada_demo_test.Infrastructure.Persistence;

public class MyDbContextDxy : IdentityDbContext<AppUser, AppRole, Guid>
{
    public MyDbContextDxy(DbContextOptions<MyDbContextDxy> options) : base(options) { }

    public DbSet<Device> Devices => Set<Device>();
    public DbSet<PlantZone> PlantZones => Set<PlantZone>();
    public DbSet<SensorReading> SensorReadings => Set<SensorReading>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<HourlyRollup> HourlyRollups => Set<HourlyRollup>();
    public DbSet<DailyRollup> DailyRollups => Set<DailyRollup>();
    public DbSet<MonthlyRollup> MonthlyRollups => Set<MonthlyRollup>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<AlertRule> AlertRules => Set<AlertRule>();
    public DbSet<AlertIncident> AlertIncidents => Set<AlertIncident>();
    public DbSet<Site> Sites => Set<Site>();
    public DbSet<StorageTank> StorageTanks => Set<StorageTank>();
    public DbSet<Sensor> Sensors => Set<Sensor>();
    public DbSet<FirmwareRelease> FirmwareReleases => Set<FirmwareRelease>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder); // Identity tables must be configured first

        modelBuilder.Entity<Device>(e =>
        {
            e.HasIndex(d => d.ExternalId).IsUnique();
            e.HasMany(d => d.Sensors)
                .WithOne(s => s.Device)
                .HasForeignKey(s => s.DeviceId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SensorReading>(e =>
        {
            e.HasIndex(r => new { r.DeviceId, r.Metric, r.Timestamp });
        });

        modelBuilder.Entity<RolePermission>(e =>
        {
            e.HasIndex(r => new { r.RoleId, r.TabKey }).IsUnique();
        });

        modelBuilder.Entity<RefreshToken>(e =>
        {
            e.HasIndex(r => r.Token).IsUnique();
            e.HasIndex(r => r.UserId);
        });

        modelBuilder.Entity<AuditLog>(e =>
        {
            e.HasIndex(a => a.Timestamp);
            e.HasIndex(a => a.UserId);
        });

        modelBuilder.Entity<AlertIncident>(e =>
        {
            e.HasIndex(a => a.TriggeredAt);
            e.HasIndex(a => a.IsResolved);
        });

        modelBuilder.Entity<Site>(e =>
        {
            e.HasIndex(s => s.Code).IsUnique();
        });

        modelBuilder.Entity<StorageTank>(e =>
        {
            e.HasIndex(t => t.TankCode).IsUnique();
        });

        modelBuilder.Entity<FirmwareRelease>(e =>
        {
            e.HasIndex(f => f.Version);
        });

        modelBuilder.Entity<Sensor>(e =>
        {
            e.HasIndex(s => s.UniqueSensorId).IsUnique();
            e.HasIndex(s => new { s.DeviceId, s.SensorTypeKey });
        });

        // Tiered rollup storage
        modelBuilder.Entity<HourlyRollup>(e =>
        {
            e.HasIndex(h => new { h.DeviceId, h.Metric, h.PeriodStart }).IsUnique();
            e.HasIndex(h => h.PeriodEnd);
        });

        modelBuilder.Entity<DailyRollup>(e =>
        {
            e.HasIndex(d => new { d.DeviceId, d.Metric, d.PeriodStart }).IsUnique();
            e.HasIndex(d => d.PeriodEnd);
        });

        modelBuilder.Entity<MonthlyRollup>(e =>
        {
            e.HasIndex(m => new { m.DeviceId, m.Metric, m.PeriodStart }).IsUnique();
        });
    }
}
