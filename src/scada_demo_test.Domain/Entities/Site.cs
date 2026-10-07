namespace scada_demo_test.Domain.Entities;

public class Site
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Code { get; set; } = string.Empty; // e.g., "PLANT-01"
    public string Name { get; set; } = string.Empty; // e.g., "Lahore Main Plant"
    public string Location { get; set; } = string.Empty; // e.g., "Lahore Industrial Area"
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
