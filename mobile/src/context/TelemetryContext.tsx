import React, { createContext, useContext, useState, useEffect, ReactNode } from 'react';
import { FlowMeter, StorageTank, ModbusLog, PlantSummary, SystemSettings } from '../types/scada';
import { getInitialMeters, getInitialTanks, tickSimulation, generateModbusLog } from '../services/mockTelemetryService';

export interface TelemetryContextType {
  meters: FlowMeter[];
  tanks: StorageTank[];
  logs: ModbusLog[];
  summary: PlantSummary;
  settings: SystemSettings;
  updateSettings: (newSettings: Partial<SystemSettings>) => void;
  isPaused: boolean;
  togglePause: () => void;
  clearLogs: () => void;
  connectionState: 'live' | 'simulation' | 'fallback';
}

const defaultSettings: SystemSettings = {
  apiBaseUrl: 'http://192.168.1.15:5080',
  isSimulationMode: true,
  pollingIntervalMs: 1500,
  simulatedMeterCount: 6,
  autoFailover: true,
};

const TelemetryContext = createContext<TelemetryContextType | undefined>(undefined);

export interface TelemetryProviderProps {
  children?: ReactNode;
}

export const TelemetryProvider: React.FC<TelemetryProviderProps> = ({ children }) => {
  const [settings, setSettings] = useState<SystemSettings>(defaultSettings);
  const [meters, setMeters] = useState<FlowMeter[]>(() => getInitialMeters(defaultSettings.simulatedMeterCount));
  const [tanks, setTanks] = useState<StorageTank[]>(() => getInitialTanks());
  const [logs, setLogs] = useState<ModbusLog[]>([]);
  const [isPaused, setIsPaused] = useState<boolean>(false);
  const [connectionState] = useState<'live' | 'simulation' | 'fallback'>('simulation');

  const updateSettings = (newSettings: Partial<SystemSettings>) => {
    setSettings((prev) => ({ ...prev, ...newSettings }));
  };

  const togglePause = () => setIsPaused((prev) => !prev);
  const clearLogs = () => setLogs([]);

  // Telemetry tick loop
  useEffect(() => {
    if (isPaused) return;

    const interval = setInterval(() => {
      // 1. Advance simulation step
      setMeters((currentMeters) => {
        const nextMeters = currentMeters.map((m) => ({ ...m }));
        
        setTanks((currentTanks) => {
          const nextTanks = currentTanks.map((t) => ({ ...t }));
          tickSimulation(nextMeters, nextTanks, settings.pollingIntervalMs / 1000);
          return nextTanks;
        });

        return nextMeters;
      });

      // 2. Generate Next Modbus Log
      setMeters((latestMeters) => {
        const log = generateModbusLog(latestMeters);
        if (log && log.id) {
          setLogs((prevLogs) => [log, ...prevLogs.slice(0, 49)]);
        }
        return latestMeters;
      });
    }, settings.pollingIntervalMs);

    return () => clearInterval(interval);
  }, [isPaused, settings.pollingIntervalMs]);

  // Compute Plant Summary
  const safeMeters = meters || [];
  const safeTanks = tanks || [];

  const summary: PlantSummary = {
    totalFlowRate: safeMeters.reduce((sum, m) => sum + (m.flowRate || 0), 0),
    totalCumulativeVolume: safeMeters.reduce((sum, m) => sum + (m.totalizer || 0), 0),
    averageTankLevel: safeTanks.length
      ? safeTanks.reduce((sum, t) => sum + (t.levelPercentage || 0), 0) / safeTanks.length
      : 0,
    onlineMeterCount: safeMeters.filter((m) => m.isOnline).length,
    totalMeterCount: safeMeters.length,
    activeAlertCount: 1,
  };

  const value: TelemetryContextType = {
    meters: safeMeters,
    tanks: safeTanks,
    logs: logs || [],
    summary,
    settings,
    updateSettings,
    isPaused,
    togglePause,
    clearLogs,
    connectionState,
  };

  return (
    <TelemetryContext.Provider value={value}>
      {children}
    </TelemetryContext.Provider>
  );
};

export const useTelemetry = (): TelemetryContextType => {
  const context = useContext(TelemetryContext);
  if (!context) {
    throw new Error('useTelemetry must be used within TelemetryProvider');
  }
  return context;
};
