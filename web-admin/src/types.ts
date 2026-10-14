import type { components } from './generated/client'
export type { paths, operations, components } from './generated/client'
// 服务端以 JSON 数字输出；OpenAPI 同时允许数字字符串的输入形式。
export type JsonNumbers<T> = number extends T ? Exclude<T, string> : T extends (infer U)[] ? JsonNumbers<U>[] : T extends object ? { [K in keyof T]: JsonNumbers<T[K]> } : T
export type SchemaDto<K extends keyof components['schemas']> = JsonNumbers<components['schemas'][K]>
export type Page<T> = Omit<SchemaDto<'PagedChannelResponse'>, 'items'> & { items: T[] }
export type User = SchemaDto<'UserDto'>
export type LoginResult = SchemaDto<'AuthResponse'>
export type Role = SchemaDto<'AdministrationRoleDto'>
export type Permission = SchemaDto<'PermissionDto'>
export type Device = SchemaDto<'DeviceDto'> & {
  pluginId?: string | null
  pluginName?: string | null
  syncError?: string | null
  terminalBoxId?: number | null
  terminalBoxName?: string | null
  terminalBoxCode?: string | null
  onuDeviceId?: number | null
  onuName?: string | null
  onuAlias?: string | null
  networkPort?: number | null
  isSwitchPort?: boolean
  portLabel?: string | null
  onuRxPower?: number | null
}
export type DeviceInput = Omit<SchemaDto<'DeviceRequest'>, 'password'> & {
  password?: string | null
  pluginId?: string | null
  terminalBoxId?: number | null
  onuDeviceId?: number | null
  portNumber?: number | null
  isSwitchPort?: boolean
  portLabel?: string | null
}
export interface DevicePlugin {
  id: string
  name: string
  vendor: string
  version: string
  description?: string | null
  status: 'active' | 'disabled'
  endpointUrl: string
  capabilities: string[]
  configSchema?: Record<string, unknown> | null
  deviceCount: number
  healthStatus?: 'online' | 'offline' | 'disabled' | 'unknown' | null
  createdAt: string
  updatedAt: string
}
export interface PluginCreateInput {
  id: string
  name: string
  vendor: string
  version: string
  description?: string | null
  endpointUrl: string
  capabilities?: string[]
  configSchema?: Record<string, unknown> | null
}
export type Channel = SchemaDto<'ChannelDto'> & {
  alias?: string | null
  ip?: string | null
  username?: string | null
  hasPassword?: boolean
  remark?: string | null
  deviceModel?: string | null
  deviceHost?: string | null
  devicePort?: number | null
  deviceSerial?: string | null
  pluginId?: string | null
  pluginName?: string | null
  firmwareVersion?: string | null
  sortOrder?: number
  terminalBoxId?: number | null
  terminalBoxName?: string | null
  terminalBoxCode?: string | null
  onuDeviceId?: number | null
  onuName?: string | null
  onuAlias?: string | null
  onuStatus?: string | null
  networkPort?: number | null
  isSwitchPort?: boolean | null
  portLabel?: string | null
  onuHealthStatus?: string | null
  onuRootCause?: string | null
  onuRxPower?: number | null
  onuIfDesc?: string | null
  opticalLink?: OpticalLinkInfoDto | null
}
export interface OpticalLinkInfoDto {
  terminalBoxId?: number | null
  boxName?: string | null
  boxCode?: string | null
  onuId?: number | null
  onuName?: string | null
  onuAlias?: string | null
  healthStatus?: string | null
  rootCause?: string | null
  rxPower?: number | null
  ifDesc?: string | null
  portNumber?: number | null
  portLabel?: string | null
}
export type OrganizationNode = SchemaDto<'OrganizationNodeDto'>
export type Organization = SchemaDto<'OrganizationTreeDto'>
export type OrganizationKind = keyof Organization

export interface OrganizationStats {
  totalWorkshops: number
  totalAreas: number
  totalUnits: number
  totalChannels: number
  totalComputers: number
  totalTerminalBoxes: number
}

export interface Computer {
  id: number
  name: string
  ip: string
  mac?: string | null
  owner?: string | null
  department?: string | null
  os?: string | null
  location?: string | null
  workshopId?: number | null
  workshopName?: string | null
  areaId?: number | null
  areaName?: string | null
  unitId?: number | null
  unitName?: string | null
  terminalBoxId?: number | null
  terminalBoxName?: string | null
  terminalBoxCode?: string | null
  onuDeviceId?: number | null
  onuName?: string | null
  onuAlias?: string | null
  portNumber: number
  isSwitchPort: boolean
  portLabel?: string | null
  status: 'online' | 'offline' | 'unknown'
  responseTimeMs?: number | null
  lastSeenAt?: string | null
  lastProbedAt?: string | null
  remarks?: string | null
  createdAt: string
  updatedAt: string
}

export interface ComputerInput {
  name: string
  ip: string
  mac?: string | null
  owner?: string | null
  department?: string | null
  os?: string | null
  location?: string | null
  workshopId?: number | null
  areaId?: number | null
  unitId?: number | null
  terminalBoxId?: number | null
  onuDeviceId?: number | null
  portNumber?: number
  isSwitchPort?: boolean
  portLabel?: string | null
  remarks?: string | null
}

export interface ComputerProbeResult {
  id: number
  ip: string
  status: string
  responseTimeMs?: number | null
  probedAt: string
}

export interface IpPool {
  id: number
  name: string
  cidr: string
  startIp: string
  endIp: string
  gateway?: string | null
  netmask: string
  vlanId?: number | null
  description?: string | null
  workshopId?: number | null
  workshopName?: string | null
  areaId?: number | null
  areaName?: string | null
  autoScan: boolean
  totalCapacity: number
  availableCount: number
  registeredCount: number
  unregisteredActiveCount: number
  conflictCount: number
  reservedCount: number
  lastScannedAt?: string | null
  createdAt: string
  updatedAt: string
}

export interface IpPoolInput {
  name: string
  cidr: string
  startIp?: string | null
  endIp?: string | null
  gateway?: string | null
  netmask?: string | null
  vlanId?: number | null
  description?: string | null
  workshopId?: number | null
  areaId?: number | null
  autoScan?: boolean
}

export interface BoundDeviceInfo {
  deviceType: 'channel' | 'device' | 'onu' | 'computer' | 'network_device'
  deviceId: number
  deviceName: string
  detail?: string | null
  mac?: string | null
  location?: string | null
  owner?: string | null
  interfaceName?: string | null
  role?: string | null
  relatedIps?: string[] | null
}

export interface NetworkDeviceIp {
  id: number
  deviceId: number
  ip: string
  vlanId?: number | null
  interfaceName: string
  role: 'gateway' | 'management' | 'interconnect' | 'service' | string
  description?: string | null
}

export interface NetworkDeviceIpInput {
  ip: string
  vlanId?: number | null
  interfaceName: string
  role: string
  description?: string | null
}

export interface NetworkDevice {
  id: number
  name: string
  deviceType: 'core_switch' | 'agg_switch' | 'access_switch' | 'router' | 'firewall' | 'ac' | 'server' | string
  vendor?: string | null
  model?: string | null
  managementIp?: string | null
  mac?: string | null
  location?: string | null
  workshopId?: number | null
  workshopName?: string | null
  areaId?: number | null
  areaName?: string | null
  owner?: string | null
  status: 'online' | 'offline' | 'unknown' | string
  responseTimeMs?: number | null
  lastProbedAt?: string | null
  remarks?: string | null
  createdAt: string
  updatedAt: string
  assignedIps: NetworkDeviceIp[]
  portCount?: number
  linkedPortCount?: number
  vmCount?: number
  ports?: DevicePort[]
  virtualMachines?: VirtualMachine[]
}

export interface DeviceLink {
  id: number
  localPortId: number
  peerPortId: number
  peerDeviceType: string
  peerDeviceId: number
  peerDeviceName: string
  peerPortName: string
  cableType: 'copper' | 'single_fiber' | 'multi_fiber' | 'dac' | 'telephone_line' | string
  cableLabel?: string | null
  remarks?: string | null
}

export interface DevicePort {
  id: number
  deviceId: number
  deviceType: string
  portName: string
  portType: 'copper' | 'fiber' | 'pon' | 'wifi' | 'pots' | 'mgmt' | string
  speed: '100M' | '1Gbps' | '2.5Gbps' | '10Gbps' | '40Gbps' | '100Gbps' | 'other' | string
  status: 'up' | 'down' | 'unused' | 'disabled' | string
  slotNumber: number
  portIndex: number
  remarks?: string | null
  link?: DeviceLink | null
}

export interface DevicePortInput {
  portName: string
  portType?: string
  speed?: string
  status?: string
  slotNumber?: number
  portIndex?: number
  remarks?: string | null
}

export interface BatchGeneratePortsInput {
  copperCount: number
  fiberCount: number
  potsCount?: number
  copperPrefix?: string
  fiberPrefix?: string
  potsPrefix?: string
  copperSpeed?: string
  fiberSpeed?: string
  replaceExisting?: boolean
}

export interface ConnectLinkInput {
  localPortId: number
  peerPortId: number
  cableType?: string
  cableLabel?: string | null
  remarks?: string | null
  replaceExisting?: boolean
}

export interface PortCandidate {
  portId: number
  deviceId: number
  deviceType: string
  deviceName: string
  portName: string
  portType: string
  speed: string
  isLinked: boolean
  linkedPeerDesc?: string | null
}

export interface VirtualMachine {
  id: number
  hostDeviceId: number
  hostDeviceName: string
  vmName: string
  ip: string
  vlanId?: number | null
  os?: string | null
  cpuCores?: number | null
  memoryGb?: number | null
  status: 'running' | 'stopped' | string
  businessDesc?: string | null
  remarks?: string | null
  createdAt: string
  updatedAt: string
}

export interface VirtualMachineInput {
  vmName: string
  ip: string
  vlanId?: number | null
  os?: string | null
  cpuCores?: number | null
  memoryGb?: number | null
  status?: string
  businessDesc?: string | null
  remarks?: string | null
}

export interface NetworkDeviceInput {
  name: string
  deviceType: string
  vendor?: string | null
  model?: string | null
  managementIp?: string | null
  mac?: string | null
  location?: string | null
  workshopId?: number | null
  areaId?: number | null
  owner?: string | null
  status?: string
  remarks?: string | null
  assignedIps?: NetworkDeviceIpInput[]
}

export interface NetworkDeviceProbeResult {
  id: number
  name: string
  managementIp?: string | null
  status: string
  responseTimeMs?: number | null
  probedAt: string
}

export interface IpAddressStatus {
  ip: string
  status: 'available' | 'registered' | 'unregistered_active' | 'conflict' | 'reserved'
  subnetSlice?: string | null
  responseTimeMs?: number | null
  lastProbedAt?: string | null
  boundDevices: BoundDeviceInfo[]
}

export interface IpPoolDetail {
  pool: IpPool
  subnetSlices: string[]
  addresses: IpAddressStatus[]
}

export interface IpPoolScanResult {
  poolId: number
  scannedCount: number
  activeCount: number
  inactiveCount: number
  scannedAt: string
}
export type ScopeType = 'workshop' | 'area' | 'unit' | 'channel'
export type AccessScope = SchemaDto<'ScopeRequest'>
export type OnlineSession = SchemaDto<'AdministrationSessionDto'>
export type Audit = SchemaDto<'AdministrationAuditDto'>
export type Settings = SchemaDto<'PlatformSettings'>
export type Dashboard = SchemaDto<'DashboardDto'>
export interface NetworkInterfaceInfo {
  name: string
  description?: string
  type: string
  status: string
  speed: number
  bytesReceived: number
  bytesSent: number
  ipAddress?: string
}
export interface NetworkMetrics {
  rxBytesPerSecond: number
  txBytesPerSecond: number
  totalBytesReceived: number
  totalBytesSent: number
  interfaces: NetworkInterfaceInfo[]
}
export interface ServerPerformance {
  cpuCores: number
  processCpuPercent?: number
  processThreads: number
  processWorkingSetBytes: number
  processPrivateMemoryBytes: number
  gcHeapBytes?: number
  swapTotalBytes?: number
  swapUsedBytes?: number
}
export interface DiskInfo {
  name: string
  label?: string
  totalBytes?: number
  freeBytes?: number
  usedBytes?: number
  usedPercent?: number
  isDataPath: boolean
}
export interface HostInfo {
  osDescription: string
  osArchitecture: string
  framework: string
  machineName: string
  systemUptimeSeconds: number
  serverTime: string
}
export type SystemStatus = SchemaDto<'SystemStatisticsDto'> & {
  network?: NetworkMetrics
  performance?: ServerPerformance
  disks?: DiskInfo[]
  host?: HostInfo
}
export type LiveSession = SchemaDto<'LiveSessionDto'>
export type Recording = SchemaDto<'RecordingListResponse'>[number]
export type TimeSegment = SchemaDto<'RecordingSegmentDto'>
export type PlaybackSession = SchemaDto<'PlaybackSessionDto'>
export type PlaybackControl = Omit<SchemaDto<'PlaybackControlRequest'>, 'action'> & { action: 'pause' | 'resume' | 'seek' | 'speed' | 'step' }
export type PtzCommand = 'up' | 'down' | 'left' | 'right' | 'auto' | 'zoomIn' | 'zoomOut' | 'focusNear' | 'focusFar' | 'irisOpen' | 'irisClose'
export type Layout = Omit<SchemaDto<'AdministrationLayoutDto'>, 'kind' | 'layout'> & { kind: 'layout' | 'patrol'; layout: 1 | 4 | 6 | 8 | 9 | 10 | 16 | 25 }
export type Alarm = SchemaDto<'AlarmDto'>
export type AlarmDetail = SchemaDto<'AlarmDetailDto'>
export type ExportJob = SchemaDto<'ExportDto'>
export type Release = SchemaDto<'ReleaseDto'>
export type PublicRelease = Omit<SchemaDto<'LatestReleaseDto'>, 'packages'> & { packages?: Release[] }
export type EventKind = 'alarm.changed' | 'device.changed' | 'asset.changed' | 'media.changed' | 'export.changed' | 'access.changed' | 'onu.changed'
export interface ResourceEvent { id: string | number; version: number; kind: string }

export interface SslCertificate {
  id: number
  name: string
  commonName: string
  dnsNames: string[]
  issuerDn: string
  subjectDn: string
  serialNumber: string
  thumbprint: string
  validFrom: string
  validTo: string
  daysRemaining: number
  status: 'valid' | 'expiring_soon' | 'expired'
  isActive: boolean
  boundDomainCount: number
  createdAt: string
  certPem?: string | null
}

export interface SslCertificateUploadInput {
  name: string
  certPem: string
  keyPem: string
}

export interface SslDomain {
  id: number
  domain: string
  port: number
  protocol: 'https' | 'http'
  description: string
  isPrimary: boolean
  forceHttps: boolean
  hstsEnabled: boolean
  certificateId?: number | null
  certificateName?: string | null
  certificateCommonName?: string | null
  certificateValidTo?: string | null
  certMatchStatus: 'matched' | 'mismatched' | 'no_cert'
  enabled: boolean
  createdAt: string
  updatedAt: string
}

export interface SslDomainInput {
  domain: string
  port: number
  protocol: 'https' | 'http'
  description: string
  isPrimary: boolean
  forceHttps: boolean
  hstsEnabled: boolean
  certificateId?: number | null
  enabled: boolean
}

export interface SslOverview {
  activeCertificate?: SslCertificate | null
  totalCertificates: number
  expiringCertificates: number
  totalDomains: number
  primaryDomain?: SslDomain | null
  httpsEnforced: boolean
  certFilePath: string
  keyFilePath: string
  fileSynced: boolean
}

export interface NginxConfig {
  content: string
  configPath: string
  reloadCommand: string
}

export interface TerminalBoxDto {
  id: number
  code: string
  name: string
  location?: string | null
  areaId?: number | null
  areaName?: string | null
  unitId?: number | null
  unitName?: string | null
  remarks?: string | null
  onuCount: number
  channelCount: number
  computerCount?: number
  createdAt: string
  updatedAt: string
}

export interface TerminalBoxRequest {
  code: string
  name: string
  location?: string | null
  areaId?: number | null
  unitId?: number | null
  remarks?: string | null
}

export interface OnuDeviceDto {
  id: number
  neDn: string
  oltName: string
  oltIp: string
  onuId: string
  onuName: string
  alias?: string | null
  sn: string
  mac?: string | null
  terminalType: string
  status: string
  runStatus?: string | null
  lastUpTime?: string | null
  lastDownTime?: string | null
  lastDownCause?: string | null
  rxPower?: number | null
  txPower?: number | null
  terminalBoxId?: number | null
  terminalBoxName?: string | null
  terminalBoxCode?: string | null
  hasSubSwitch: boolean
  subSwitchModel?: string | null
  subSwitchPorts: number
  portsConfig?: string | null
  lastSyncedAt?: string | null
  boundChannelCount: number
  description?: string | null
  groupName?: string | null
  ip?: string | null
  syncWarning?: string | null
  oltId?: number | null
  portId?: number
  ifDesc?: string | null
  lastDyingGaspTime?: string | null
  healthStatus?: string
  rootCause?: string | null
  lastStateChangeTime?: string | null
}

export interface PonPortSummaryDto {
  portId: number
  ifDesc: string
  totalOnus: number
  onlineOnus: number
  offlineOnus: number
  subHealthOnus: number
  status: string
  brokenCauses?: string[] | null
}

export interface OltDeviceDto {
  id: number
  neDn: string
  name: string
  ip: string
  model: string
  vendor: string
  osVersion?: string | null
  patchVersion?: string | null
  status: string
  syncedAt: string
  totalPorts: number
  ports?: PonPortSummaryDto[] | null
}

export interface OpticalHealthTrendItemDto {
  time: string
  score: number
  availabilityRate: number
  offlineCount: number
  subHealthCount: number
}

export interface OpticalHealthIndexDto {
  score: number
  grade: string
  totalOnus: number
  onlineOnus: number
  offlineOnus: number
  healthyOnus: number
  subHealthOnus: number
  powerOffOnus: number
  fiberCutOnus: number
  trunkBrokenOnus: number
  splitterBrokenOnus?: number
  availabilityRate: number
  avgRxPower?: number | null
  evaluatedAt: string
  trend?: OpticalHealthTrendItemDto[] | null
}

export interface AutoTopologyBindingSuggestionDto {
  channelId: number
  channelName: string
  channelAlias?: string | null
  channelIp?: string | null
  currentOnuId?: number | null
  currentOnuName?: string | null
  suggestedOnuId: number
  suggestedOnuName: string
  suggestedOnuAlias?: string | null
  suggestedPortNumber: number
  suggestedTerminalBoxId?: number | null
  suggestedTerminalBoxName?: string | null
  confidenceScore: number
  matchReason: string
  needsCorrection: boolean
}

export interface AutoTopologyDiscoverResultDto {
  totalEvaluated: number
  suggestedCount: number
  suggestions: AutoTopologyBindingSuggestionDto[]
}

export interface ApplyTopologyItemRequest {
  channelId: number
  onuDeviceId: number
  terminalBoxId?: number | null
  portNumber?: number
  isSwitchPort?: boolean
  portLabel?: string | null
}

export interface ApplyTopologyCorrectionRequest {
  bindings: ApplyTopologyItemRequest[]
}

export interface OnuUpdateRequest {
  alias?: string | null
  terminalBoxId?: number | null
  hasSubSwitch?: boolean | null
  subSwitchModel?: string | null
  subSwitchPorts?: number | null
  portsConfig?: string | null
}

export interface OnuPortStatusDto {
  portId: number
  name: string
  status: string
  speed?: string | null
  duplex?: string | null
  terminalMac?: string | null
  deviceName?: string | null
  deviceIp?: string | null
  deviceType?: string | null
  deviceStatus?: string | null
}

export interface OnuDiagnosisItemDto {
  key: string
  value?: string | null
  status: number
  suggestion?: string | null
}

export interface OnuDiagnosisDto {
  neDn: string
  onuId: string
  status: string
  rxPower?: number | null
  txPower?: number | null
  lastDownCause?: string | null
  lastUpTime?: string | null
  lastDownTime?: string | null
  ports: OnuPortStatusDto[]
  opticalStatus?: string | null
  fiberStatus?: string | null
  items?: OnuDiagnosisItemDto[] | null
  healthStatus?: string | null
  rootCause?: string | null
}

export interface NameCountDto {
  name: string
  count: number
}

export interface PonStatisticsDto {
  totalCount: number
  onlineCount: number
  offlineCount: number
  models: NameCountDto[]
}

export interface NetworkTopologyPortDto {
  portNumber: number
  isSwitchPort: boolean
  portLabel: string
  boundChannelId?: number | null
  boundChannelName?: string | null
  boundChannelIp?: string | null
  boundChannelStatus?: string | null
  boundComputerId?: number | null
  boundComputerName?: string | null
  boundComputerIp?: string | null
  boundComputerStatus?: string | null
  boundComputerOwner?: string | null
  boundComputerDepartment?: string | null
  boundDeviceId?: number | null
  boundDeviceName?: string | null
  boundDeviceHost?: string | null
  boundDeviceStatus?: string | null
  boundDeviceModel?: string | null
}

export interface NetworkTopologyOnuDto {
  id: number
  onuName: string
  alias?: string | null
  sn: string
  terminalType: string
  status: string
  rxPower?: number | null
  hasSubSwitch: boolean
  subSwitchModel?: string | null
  subSwitchPorts: number
  ports: NetworkTopologyPortDto[]
}

export interface TerminalBoxTopologyDto {
  id: number
  code: string
  name: string
  location?: string | null
  areaId?: number | null
  areaName?: string | null
  unitId?: number | null
  unitName?: string | null
  onus: NetworkTopologyOnuDto[]
}

export interface NvrAvailableChannelDto {
  deviceId: number
  deviceName: string
  host: string
  port: number
  totalChannels: number
  usedChannels: number
  freeChannels: number
  availableChannelNumbers: number[]
  pluginId?: string
  model?: string | null
}

export interface AddCameraRequest {
  nvrDeviceId: number
  channelNumber?: number | null
  alias?: string
  name?: string
  ipcHost: string
  ipcPort?: number
  ipcUsername?: string
  ipcPassword?: string
  protocolType?: string
  unitId?: number | null
  terminalBoxId?: number | null
  onuDeviceId?: number | null
  portNumber?: number | null
  isSwitchPort?: boolean
  portLabel?: string | null
}

export interface PonKanbanCameraDto {
  channelId: number
  deviceChannel: number
  name: string
  alias?: string | null
  ip?: string | null
  status: string
  portNumber: number
  isSwitchPort: boolean
  portLabel: string
  model?: string | null
}

export interface PonKanbanComputerDto {
  id: number
  name: string
  ip: string
  mac?: string | null
  owner?: string | null
  department?: string | null
  status: string
  portNumber: number
  isSwitchPort: boolean
  portLabel: string
  responseTimeMs?: number | null
}

export interface PonKanbanDeviceDto {
  deviceId: number
  name: string
  host: string
  port: number
  status: string
  portNumber: number
  isSwitchPort: boolean
  portLabel: string
  model?: string | null
  channelCount: number
  onlineChannels: number
}

export interface PonKanbanOnuDto {
  id: number
  onuName: string
  alias?: string | null
  sn: string
  terminalType: string
  status: string
  runStatus?: string | null
  lastDownTime?: string | null
  lastDownCause?: string | null
  lastUpTime?: string | null
  rxPower?: number | null
  hasSubSwitch: boolean
  subSwitchModel?: string | null
  subSwitchPorts: number
  cameras: PonKanbanCameraDto[]
  computers?: PonKanbanComputerDto[]
  devices?: PonKanbanDeviceDto[]
}

export interface PonKanbanBoxDto {
  id: number
  code: string
  name: string
  location?: string | null
  areaId?: number | null
  areaName?: string | null
  unitId?: number | null
  unitName?: string | null
  healthState: 'danger' | 'warning' | 'healthy' | 'empty'
  totalOnus: number
  onlineOnus: number
  offlineOnus: number
  totalCameras: number
  onlineCameras: number
  offlineCameras: number
  totalComputers?: number
  onlineComputers?: number
  offlineComputers?: number
  onus: PonKanbanOnuDto[]
  directCameras: PonKanbanCameraDto[]
  directComputers?: PonKanbanComputerDto[]
  directDevices?: PonKanbanDeviceDto[]
}

export interface PonKanbanSummaryDto {
  totalBoxes: number
  healthyBoxes: number
  warningBoxes: number
  dangerBoxes: number
  totalOnus: number
  onlineOnus: number
  offlineOnus: number
  totalCameras: number
  onlineCameras: number
  offlineCameras: number
  totalComputers?: number
  onlineComputers?: number
  offlineComputers?: number
  ongoingOutagesCount: number
  todayOutagesCount: number
}

export interface PonKanbanResponseDto {
  summary: PonKanbanSummaryDto
  boxes: PonKanbanBoxDto[]
  unassignedOnus: PonKanbanOnuDto[]
  unassignedCameras: PonKanbanCameraDto[]
  unassignedComputers?: PonKanbanComputerDto[]
}

export interface DeviceOfflineHistoryDto {
  id: number
  deviceType: 'onu' | 'camera'
  terminalBoxId?: number | null
  terminalBoxCode?: string | null
  terminalBoxName?: string | null
  onuDeviceId?: number | null
  channelId?: number | null
  deviceName: string
  deviceIdentifier?: string | null
  eventType: 'offline' | 'online'
  downTime: string
  upTime?: string | null
  durationSeconds?: number | null
  reason?: string | null
  createdAt: string
}

export interface DigitalTwinSceneDto {
  id: number
  sceneCode: string
  name: string
  levelType: 'l1_overview' | 'l2_workshop'
  workshopId?: number | null
  workshopName?: string | null
  bgImageUrl: string
  naturalWidth: number
  naturalHeight: number
  defaultZoom: number
  minZoom: number
  maxZoom: number
  centerXPct: number
  centerYPct: number
  sortOrder: number
  nodeCount: number
  cableCount: number
  onlineCount: number
  totalCount: number
}

export interface DigitalTwinNodeDto {
  coordinateId: number
  sceneId: number
  targetType: 'terminal_box' | 'network_device' | 'channel' | 'computer'
  targetId: number
  name: string
  code?: string | null
  ip?: string | null
  status: 'online' | 'offline' | 'abnormal' | 'unused'
  xPct: number
  yPct: number
  altitudeM?: number | null
  fovYaw?: number | null
  fovAngle?: number | null
  fovRadiusM?: number | null
  iconStyle?: string | null
  remarks?: string | null
  rxPower?: number | null
  txPower?: number | null
  onuCount?: number | null
  onuOnlineCount?: number | null
  computerCount?: number | null
  cameraCount?: number | null
  subSwitchModel?: string | null
  responseTimeMs?: number | null
  liveStreamUrl?: string | null
  channelId?: number | null
  deviceId?: number | null
  location?: string | null
  responsiblePerson?: string | null
  department?: string | null
}

export interface DigitalTwinCableDto {
  id: number
  sceneId: number
  name: string
  cableType: string
  fromDeviceType?: string | null
  fromDeviceId?: number | null
  fromDeviceName?: string | null
  toDeviceType?: string | null
  toDeviceId?: number | null
  toDeviceName?: string | null
  color: string
  pointsJson: string
  status: string
  remarks?: string | null
}

export interface DigitalTwinStatsDto {
  totalNodes: number
  onlineNodes: number
  abnormalNodes: number
  offlineNodes: number
  terminalBoxCount: number
  cameraCount: number
  computerCount: number
  networkDeviceCount: number
  opticalHealthPercent: number
}

export interface DigitalTwinSceneDetailDto {
  scene: DigitalTwinSceneDto
  nodes: DigitalTwinNodeDto[]
  cables: DigitalTwinCableDto[]
  stats: DigitalTwinStatsDto
}

export interface SaveCoordinateRequest {
  targetType: 'terminal_box' | 'network_device' | 'channel' | 'computer'
  targetId: number
  xPct: number
  yPct: number
  altitudeM?: number
  fovYaw?: number
  fovAngle?: number
  fovRadiusM?: number
  iconStyle?: string | null
  remarks?: string | null
}

export interface SaveCablePathRequest {
  name: string
  cableType?: string
  fromDeviceType?: string | null
  fromDeviceId?: number | null
  toDeviceType?: string | null
  toDeviceId?: number | null
  color?: string
  pointsJson?: string
  status?: string
  remarks?: string | null
}

export interface CandidateNodeDto {
  targetType: 'terminal_box' | 'network_device' | 'channel' | 'computer'
  targetId: number
  name: string
  code?: string | null
  ip?: string | null
  location?: string | null
  areaId?: number | null
  areaName?: string | null
  isPlaced: boolean
}

export interface BatchChannelNetworkBindingRequest {
  channelIds: number[]
  terminalBoxId?: number | null
  onuDeviceId?: number | null
  startPortNumber?: number | null
  isSwitchPort?: boolean | null
}

export interface BatchClaimChannelsRequest {
  channelIds: number[]
  unitId: number
  terminalBoxId?: number | null
  onuDeviceId?: number | null
  startPortNumber?: number | null
  isSwitchPort?: boolean | null
}

export interface IpConflictCheckResultDto {
  occupied: boolean
  boundType?: string | null
  boundName?: string | null
  boundDetail?: string | null
}

export interface IpRecommendResultDto {
  ip: string
  poolName: string
  gateway?: string | null
  netmask: string
  poolId: number
}
