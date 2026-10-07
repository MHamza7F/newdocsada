export interface FlowMeter {
  deviceExternalId: string;
  deviceName: string;
  flowRate: number;
  flowUnit: string;
  totalizer: number;
  totalizerUnit: string;
  isOnline: boolean;
  status: string;
  lastSeen: string;
  modbusSlaveId?: number;
  ipAddress?: string;
  isFlowIncreasing?: boolean;
}

export interface StorageTank {
  id: string;
  tankCode: string;
  name: string;
  capacityLiters: number;
  currentVolumeLiters: number;
  levelPercentage: number;
  temperatureCelsius: number;
  status: string;
  liquidType: string;
  inletFlowRate: number;
  outletFlowRate: number;
  lastUpdatedAt: string;
}

export interface ModbusLog {
  id: string;
  timestamp: string;
  deviceId: string;
  slaveId: number;
  registerAddress: number;
  metric: string;
  value: number;
  unit: string;
  rawHexPayload: string;
  status: string;
  isError: boolean;
}

export interface PlantSummary {
  totalFlowRate: number;
  totalCumulativeVolume: number;
  averageTankLevel: number;
  onlineMeterCount: number;
  totalMeterCount: number;
  activeAlertCount: number;
}

export interface SystemSettings {
  apiBaseUrl: string;
  isSimulationMode: boolean;
  pollingIntervalMs: number;
  simulatedMeterCount: number;
  autoFailover: boolean;
}
