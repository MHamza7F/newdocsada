using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using scada_demo_test.Domain.Entities;
using scada_demo_test.Infrastructure.Persistence;

namespace scada_demo_test.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TanksController : ControllerBase
{
    private readonly MyDbContextDxy _db;

    public TanksController(MyDbContextDxy db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var tanks = await _db.StorageTanks
            .AsNoTracking()
            .OrderBy(t => t.TankCode)
            .ToListAsync();
        return Ok(tanks);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var tank = await _db.StorageTanks.FindAsync(id);
        return tank is null ? NotFound() : Ok(tank);
    }

    public record CreateTankRequest(string TankCode, string Name, double CapacityLiters, string LiquidType, Guid? SiteId);

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateTankRequest dto)
    {
        if (string.IsNullOrWhiteSpace(dto.TankCode) || string.IsNullOrWhiteSpace(dto.Name))
            return BadRequest(new { message = "Tank code and name are required." });

        var existing = await _db.StorageTanks.AnyAsync(t => t.TankCode == dto.TankCode);
        if (existing)
            return BadRequest(new { message = $"Tank with code '{dto.TankCode}' already exists." });

        var tank = new StorageTank
        {
            Id = Guid.NewGuid(),
            TankCode = dto.TankCode.Trim().ToUpperInvariant(),
            Name = dto.Name.Trim(),
            CapacityLiters = dto.CapacityLiters > 0 ? dto.CapacityLiters : 50000,
            CurrentVolumeLiters = (dto.CapacityLiters > 0 ? dto.CapacityLiters : 50000) * 0.65,
            LevelPercentage = 65.0,
            TemperatureCelsius = 22.0,
            Status = "Normal",
            LiquidType = string.IsNullOrWhiteSpace(dto.LiquidType) ? "Industrial Water" : dto.LiquidType.Trim(),
            InletFlowRate = 75.0,
            OutletFlowRate = 72.5,
            SiteId = dto.SiteId,
            LastUpdatedAt = DateTime.UtcNow
        };

        _db.StorageTanks.Add(tank);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById), new { id = tank.Id }, tank);
    }

    public record UpdateTankRequest(string Name, double CapacityLiters, string LiquidType);

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateTankRequest dto)
    {
        var tank = await _db.StorageTanks.FindAsync(id);
        if (tank is null) return NotFound(new { message = "Tank not found." });

        tank.Name = dto.Name;
        tank.CapacityLiters = dto.CapacityLiters;
        tank.LiquidType = dto.LiquidType;
        tank.LastUpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return Ok(tank);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var tank = await _db.StorageTanks.FindAsync(id);
        if (tank is null) return NotFound(new { message = "Tank not found." });

        _db.StorageTanks.Remove(tank);
        await _db.SaveChangesAsync();

        return Ok(new { message = "Tank removed successfully." });
    }
}
