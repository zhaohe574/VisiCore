import type { AccessScope, AddCameraRequest, Alarm, AlarmDetail, ApplyTopologyCorrectionRequest, Audit, AutoTopologyDiscoverResultDto, BatchChannelNetworkBindingRequest, BatchClaimChannelsRequest, BatchGeneratePortsInput, CandidateNodeDto, Channel, Computer, ComputerInput, ComputerProbeResult, ConnectLinkInput, Dashboard, Device, DeviceInput, DeviceLink, DeviceOfflineHistoryDto, DevicePlugin, DevicePort, DevicePortInput, DigitalTwinCableDto, DigitalTwinNodeDto, DigitalTwinSceneDetailDto, DigitalTwinSceneDto, DigitalTwinStatsDto, ExportJob, IpConflictCheckResultDto, IpPool, IpPoolDetail, IpPoolInput, IpPoolScanResult, IpRecommendResultDto, Layout, LiveSession, LoginResult, NetworkDevice, NetworkDeviceInput, NetworkDeviceProbeResult, NetworkTopologyOnuDto, NetworkTopologyPortDto, NginxConfig, NvrAvailableChannelDto, OltDeviceDto, OnlineSession, OnuDeviceDto, OnuDiagnosisDto, OnuUpdateRequest, OpticalHealthIndexDto, Organization, OrganizationKind, OrganizationNode, OrganizationStats, Page, Permission, PlaybackControl, PlaybackSession, PluginCreateInput, PonKanbanResponseDto, PonStatisticsDto, PortCandidate, PtzCommand, Recording, Release, Role, SaveCablePathRequest, SaveCoordinateRequest, Settings, SslCertificate, SslCertificateUploadInput, SslDomain, SslDomainInput, SslOverview, SystemStatus, TerminalBoxDto, TerminalBoxRequest, TerminalBoxTopologyDto, User, VirtualMachine, VirtualMachineInput } from './types'
import type { components } from './generated/client'
import type { PublicRelease } from './types'
import type { SchemaDto } from './types'
export type * from './types'
export type Asset = SchemaDto<'AssetDto'>
export type AssetContext = SchemaDto<'AssetContextDto'>
export type AssetSummary = SchemaDto<'AssetSummaryDto'>
export type ComputerBatchUpdate = SchemaDto<'ComputerBatchUpdateRequest'>

export const apiBase = (import.meta.env.VITE_API_BASE || '').replace(/\/+$/, '')
export const apiUrl = (path: string) => `${apiBase}/api/v2${path}`
export class ApiError extends Error {
  constructor(public status: number, public code: string, message: string, public traceId = '') { super(message); this.name = 'ApiError' }
}
let csrfToken: string | null = null
let csrfRequest: Promise<string> | null = null
let refreshRequest: Promise<LoginResult> | null = null

export function clearCsrf() { csrfToken = null }
export async function getCsrf(): Promise<string> {
  if (csrfToken) return csrfToken
  if (!csrfRequest) csrfRequest = (async () => {
    let response: Response
    try { response = await fetch(apiUrl('/auth/csrf'), { credentials: 'include', headers: { Accept: 'application/json' } }) }
    catch { throw new ApiError(0, 'network_error', '无法连接平台服务，请检查网络连接') }
    if (!response.ok) throw await readError(response)
    const result = await response.json() as { token: string }
    if (!result.token) throw new ApiError(502, 'invalid_csrf', '服务端未返回请求校验令牌')
    csrfToken = result.token
    return result.token
  })().finally(() => { csrfRequest = null })
  return csrfRequest
}
async function readError(response: Response) {
  const data = await response.json().catch(() => ({})) as { code?: string; message?: string; traceId?: string }
  return new ApiError(response.status, data.code || 'request_failed', data.message || `请求失败（${response.status}）`, data.traceId)
}
export async function api<T>(path: string, init: RequestInit = {}, retry = true): Promise<T> {
  const method = (init.method || 'GET').toUpperCase()
  const writing = !['GET', 'HEAD', 'OPTIONS'].includes(method)
  const headers = new Headers(init.headers)
  headers.set('Accept', 'application/json')
  if (init.body && !(init.body instanceof FormData)) headers.set('Content-Type', 'application/json')
  if (writing) headers.set('X-CSRF-Token', await getCsrf())
  let response: Response
  try { response = await fetch(apiUrl(path), { ...init, method, headers, credentials: 'include' }) }
  catch (error) {
    if (error instanceof Error && error.name === 'AbortError') throw error
    throw new ApiError(0, 'network_error', '无法连接平台服务，请检查网络连接')
  }
  if (!response.ok) {
    const error = await readError(response)
    if (response.status === 401 && path !== '/auth/login') window.dispatchEvent(new Event('platform-auth-expired'))
    // 仅在明确的防伪校验失败时重新获取令牌，业务拒绝不重试。
    if ([400, 403].includes(response.status) && retry && /csrf|antiforgery/i.test(error.code)) { clearCsrf(); return api<T>(path, init, false) }
    throw error
  }
  if (response.status === 204 || response.headers.get('content-length') === '0') return undefined as T
  const contentType = response.headers.get('content-type') || ''
  if (!contentType.includes('json')) throw new ApiError(502, 'invalid_response', '服务端返回了非预期的数据格式')
  return response.json() as Promise<T>
}
export function refreshSession(): Promise<LoginResult> {
  if (!refreshRequest) refreshRequest = api<LoginResult>('/auth/refresh', { method: 'POST' }, false)
    .then(result => { clearCsrf(); window.dispatchEvent(new CustomEvent('platform-auth-refreshed', { detail: result })); return result })
    .finally(() => { refreshRequest = null })
  return refreshRequest
}
export type Query = Record<string, string | number | boolean | null | undefined>
export function queryString(query: Query = {}) {
  const params = new URLSearchParams()
  for (const [key, value] of Object.entries(query)) if (value !== '' && value !== undefined && value !== null) params.set(key, String(value))
  return params.size ? `?${params}` : ''
}
export const list = <T>(path: string, query: Query = {}, signal?: AbortSignal) => api<Page<T>>(`${path}${queryString(query)}`, { signal })
const post = <T = void>(path: string, body?: unknown, signal?: AbortSignal) => api<T>(path, { method: 'POST', body: body === undefined ? undefined : JSON.stringify(body), signal })
const put = <T = void>(path: string, body: unknown) => api<T>(path, { method: 'PUT', body: JSON.stringify(body) })
const remove = (path: string, keepalive = false) => api<void>(path, { method: 'DELETE', keepalive }, !keepalive)
const segment = (id: string | number) => encodeURIComponent(id)

export const authApi = {
  login: async (username: string, password: string) => { const result = await post<LoginResult>('/auth/login', { username, password, clientType: 'web', clientVersion: '2.1.7' } satisfies components['schemas']['LoginRequest']); clearCsrf(); return result },
  me: () => api<User>('/auth/me'),
  logout: async () => { await post('/auth/logout'); clearCsrf() },
  profile: (displayName: string, phone: string) => put('/auth/profile', { displayName, phone }),
  password: (currentPassword: string, newPassword: string) => put('/auth/password', { currentPassword, newPassword }),
}
export const managementApi = {
  dashboard: () => api<Dashboard>('/dashboard'), system: () => api<SystemStatus>('/system'),
  devices: (query?: Query, signal?: AbortSignal) => list<Device>('/devices', query, signal),
  device: (id: number) => api<Device>(`/devices/${id}`),
  saveDevice: (id: number | null, data: DeviceInput) => id ? put<Device>(`/devices/${id}`, data) : post<Device>('/devices', data),
  deleteDevice: (id: number) => remove(`/devices/${id}`), testDevice: (id: number) => post<unknown>(`/devices/${id}/test`), syncDevice: (id: number) => post<unknown>(`/devices/${id}/sync`),
  plugins: () => api<DevicePlugin[]>('/plugins'),
  updatePluginStatus: (id: string, status: 'active' | 'disabled') => put<DevicePlugin>(`/plugins/${id}/status`, { status }),
  pluginHealth: (id: string) => api<unknown>(`/plugins/${id}/health`),
  installPlugin: (formData: FormData) => api<DevicePlugin>('/plugins/install', { method: 'POST', body: formData }),
  createPlugin: (data: PluginCreateInput) => post<DevicePlugin>('/plugins', data),
  probePlugin: (endpointUrl: string) => post<Partial<DevicePlugin>>('/plugins/probe', { endpointUrl }),
  deletePlugin: (id: string) => remove(`/plugins/${segment(id)}`),
  exportPluginUrl: (id: string) => apiUrl(`/plugins/${segment(id)}/export`),
  channels: (query?: Query, signal?: AbortSignal) => list<Channel>('/channels', query, signal),
  channel: (id: number, signal?: AbortSignal) => api<Channel>(`/channels/${id}`, { signal }),
  updateChannel: (id: number, data: { alias?: string | null; unitId?: number | null; ip?: string | null; username?: string | null; password?: string | null; remark?: string | null; terminalBoxId?: number | null; onuDeviceId?: number | null; networkPort?: number | null; isSwitchPort?: boolean | null; portLabel?: string | null; clearNetworkBinding?: boolean | null }) => put<Channel>(`/channels/${id}`, data),
  assign: (channelIds: number[], unitId: number | null) => put('/channels/assignment', { channelIds, unitId }),
  batchSetNetworkBindings: (data: BatchChannelNetworkBindingRequest) => put('/channels/batch-network-bindings', data),
  batchClaimChannels: (data: BatchClaimChannelsRequest) => post('/channels/batch-claim', data),
  unassignedChannels: () => api<Channel[]>('/channels/unassigned'),
  sortChannels: (unitId: number, channelIds: number[]) => put('/channels/sort', { unitId, channelIds }),
  availableNvrChannels: () => api<NvrAvailableChannelDto[]>('/nvr/available-channels'),
  addCamera: (data: AddCameraRequest) => post<Channel>('/organization/channels/add-camera', data),
  organization: () => api<Organization>('/organization'),
  organizationStats: () => api<OrganizationStats>('/organization/stats'),
  saveNode: (kind: OrganizationKind, id: number | null, data: Omit<OrganizationNode, 'id'>) => id ? put(`/organization/${kind}/${id}`, data) : post(`/organization/${kind}`, data),
  deleteNode: (kind: OrganizationKind, id: number) => remove(`/organization/${kind}/${id}`),
  users: (query?: Query, signal?: AbortSignal) => list<User>('/users', query, signal),
  saveUser: (id: number | null, data: Omit<User, 'id' | 'permissions'> & { password?: string }) => id ? put(`/users/${id}`, data) : post('/users', data),
  roles: () => api<Role[]>('/roles'),
  saveRole: (id: number | null, data: Omit<Role, 'id' | 'userCount'>) => id ? put(`/roles/${id}`, data) : post('/roles', data),
  deleteRole: (id: number) => remove(`/roles/${id}`), permissions: () => api<Permission[]>('/permissions'),
  rolePermissions: (id: number, codes: string[]) => put(`/roles/${id}/permissions`, { codes }),
  scope: (kind: 'user' | 'role', id: number) => api<AccessScope>(`/scopes/${kind}/${id}`),
  saveScope: (kind: 'user' | 'role', id: number, data: AccessScope) => put(`/scopes/${kind}/${id}`, data),
  sessions: (query?: Query, signal?: AbortSignal) => list<OnlineSession>('/sessions', query, signal),
  revokeSession: (id: string) => remove(`/sessions/${segment(id)}`),
  audit: (query?: Query, signal?: AbortSignal) => list<Audit>('/audit', query, signal),
  settings: () => api<Settings>('/settings'), saveSettings: (data: Settings) => put('/settings', data),
}
export const mediaApi = {
  live: (channelId: number, streamType: 1 | 2, consumerId?: string) => post<LiveSession>('/live-sessions', { channelId, streamType, profile: 'browser', consumerId } satisfies components['schemas']['LiveRequest']),
  renewLive: (id: string) => post<LiveSession>(`/live-sessions/${segment(id)}/renew`), stopLive: (id: string, keepalive = false) => remove(`/live-sessions/${segment(id)}`, keepalive),
  recordings: (channelId: number, start: string, end: string) => post<Recording[]>('/recordings/search', { channelId, start, end }),
  playback: (channelId: number, start: string, end: string, consumerId?: string) => post<PlaybackSession>('/playback-sessions', { channelId, start, end, profile: 'browser', streamType: 1, consumerId } satisfies components['schemas']['PlaybackRequest']),
  getPlayback: (id: string) => api<PlaybackSession>(`/playback-sessions/${segment(id)}`),
  renewPlayback: (id: string) => post<PlaybackSession>(`/playback-sessions/${segment(id)}/renew`),
  stopPlayback: (id: string, keepalive = false) => remove(`/playback-sessions/${segment(id)}`, keepalive),
  control: (id: string, data: PlaybackControl) => post<PlaybackSession>(`/playback-sessions/${segment(id)}/control`, data),
  ptz: (id: number, command: PtzCommand, speed: number) => post(`/channels/${id}/ptz`, { command, speed }),
  stopPtz: (id: number, keepalive = false) => api<void>(`/channels/${id}/ptz/stop`, { method: 'POST', keepalive }, !keepalive),
  preset: (id: number, preset: number) => post(`/channels/${id}/ptz/presets/${preset}`),
  favorites: () => api<Channel[]>('/favorites'), saveFavorites: (channelIds: number[]) => put('/favorites', { channelIds }),
  layouts: () => api<Layout[]>('/layouts'), saveLayout: (id: number | null, data: Omit<Layout, 'id'>) => id ? put(`/layouts/${id}`, data) : post('/layouts', data), deleteLayout: (id: number) => remove(`/layouts/${id}`),
}
export const workflowApi = {
  alarms: (query?: Query, signal?: AbortSignal) => list<Alarm>('/alarms', query, signal),
  alarm: (id: number) => api<AlarmDetail>(`/alarms/${id}`), alarmImage: (id: number) => apiUrl(`/alarms/${id}/image`),
  alarmAction: (id: number, action: 'claim' | 'note' | 'close' | 'reopen', note?: string) => post(`/alarms/${id}/actions`, { action, note }),
  batchAlarmActions: (data: { ids?: number[]; action: 'claim' | 'close'; note?: string; allMatching?: boolean; filter?: Record<string, any> }) => post<{ count: number }>('/alarms/batch-actions', data),
  exports: (query?: Query, signal?: AbortSignal) => list<ExportJob>('/exports', query, signal),
  createExport: (channelId: number, start: string, end: string) => post<ExportJob>('/exports', { channelId, start, end }),
  cancelExport: (id: string) => post(`/exports/${segment(id)}/cancel`), retryExport: (id: string) => post(`/exports/${segment(id)}/retry`),
  exportUrl: (id: string) => apiUrl(`/exports/${segment(id)}/download`),
  releases: (query?: Query, signal?: AbortSignal) => list<Release>('/releases', query, signal),
  latest: (packageType?: 'zip' | 'msi' | 'patch') => api<PublicRelease | undefined>(`/public/releases/latest${queryString({ packageType })}`),
  publicReleases: () => api<PublicRelease[]>('/public/releases'),
  uploadRelease: (data: FormData) => api<Release>('/releases', { method: 'POST', body: data }),
  updateRelease: (id: number, data: { version?: string; minimumVersion?: string; forceUpdate?: boolean; releaseNotes?: string }) => put<Release>(`/releases/${id}`, data),
  deleteRelease: (id: number) => remove(`/releases/${id}`),
  publish: (id: number, minimumVersion: string, forceUpdate: boolean) => post(`/releases/${id}/publish`, { minimumVersion, forceUpdate }),
  publishVersion: (version: string, minimumVersion: string, forceUpdate: boolean) => post(`/releases/version/${segment(version)}/publish`, { minimumVersion, forceUpdate }),
  deleteReleaseVersion: (version: string) => remove(`/releases/version/${segment(version)}`),
  revokeRelease: (id: number) => post(`/releases/${id}/revoke`), releaseUrl: (id: number) => apiUrl(`/releases/${id}/download`),
}
export const sslApi = {
  overview: () => api<SslOverview>('/ssl/overview'),
  domains: (query?: Query, signal?: AbortSignal) => api<SslDomain[]>(`/ssl/domains${queryString(query)}`, { signal }),
  saveDomain: (id: number | null, data: SslDomainInput) => id ? put<SslDomain>(`/ssl/domains/${id}`, data) : post<SslDomain>('/ssl/domains', data),
  setPrimaryDomain: (id: number) => put<SslDomain>(`/ssl/domains/${id}/primary`, {}),
  deleteDomain: (id: number) => remove(`/ssl/domains/${id}`),
  certificates: (query?: Query, signal?: AbortSignal) => api<SslCertificate[]>(`/ssl/certificates${queryString(query)}`, { signal }),
  certificate: (id: number) => api<SslCertificate>(`/ssl/certificates/${id}`),
  uploadCertificate: (data: SslCertificateUploadInput | FormData) => api<SslCertificate>('/ssl/certificates', {
    method: 'POST',
    body: data instanceof FormData ? data : JSON.stringify(data),
  }),
  setActiveCertificate: (id: number) => put<SslCertificate>(`/ssl/certificates/${id}/active`, {}),
  deleteCertificate: (id: number) => remove(`/ssl/certificates/${id}`),
  downloadCertUrl: (id: number) => apiUrl(`/ssl/certificates/${id}/download`),
  nginxConfig: () => api<NginxConfig>('/ssl/nginx-config'),
}
export const ponApi = {
  statistics: () => api<PonStatisticsDto>('/pon/statistics'),
  onus: (query?: Query, signal?: AbortSignal) => list<OnuDeviceDto>('/pon/onus', query, signal),
  onu: (id: number) => api<OnuDeviceDto>(`/pon/onus/${id}`),
  updateOnu: (id: number, data: OnuUpdateRequest) => put<OnuDeviceDto>(`/pon/onus/${id}`, data),
  diagnoseOnu: (id: number) => api<OnuDiagnosisDto>(`/pon/onus/${id}/diagnosis`),
  rebootOnu: (id: number) => post<{ success: boolean; message: string }>(`/pon/onus/${id}/reboot`),
  syncEsight: () => post<{ success: boolean; count: number }>('/pon/sync'),
  terminalBoxes: (signal?: AbortSignal) => api<TerminalBoxDto[]>('/terminal-boxes', { signal }),
  terminalBox: (id: number) => api<TerminalBoxDto>(`/terminal-boxes/${id}`),
  saveTerminalBox: (id: number | null, data: TerminalBoxRequest) => id ? put<TerminalBoxDto>(`/terminal-boxes/${id}`, data) : post<TerminalBoxDto>('/terminal-boxes', data),
  deleteTerminalBox: (id: number) => remove(`/terminal-boxes/${id}`),
  terminalBoxTopologyTree: (signal?: AbortSignal) => api<TerminalBoxTopologyDto[]>('/terminal-boxes/topology-tree', { signal }),
  terminalBoxTopology: (id: number, signal?: AbortSignal) => api<TerminalBoxTopologyDto>(`/terminal-boxes/${id}/topology`, { signal }),
  kanban: (signal?: AbortSignal) => api<PonKanbanResponseDto>('/pon/dashboard/kanban', { signal }),
  offlineHistory: (query?: Query, signal?: AbortSignal) => list<DeviceOfflineHistoryDto>('/pon/dashboard/history', query, signal),
  olts: (signal?: AbortSignal) => api<OltDeviceDto[]>('/pon/olts', { signal }),
  healthIndex: (signal?: AbortSignal) => api<OpticalHealthIndexDto>('/pon/health-index', { signal }),
  autoDiscoverTopology: (signal?: AbortSignal) => post<AutoTopologyDiscoverResultDto>('/pon/bindings/auto-discover', {}, signal),
  applyTopologyCorrections: (data: ApplyTopologyCorrectionRequest) => post<{ success: boolean; appliedCount: number }>('/pon/bindings/apply', data),
}

export const computerApi = {
  batchUpdate: (data: ComputerBatchUpdate) => post<components['schemas']['AssetBatchResultDto']>('/computers/batch-update', data),
  list: (query?: Query, signal?: AbortSignal) => list<Computer>('/computers', query, signal),
  get: (id: number) => api<Computer>(`/computers/${id}`),
  save: (id: number | null, data: ComputerInput) => id ? put<Computer>(`/computers/${id}`, data) : post<Computer>('/computers', data),
  delete: (id: number) => remove(`/computers/${id}`),
  probe: (id: number) => post<ComputerProbeResult>(`/computers/${id}/probe`),
  probeAll: () => post<ComputerProbeResult[]>('/computers/probe-all'),
}

export const assetApi = {
  search: (query?: Query, signal?: AbortSignal) => list<Asset>('/assets/search', query, signal),
  summary: (query?: Query, signal?: AbortSignal) => api<AssetSummary>(`/assets/summary${queryString(query)}`, { signal }),
  context: (type: string, id: number, signal?: AbortSignal) => api<AssetContext>(`/assets/${segment(type)}/${id}/context`, { signal }),
}

export const ipPoolApi = {
  list: (signal?: AbortSignal) => api<IpPool[]>('/ip-pools', { signal }),
  get: (id: number) => api<IpPool>(`/ip-pools/${id}`),
  save: (id: number | null, data: IpPoolInput) => id ? put<IpPool>(`/ip-pools/${id}`, data) : post<IpPool>('/ip-pools', data),
  delete: (id: number) => remove(`/ip-pools/${id}`),
  detail: (id: number, signal?: AbortSignal) => api<IpPoolDetail>(`/ip-pools/${id}/detail`, { signal }),
  scan: (id: number, subnetSlice?: string | null) => post<IpPoolScanResult>(`/ip-pools/${id}/scan${queryString({ subnetSlice })}`),
  checkIp: (ip: string, excludeType?: string, excludeId?: number) => api<IpConflictCheckResultDto>(`/ip-pools/check-ip?ip=${encodeURIComponent(ip)}${excludeType ? `&excludeType=${excludeType}` : ''}${excludeId ? `&excludeId=${excludeId}` : ''}`),
  recommendIp: (workshopId?: number | null, poolId?: number | null) => api<IpRecommendResultDto>(`/ip-pools/recommend-ip?${workshopId ? `workshopId=${workshopId}` : ''}${poolId ? `&poolId=${poolId}` : ''}`),
}

export const networkDeviceApi = {
  list: (query?: Query, signal?: AbortSignal) => list<NetworkDevice>('/network-devices', query, signal),
  get: (id: number) => api<NetworkDevice>(`/network-devices/${id}`),
  save: (id: number | null, data: NetworkDeviceInput) => id ? put<NetworkDevice>(`/network-devices/${id}`, data) : post<NetworkDevice>('/network-devices', data),
  delete: (id: number) => remove(`/network-devices/${id}`),
  probe: (id: number) => post<NetworkDeviceProbeResult>(`/network-devices/${id}/probe`),
  probeAll: () => post<NetworkDeviceProbeResult[]>('/network-devices/probe-all'),
}

export const devicePortApi = {
  list: (deviceType: string, deviceId: number, signal?: AbortSignal) => api<DevicePort[]>(`/devices/${deviceType}/${deviceId}/ports`, { signal }),
  batchGenerate: (deviceType: string, deviceId: number, data: BatchGeneratePortsInput) => post<DevicePort[]>(`/devices/${deviceType}/${deviceId}/ports/batch-generate`, data),
  create: (deviceType: string, deviceId: number, data: DevicePortInput) => post<DevicePort>(`/devices/${deviceType}/${deviceId}/ports`, data),
  update: (id: number, data: DevicePortInput) => put<DevicePort>(`/device-ports/${id}`, data),
  delete: (id: number) => remove(`/device-ports/${id}`),
  connect: (data: ConnectLinkInput) => post<DeviceLink>('/device-links/connect', data),
  disconnect: (id: number) => remove(`/device-links/${id}`),
  candidates: (query?: { search?: string; deviceType?: string }, signal?: AbortSignal) => api<PortCandidate[]>(`/device-ports/candidates${queryString(query || {})}`, { signal }),
}

export const virtualMachineApi = {
  list: (hostDeviceId: number, signal?: AbortSignal) => api<VirtualMachine[]>(`/network-devices/${hostDeviceId}/vms`, { signal }),
  create: (hostDeviceId: number, data: VirtualMachineInput) => post<VirtualMachine>(`/network-devices/${hostDeviceId}/vms`, data),
  update: (id: number, data: VirtualMachineInput) => put<VirtualMachine>(`/virtual-machines/${id}`, data),
  delete: (id: number) => remove(`/virtual-machines/${id}`),
}

export const digitalTwinApi = {
  scenes: (signal?: AbortSignal) => api<DigitalTwinSceneDto[]>('/digital-twin/scenes', { signal }),
  sceneDetail: (id: number, signal?: AbortSignal) => api<DigitalTwinSceneDetailDto>(`/digital-twin/scenes/${id}`, { signal }),
  candidates: (id: number, signal?: AbortSignal) => api<CandidateNodeDto[]>(`/digital-twin/scenes/${id}/candidates`, { signal }),
  saveCoordinate: (sceneId: number, data: SaveCoordinateRequest) => post<DigitalTwinNodeDto>(`/digital-twin/scenes/${sceneId}/coordinates`, data),
  batchSaveCoordinates: (sceneId: number, data: { items: SaveCoordinateRequest[] }) => post<{ count: number }>(`/digital-twin/scenes/${sceneId}/coordinates/batch`, data),
  deleteCoordinate: (id: number) => remove(`/digital-twin/coordinates/${id}`),
  createCable: (sceneId: number, data: SaveCablePathRequest) => post<DigitalTwinCableDto>(`/digital-twin/scenes/${sceneId}/cables`, data),
  updateCable: (sceneId: number, cableId: number, data: SaveCablePathRequest) => put<DigitalTwinCableDto>(`/digital-twin/scenes/${sceneId}/cables/${cableId}`, data),
  deleteCable: (cableId: number) => remove(`/digital-twin/cables/${cableId}`),
  autoAlignCad: (sceneId: number) => post<{ count: number }>(`/digital-twin/scenes/${sceneId}/auto-align`),
}



export async function allPages<T>(loader: (query: Query) => Promise<Page<T>>, query: Query = {}): Promise<T[]> {
  const result: T[] = []
  let page = 1
  while (true) {
    const batch = await loader({ ...query, page, pageSize: 100 })
    result.push(...batch.items)
    if (result.length >= batch.total || !batch.items.length) return result
    page++
  }
}
