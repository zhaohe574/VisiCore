const apiBase = import.meta.env.VITE_API_BASE ?? ''
const configuredApiBase = () => localStorage.getItem('video-platform-api-base')?.trim().replace(/\/+$/, '') || apiBase
let refreshRequest: Promise<string | null> | null = null

export async function refreshSession(expectedToken = localStorage.getItem('video-platform-token')): Promise<string | null> {
  if (!expectedToken) return null
  if (localStorage.getItem('video-platform-token') !== expectedToken) return localStorage.getItem('video-platform-token')
  if (refreshRequest) return refreshRequest
  refreshRequest = (async () => {
    const response = await fetch(`${configuredApiBase()}/api/auth/refresh`, { method: 'POST', headers: { Authorization: `Bearer ${expectedToken}` } })
    if (!response.ok) {
      if (response.status === 401 && localStorage.getItem('video-platform-token') === expectedToken) {
        localStorage.removeItem('video-platform-token')
        window.dispatchEvent(new Event('platform-auth-expired'))
      }
      throw new Error(`登录续期失败（${response.status}）`)
    }
    const result = await response.json() as { accessToken: string }
    if (localStorage.getItem('video-platform-token') !== expectedToken) return localStorage.getItem('video-platform-token')
    localStorage.setItem('video-platform-token', result.accessToken)
    window.dispatchEvent(new Event('platform-auth-refreshed'))
    return result.accessToken
  })()
  try { return await refreshRequest } finally { refreshRequest = null }
}

export async function api<T>(path: string, init: RequestInit = {}): Promise<T> {
  if (refreshRequest && !path.startsWith('/api/auth/')) await refreshRequest
  let token = localStorage.getItem('video-platform-token')
  const canRetry = !(init.body instanceof FormData) && (init.body === undefined || typeof init.body === 'string')
  const send = async (accessToken: string | null) => {
    const headers = new Headers(init.headers)
    headers.set('Accept', 'application/json')
    if (init.body && !(init.body instanceof FormData) && !headers.has('Content-Type')) headers.set('Content-Type', 'application/json')
    if (accessToken) headers.set('Authorization', `Bearer ${accessToken}`)
    return fetch(`${configuredApiBase()}${path}`, { ...init, headers })
  }
  let response: Response
  try {
    response = await send(token)
  } catch (error) {
    if (error instanceof DOMException && error.name === 'AbortError') throw error
    throw new Error('无法连接平台服务，请检查 API 服务是否启动')
  }
  if (response.status === 401 && token && canRetry && !path.startsWith('/api/auth/')) {
    try {
      token = await refreshSession(token)
      if (token) response = await send(token)
    } catch { /* 后续按原始认证错误处理 */ }
  }
  if (response.status === 401 && token && token === localStorage.getItem('video-platform-token') && !path.startsWith('/api/auth/login')) {
    localStorage.removeItem('video-platform-token')
    window.dispatchEvent(new Event('platform-auth-expired'))
  }
  if (!response.ok) {
    let message = `请求失败（${response.status}）`
    try {
      const body = await response.json() as { error?: string; detail?: string }
      message = body.error ?? body.detail ?? message
    } catch { /* 保留状态码错误 */ }
    throw new Error(message)
  }
  if (response.status === 204) return undefined as T
  return response.json() as Promise<T>
}

export type User = { id: number; username: string; displayName?: string | null; phone?: string | null; permissions?: string[] }
export type ManagedUser = { id: number; username: string; displayName: string | null; phone: string | null; status: 'active' | 'disabled' | 'locked'; lastLoginAt: string | null; roleIds: number[]; roleNames: string[] }
export type Role = { id: number; name: string; code: string; status: string; userCount: number; permissionCodes: string[] }
export type Permission = { code: string; name: string; resourceType: string; operationType: string }
export type Device = { id?: number; deviceKey?: string; ip?: string; servicePort?: number; model?: string | null; serialNumber: string; analogChannels: number; digitalChannels: number; digitalStartChannel: number; diskCount: number; alarmInputCount: number; alarmOutputCount: number; supportsRtsp: boolean; status?: string; lastSeenAt?: string | null }
export type Channel = { channelNumber: number; enabled: boolean; streamType: number; name: string; model: string; online: boolean; id?: number; unitId?: number | null; ptzCapable?: boolean }
export type Alarm = { id: number; eventType: string; occurredAt: string; state: string; payload: string; channelId: number | null; channelNumber?: number | null; imageAvailable: boolean }
export type LiveSession = { id: string; channel: number; streamType: 1 | 2; stream: string; expiresAt: string; rtspUrl: string; httpFlvUrl: string; hlsUrl: string }
export type Recording = { fileName: string; start: string; end: string; fileSize: number; fileType: number; streamType: number; fileIndex: number }
export type PlaybackSession = { id: string; channel: number; start: string; end: string; state: 'playing' | 'paused' | 'gap' | 'completed'; timelineState?: 'playing' | 'gap' | 'paused' | 'completed'; currentTime?: string; progress: number; bytes: number; fileName: string; stream: string; expiresAt: string; rtspUrl: string; httpFlvUrl: string; hlsUrl: string }
export type Stats = { users: number; roles: number; workshops: number; areas: number; units: number; channels: number; alarmsToday: number; unacknowledgedAlarms: number; activeLiveSessions: number }
export type SystemStats = {
  hostName: string; osDescription: string; architecture: string; processorCount: number; serverTime: string; uptimeSeconds: number | null
  loadAverage: { one: number | null; five: number | null; fifteen: number | null; percent: number | null }
  memory: { totalBytes: number | null; availableBytes: number | null; usedBytes: number | null; usedPercent: number | null }
  disk: { path: string; totalBytes: number | null; freeBytes: number | null; usedBytes: number | null; usedPercent: number | null }
  process: { workingSetBytes: number; cpuSeconds: number }
  network: { receivedBytes: number | null; transmittedBytes: number | null; receivedBytesPerSecond: number | null; transmittedBytesPerSecond: number | null }
}
export type DeviceStats = { deviceId: number; deviceKey: string; ip: string; servicePort: number; model: string | null; serialNumber: string | null; status: string; lastSeenAt: string | null; deviceCount: number; onlineDevices: number; offlineDevices: number; channels: number; onlineChannels: number; offlineChannels: number; alarmsToday: number; unacknowledgedAlarms: number; onlineTrend: { timestamp: string; online: number; total: number }[] }
export type BusinessNode = { name: string; code: string; status?: 'active' | 'disabled'; workshopId?: number | null; areaId?: number | null }
export type DesktopRelease = { id: number; version: string; fileName: string; sha256: string; fileSize: number; releaseNotes: string; minimumVersion: string | null; forceUpdate: boolean; status: 'draft' | 'published' | 'revoked'; downloadCount: number; publishedAt: string | null; createdAt: string }
export type PublicDesktopRelease = { id: number; version: string; fileName: string; sha256: string; fileSize: number; releaseNotes: string; minimumVersion: string | null; forceUpdate: boolean; updateAvailable?: boolean; publishedAt: string | null; downloadUrl: string }
export type AccessScope = { scopeType: 'workshop' | 'area' | 'unit' | 'channel'; scopeId: number }

export const login = (username: string, password: string) => api<{ accessToken: string; expiresAt: string; user: User }>('/api/auth/login', { method: 'POST', body: JSON.stringify({ username, password }) })
export const logout = () => api<void>('/api/auth/logout', { method: 'POST' })
export const me = () => api<User>('/api/auth/me')
export const updateProfile = (displayName: string, phone: string) => api<User>('/api/auth/profile', { method: 'PUT', body: JSON.stringify({ displayName: displayName || null, phone: phone || null }) })
export const changePassword = (currentPassword: string, newPassword: string) => api<void>('/api/auth/password', { method: 'PUT', body: JSON.stringify({ currentPassword, newPassword }) })
export const getDevice = () => api<Device>('/api/devices')
export const getChannels = () => api<Channel[]>('/api/channels')
export const getStats = () => api<Stats>('/api/stats')
export const getSystemStats = () => api<SystemStats>('/api/system-stats')
export const getDeviceStats = () => api<DeviceStats>('/api/device-stats')
export type AlarmQuery = { limit?: number; state?: string; channel?: number; eventType?: string; from?: string; to?: string }
export const getAlarms = (query: AlarmQuery | number = 100) => {
  const options = typeof query === 'number' ? { limit: query } : query
  const params = new URLSearchParams()
  for (const [key, value] of Object.entries(options)) if (value !== undefined && value !== '') params.set(key, String(value))
  return api<Alarm[]>(`/api/alarms?${params}`)
}
export const getAlarm = (id: number) => api<Alarm & { imageLength: number }>(`/api/alarms/${id}`)
export async function getAlarmImage(id: number) {
  const token = localStorage.getItem('video-platform-token')
  let response: Response
  try {
    response = await fetch(`${configuredApiBase()}/api/alarms/${id}/image`, { headers: token ? { Authorization: `Bearer ${token}` } : undefined })
  } catch {
    throw new Error('无法连接平台服务，请检查 API 服务是否启动')
  }
  if (!response.ok) throw new Error(`图片读取失败（${response.status}）`)
  return URL.createObjectURL(await response.blob())
}
export const ackAlarm = (id: number, note?: string) => api(`/api/alarms/${id}/ack`, { method: 'POST', body: JSON.stringify({ note: note ?? '' }) })
export const startLive = (channel: number, streamType: 1 | 2 = 2) => api<LiveSession>('/api/live-sessions', { method: 'POST', body: JSON.stringify({ channel, streamType }) })
export const renewLive = (id: string) => api<LiveSession>(`/api/live-sessions/${id}/renew`, { method: 'POST' })
export const stopLive = (id: string) => api(`/api/live-sessions/${id}`, { method: 'DELETE' })
export const searchRecordings = (channel: number, start: string, end: string) => api<Recording[]>('/api/recordings/search', { method: 'POST', body: JSON.stringify({ channel, start, end }) })
export const startPlayback = (channel: number, start: string, end: string) => api<PlaybackSession>('/api/playback-sessions', { method: 'POST', body: JSON.stringify({ channel, start, end }) })
export const getPlayback = (id: string) => api<PlaybackSession>(`/api/playback-sessions/${id}`)
export const renewPlayback = (id: string) => api<PlaybackSession>(`/api/playback-sessions/${id}/renew`, { method: 'POST' })
export const controlPlayback = (id: string, action: 'pause' | 'resume' | 'fast' | 'slow' | 'normal' | 'seek', position?: number) => api<PlaybackSession>(`/api/playback-sessions/${id}/control`, { method: 'POST', body: JSON.stringify({ action, position }) })
export const stopPlayback = (id: string) => api(`/api/playback-sessions/${id}`, { method: 'DELETE' })
export const ptzStart = (channel: number, command: 'up' | 'down' | 'left' | 'right' | 'auto' | 'zoomIn' | 'zoomOut', speed = 4) => api(`/api/channels/${channel}/ptz/start`, { method: 'POST', body: JSON.stringify({ command, speed }) })
export const ptzStop = (channel: number) => api(`/api/channels/${channel}/ptz/stop`, { method: 'POST' })
export const ptzPreset = (channel: number, preset: number) => api(`/api/channels/${channel}/ptz/preset/${preset}`, { method: 'POST' })
export const getWorkshops = () => api<unknown[][]>('/api/workshops')
export const getAreas = () => api<unknown[][]>('/api/areas')
export const getUnits = () => api<unknown[][]>('/api/units')
export const getUnassignedChannels = () => api<unknown[][]>('/api/channels/unassigned')
export const createWorkshop = (node: BusinessNode) => api('/api/workshops', { method: 'POST', body: JSON.stringify(node) })
export const updateWorkshop = (id: number, node: BusinessNode) => api(`/api/workshops/${id}`, { method: 'PUT', body: JSON.stringify(node) })
export const deleteWorkshop = (id: number) => api<void>(`/api/workshops/${id}`, { method: 'DELETE' })
export const createArea = (node: BusinessNode) => api('/api/areas', { method: 'POST', body: JSON.stringify(node) })
export const updateArea = (id: number, node: BusinessNode) => api(`/api/areas/${id}`, { method: 'PUT', body: JSON.stringify(node) })
export const deleteArea = (id: number) => api<void>(`/api/areas/${id}`, { method: 'DELETE' })
export const createUnit = (node: BusinessNode) => api('/api/units', { method: 'POST', body: JSON.stringify(node) })
export const updateUnit = (id: number, node: BusinessNode) => api(`/api/units/${id}`, { method: 'PUT', body: JSON.stringify(node) })
export const deleteUnit = (id: number) => api<void>(`/api/units/${id}`, { method: 'DELETE' })
export const assignChannel = (id: number, unitId: number | null) => api(`/api/channels/${id}/unit`, { method: 'PUT', body: JSON.stringify({ unitId }) })
export const getUsers = () => api<ManagedUser[]>('/api/users')
export const getRoles = () => api<Role[]>('/api/roles')
export const getPermissions = () => api<Permission[]>('/api/permissions')
export const updateRolePermissions = (id: number, codes: string[]) => api(`/api/roles/${id}/permissions`, { method: 'PUT', body: JSON.stringify({ codes }) })
export const createUser = (username: string, password: string, displayName = '', phone = '') => api<ManagedUser>('/api/users', { method: 'POST', body: JSON.stringify({ username, password, displayName: displayName || null, phone: phone || null }) })
export const updateUser = (id: number, payload: { username: string; displayName?: string; phone?: string; status: ManagedUser['status']; password?: string | null }) => api<ManagedUser>(`/api/users/${id}`, { method: 'PUT', body: JSON.stringify({ ...payload, displayName: payload.displayName || null, phone: payload.phone || null, password: payload.password || null }) })
export const assignUserRole = (id: number, roleId: number | null) => api(`/api/users/${id}/role`, { method: 'PUT', body: JSON.stringify({ roleId }) })
export const createRole = (name: string, code: string, status = 'active') => api<Role>('/api/roles', { method: 'POST', body: JSON.stringify({ name, code, status }) })
export const updateRole = (id: number, name: string, code: string, status: string) => api<Role>(`/api/roles/${id}`, { method: 'PUT', body: JSON.stringify({ name, code, status }) })
export const deleteRole = (id: number) => api<void>(`/api/roles/${id}`, { method: 'DELETE' })
export const getDesktopReleases = () => api<DesktopRelease[]>('/api/desktop-releases')
export const getPublicDesktopRelease = () => api<PublicDesktopRelease>('/api/desktop-releases/latest')
export const uploadDesktopRelease = (file: File, version: string, releaseNotes: string, minimumVersion: string, forceUpdate: boolean) => {
  const form = new FormData()
  form.append('file', file)
  form.append('version', version)
  form.append('releaseNotes', releaseNotes)
  form.append('minimumVersion', minimumVersion)
  form.append('forceUpdate', String(forceUpdate))
  return api<{ id: number; version: string; sha256: string; fileSize: number; status: string }>('/api/desktop-releases', { method: 'POST', body: form })
}
export const publishDesktopRelease = (id: number, minimumVersion: string, forceUpdate: boolean) => api(`/api/desktop-releases/${id}/publish`, { method: 'POST', body: JSON.stringify({ minimumVersion: minimumVersion || null, forceUpdate }) })
export const revokeDesktopRelease = (id: number) => api(`/api/desktop-releases/${id}/revoke`, { method: 'POST' })
export const getAccessScopes = (target: 'user' | 'role', id: number) => api<AccessScope[]>(`/api/access-scopes/${target}/${id}`)
export const updateAccessScopes = (target: 'user' | 'role', id: number, scopes: AccessScope[]) => api(`/api/access-scopes/${target}/${id}`, { method: 'PUT', body: JSON.stringify({ scopes }) })
