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
export type Device = SchemaDto<'DeviceDto'> & { pluginId?: string | null; pluginName?: string | null }
export type DeviceInput = Omit<SchemaDto<'DeviceRequest'>, 'password'> & { password?: string | null; pluginId?: string | null }
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
  password?: string | null
  remark?: string | null
  deviceModel?: string | null
  deviceHost?: string | null
  devicePort?: number | null
  deviceSerial?: string | null
  pluginId?: string | null
  pluginName?: string | null
  firmwareVersion?: string | null
  sortOrder?: number
}
export type OrganizationNode = SchemaDto<'OrganizationNodeDto'>
export type Organization = SchemaDto<'OrganizationTreeDto'>
export type OrganizationKind = keyof Organization
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
export type EventKind = 'alarm.changed' | 'device.changed' | 'media.changed' | 'export.changed' | 'access.changed'
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

