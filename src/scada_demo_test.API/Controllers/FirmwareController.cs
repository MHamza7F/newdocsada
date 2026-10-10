using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using scada_demo_test.Domain.Constants;
using scada_demo_test.Domain.Entities;
using scada_demo_test.Infrastructure.Persistence;

namespace scada_demo_test.API.Controllers;

[ApiController]
[Route("api/firmware")]
public sealed class FirmwareController : ControllerBase
{
    private readonly MyDbContextDxy _db;
    private readonly AuditLogService _audit;

    public FirmwareController(MyDbContextDxy db, AuditLogService audit)
    {
        _db = db;
        _audit = audit;
    }

    public record FirmwareReleaseDto(Guid Id, string Version, string Description, string HardwareTarget,
        string BinaryFileName, long FileSizeBytes, string ChecksumSha256, bool IsActive, DateTime CreatedAt, string? ReleaseNotes);
    public record UploadFirmwareRequest(string Version, string Description, string HardwareTarget,
        string BinaryFileName, long FileSizeBytes, string ReleaseNotes);
    public record RolloutRequest(Guid ReleaseId, string TargetDeviceExternalId);

    [HttpGet]
    [Authorize(Policy = $"Action:{AppPermissions.FirmwareView}")]
    public async Task<ActionResult<IEnumerable<FirmwareReleaseDto>>> Get(CancellationToken cancellationToken)
    {
        var releases = await _db.FirmwareReleases.AsNoTracking().OrderByDescending(r => r.CreatedAt)
            .Select(r => new FirmwareReleaseDto(r.Id, r.Version, r.Description, r.HardwareTarget,
                r.BinaryFileName, r.FileSizeBytes, r.ChecksumSha256, r.IsActive, r.CreatedAt, r.ReleaseNotes))
            .ToListAsync(cancellationToken);
        return Ok(releases);
    }

    [HttpPost]
    [Authorize(Policy = $"Action:{AppPermissions.FirmwareUpload}")]
    public async Task<ActionResult<FirmwareReleaseDto>> Create([FromBody] UploadFirmwareRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Version) || request.Version.Trim().Length > 100 ||
            string.IsNullOrWhiteSpace(request.HardwareTarget) || request.HardwareTarget.Trim().Length > 100 ||
            string.IsNullOrWhiteSpace(request.BinaryFileName) || request.BinaryFileName.Trim().Length > 255)
            return BadRequest(new { message = "Version, hardware target, and binary file name are required and have valid lengths." });
        if (request.Description is null || request.Description.Length > 2000 || request.ReleaseNotes is null || request.ReleaseNotes.Length > 5000)
            return BadRequest(new { message = "Description or release notes exceed the allowed length." });
        if (request.FileSizeBytes < 0 || request.FileSizeBytes > 2_000_000_000)
            return BadRequest(new { message = "FileSizeBytes must be between 0 and 2,000,000,000." });

        var release = new FirmwareRelease
        {
            Id = Guid.NewGuid(), Version = request.Version.Trim(), Description = request.Description.Trim(),
            HardwareTarget = request.HardwareTarget.Trim(), BinaryFileName = request.BinaryFileName.Trim(),
            FileSizeBytes = request.FileSizeBytes, ChecksumSha256 = string.Empty, IsActive = true,
            CreatedAt = DateTime.UtcNow, ReleaseNotes = request.ReleaseNotes.Trim()
        };
        _db.FirmwareReleases.Add(release);
        await _db.SaveChangesAsync(cancellationToken);
        await _audit.LogAsync(CurrentUserId(), User.FindFirstValue(ClaimTypes.Email), User.Identity?.Name,
            "Firmware.UploadMetadata", "FirmwareRelease", release.Id.ToString(),
            $"Stored firmware metadata {release.Version} for {release.HardwareTarget}", HttpContext.Connection.RemoteIpAddress?.ToString());

        return CreatedAtAction(nameof(Get), new { id = release.Id }, ToDto(release));
    }

    [HttpPost("rollout")]
    [Authorize(Policy = $"Action:{AppPermissions.FirmwareRollout}")]
    public async Task<IActionResult> Rollout([FromBody] RolloutRequest request, CancellationToken cancellationToken)
    {
        if (request.ReleaseId == Guid.Empty || string.IsNullOrWhiteSpace(request.TargetDeviceExternalId) || request.TargetDeviceExternalId.Trim().Length > 200)
            return BadRequest(new { message = "ReleaseId and a valid target device external id are required." });

        await _audit.LogAsync(CurrentUserId(), User.FindFirstValue(ClaimTypes.Email), User.Identity?.Name,
            "Firmware.RolloutUnsupported", "FirmwareRelease", request.ReleaseId.ToString(),
            $"OTA rollout requested for {request.TargetDeviceExternalId.Trim()} but local hardware flashing is unsupported.", HttpContext.Connection.RemoteIpAddress?.ToString());
        return StatusCode(StatusCodes.Status501NotImplemented, new { status = "unsupported", message = "Local API does not flash hardware or perform OTA rollouts." });
    }

    private Guid? CurrentUserId() => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
    private static FirmwareReleaseDto ToDto(FirmwareRelease r) => new(r.Id, r.Version, r.Description, r.HardwareTarget,
        r.BinaryFileName, r.FileSizeBytes, r.ChecksumSha256, r.IsActive, r.CreatedAt, r.ReleaseNotes);
}
