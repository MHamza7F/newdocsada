    namespace scada_demo_test.Domain.Drivers;

    /// <summary>
    /// V880BR / LUGB Vortex Steam &amp; Gas Flowmeter
    /// ----------------------------------------------
    /// Function Code 0x04 (Read Input Registers), IEEE-754 float32, HighWordFirst.
    ///   Window 0: start 1026, qty 2 -> Flow percentage (%) converted to m³/h via FullScaleFlowM3H
    ///   Window 1: start 1032, qty 2 -> Accumulated Totalizer (m³)
    ///   CorroborationWindow: start 1067, qty 2 (raw temperature input register block)
    /// </summary>
    public class VortexFlowmeterDriver : ISensorDriver
    {
        public const double FullScaleFlowM3H = 1000.0;

        private static readonly IReadOnlyList<SensorReadWindow> Windows = new[]
        {
            new SensorReadWindow(0x04, 1026, 2), // Flow % -> m³/h
            new SensorReadWindow(0x04, 1032, 2)  // Totalizer (m³)
        };

        private static readonly SensorReadWindow ProofWindow = new(0x04, 1067, 2);

        public string DriverKey => "VORTEX_FLOWMETER";
        public string SimpleName => "vortex";
        public string DisplayName => "V880BR / LUGB Vortex Flowmeter";
        public string Description => "Industrial vortex shedding flowmeter (FC04 input registers 1026/1032, IEEE-754 float32)";

        public int DefaultSlaveAddress => 1;
        public int DefaultPollIntervalSeconds => 3;
        public ushort StartRegister => 1026;
        public ushort RegisterQuantity => 2;
        public byte FunctionCode => 0x04;

        public string UnitPrimary => "m³/h";
        public string UnitSecondary => "m³";
        public string PrimaryColumnName => "InstantaneousFlowRate";
        public string SecondaryColumnName => "AccumulatedTotalizer";

        public IReadOnlyList<SensorReadWindow> ReadWindows => Windows;
        public SensorReadWindow? CorroborationWindow => ProofWindow;

        public ParsedTelemetry ParseData(byte[] rawModbusPayload)
        {
            if (rawModbusPayload is null || rawModbusPayload.Length < 4)
            {
                return new ParsedTelemetry { Success = false, ErrorCode = "INVALID_PAYLOAD" };
            }

            var flowPct = ModbusValueCodec.ReadFloat32HighWordFirst(rawModbusPayload, 0);
            var flowM3H = (flowPct / 100.0) * FullScaleFlowM3H;
            var totalizer = rawModbusPayload.Length >= 8
                ? ModbusValueCodec.ReadFloat32HighWordFirst(rawModbusPayload, 4)
                : 0.0;

            return new ParsedTelemetry
            {
                Success = true,
                PrimaryValue = Math.Round(flowM3H, 4),
                SecondaryValue = Math.Round(totalizer, 3)
            };
        }

        public WindowTelemetry ParseWindow(int windowIndex, byte[] rawModbusPayload)
        {
            if (rawModbusPayload is null || rawModbusPayload.Length < 4)
            {
                return new WindowTelemetry();
            }

            var val = ModbusValueCodec.ReadFloat32HighWordFirst(rawModbusPayload, 0);
            return windowIndex switch
            {
                0 => new WindowTelemetry { PrimaryValue = Math.Round((val / 100.0) * FullScaleFlowM3H, 4) },
                1 => new WindowTelemetry { SecondaryValue = Math.Round(val, 3) },
                _ => new WindowTelemetry()
            };
        }

        public bool ValidatePayloadStructure(byte[] rawPayload) => rawPayload != null && rawPayload.Length == 8;

        public bool ValidateValueBoundaries(byte[] rawPayload)
        {
            if (rawPayload == null || rawPayload.Length < 4) return false;
            var f1 = ModbusValueCodec.ReadFloat32HighWordFirst(rawPayload, 0);
            if (!double.IsFinite(f1) || Math.Abs(f1) > 1e7) return false;
            if (rawPayload.Length >= 8)
            {
                var f2 = ModbusValueCodec.ReadFloat32HighWordFirst(rawPayload, 4);
                if (!double.IsFinite(f2) || Math.Abs(f2) > 1e12) return false;
            }
            return true;
        }
    }
