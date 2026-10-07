using scada_demo_test.Maui.Models;

namespace scada_demo_test.Maui.Services;

/// <summary>
/// High-fidelity industrial mock telemetry simulation engine.
/// Simulates realistic physical dynamics (harmonic flow curves, cumulative totalizers,
/// fluid dynamics for storage tanks, and raw Modbus TCP/RTU packet dumps).
/// </summary>
public class MockTelemetryService : IMockTelemetryService
{
    private readonly Random _rand = new();
    private double _timeElapsedSeconds;
    private int _logCycleIndex;

    // Base nominal flow rates per meter ID for realistic varied plant zones (Named tuple elements)
    private readonly Dictionary<int, (double BaseFlow, double Amplitude, string Name)> _meterProfiles = new()
    {
        { 1, (45.0, 8.5, "Inlet Line #1 (Raw)") },
        { 2, (38.5, 6.0, "Boiler Feed Loop #2") },
        { 3, (62.0, 12.0, "Main Cooling Loop #3") },
        { 4, (28.0, 5.0, "RO Permeate Line #4") },
        { 5, (52.5, 9.0, "Chemical Dosing Header #5") },
        { 6, (18.5, 4.0, "Effluent Discharge #6") },
        { 7, (74.0, 15.0, "Turbine Condenser #7") },
        { 8, (31.0, 7.0, "Scrubber Rinse Line #8") },
        { 9, (42.0, 8.0, "CIP Sanitization Loop #9") },
        { 10, (55.0, 10.0, "Domestic Utility Supply #10") },
        { 11, (24.0, 4.5, "Chiller Loop #11") },
        { 12, (68.0, 11.0, "Fire Suppression Ring #12") },
        { 13, (15.0, 3.0, "Sludge Decanter #13") },
        { 14, (33.0, 6.5, "Recirculation Return #14") }
    };

    public List<FlowMeterModel> GetInitialMeters(int count)
    {
        var meters = new List<FlowMeterModel>();
        int effectiveCount = Math.Clamp(count, 1, 14);

        for (int i = 1; i <= effectiveCount; i++)
        {
            var profile = _meterProfiles.TryGetValue(i, out var p) ? p : (BaseFlow: 35.0, Amplitude: 7.0, Name: $"Flowmeter Unit #{i}");
            double initialFlow = profile.BaseFlow + (_rand.NextDouble() * 4.0 - 2.0);
            double initialTotalizer = 12500.0 * i + _rand.Next(100, 2000);

            meters.Add(new FlowMeterModel
            {
                DeviceExternalId = $"norvi-slave-{i}",
                DeviceName = profile.Name,
                FlowRate = Math.Round(initialFlow, 1),
                FlowUnit = "L/min",
                Totalizer = Math.Round(initialTotalizer, 1),
                TotalizerUnit = "L",
                IsOnline = true,
                Status = "Online",
                ModbusSlaveId = i,
                IpAddress = $"192.168.1.{100 + i}",
                LastSeen = DateTime.UtcNow
            });
        }

        return meters;
    }

    public List<StorageTankModel> GetInitialTanks()
    {
        return new List<StorageTankModel>
        {
            new()
            {
                Id = Guid.NewGuid(),
                TankCode = "TANK-RAW-01",
                Name = "Raw Water Buffer Tank #1",
                CapacityLiters = 50000,
                CurrentVolumeLiters = 34500,
                LevelPercentage = 69.0,
                TemperatureCelsius = 22.4,
                Status = "Normal",
                LiquidType = "Raw Utility Water",
                InletFlowRate = 125.0,
                OutletFlowRate = 110.0,
                LastUpdatedAt = DateTime.UtcNow
            },
            new()
            {
                Id = Guid.NewGuid(),
                TankCode = "TANK-RO-02",
                Name = "RO Permeate Clean Tank #2",
                CapacityLiters = 40000,
                CurrentVolumeLiters = 31200,
                LevelPercentage = 78.0,
                TemperatureCelsius = 20.1,
                Status = "Filling",
                LiquidType = "Treated Pure Water",
                InletFlowRate = 95.0,
                OutletFlowRate = 60.0,
                LastUpdatedAt = DateTime.UtcNow
            },
            new()
            {
                Id = Guid.NewGuid(),
                TankCode = "TANK-CHEM-03",
                Name = "Polymer Neutralizer Tank #3",
                CapacityLiters = 25000,
                CurrentVolumeLiters = 11750,
                LevelPercentage = 47.0,
                TemperatureCelsius = 28.5,
                Status = "Normal",
                LiquidType = "Neutralizer Chemical",
                InletFlowRate = 35.0,
                OutletFlowRate = 38.0,
                LastUpdatedAt = DateTime.UtcNow
            },
            new()
            {
                Id = Guid.NewGuid(),
                TankCode = "TANK-EFF-04",
                Name = "Clarifier Discharge Tank #4",
                CapacityLiters = 60000,
                CurrentVolumeLiters = 51600,
                LevelPercentage = 86.0,
                TemperatureCelsius = 25.8,
                Status = "HighAlert",
                LiquidType = "Treated Effluent",
                InletFlowRate = 140.0,
                OutletFlowRate = 80.0,
                LastUpdatedAt = DateTime.UtcNow
            }
        };
    }

    public List<SystemAlertModel> GetInitialAlerts()
    {
        return new List<SystemAlertModel>
        {
            new()
            {
                Title = "High Level Alert (>85%)",
                Message = "Clarifier Discharge Tank #4 approaching high liquid threshold.",
                SourceDevice = "TANK-EFF-04",
                Severity = AlertSeverity.Warning,
                Timestamp = DateTime.UtcNow.AddMinutes(-12),
                IsAcknowledged = false
            },
            new()
            {
                Title = "Hardware Gateway Offline",
                Message = "USR-W610 Modbus Gateway at 192.168.1.15 is unreachable. Running on simulation stream.",
                SourceDevice = "GATEWAY-W610",
                Severity = AlertSeverity.Info,
                Timestamp = DateTime.UtcNow.AddMinutes(-5),
                IsAcknowledged = true
            }
        };
    }

    public void TickMeters(List<FlowMeterModel> meters, double elapsedSeconds)
    {
        _timeElapsedSeconds += elapsedSeconds;

        foreach (var meter in meters)
        {
            int slaveId = meter.ModbusSlaveId ?? 1;
            var profile = _meterProfiles.TryGetValue(slaveId, out var p) ? p : (BaseFlow: 35.0, Amplitude: 7.0, Name: meter.DeviceName);

            // Sinusoidal oscillation + brownian noise for natural industrial look
            double frequency = 0.08 + (slaveId * 0.015);
            double phase = slaveId * 0.9;
            double wave = Math.Sin((_timeElapsedSeconds * frequency) + phase) * profile.Amplitude;
            double noise = (_rand.NextDouble() - 0.5) * 2.5;

            double targetFlow = Math.Max(0.0, profile.BaseFlow + wave + noise);
            meter.FlowRate = Math.Round(targetFlow, 1);

            // Increment cumulative totalizer (FlowRate L/min -> Liters added per second = FlowRate / 60 * elapsed)
            double litersDelta = (meter.FlowRate / 60.0) * elapsedSeconds;
            meter.Totalizer = Math.Round(meter.Totalizer + litersDelta, 1);

            meter.LastSeen = DateTime.UtcNow;
            meter.IsOnline = true;
        }
    }

    public void TickTanks(List<StorageTankModel> tanks, double elapsedSeconds)
    {
        foreach (var tank in tanks)
        {
            // Fluid volume delta in liters based on inlet and outlet rates
            double netFlowPerMinute = tank.InletFlowRate - tank.OutletFlowRate;
            double volumeDeltaLiters = (netFlowPerMinute / 60.0) * elapsedSeconds;

            double newVolume = Math.Clamp(tank.CurrentVolumeLiters + volumeDeltaLiters, 0, tank.CapacityLiters);
            tank.CurrentVolumeLiters = Math.Round(newVolume, 0);
            tank.LevelPercentage = Math.Round((newVolume / tank.CapacityLiters) * 100.0, 1);

            // Natural temperature variation
            double tempNoise = (_rand.NextDouble() - 0.5) * 0.05;
            tank.TemperatureCelsius = Math.Round(tank.TemperatureCelsius + tempNoise, 1);

            // Update status string based on state
            if (tank.LevelPercentage >= 88.0)
            {
                tank.Status = "HighAlert";
            }
            else if (tank.LevelPercentage <= 15.0)
            {
                tank.Status = "LowAlert";
            }
            else if (tank.NetFlowRate > 10.0)
            {
                tank.Status = "Filling";
            }
            else if (tank.NetFlowRate < -10.0)
            {
                tank.Status = "Draining";
            }
            else
            {
                tank.Status = "Normal";
            }

            tank.LastUpdatedAt = DateTime.UtcNow;
        }
    }

    public ModbusLogEntry GenerateNextModbusLog(List<FlowMeterModel> meters)
    {
        if (meters.Count == 0)
        {
            return new ModbusLogEntry();
        }

        _logCycleIndex = (_logCycleIndex + 1) % meters.Count;
        var meter = meters[_logCycleIndex];
        int slaveId = meter.ModbusSlaveId ?? (_logCycleIndex + 1);

        // Build simulated raw Hex frame representation
        byte[] hexBytes = new byte[8];
        hexBytes[0] = (byte)slaveId; // Slave ID
        hexBytes[1] = 0x03;          // Function Code (Read Holding Registers)
        hexBytes[2] = 0x04;          // Byte Count (4 bytes float)
        _rand.NextBytes(hexBytes.AsSpan(3, 4)); // Telemetry float payload
        hexBytes[7] = (byte)(_rand.Next(0, 255)); // CRC check byte

        string hexStr = $"[{BitConverter.ToString(hexBytes).Replace("-", " ")}]";

        return new ModbusLogEntry
        {
            Timestamp = DateTime.UtcNow,
            DeviceId = meter.DeviceExternalId,
            SlaveId = slaveId,
            FunctionCode = "03 Read Holding Registers",
            RegisterAddress = 40001,
            Metric = "FlowRate",
            Value = meter.FlowRate,
            Unit = meter.FlowUnit,
            RawHexPayload = hexStr,
            Status = "ACK / CRC OK",
            IsError = false
        };
    }
}