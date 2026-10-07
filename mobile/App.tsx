import React, { useState } from 'react';
import {
  SafeAreaView,
  View,
  Text,
  TouchableOpacity,
  ScrollView,
  StyleSheet,
  StatusBar,
  Alert,
  TextInput,
  Switch,
  Platform,
} from 'react-native';
import { TelemetryProvider, useTelemetry } from './src/context/TelemetryContext';
import { Colors } from './src/theme/colors';
import { FlowMeter, StorageTank, ModbusLog } from './src/types/scada';

const showAlert = (title: string, message: string): void => {
  if (Platform.OS === 'web') {
    if (typeof window !== 'undefined' && window.alert) {
      window.alert(`${title}\n\n${message}`);
    }
  } else {
    Alert.alert(title, message);
  }
};

const MainDashboard: React.FC = () => {
  const [activeTab, setActiveTab] = useState<'monitoring' | 'analytics' | 'logs' | 'settings'>('monitoring');
  const { meters, tanks, logs, summary, settings, updateSettings, isPaused, togglePause, clearLogs } = useTelemetry();

  return (
    <SafeAreaView style={styles.container}>
      <StatusBar barStyle="light-content" backgroundColor={Colors.bgDark} />

      {/* App Header */}
      <View style={styles.header}>
        <View>
          <Text style={styles.headerTitle}>⚡ SCADA INDUSTRIAL</Text>
          <Text style={styles.headerSubtitle}>Real-Time Flowmeter &amp; Utility Stream</Text>
        </View>
        <View style={styles.badgeSim}>
          <Text style={styles.badgeSimText}>● SIMULATION</Text>
        </View>
      </View>

      {/* Main Tab Navigation */}
      <View style={styles.tabBar}>
        <TouchableOpacity
          style={[styles.tabBtn, activeTab === 'monitoring' && styles.tabBtnActive]}
          onPress={() => setActiveTab('monitoring')}
        >
          <Text style={[styles.tabBtnText, activeTab === 'monitoring' && styles.tabBtnTextActive]}>📊 Monitor</Text>
        </TouchableOpacity>

        <TouchableOpacity
          style={[styles.tabBtn, activeTab === 'analytics' && styles.tabBtnActive]}
          onPress={() => setActiveTab('analytics')}
        >
          <Text style={[styles.tabBtnText, activeTab === 'analytics' && styles.tabBtnTextActive]}>🛢️ Tanks</Text>
        </TouchableOpacity>

        <TouchableOpacity
          style={[styles.tabBtn, activeTab === 'logs' && styles.tabBtnActive]}
          onPress={() => setActiveTab('logs')}
        >
          <Text style={[styles.tabBtnText, activeTab === 'logs' && styles.tabBtnTextActive]}>📋 Logs</Text>
        </TouchableOpacity>

        <TouchableOpacity
          style={[styles.tabBtn, activeTab === 'settings' && styles.tabBtnActive]}
          onPress={() => setActiveTab('settings')}
        >
          <Text style={[styles.tabBtnText, activeTab === 'settings' && styles.tabBtnTextActive]}>⚙️ Config</Text>
        </TouchableOpacity>
      </View>

      {/* Tab Content */}
      <ScrollView contentContainerStyle={styles.content}>
        {activeTab === 'monitoring' && (
          <View>
            {/* KPI Summary Cards */}
            <View style={styles.kpiGrid}>
              <View style={styles.kpiCard}>
                <Text style={styles.kpiLabel}>Total Flow Rate</Text>
                <Text style={[styles.kpiValue, { color: Colors.cyan }]}>
                  {(summary?.totalFlowRate ?? 0).toFixed(1)} L/min
                </Text>
              </View>

              <View style={styles.kpiCard}>
                <Text style={styles.kpiLabel}>Cumulative Volume</Text>
                <Text style={[styles.kpiValue, { color: Colors.textPrimary }]}>
                  {Math.round(summary?.totalCumulativeVolume ?? 0).toLocaleString()} L
                </Text>
              </View>
            </View>

            <View style={styles.kpiGrid}>
              <View style={styles.kpiCard}>
                <Text style={styles.kpiLabel}>Avg Tank Level</Text>
                <Text style={[styles.kpiValue, { color: Colors.emerald }]}>
                  {(summary?.averageTankLevel ?? 0).toFixed(1)}%
                </Text>
              </View>

              <View style={styles.kpiCard}>
                <Text style={styles.kpiLabel}>Meters Online</Text>
                <Text style={[styles.kpiValue, { color: Colors.textPrimary }]}>
                  {summary?.onlineMeterCount ?? 0} / {summary?.totalMeterCount ?? 0}
                </Text>
              </View>
            </View>

            {/* Action Bar */}
            <View style={styles.sectionHeader}>
              <Text style={styles.sectionTitle}>Connected Flowmeters</Text>
              <TouchableOpacity style={styles.pauseBtn} onPress={togglePause}>
                <Text style={styles.pauseBtnText}>{isPaused ? '▶ Resume' : '⏸ Pause'}</Text>
              </TouchableOpacity>
            </View>

            {/* Meter Cards List with guaranteed unique keys */}
            {(meters || []).map((meter: FlowMeter, index: number) => {
              const itemKey = meter?.deviceExternalId ? `meter-${meter.deviceExternalId}` : `meter-idx-${index}`;
              return (
                <View key={itemKey} style={styles.meterCard}>
                  <View style={styles.meterCardHeader}>
                    <View>
                      <Text style={styles.meterName}>{meter?.deviceName ?? `Flowmeter #${index + 1}`}</Text>
                      <Text style={styles.meterId}>{meter?.deviceExternalId ?? `node-${index + 1}`}</Text>
                    </View>
                    <View style={styles.statusPill}>
                      <View style={styles.statusDot} />
                      <Text style={styles.statusText}>{meter?.status ?? 'Online'}</Text>
                    </View>
                  </View>

                  <View style={styles.readoutGrid}>
                    <View style={styles.readoutBox}>
                      <View style={{ flexDirection: 'row', alignItems: 'center' }}>
                        <Text style={styles.readoutValueCyan}>
                          {(meter?.flowRate ?? 0).toFixed(1)} <Text style={styles.readoutUnit}>{meter?.flowUnit ?? 'L/min'}</Text>
                        </Text>
                        <Text style={{ marginLeft: 6, color: meter?.isFlowIncreasing ? Colors.cyan : Colors.textMuted }}>
                          {meter?.isFlowIncreasing ? '▲' : '▼'}
                        </Text>
                      </View>
                      <Text style={styles.readoutLabel}>Instantaneous Flow</Text>
                    </View>

                    <View style={styles.readoutBox}>
                      <Text style={styles.readoutValueWhite}>
                        {(meter?.totalizer ?? 0).toFixed(1)} <Text style={styles.readoutUnit}>{meter?.totalizerUnit ?? 'L'}</Text>
                      </Text>
                      <Text style={styles.readoutLabel}>Cumulative Total</Text>
                    </View>
                  </View>

                  <View style={styles.meterFooter}>
                    <Text style={styles.lastSeen}>Heartbeat: {meter?.lastSeen ?? '--:--:--'}</Text>
                    <TouchableOpacity
                      style={styles.auditBtn}
                      onPress={() =>
                        showAlert(
                          '📄 Meter Audit Report',
                          `Certified Reading:\n• Meter: ${meter?.deviceName}\n• Node: ${meter?.deviceExternalId}\n• Flow: ${meter?.flowRate} L/min\n• Total: ${meter?.totalizer} L\n• Last Seen: ${meter?.lastSeen}`
                        )
                      }
                    >
                      <Text style={styles.auditBtnText}>Audit Report</Text>
                    </TouchableOpacity>
                  </View>
                </View>
              );
            })}
          </View>
        )}

        {activeTab === 'analytics' && (
          <View>
            <Text style={styles.sectionTitle}>Plant Storage Inventory</Text>
            {(tanks || []).map((tank: StorageTank, index: number) => {
              const tankKey = tank?.id ? `tank-${tank.id}` : `tank-${tank?.tankCode || index}`;
              return (
                <View key={tankKey} style={styles.tankCard}>
                  <View style={styles.meterCardHeader}>
                    <View>
                      <Text style={styles.meterName}>{tank?.name ?? 'Storage Tank'}</Text>
                      <Text style={styles.meterId}>{tank?.tankCode ?? `TANK-${index + 1}`}</Text>
                    </View>
                    <View style={styles.liquidTag}>
                      <Text style={styles.liquidTagText}>{tank?.liquidType ?? 'Fluid'}</Text>
                    </View>
                  </View>

                  <View style={{ marginVertical: 10 }}>
                    <View style={{ flexDirection: 'row', justifyContent: 'space-between', marginBottom: 4 }}>
                      <Text style={styles.readoutLabel}>Fluid Level</Text>
                      <Text style={{ color: Colors.cyan, fontWeight: 'bold' }}>{tank?.levelPercentage ?? 0}%</Text>
                    </View>
                    <View style={styles.progressBarTrack}>
                      <View style={[styles.progressBarFill, { width: `${Math.min(tank?.levelPercentage ?? 0, 100)}%` }]} />
                    </View>
                  </View>

                  <View style={styles.tankStatsRow}>
                    <View style={styles.tankStatItem}>
                      <Text style={styles.tankStatValue}>{Math.round(tank?.currentVolumeLiters ?? 0).toLocaleString()} L</Text>
                      <Text style={styles.tankStatLabel}>Volume</Text>
                    </View>
                    <View style={styles.tankStatItem}>
                      <Text style={[styles.tankStatValue, { color: Colors.emerald }]}>
                        {((tank?.inletFlowRate ?? 0) - (tank?.outletFlowRate ?? 0)).toFixed(1)} L/m
                      </Text>
                      <Text style={styles.tankStatLabel}>Net Delta</Text>
                    </View>
                    <View style={styles.tankStatItem}>
                      <Text style={[styles.tankStatValue, { color: Colors.amber }]}>{(tank?.temperatureCelsius ?? 0).toFixed(1)}°C</Text>
                      <Text style={styles.tankStatLabel}>Temp</Text>
                    </View>
                  </View>
                </View>
              );
            })}
          </View>
        )}

        {activeTab === 'logs' && (
          <View>
            <View style={styles.sectionHeader}>
              <Text style={styles.sectionTitle}>Modbus Register Stream (value.txt)</Text>
              <TouchableOpacity style={styles.clearBtn} onPress={clearLogs}>
                <Text style={styles.clearBtnText}>Clear Logs</Text>
              </TouchableOpacity>
            </View>

            {(logs || []).map((log: ModbusLog, index: number) => {
              const logKey = log?.id ? `log-${log.id}` : `log-${index}`;
              return (
                <View key={logKey} style={styles.logCard}>
                  <View style={{ flexDirection: 'row', justifyContent: 'space-between' }}>
                    <Text style={styles.logTimestamp}>{log?.timestamp ?? '--:--:--'}</Text>
                    <Text style={styles.logDevice}>[{log?.deviceId ?? 'unknown'}]</Text>
                    <Text style={styles.logStatus}>{log?.status ?? 'OK'}</Text>
                  </View>
                  <View style={{ flexDirection: 'row', justifyContent: 'space-between', marginVertical: 4 }}>
                    <Text style={styles.logRegister}>Reg {log?.registerAddress ?? 40001} (FlowRate)</Text>
                    <Text style={styles.logValue}>{log?.value ?? 0} L/min</Text>
                  </View>
                  <Text style={styles.logHex}>HEX: {log?.rawHexPayload ?? '[-- -- --]'}</Text>
                </View>
              );
            })}
          </View>
        )}

        {activeTab === 'settings' && (
          <View style={styles.settingsCard}>
            <Text style={styles.sectionTitle}>Network &amp; Simulation Settings</Text>

            <View style={styles.settingRow}>
              <View>
                <Text style={styles.settingTitle}>Simulation Mode</Text>
                <Text style={styles.settingDesc}>Run realistic telemetry stream</Text>
              </View>
              <Switch
                value={settings?.isSimulationMode ?? true}
                onValueChange={(val) => updateSettings({ isSimulationMode: val })}
                trackColor={{ true: Colors.cyan, false: Colors.border }}
              />
            </View>

            <View style={styles.settingRow}>
              <View>
                <Text style={styles.settingTitle}>Auto-Failover</Text>
                <Text style={styles.settingDesc}>Fallback if backend drops</Text>
              </View>
              <Switch
                value={settings?.autoFailover ?? true}
                onValueChange={(val) => updateSettings({ autoFailover: val })}
                trackColor={{ true: Colors.emerald, false: Colors.border }}
              />
            </View>

            <View style={{ marginTop: 14 }}>
              <Text style={styles.settingTitle}>Backend API Base URL</Text>
              <TextInput
                style={styles.textInput}
                value={settings?.apiBaseUrl ?? ''}
                onChangeText={(val) => updateSettings({ apiBaseUrl: val })}
                placeholder="http://192.168.1.15:5080"
                placeholderTextColor={Colors.textDim}
              />
            </View>
          </View>
        )}
      </ScrollView>
    </SafeAreaView>
  );
};

export default function App(): React.JSX.Element {
  return (
    <TelemetryProvider>
      <MainDashboard />
    </TelemetryProvider>
  );
}

const styles = StyleSheet.create({
  container: {
    flex: 1,
    backgroundColor: Colors.bgDark,
  },
  header: {
    paddingHorizontal: 16,
    paddingVertical: 12,
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
    borderBottomWidth: 1,
    borderBottomColor: Colors.border,
    backgroundColor: Colors.bgSurface,
  },
  headerTitle: {
    fontSize: 16,
    fontWeight: 'bold',
    color: Colors.textPrimary,
  },
  headerSubtitle: {
    fontSize: 11,
    color: Colors.textMuted,
    marginTop: 2,
  },
  badgeSim: {
    backgroundColor: 'rgba(6, 182, 212, 0.15)',
    borderColor: Colors.cyan,
    borderWidth: 1,
    paddingHorizontal: 8,
    paddingVertical: 4,
    borderRadius: 6,
  },
  badgeSimText: {
    color: Colors.cyan,
    fontSize: 10,
    fontWeight: 'bold',
  },
  tabBar: {
    flexDirection: 'row',
    backgroundColor: Colors.bgSurface,
    borderBottomWidth: 1,
    borderBottomColor: Colors.border,
    paddingHorizontal: 8,
  },
  tabBtn: {
    flex: 1,
    paddingVertical: 10,
    alignItems: 'center',
  },
  tabBtnActive: {
    borderBottomWidth: 2,
    borderBottomColor: Colors.cyan,
  },
  tabBtnText: {
    color: Colors.textMuted,
    fontSize: 12,
    fontWeight: '600',
  },
  tabBtnTextActive: {
    color: Colors.cyan,
    fontWeight: 'bold',
  },
  content: {
    padding: 16,
    paddingBottom: 40,
  },
  kpiGrid: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    marginBottom: 10,
  },
  kpiCard: {
    flex: 1,
    backgroundColor: Colors.bgSurface,
    borderColor: Colors.border,
    borderWidth: 1,
    borderRadius: 10,
    padding: 12,
    marginHorizontal: 4,
  },
  kpiLabel: {
    color: Colors.textMuted,
    fontSize: 11,
    marginBottom: 4,
  },
  kpiValue: {
    fontSize: 17,
    fontWeight: 'bold',
  },
  sectionHeader: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
    marginVertical: 12,
  },
  sectionTitle: {
    color: Colors.textPrimary,
    fontSize: 15,
    fontWeight: 'bold',
  },
  pauseBtn: {
    borderWidth: 1,
    borderColor: Colors.borderLight,
    paddingHorizontal: 10,
    paddingVertical: 4,
    borderRadius: 6,
  },
  pauseBtnText: {
    color: Colors.textPrimary,
    fontSize: 11,
  },
  meterCard: {
    backgroundColor: Colors.bgSurface,
    borderColor: Colors.border,
    borderWidth: 1,
    borderRadius: 12,
    padding: 14,
    marginBottom: 12,
  },
  meterCardHeader: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
    marginBottom: 10,
  },
  meterName: {
    color: Colors.textPrimary,
    fontSize: 14,
    fontWeight: 'bold',
  },
  meterId: {
    color: Colors.textMuted,
    fontSize: 11,
  },
  statusPill: {
    flexDirection: 'row',
    alignItems: 'center',
    backgroundColor: Colors.bgDark,
    borderColor: Colors.emerald,
    borderWidth: 1,
    paddingHorizontal: 8,
    paddingVertical: 3,
    borderRadius: 12,
  },
  statusDot: {
    width: 6,
    height: 6,
    borderRadius: 3,
    backgroundColor: Colors.emerald,
    marginRight: 5,
  },
  statusText: {
    color: Colors.emerald,
    fontSize: 10,
    fontWeight: 'bold',
  },
  readoutGrid: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    marginBottom: 10,
  },
  readoutBox: {
    flex: 1,
    backgroundColor: Colors.bgDark,
    borderColor: Colors.border,
    borderWidth: 1,
    borderRadius: 8,
    padding: 10,
    marginHorizontal: 3,
  },
  readoutValueCyan: {
    color: Colors.cyan,
    fontSize: 18,
    fontWeight: 'bold',
    fontFamily: Platform.OS === 'ios' ? 'Courier' : 'monospace',
  },
  readoutValueWhite: {
    color: Colors.textPrimary,
    fontSize: 16,
    fontWeight: 'bold',
    fontFamily: Platform.OS === 'ios' ? 'Courier' : 'monospace',
  },
  readoutUnit: {
    fontSize: 11,
    color: Colors.textMuted,
  },
  readoutLabel: {
    color: Colors.textMuted,
    fontSize: 10,
    marginTop: 2,
  },
  meterFooter: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
    borderTopWidth: 1,
    borderTopColor: Colors.border,
    paddingTop: 8,
  },
  lastSeen: {
    color: Colors.textDim,
    fontSize: 10,
  },
  auditBtn: {
    backgroundColor: Colors.bgCard,
    borderColor: Colors.borderLight,
    borderWidth: 1,
    paddingHorizontal: 8,
    paddingVertical: 4,
    borderRadius: 6,
  },
  auditBtnText: {
    color: Colors.textPrimary,
    fontSize: 10,
  },
  tankCard: {
    backgroundColor: Colors.bgSurface,
    borderColor: Colors.border,
    borderWidth: 1,
    borderRadius: 12,
    padding: 14,
    marginBottom: 12,
  },
  liquidTag: {
    backgroundColor: Colors.bgDark,
    borderColor: Colors.border,
    borderWidth: 1,
    paddingHorizontal: 8,
    paddingVertical: 3,
    borderRadius: 6,
  },
  liquidTagText: {
    color: Colors.cyan,
    fontSize: 11,
    fontWeight: 'bold',
  },
  progressBarTrack: {
    height: 8,
    backgroundColor: Colors.bgDark,
    borderRadius: 4,
    overflow: 'hidden',
  },
  progressBarFill: {
    height: '100%',
    backgroundColor: Colors.cyan,
  },
  tankStatsRow: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    marginTop: 10,
  },
  tankStatItem: {
    flex: 1,
    backgroundColor: Colors.bgDark,
    borderRadius: 6,
    padding: 8,
    marginHorizontal: 3,
    alignItems: 'center',
  },
  tankStatValue: {
    color: Colors.textPrimary,
    fontSize: 12,
    fontWeight: 'bold',
  },
  tankStatLabel: {
    color: Colors.textMuted,
    fontSize: 10,
    marginTop: 2,
  },
  logCard: {
    backgroundColor: '#080C14',
    borderColor: '#1E293B',
    borderWidth: 1,
    borderRadius: 6,
    padding: 8,
    marginBottom: 6,
  },
  logTimestamp: {
    color: Colors.textMuted,
    fontSize: 10,
    fontFamily: Platform.OS === 'ios' ? 'Courier' : 'monospace',
  },
  logDevice: {
    color: Colors.cyan,
    fontSize: 10,
    fontFamily: Platform.OS === 'ios' ? 'Courier' : 'monospace',
    fontWeight: 'bold',
  },
  logStatus: {
    color: Colors.emerald,
    fontSize: 10,
    fontFamily: Platform.OS === 'ios' ? 'Courier' : 'monospace',
    fontWeight: 'bold',
  },
  logRegister: {
    color: Colors.textSecondary,
    fontSize: 11,
  },
  logValue: {
    color: Colors.textPrimary,
    fontSize: 11,
    fontWeight: 'bold',
    fontFamily: Platform.OS === 'ios' ? 'Courier' : 'monospace',
  },
  logHex: {
    color: Colors.amber,
    fontSize: 9,
    fontFamily: Platform.OS === 'ios' ? 'Courier' : 'monospace',
  },
  clearBtn: {
    borderWidth: 1,
    borderColor: Colors.borderLight,
    paddingHorizontal: 8,
    paddingVertical: 3,
    borderRadius: 6,
  },
  clearBtnText: {
    color: Colors.textMuted,
    fontSize: 10,
  },
  settingsCard: {
    backgroundColor: Colors.bgSurface,
    borderColor: Colors.border,
    borderWidth: 1,
    borderRadius: 12,
    padding: 16,
  },
  settingRow: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
    paddingVertical: 12,
    borderBottomWidth: 1,
    borderBottomColor: Colors.border,
  },
  settingTitle: {
    color: Colors.textPrimary,
    fontSize: 13,
    fontWeight: 'bold',
  },
  settingDesc: {
    color: Colors.textMuted,
    fontSize: 11,
    marginTop: 2,
  },
  textInput: {
    backgroundColor: Colors.bgDark,
    borderColor: Colors.border,
    borderWidth: 1,
    borderRadius: 8,
    padding: 10,
    color: Colors.textPrimary,
    fontSize: 13,
    marginTop: 6,
  },
});
