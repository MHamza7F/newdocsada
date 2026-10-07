namespace scada_demo_test.Domain.Entities;

public class StorageTank
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? SiteId { get; set; }
    public string TankCode { get; set; } = string.Empty; // e.g., "TANK-RAW-01"
    public string Name { get; set; } = string.Empty; // e.g., "Raw Water Buffer Tank #1"
    public double CapacityLiters { get; set; } = 50000;
    public double CurrentVolumeLiters { get; set; } = 32500;
    public double LevelPercentage { get; set; } = 65.0; // 0 - 100%
    public double TemperatureCelsius { get; set; } = 24.5;
    public string Status { get; set; } = "Normal"; // Normal, Filling, Draining, HighAlert, LowAlert
    public string LiquidType { get; set; } = "Water"; // Water, Chemical, Diesel, Milk, Slurry
    public double InletFlowRate { get; set; } = 120.5; // L/min
    public double OutletFlowRate { get; set; } = 95.2; // L/min
    public DateTime LastUpdatedAt { get; set; } = DateTime.UtcNow;
}
