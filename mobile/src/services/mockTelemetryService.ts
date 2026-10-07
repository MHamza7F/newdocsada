import { FlowMeter, StorageTank, ModbusLog } from '../types/scada';

const meterProfiles: Record<number, { baseFlow: number; amplitude: number; name: string }> = {
  1: { baseFlow: 45.0, amplitude: 8.5, name: 'Inlet Line #1 (Raw)' },
  2: { baseFlow: 38.5, amplitude: 6.0, name: 'Boiler Feed Loop #2' },
  3: { baseFlow: 62.0, amplitude: 12.0, name: 'Main Cooling Loop #3' },
  4: { baseFlow: 28.0, amplitude: 5.0, name: 'RO Permeate Line #4' },
  5: { baseFlow: 52.5, amplitude: 9.0, name: 'Chemical Dosing Header #5' },
  6: { baseFlow: 18.5, amplitude: 4.0, name: 'Effluent Discharge #6' },
  7: { baseFlow: 74.0, amplitude: 15.0, name: 'Turbine Condenser #7' },
  8: { baseFlow: 31.0, amplitude: 7.0, name: 'Scrubber Rinse Line #8' },
};

let timeElapsed = 0;
let logCycle = 0;

export const getInitialMeters = (count = 6): FlowMeter[] => {
  const list: FlowMeter[] = [];
  const total = Math.min(Math.max(count, 1), 8);

  for (let i = 1; i <= total; i++) {
    const profile = meterProfiles[i] || { baseFlow: 35.0, amplitude: 7.0, name: `Flowmeter #${i}` };
    list.push({
      deviceExternalId: `norvi-slave-${i}`,
      deviceName: profile.name,
      flowRate: profile.baseFlow,
      flowUnit: 'L/min',
      totalizer: 12500 * i + Math.floor(Math.random() * 500),
      totalizerUnit: 'L',
      isOnline: true,
      status: 'Online',
      lastSeen: new Date().toLocaleTimeString(),
      modbusSlaveId: i,
      ipAddress: `192.168.1.${100 + i}`,
      isFlowIncreasing: true,
    });
  }
  return list;
};

export const getInitialTanks = (): StorageTank[] => [
  {
    id: 'tank-1',
    tankCode: 'TANK-RAW-01',
    name: 'Raw Water Buffer Tank #1',
    capacityLiters: 50000,
    currentVolumeLiters: 34500,
    levelPercentage: 69.0,
    temperatureCelsius: 22.4,
    status: 'Normal',
    liquidType: 'Raw Utility Water',
    inletFlowRate: 125.0,
    outletFlowRate: 110.0,
    lastUpdatedAt: new Date().toLocaleTimeString(),
  },
  {
    id: 'tank-2',
    tankCode: 'TANK-RO-02',
    name: 'RO Permeate Clean Tank #2',
    capacityLiters: 40000,
    currentVolumeLiters: 31200,
    levelPercentage: 78.0,
    temperatureCelsius: 20.1,
    status: 'Filling',
    liquidType: 'Treated Pure Water',
    inletFlowRate: 95.0,
    outletFlowRate: 60.0,
    lastUpdatedAt: new Date().toLocaleTimeString(),
  },
  {
    id: 'tank-3',
    tankCode: 'TANK-EFF-04',
    name: 'Clarifier Discharge Tank #4',
    capacityLiters: 60000,
    currentVolumeLiters: 51600,
    levelPercentage: 86.0,
    temperatureCelsius: 25.8,
    status: 'HighAlert',
    liquidType: 'Treated Effluent',
    inletFlowRate: 140.0,
    outletFlowRate: 80.0,
    lastUpdatedAt: new Date().toLocaleTimeString(),
  }
];

export const tickSimulation = (meters: FlowMeter[], tanks: StorageTank[], deltaSeconds: number) => {
  timeElapsed += deltaSeconds;

  meters.forEach((meter) => {
    const id = meter.modbusSlaveId || 1;
    const profile = meterProfiles[id] || { baseFlow: 35.0, amplitude: 7.0 };
    const wave = Math.sin(timeElapsed * 0.1 + id) * profile.amplitude;
    const noise = (Math.random() - 0.5) * 2;
    const newFlow = Math.max(0, parseFloat((profile.baseFlow + wave + noise).toFixed(1)));
    
    meter.isFlowIncreasing = newFlow >= meter.flowRate;
    meter.flowRate = newFlow;
    meter.totalizer = parseFloat((meter.totalizer + (meter.flowRate / 60) * deltaSeconds).toFixed(1));
    meter.lastSeen = new Date().toLocaleTimeString();
  });

  tanks.forEach((tank) => {
    const netRate = tank.inletFlowRate - tank.outletFlowRate;
    const volDelta = (netRate / 60) * deltaSeconds;
    const newVol = Math.min(Math.max(tank.currentVolumeLiters + volDelta, 0), tank.capacityLiters);
    tank.currentVolumeLiters = Math.round(newVol);
    tank.levelPercentage = parseFloat(((newVol / tank.capacityLiters) * 100).toFixed(1));
    tank.lastUpdatedAt = new Date().toLocaleTimeString();
  });
};

export const generateModbusLog = (meters: FlowMeter[]): ModbusLog => {
  if (!meters || meters.length === 0) {
    return {
      id: `log-${Date.now()}`,
      timestamp: new Date().toLocaleTimeString(),
      deviceId: 'norvi-slave-1',
      slaveId: 1,
      registerAddress: 40001,
      metric: 'FlowRate',
      value: 0,
      unit: 'L/min',
      rawHexPayload: '[01 03 00 00 00 00 00 00]',
      status: 'IDLE',
      isError: false,
    };
  }

  logCycle = (logCycle + 1) % meters.length;
  const m = meters[logCycle];
  const slaveId = m?.modbusSlaveId ?? (logCycle + 1);
  const hex = `[0${slaveId} 03 04 ${(Math.random() * 0xFF).toString(16).padStart(2, '0').toUpperCase()} ${(Math.random() * 0xFF).toString(16).padStart(2, '0').toUpperCase()} 9B 42]`;

  return {
    id: `log-${Date.now()}-${Math.random().toString(36).substring(2, 7)}`,
    timestamp: new Date().toLocaleTimeString(),
    deviceId: m?.deviceExternalId ?? `norvi-slave-${slaveId}`,
    slaveId: slaveId,
    registerAddress: 40001,
    metric: 'FlowRate',
    value: m?.flowRate ?? 0,
    unit: m?.flowUnit ?? 'L/min',
    rawHexPayload: hex,
    status: 'ACK / CRC OK',
    isError: false,
  };
};
