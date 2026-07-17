export type Id = string

export interface LoginResponse {
  accessToken: string
  expiresAt: string
  username: string
  requiresPasswordChange: boolean
}

export interface Region {
  id: Id
  parentId: Id | null
  code: string
  name: string
}

export interface Camera {
  id: Id
  recorderId: Id
  regionId: Id
  code: string
  alias: string
  inputChannelNumber: number
  streamingChannelMap: string
  supportsPtz: boolean
  connectivity: number | string
  lastVerifiedAt: string | null
  sourceType: 'recorder-channel' | 'direct' | string
  provisioningMode: 'discovered' | 'manual' | string
  devicePluginId: Id | null
  manufacturer: string | null
  model: string | null
  serialNumber: string | null
  description: string | null
  createdAt: string
  mainStreamUrl: string | null
  subStreamUrl: string | null
  credentialReference: string | null
  credentialId?: Id | null
  workerId: Id | null
  edgeAgentId?: Id | null
  agentId?: Id | null
  timeZoneId: string | null
}

export interface RecorderEndpoint {
  id: Id
  recorderId: Id
  protocol: number | string
  host: string
  port: number
  useTls: boolean
  certificateThumbprint: string | null
  credentialReference: string
}

export interface Recorder {
  id: Id
  code: string
  name: string
  vendor: string
  model: string | null
  adapterType: string
  devicePluginId: Id | null
  deviceKind: string
  serialNumber: string | null
  firmwareVersion: string | null
  description: string | null
  configurationJson: string
  timeZoneId: string
  createdAt: string
  connectivity: number | string
  lastVerifiedAt: string | null
  clockSynchronization: number | string
  clockDriftSinceAt: string | null
  lastClockOffsetMilliseconds: number | null
  lastClockObservedAt: string | null
}

export interface RecorderResponse {
  recorder: Recorder
  endpoints: RecorderEndpoint[]
}

export interface DevicePluginEndpointDefinition {
  protocol: string
  label: string
  defaultPort: number
  required: boolean
  supportsTls: boolean
}

export interface DevicePluginCapabilities {
  liveView: boolean
  channelDiscovery: boolean
  playback: boolean
  ptz: boolean
  export: boolean
  clockSynchronization: boolean
}

export interface DevicePluginPackage {
  imageReference: string
  imageDigest: string
  packageSha256: string
  signingKeyId: string
  signature: string
}

export interface DevicePluginManifest {
  key: string
  name: string
  version: string
  protocolType: string
  runtimeType: string
  adapterType: string
  supportedDeviceKinds: string[]
  endpoints: DevicePluginEndpointDefinition[]
  capabilities: DevicePluginCapabilities
  vendor: string | null
  models: string[] | null
  description: string | null
  minimumPlatformVersion: string
  package: DevicePluginPackage | null
  configurationSchema: string | null
}

export interface DevicePlugin {
  id: Id
  key: string
  name: string
  version: string
  protocolType: string
  runtimeType: string
  adapterType: string
  vendor: string | null
  description: string | null
  packageHash: string
  isBuiltIn: boolean
  enabled: boolean
  installedAt: string
  updatedAt: string
  usageCount: number
  manifest: DevicePluginManifest
}

export interface RoleScope {
  id: Id
  regionId: Id | null
  cameraId: Id | null
  permissions: number
}

export interface Role {
  id: Id
  code: string
  name: string
  systemPermissions: number
  cameraScopes: RoleScope[]
}

export interface User {
  id: Id
  username: string
  isSystemAdministrator: boolean
  disabledAt: string | null
  requiresPasswordChange: boolean
  roleIds: Id[]
}

export interface WorkerAssignment {
  recorderId: Id
  defaultRegionId: Id
}

export interface DeviceWorker {
  id: Id
  name: string
  disabledAt: string | null
  lastSeenAt: string | null
  assignments: WorkerAssignment[]
}

export interface DeviceWorkerOperationStatus {
  workerId: Id
  recorderId: Id
  operationType: string
  isReady: boolean
  failureKind: string | null
  reportedAt: string
  isEffective: boolean
  workerLastSeenAt: string | null
  workerDisabledAt: string | null
}

export interface AgentPublicKeyContract {
  agentId: Id
  keyId: string
  keyEncryptionAlgorithm: string
  subjectPublicKeyInfoBase64: string
}

export interface AgentCredentialEnvelope {
  schemaVersion: number
  agentId: Id
  credentialVersionId: Id
  keyId: string
  keyEncryptionAlgorithm: string
  contentEncryptionAlgorithm: string
  encryptedKeyBase64: string
  initializationVectorBase64: string
  ciphertextBase64: string
  authenticationTagBase64: string
}

export interface DeviceCredential {
  id: Id
  name: string
  version?: string | null
  keyVersion?: string | null
  status?: string | null
  protectionMode?: string | number | null
  disabledAt?: string | null
  activeVersion?: number | null
  agentIds?: Id[] | null
  lastVerifiedAt?: string | null
  lastVerificationError?: string | null
  usageCount?: number | null
  createdAt?: string | null
  rotatedAt?: string | null
}

export interface EdgeAgent {
  id: Id
  name: string
  status?: string | number | null
  platform?: string | null
  version?: string | null
  agentVersion?: string | null
  lastSeenAt?: string | null
  lastHeartbeatAt?: string | null
  configVersion?: string | null
  appliedConfigVersion?: string | null
  configurationVersion?: number | null
  upgradeStatus?: string | null
  certificateExpiresAt?: string | null
  capabilities?: string[] | Record<string, boolean> | null
  capabilitiesJson?: string | null
  serviceStatusJson?: string | null
  publicKey?: AgentPublicKeyContract | null
  assignedCameraCount?: number | null
  credentialCount?: number | null
  assignmentCount?: number | null
  disabledAt?: string | null
  createdAt?: string | null
  lastDiagnosticAt?: string | null
  lastDiagnosticSucceeded?: boolean | null
  lastDiagnosticMessage?: string | null
}

export interface EdgeAgentEnrollment {
  agentId?: Id
  id?: Id
  name?: string
  enrollmentCode?: string
  enrollmentToken?: string
  pairingCode?: string
  token?: string
  expiresAt?: string | null
}

export interface PlatformDiagnosticCheck {
  name?: string
  status?: string | null
  message?: string | null
  checkedAt?: string | null
}

export interface PlatformDiagnosticResult {
  id?: Id
  edgeAgentId?: Id | null
  operationType?: string | null
  status?: string | null
  summary?: string | null
  agentId?: Id | null
  requestedAt?: string | null
  completedAt?: string | null
  detailsJson?: string | null
  checks?: PlatformDiagnosticCheck[] | null
}

export interface PlatformServiceStatus {
  id?: string
  name?: string
  status?: string | null
  detail?: string | null
  updatedAt?: string | null
  resourceUsage?: {
    cpuPercent?: number | null
    memoryBytes?: number | null
  } | null
}

export interface PlatformOperationsOverview {
  generatedAt?: string | null
  services?: PlatformServiceStatus[] | null
  edgeAgents?: EdgeAgent[] | null
  edgeAgentCount?: number | null
  onlineEdgeAgentCount?: number | null
  unhealthyEdgeAgentCount?: number | null
  pendingOperationCount?: number | null
  recentOperations?: PlatformDeployment[] | null
  openAlertCount?: number | null
  activeDeploymentCount?: number | null
  configVersion?: string | null
  resourceUsage?: {
    cpuPercent?: number | null
    memoryBytes?: number | null
    diskBytes?: number | null
    diskTotalBytes?: number | null
  } | null
}

export interface PlatformDeployment {
  id: Id
  edgeAgentId?: Id | null
  operationType?: string | null
  status?: string | null
  version?: string | null
  configVersion?: string | null
  requestedAt?: string | null
  startedAt?: string | null
  completedAt?: string | null
  requestedBy?: string | null
  summary?: string | null
  detailsJson?: string | null
  detail?: string | null
}

export interface NotificationChannel {
  id: Id
  name: string
  type: number | string
  configuration: Record<string, unknown>
  secretReference: string
  webhookConfigured?: boolean
  enabled: boolean
  createdAt: string
}

export interface AlertRule {
  id: Id
  name: string
  resourceType: string
  regionId: Id | null
  notifyOnRecovery: boolean
  enabled: boolean
  notificationChannelIds: Id[]
}

export interface AlertIncident {
  id: Id
  resourceType: string
  resourceId: Id
  regionId: Id | null
  resourceName: string
  incidentType: string
  openedAt: string
  lastObservedAt: string
  resolvedAt: string | null
}

export interface NotificationDelivery {
  id: Id
  alertIncidentId: Id
  notificationChannelId: Id
  eventType: number | string
  status: number | string
  attempts: number
  createdAt: string
  nextAttemptAt: string
  sentAt: string | null
  lastError: string | null
}

export interface AuditLog {
  id: Id
  actorUserId: Id | null
  action: string
  resourceType: string
  resourceId: string
  detailsJson: string
  occurredAt: string
}

export interface DeadLetter {
  id: Id
  eventType: string
  aggregateType: string
  aggregateId: Id
  attempts: number
  deadLetteredAt: string | null
  lastError: string | null
}

export interface PublicOfflineDevice {
  name: string
  region: string
  deviceType: string
  offlineDurationSeconds: number
}

export interface PublicOfflineDeviceList {
  generatedAt: string
  total: number
  page: number
  pageSize: number
  items: PublicOfflineDevice[]
  regions: string[]
  deviceTypes: string[]
}

export interface ExportCamera {
  id: Id
  code: string
  alias: string
  regionId: Id
  connectivity: number | string
}

export interface PlaybackExport {
  id: Id
  cameraId: Id
  status: string
  startedAt: string
  endedAt: string
  container: string
  requestedAt: string
  artifact: ExportArtifact | null
  failureCode: string | null
}

export interface ExportArtifact {
  id: Id
  fileName: string
  sizeBytes: number
  sha256: string
  expiresAt: string
}

export type Section = 'overview' | 'credentials' | 'edgeAgents' | 'operations' | 'assets' | 'plugins' | 'access' | 'workers' | 'exports' | 'alerts' | 'audit'
