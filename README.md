<<<<<<< HEAD
# SCADA Demo Test Project

## 📌 Overview
A **SCADA (Supervisory Control and Data Acquisition)** demo project built with **ASP.NET Core 10.0** and **Blazor Web UI**. It includes:
- **API Backend** (`scada_demo_test.API`) – Handles Modbus polling, data rollup, and compression.
- **Web UI** (`scada_demo_test.Web`) – Blazor-based frontend for monitoring and control.
- **Modbus Support** – Supports multiple industrial devices (AOSONG_AQ3485, KAIFENG_EM_FLOWMETER, VORTEX_FLOWMETER, SELEC_POWER_METER).
- **Data Rollup** – Automatically compresses raw data into hourly, daily, and monthly aggregates.

---

## 🛠️ Prerequisites
- **.NET 10.0 SDK** (or later)
- **Node.js** (for frontend dependencies, if any)
- **Modbus Devices** (optional, for real-time data polling)

---

## 🚀 Quick Start

### 1️⃣ Clone & Restore Dependencies
```bash
git clone <repository-url>
cd myprojectnewui
dotnet restore
```

### 2️⃣ Build the Project
```bash
dotnet build src/scada_demo_test.API/scada_demo_test.API.csproj
dotnet build src/scada_demo_test.Web/scada_demo_test.Web.csproj
```

### 3️⃣ Run the API Backend
```bash
cd src/scada_demo_test.API
dotnet run --urls "http://0.0.0.0:5080"
```
- **API will start at:** `http://localhost:5080`
- **Features:**
  - Modbus polling (every few seconds)
  - Data rollup (raw → hourly → daily → monthly)
  - REST endpoints for sensor data

### 4️⃣ Run the Web UI
```bash
cd src/scada_demo_test.Web
dotnet run --urls "http://0.0.0.0:3000"
```
- **UI will start at:** `http://localhost:3000`
- **Features:**
  - Real-time sensor monitoring
  - Reports & historical data
  - Modbus device control

> ⚠️ **Note:** If port `3000` is blocked, try `5001` or `8080`:
> ```bash
> dotnet run --urls "http://0.0.0.0:5001"
> ```

---

## 📂 Project Structure
```
myprojectnewui/
├── src/
│   ├── scada_demo_test.API/          # Backend API (Modbus, Rollup, Data Processing)
│   ├── scada_demo_test.Web/          # Blazor Web UI
│   ├── scada_demo_test.Application/  # Business Logic
│   ├── scada_demo_test.Domain/       # Core Models & Interfaces
│   ├── scada_demo_test.Infrastructure/ # Database, Modbus, Services
│   └── scada_demo_test.Maui/        # Mobile App (MAUI)
├── tests/
│   └── ScadaEngine.Tests/            # Unit & Integration Tests
├── package.json                      # Frontend dependencies (if any)
├── scada_demo_test.slnx             # Solution file
└── README.md                         # This file
```

---

## 🔌 Modbus Configuration
The system supports the following **Modbus devices** (configured in `scada_demo_test.Infrastructure`):
- **AOSONG_AQ3485** (Air Quality Sensor)
- **KAIFENG_EM_FLOWMETER** (Energy Meter)
- **VORTEX_FLOWMETER** (Flow Meter)
- **SELEC_POWER_METER** (Power Meter)

### 🔧 Customizing Modbus Devices
1. Open `src/scada_demo_test.Infrastructure/Modbus/ModbusConfig.cs`
2. Add/Modify device configurations:
   ```csharp
   public static readonly Dictionary<string, ModbusDeviceConfig> Devices = new()
   {
       ["NEW_DEVICE"] = new ModbusDeviceConfig
       {
           IpAddress = "192.168.1.100",
           Port = 502,
           SlaveId = 1,
           PollingIntervalMs = 1000
       }
   };
   ```
3. Restart the API to apply changes.

---

## 📡 API Endpoints
| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/sensors` | `GET` | List all connected sensors |
| `/api/sensors/{id}/data` | `GET` | Get real-time data for a sensor |
| `/api/sensors/{id}/history` | `GET` | Get historical data (supports `?from=` & `?to=` query params) |
| `/api/modbus/devices` | `GET` | List all Modbus devices |
| `/api/modbus/{deviceId}/read` | `GET` | Manually trigger a Modbus read |
| `/api/rollup/status` | `GET` | Check rollup/compression status |

---

## 🌐 Web UI Features
- **Dashboard** – Real-time sensor readings
- **Reports** – Historical data visualization
- **Device Control** – Manually poll Modbus devices
- **Settings** – Configure polling intervals & thresholds

---

## 🧪 Testing
### Run Unit Tests
```bash
dotnet test tests/ScadaEngine.Tests/ScadaEngine.Tests.csproj
```

### Manual Testing
1. Start the API (`dotnet run --project src/scada_demo_test.API`)
2. Start the Web UI (`dotnet run --project src/scada_demo_test.Web`)
3. Open `http://localhost:3000` in a browser
4. Verify:
   - Sensors are polling and displaying data
   - Historical data is being compressed (check logs for rollup status)
   - Modbus devices respond to manual polls

---

## ⚠️ Troubleshooting

### 🔥 Common Issues & Fixes
| Issue | Solution |
|-------|----------|
| **Port already in use** | Change the port in `launchSettings.json` or use `--urls "http://0.0.0.0:NEW_PORT"` |
| **Modbus connection failed** | Check IP/port in `ModbusConfig.cs` and ensure the device is online |
| **Build errors** | Run `dotnet restore` and rebuild |
| **Socket permission denied (Windows)** | Run as Admin or use a non-reserved port (e.g., `5001`, `8080`) |
| **Rollup not working** | Check logs for `RollupCompressionHostedService` errors |

### 🔍 Check Port Availability (Windows)
```bash
netstat -ano | findstr :3000
```
If port is in use, kill the process:
```bash
taskkill /PID <PID> /F
```

### 🔍 Check Excluded Ports (Windows)
```bash
netsh interface ipv4 show excludedportrange protocol=tcp
```
Avoid ports in the excluded range (e.g., `50000-50059`).

---

## 📝 Changelog
- **2026-10-07** – Initial README setup
- **2026-10-05** – Added Modbus polling & rollup compression
- **2026-10-01** – Project structure finalized

---

## 🤝 Contributing
1. Fork the repository
2. Create a feature branch (`git checkout -b feature/your-feature`)
3. Commit changes (`git commit -m "Add your feature"`)
4. Push to the branch (`git push origin feature/your-feature`)
5. Open a Pull Request

---

## 📄 License
MIT License – See [LICENSE](LICENSE) for details.
=======
# newdocsada
xyz
>>>>>>> fa6885ae348cb18216490e4c7533b25023a90199
