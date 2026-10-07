namespace scada_demo_test.Domain.Entities;

public class FirmwareRelease
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Version { get; set; } = "v1.0.0"; // e.g., "v2.1.4"
    public string Description { get; set; } = string.Empty;
    public string HardwareTarget { get; set; } = "Norvi-ESP32"; // ESP32, ESP32-S3, STM32
    public string BinaryFileName { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public string ChecksumSha256 { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? ReleaseNotes { get; set; }
}
