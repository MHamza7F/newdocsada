namespace scada_demo_test.Domain.Entities;

// Represents an area/line of the plant - lets devices be grouped for the
// future 3D digital-twin view and for multi-meter dashboards.
public class PlantZone
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;       // e.g. "Zone 1 - Intake Line"
    public string? Description { get; set; }

    public ICollection<Device> Devices { get; set; } = new List<Device>();
}
