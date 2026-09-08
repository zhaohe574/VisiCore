import type { AccessScope, Alarm, AlarmDetail, Audit, Channel, Dashboard, Device, DeviceInput, ExportJob, Layout, LiveSession, LoginResult, OnlineSession, Organization, OrganizationKind, OrganizationNode, Page, Permission, PlaybackControl, PlaybackSession, PtzCommand, Recording, Release, Role, Settings, SystemStatus, User } from './types'
import type { components } from './generated/api-schema'
import type { PublicRelease } from './types'
export type * from './types'

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
  if (response.status === 401 && retry && !path.startsWith('/auth/') && !path.startsWith('/public/')) {
    await refreshSession()
    return api<T>(path, init, false)
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
const post = <T = void>(path: string, body?: unknown) => api<T>(path, { method: 'POST', body: body === undefined ? undefined : JSON.stringify(body) })
const put = <T = void>(path: string, body: unknown) => api<T>(path, { method: 'PUT', body: JSON.stringify(body) })
const remove = (path: string, keepalive = false) => api<void>(path, { method: 'DELETE', keepalive }, !keepalive)
const segment = (id: string | number) => encodeURIComponent(id)

export const authApi = {
  login: async (username: string, password: string) => { const result = await post<LoginResult>('/auth/login', { username, password, clientType: 'web', clientVersion: '2.0.0' } satisfies components['schemas']['LoginRequest']); clearCsrf(); return result },
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
  channels: (query?: Query, signal?: AbortSignal) => list<Channel>('/channels', query, signal),
  updateChannel: (id: number, data: { alias?: string | null; unitId?: number | null }) => put<Channel>(`/channels/${id}`, data),
  assign: (channelIds: number[], unitId: number | null) => put('/channels/assignment', { channelIds, unitId }),
  organization: () => api<Organization>('/organization'),
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
  live: (channelId: number, streamType: 1 | 2) => post<LiveSession>('/live-sessions', { channelId, streamType, profile: 'browser' } satisfies components['schemas']['LiveRequest']),
  renewLive: (id: string) => post<LiveSession>(`/live-sessions/${segment(id)}/renew`), stopLive: (id: string, keepalive = false) => remove(`/live-sessions/${segment(id)}`, keepalive),
  recordings: (channelId: number, start: string, end: string) => post<Recording[]>('/recordings/search', { channelId, start, end }),
  playback: (channelId: number, start: string, end: string) => post<PlaybackSession>('/playback-sessions', { channelId, start, end, profile: 'browser' } satisfies components['schemas']['PlaybackRequest']),
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
  exports: (query?: Query, signal?: AbortSignal) => list<ExportJob>('/exports', query, signal),
  createExport: (channelId: number, start: string, end: string) => post<ExportJob>('/exports', { channelId, start, end }),
  cancelExport: (id: string) => post(`/exports/${segment(id)}/cancel`), retryExport: (id: string) => post(`/exports/${segment(id)}/retry`),
  exportUrl: (id: string) => apiUrl(`/exports/${segment(id)}/download`),
  releases: (query?: Query, signal?: AbortSignal) => list<Release>('/releases', query, signal),
  latest: (packageType?: 'zip' | 'msi') => api<PublicRelease | undefined>(`/public/releases/latest${queryString({ packageType })}`),
  uploadRelease: (data: FormData) => api<Release>('/releases', { method: 'POST', body: data }),
  publish: (id: number, minimumVersion: string, forceUpdate: boolean) => post(`/releases/${id}/publish`, { minimumVersion, forceUpdate }),
  revokeRelease: (id: number) => post(`/releases/${id}/revoke`), releaseUrl: (id: number) => apiUrl(`/releases/${id}/download`),
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
