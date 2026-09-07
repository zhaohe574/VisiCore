<script setup lang="ts">
import { computed, nextTick, onMounted, onUnmounted, ref, watch } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import {
  Warning, Bell, Camera, Check, CircleCheckFilled, Connection, DataLine,
  ArrowDown, ArrowLeft, ArrowRight, ArrowUp, Delete, Download, Edit, Expand, Fold, FullScreen, House, Key, Lock, Monitor, Plus, Promotion, Refresh, Search, Setting,
  SwitchButton, UserFilled, VideoCamera, VideoPause, VideoPlay, View, WarningFilled, ZoomIn, ZoomOut
} from '@element-plus/icons-vue'
import mpegts from 'mpegts.js'
import { HubConnection, HubConnectionBuilder } from '@microsoft/signalr'
import { refreshSession } from './api'
import {
  ackAlarm, assignChannel, assignUserRole, changePassword, controlPlayback, createArea, createRole, createUnit, createUser, createWorkshop, deleteArea, deleteRole, deleteUnit, deleteWorkshop, getAccessScopes, getAlarm, getAlarmImage, getAlarms, getAreas, getChannels, getDesktopReleases, getDevice, getDeviceStats, getPermissions, getPlayback, getPublicDesktopRelease, getRoles, getStats, getSystemStats, getUnits, getUnassignedChannels, getUsers, getWorkshops, login, logout as apiLogout, me, ptzPreset, ptzStart, ptzStop, publishDesktopRelease, renewLive, renewPlayback, revokeDesktopRelease, searchRecordings, startLive, startPlayback, stopLive, stopPlayback, updateAccessScopes, updateArea, updateProfile, updateRole, updateRolePermissions, updateUnit, updateUser, updateWorkshop, uploadDesktopRelease,
  type AccessScope, type Alarm, type Channel, type DesktopRelease, type Device, type DeviceStats, type LiveSession, type ManagedUser, type Permission, type PlaybackSession, type PublicDesktopRelease, type Recording, type Role, type Stats, type SystemStats, type User
} from './api'

type View = 'dashboard' | 'system-stats' | 'device-stats' | 'preview' | 'playback' | 'alarms' | 'devices' | 'business' | 'accounts' | 'releases' | 'settings'
type TreeNode = { id: string; label: string; children?: TreeNode[]; type?: 'workshop' | 'area' | 'unit' | 'channel'; source?: unknown[] }

const authenticated = ref(Boolean(localStorage.getItem('video-platform-token')))
const showAdminLogin = ref(window.location.hash === '#admin')
const loginForm = ref({ username: 'admin', password: '' })
const loginLoading = ref(false)
const loginError = ref('')
const user = ref<User | null>(null)
const activeView = ref<View>('dashboard')
const collapsed = ref(false)
const loading = ref(false)
const refreshing = ref(false)
const errorText = ref('')
const device = ref<Device | null>(null)
const channels = ref<Channel[]>([])
const alarms = ref<Alarm[]>([])
const alarmLoading = ref(false)
let alarmRequest = 0
const stats = ref<Stats | null>(null)
const systemStats = ref<SystemStats | null>(null)
const deviceStats = ref<DeviceStats | null>(null)
const search = ref('')
const alarmFilter = ref('all')
const alarmEventType = ref('')
const alarmChannelFilter = ref<number | undefined>(undefined)
const alarmFrom = ref('')
const alarmTo = ref('')
const layout = ref(4)
const previewStreamType = ref<1 | 2>(2)
const selectedChannels = ref<number[]>([])
const liveSessions = ref<Record<number, LiveSession>>({})
const players = new Map<number, any>()
const previewRetryTimers = new Map<number, number>()
const previewRetryCounts = new Map<number, number>()
const previewErrors = ref<Record<number, string>>({})
const videoRefs = new Map<number, HTMLVideoElement>()
const playbackVideoRef = ref<HTMLVideoElement | null>(null)
const playbackPlayer = ref<any>(null)
const playbackSession = ref<PlaybackSession | null>(null)
const playbackProgress = ref(0)
const playbackStatusTimer = ref<number | null>(null)
const playbackRenewTimer = ref<number | null>(null)
const liveRenewTimer = ref<number | null>(null)
const playbackRetryTimer = ref<number | null>(null)
const playbackRetryCount = ref(0)
const playbackChannel = ref<number | null>(null)
const playbackForm = ref({ channel: 0, start: '', end: '' })
const recordings = ref<Recording[]>([])
let recordingChannel = 0
let playbackGeneration = 0
let playbackStatusRequest = 0
const recordingLoading = ref(false)
const playbackLoading = ref(false)
const ptzBusy = ref<string | null>(null)
let ptzChannel: number | null = null
let ptzStarting: Promise<unknown> | null = null
let ptzKeepalive: number | null = null
let authTimer: number | null = null
let mediaGeneration = 0
const pendingPreviews = new Set<number>()
const ptzPresetValue = ref<number | null>(null)
const alarmDetailDialog = ref(false)
const alarmDetailLoading = ref(false)
const alarmDetail = ref<(Alarm & { imageLength: number }) | null>(null)
const alarmImageUrl = ref('')
const workshops = ref<unknown[][]>([])
const areas = ref<unknown[][]>([])
const units = ref<unknown[][]>([])
const unassigned = ref<unknown[][]>([])
const managedUsers = ref<ManagedUser[]>([])
const roles = ref<Role[]>([])
const accountLoading = ref(false)
const accountDialog = ref(false)
const accountForm = ref<{ id: number | null; username: string; displayName: string; phone: string; password: string; status: ManagedUser['status']; roleId: number | null }>({ id: null, username: '', displayName: '', phone: '', password: '', status: 'active', roleId: null })
const profileDialog = ref(false)
const profileLoading = ref(false)
const profileForm = ref({ displayName: '', phone: '', currentPassword: '', newPassword: '' })
const loggingOut = ref(false)
const roleDialog = ref(false)
const roleLoading = ref(false)
const roleForm = ref<{ id: number | null; name: string; code: string; status: 'active' | 'disabled' }>({ id: null, name: '', code: '', status: 'active' })
const businessDialog = ref(false)
const businessForm = ref<{ type: 'workshop' | 'area' | 'unit'; id: number | null; name: string; code: string; parentId: number | null; status: 'active' | 'disabled' }>({ type: 'workshop', id: null, name: '', code: '', parentId: null, status: 'active' })
const permissions = ref<Permission[]>([])
const permissionDialog = ref(false)
const permissionRole = ref<Role | null>(null)
const permissionCodes = ref<string[]>([])
const permissionTreeRef = ref<{ getCheckedKeys: (leafOnly?: boolean) => Array<string | number>; setCheckedKeys: (keys: Array<string | number>) => void } | null>(null)
const releases = ref<DesktopRelease[]>([])
const publicRelease = ref<PublicDesktopRelease | null>(null)
const publicReleaseLoading = ref(false)
const releaseDialog = ref(false)
const releaseLoading = ref(false)
const releaseFile = ref<File | null>(null)
const releaseForm = ref({ version: '', releaseNotes: '', minimumVersion: '', forceUpdate: false })
const scopeDialog = ref(false)
const scopeLoading = ref(false)
const scopeTarget = ref<{ target: 'user' | 'role'; id: number; label: string } | null>(null)
const selectedScopeKeys = ref<string[]>([])
const scopeTreeRef = ref<{ getCheckedKeys: (leafOnly?: boolean) => Array<string | number>; setCheckedKeys: (keys: Array<string | number>) => void } | null>(null)
const currentProtocol = window.location.protocol
const currentHost = window.location.host
const settingsForm = ref({
  apiBase: localStorage.getItem('video-platform-api-base') || window.location.origin,
  sslEnabled: currentProtocol === 'https:',
  sslDomain: window.location.hostname,
  certificatePath: '/etc/nginx/ssl/video-platform.crt',
  keyPath: '/etc/nginx/ssl/video-platform.key'
})
let alarmConnection: HubConnection | null = null
let deviceConnection: HubConnection | null = null
let statsTimer: number | null = null

const onlineChannels = computed(() => channels.value.filter(channel => channel.online && channel.enabled))
const filteredChannels = computed(() => {
  const needle = search.value.trim().toLowerCase()
  if (!needle) return channels.value
  return channels.value.filter(channel => `${channel.channelNumber} ${channel.name} ${channel.model}`.toLowerCase().includes(needle))
})
const visibleAlarms = computed(() => alarms.value)
const newAlarmCount = computed(() => stats.value?.unacknowledgedAlarms ?? alarms.value.filter(item => item.state === 'new').length)
const activeViewTitle = computed(() => ({ dashboard: '运行总览', 'system-stats': '系统状态', 'device-stats': '设备统计', preview: '实时预览', playback: '录像回放', alarms: '报警中心', devices: '设备与通道', business: '业务结构', accounts: '账号与权限', releases: '桌面端版本', settings: '连接与安全' })[activeView.value])
const canManageUsers = computed(() => user.value?.permissions?.includes('user.manage') ?? false)
const canManageRoles = computed(() => user.value?.permissions?.includes('role.manage') ?? false)
const canManageAreas = computed(() => user.value?.permissions?.includes('area.manage') ?? false)
const canAssignChannels = computed(() => user.value?.permissions?.includes('channel.assign') ?? false)
const canManageReleases = computed(() => user.value?.permissions?.includes('desktop.release.manage') ?? false)
const canViewPlayback = computed(() => user.value?.permissions?.includes('playback.view') ?? false)
const canControlPtz = computed(() => user.value?.permissions?.includes('ptz.control') ?? false)
const publicDownloadUrl = computed(() => publicRelease.value ? `/api/desktop-releases/${publicRelease.value.id}/download` : '')
const businessDialogTitle = computed(() => `${businessForm.value.id ? '编辑' : '新增'}${businessForm.value.type === 'workshop' ? '车间' : businessForm.value.type === 'area' ? '区域' : '机组'}`)
const scopeDialogTitle = computed(() => `配置${scopeTarget.value?.label ?? ''}数据范围`)
const permissionGroupLabels: Record<string, string> = { user: '账号', device: '设备', channel: '通道', live: '实时预览', playback: '录像回放', ptz: '云台', alarm: '报警', area: '业务区域', role: '角色', statistics: '统计' }
const permissionTree = computed<TreeNode[]>(() => {
  const groups = new Map<string, Permission[]>()
  for (const item of permissions.value) groups.set(item.resourceType, [...(groups.get(item.resourceType) ?? []), item])
  return [...groups.entries()].sort(([a], [b]) => a.localeCompare(b)).map(([resource, items]) => ({
    id: `permission-group:${resource}`,
    label: permissionGroupLabels[resource] ?? resource,
    children: items.map(item => ({ id: item.code, label: item.name }))
  }))
})
const businessTree = computed<TreeNode[]>(() => workshops.value.map(workshop => ({
  id: `workshop:${workshop[0]}`,
  label: String(workshop[1] ?? ''),
  type: 'workshop',
  source: workshop,
  children: areas.value.filter(area => Number(area[1]) === Number(workshop[0])).map(area => ({
    id: `area:${area[0]}`,
    label: String(area[2] ?? ''),
    type: 'area',
    source: area,
    children: units.value.filter(unit => Number(unit[1]) === Number(area[0])).map(unit => ({
      id: `unit:${unit[0]}`,
      label: String(unit[2] ?? ''),
      type: 'unit',
      source: unit
    }))
  }))
})))
const scopeTree = computed<TreeNode[]>(() => {
  const activeWorkshops = workshops.value.filter(row => row[3] === 'active')
  const activeAreas = areas.value.filter(row => row[4] === 'active')
  const activeUnits = units.value.filter(row => row[4] === 'active')
  const channelNodes = (items: Channel[]) => items.filter(channel => channel.id).map(channel => ({ id: `channel:${channel.id}`, label: `通道 ${channel.channelNumber}／${channel.name || '未命名'}`, type: 'channel' as const }))
  const assignedChannels = new Set<number>()
  const tree: TreeNode[] = activeWorkshops.map(workshop => ({
    id: `workshop:${workshop[0]}`,
    label: `车间／${workshop[1]}`,
    children: activeAreas.filter(area => Number(area[1]) === Number(workshop[0])).map(area => ({
      id: `area:${area[0]}`,
      label: `区域／${area[2]}`,
      children: activeUnits.filter(unit => Number(unit[1]) === Number(area[0])).map(unit => {
        const children = channels.value.filter(channel => channel.unitId === Number(unit[0]))
        children.forEach(channel => { if (channel.id) assignedChannels.add(channel.id) })
        return { id: `unit:${unit[0]}`, label: `机组／${unit[2]}`, children: channelNodes(children) }
      })
    }))
  }))
  const unassignedChannels = channels.value.filter(channel => channel.id && !assignedChannels.has(channel.id))
  if (unassignedChannels.length) tree.push({ id: 'scope-group:unassigned', label: '未分配通道', children: channelNodes(unassignedChannels) })
  return tree
})
const previewSlots = computed(() => Array.from({ length: layout.value }, (_, index) => selectedChannels.value[index] ?? null))
const sessionCount = computed(() => Object.keys(liveSessions.value).length)
const focusedChannel = computed(() => selectedChannels.value.at(-1) ?? null)
const focusedChannelInfo = computed(() => focusedChannel.value ? channels.value.find(item => item.channelNumber === focusedChannel.value) ?? null : null)
const playbackChannelInfo = computed(() => playbackChannel.value ? channels.value.find(item => item.channelNumber === playbackChannel.value) ?? null : null)
const systemCpuPercent = computed(() => Math.round(systemStats.value?.loadAverage.percent ?? 0))
const systemMemoryPercent = computed(() => Math.round(systemStats.value?.memory.usedPercent ?? 0))
const systemDiskPercent = computed(() => Math.round(systemStats.value?.disk.usedPercent ?? 0))
const deviceOnlinePercent = computed(() => deviceStats.value?.channels ? Math.round(deviceStats.value.onlineChannels / deviceStats.value.channels * 100) : 0)
const deviceTrendMax = computed(() => Math.max(1, ...(deviceStats.value?.onlineTrend ?? []).map(item => item.total || item.online)))

function setVideoRef(channel: number, element: unknown) {
  if (element instanceof HTMLVideoElement) videoRefs.set(channel, element)
  else videoRefs.delete(channel)
}

function resolveMediaUrl(value: string) {
  try {
    const url = new URL(value, window.location.origin)
    if (url.pathname.startsWith('/media/')) {
      url.protocol = window.location.protocol
      url.host = window.location.host
    }
    return url.toString()
  } catch {
    return value
  }
}

function alarmQuery() {
  return {
    limit: 100,
    state: alarmFilter.value === 'all' ? undefined : alarmFilter.value,
    channel: alarmChannelFilter.value,
    eventType: alarmEventType.value.trim() || undefined,
    from: alarmFrom.value ? toIsoDateTime(alarmFrom.value) : undefined,
    to: alarmTo.value ? toIsoDateTime(alarmTo.value) : undefined
  }
}

async function loadAlarms() {
  const request = ++alarmRequest
  alarmLoading.value = true
  try {
    const current = await getAlarms(alarmQuery())
    if (request === alarmRequest && authenticated.value) alarms.value = current
  }
  finally { if (request === alarmRequest) alarmLoading.value = false }
}

async function loadCore() {
  loading.value = true
  errorText.value = ''
  let succeeded = true
  try {
    const currentUser = await me()
    if (!authenticated.value) return false
    user.value = currentUser
    const allowed = (permission: string) => currentUser.permissions?.includes(permission)
    const [currentDevice, currentChannels, currentAlarms, currentStats] = await Promise.all([
      allowed('device.read') ? getDevice() : null,
      allowed('channel.read') ? getChannels() : [],
      allowed('alarm.read') ? getAlarms(alarmQuery()) : [],
      allowed('statistics.read') ? getStats() : null
    ])
    if (!authenticated.value) return false
    const currentDeviceStats = currentUser.permissions?.includes('statistics.read') ? await getDeviceStats().catch(() => null) : null
    device.value = currentDevice && currentDeviceStats ? { ...currentDevice, id: currentDeviceStats.deviceId, deviceKey: currentDeviceStats.deviceKey, ip: currentDeviceStats.ip, servicePort: currentDeviceStats.servicePort, model: currentDeviceStats.model, status: currentDeviceStats.status, lastSeenAt: currentDeviceStats.lastSeenAt } : currentDevice
    channels.value = currentChannels
    alarms.value = currentAlarms
    stats.value = currentStats
    for (const channel of Object.keys(liveSessions.value).map(Number)) {
      if (!allowed('live.view') || !currentChannels.some(item => item.channelNumber === channel)) await endPreview(channel)
    }
    if (playbackSession.value && (!allowed('playback.view') || !currentChannels.some(item => item.channelNumber === playbackSession.value?.channel))) await stopPlaybackSession()
  } catch (error) {
    succeeded = false
    errorText.value = error instanceof Error ? error.message : '数据加载失败'
  } finally {
    loading.value = false
  }
  return succeeded
}

async function loadPublicRelease() {
  publicReleaseLoading.value = true
  try { publicRelease.value = await getPublicDesktopRelease() }
  catch { publicRelease.value = null }
  finally { publicReleaseLoading.value = false }
}

function openAdminLogin() {
  showAdminLogin.value = true
  window.history.replaceState(null, '', `${window.location.pathname}#admin`)
}

function closeAdminLogin() {
  showAdminLogin.value = false
  window.history.replaceState(null, '', window.location.pathname)
  loginError.value = ''
}

async function loadSystemStats() {
  try { systemStats.value = await getSystemStats() }
  catch (error) { ElMessage.error(error instanceof Error ? error.message : '系统统计加载失败') }
}

async function loadDeviceStats() {
  try { deviceStats.value = await getDeviceStats() }
  catch (error) { ElMessage.error(error instanceof Error ? error.message : '设备统计加载失败') }
}

async function connectRealtime() {
  if (!authenticated.value || !user.value?.permissions?.includes('alarm.read') || alarmConnection) return
  const connection = new HubConnectionBuilder()
    .withUrl('/hubs/alarm', { accessTokenFactory: () => localStorage.getItem('video-platform-token') ?? '' })
    .withAutomaticReconnect({ nextRetryDelayInMilliseconds: context => Math.min(30000, 1000 * 2 ** Math.min(context.previousRetryCount, 5)) })
    .build()
  connection.on('alarm', async () => {
    try {
      await loadAlarms()
      stats.value = await getStats()
    } catch { /* 保留当前快照，下一次刷新重试 */ }
  })
  connection.onclose(() => { if (alarmConnection === connection) alarmConnection = null })
  alarmConnection = connection
  try { await connection.start() } catch (error) {
    alarmConnection = null
    ElMessage.warning(error instanceof Error ? `实时推送未连接：${error.message}` : '实时推送未连接')
  }
}

async function disconnectRealtime() {
  const connection = alarmConnection
  alarmConnection = null
  if (connection) await connection.stop().catch(() => undefined)
  const device = deviceConnection
  deviceConnection = null
  if (device) await device.stop().catch(() => undefined)
}

function defaultPlaybackRange(channel?: number) {
  const end = new Date()
  const start = new Date(end.getTime() - 60 * 60 * 1000)
  playbackForm.value = {
    channel: channel ?? focusedChannel.value ?? channels.value.find(item => item.online)?.channelNumber ?? 0,
    start: toLocalDateTime(start),
    end: toLocalDateTime(end)
  }
}

function toLocalDateTime(value: Date) {
  const offset = value.getTimezoneOffset() * 60_000
  return new Date(value.getTime() - offset).toISOString().slice(0, 16)
}

function toIsoDateTime(value: string) {
  return new Date(value).toISOString()
}

async function connectDeviceRealtime() {
  if (!authenticated.value || !user.value?.permissions?.includes('device.read') || deviceConnection) return
  const connection = new HubConnectionBuilder()
    .withUrl('/hubs/device-status', { accessTokenFactory: () => localStorage.getItem('video-platform-token') ?? '' })
    .withAutomaticReconnect({ nextRetryDelayInMilliseconds: context => Math.min(30000, 1000 * 2 ** Math.min(context.previousRetryCount, 5)) })
    .build()
  connection.on('deviceStatus', () => { void loadCore() })
  connection.onclose(() => { if (deviceConnection === connection) deviceConnection = null })
  deviceConnection = connection
  try { await connection.start() } catch {
    deviceConnection = null
  }
}

async function refreshAll() {
  refreshing.value = true
  try {
    if (!await loadCore()) {
      ElMessage.error(errorText.value || '数据刷新失败')
      return
    }
    if (activeView.value === 'business') await loadBusiness()
    if (activeView.value === 'accounts') await loadAccounts()
    if (activeView.value === 'releases') await loadReleases()
    if (activeView.value === 'system-stats') await loadSystemStats()
    if (activeView.value === 'device-stats') await loadDeviceStats()
    ElMessage.success('数据已刷新')
  } finally {
    refreshing.value = false
  }
}

async function submitLogin() {
  loginError.value = ''
  if (!loginForm.value.username || !loginForm.value.password) {
    loginError.value = '请输入用户名和密码'
    return
  }
  loginLoading.value = true
  try {
    const result = await login(loginForm.value.username, loginForm.value.password)
    localStorage.setItem('video-platform-token', result.accessToken)
    authenticated.value = true
    showAdminLogin.value = false
    window.history.replaceState(null, '', window.location.pathname)
    await loadCore()
    await connectRealtime()
    await connectDeviceRealtime()
  } catch (error) {
    const message = error instanceof Error ? error.message : '登录失败'
    loginError.value = message === '请求失败（401）' ? '账号或密码不正确' : message
  } finally {
    loginLoading.value = false
  }
}

async function logout() {
  if (loggingOut.value) return
  loggingOut.value = true
  try {
    await stopAllPreviews()
    void disconnectRealtime()
    await apiLogout().catch(() => undefined)
  } finally {
    localStorage.removeItem('video-platform-token')
    authenticated.value = false
    user.value = null
    channels.value = []
    alarms.value = []
    recordings.value = []
    managedUsers.value = []
    roles.value = []
    workshops.value = []
    areas.value = []
    units.value = []
    unassigned.value = []
    releases.value = []
    stats.value = null
    device.value = null
    systemStats.value = null
    deviceStats.value = null
    closeAlarmDetail()
    profileDialog.value = false
    accountDialog.value = false
    roleDialog.value = false
    businessDialog.value = false
    permissionDialog.value = false
    scopeDialog.value = false
    releaseDialog.value = false
    showAdminLogin.value = false
    window.history.replaceState(null, '', window.location.pathname)
    loggingOut.value = false
  }
}

async function loadBusiness() {
  try {
    const [workshopRows, areaRows, unitRows, unassignedRows] = await Promise.all([getWorkshops(), getAreas(), getUnits(), getUnassignedChannels()])
    workshops.value = workshopRows
    areas.value = areaRows
    units.value = unitRows
    unassigned.value = unassignedRows
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '业务结构加载失败')
  }
}

function openBusinessCreate(type: 'workshop' | 'area' | 'unit', parentId: number | null = null) {
  businessForm.value = { type, id: null, name: '', code: '', parentId, status: 'active' }
  businessDialog.value = true
}

function openBusinessEdit(type: 'workshop' | 'area' | 'unit', row: unknown[]) {
  const area = type === 'workshop' ? null : Number(row[1])
  businessForm.value = type === 'workshop'
    ? { type, id: Number(row[0]), name: String(row[1] ?? ''), code: String(row[2] ?? ''), parentId: null, status: String(row[3]) as 'active' | 'disabled' }
    : { type, id: Number(row[0]), name: String(row[2] ?? ''), code: String(row[3] ?? ''), parentId: area, status: String(row[4]) as 'active' | 'disabled' }
  businessDialog.value = true
}

async function saveBusinessNode() {
  const form = businessForm.value
  if (!form.name.trim() || !form.code.trim()) {
    ElMessage.warning('名称和编码不能为空')
    return
  }
  const payload = { name: form.name.trim(), code: form.code.trim(), status: form.status, ...(form.type === 'area' ? { workshopId: form.parentId } : {}), ...(form.type === 'unit' ? { areaId: form.parentId } : {}) }
  try {
    if (form.type === 'workshop') form.id ? await updateWorkshop(form.id, payload) : await createWorkshop(payload)
    if (form.type === 'area') form.id ? await updateArea(form.id, payload) : await createArea(payload)
    if (form.type === 'unit') form.id ? await updateUnit(form.id, payload) : await createUnit(payload)
    businessDialog.value = false
    await loadBusiness()
    ElMessage.success(form.id ? '结构已更新' : '结构已创建')
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '业务结构保存失败')
  }
}

async function deleteBusinessNode(type: 'workshop' | 'area' | 'unit', row: unknown[]) {
  const id = Number(row[0]); const nameIndex = type === 'workshop' ? 1 : 2; const codeIndex = type === 'workshop' ? 2 : 3
  try {
    await ElMessageBox.confirm(`确定删除“${String(row[nameIndex])}”吗？有子级或关联通道时无法删除。`, '删除业务结构', { type: 'warning' })
    if (type === 'workshop') await deleteWorkshop(id)
    if (type === 'area') await deleteArea(id)
    if (type === 'unit') await deleteUnit(id)
    await loadBusiness(); ElMessage.success('业务结构已删除')
  } catch (error) { if (!isDialogCancel(error)) ElMessage.error(error instanceof Error ? error.message : '删除业务结构失败') }
}

async function assignChannelToUnit(row: unknown[], unitId: number) {
  if (!unitId) return
  try {
    await assignChannel(Number(row[0]), unitId)
    await loadBusiness()
    ElMessage.success(`通道 ${row[2]} 已分配`)
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '通道分配失败')
  }
}

async function loadAccounts() {
  accountLoading.value = true
  try {
    const [userRows, roleRows, permissionRows] = await Promise.all([
      canManageUsers.value ? getUsers() : Promise.resolve([] as ManagedUser[]),
      canManageRoles.value ? getRoles() : Promise.resolve([] as Role[]),
      canManageRoles.value ? getPermissions() : Promise.resolve([] as Permission[])
    ])
    managedUsers.value = userRows
    roles.value = roleRows
    permissions.value = permissionRows
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '账号数据加载失败')
  } finally {
    accountLoading.value = false
  }
}

async function openScopeDialog(target: 'user' | 'role', id: number, label: string) {
  scopeTarget.value = { target, id, label }
  scopeLoading.value = true
  try {
    if (!workshops.value.length) await loadBusiness()
    const current = await getAccessScopes(target, id)
    selectedScopeKeys.value = current.map(scope => `${scope.scopeType}:${scope.scopeId}`)
    scopeDialog.value = true
    await nextTick()
    scopeTreeRef.value?.setCheckedKeys(selectedScopeKeys.value)
  } catch (error) { ElMessage.error(error instanceof Error ? error.message : '数据范围加载失败') }
  finally { scopeLoading.value = false }
}

function scopeTreeParents() {
  const parents = new Map<string, string>()
  const walk = (nodes: TreeNode[], parent?: string) => {
    for (const node of nodes) {
      if (parent) parents.set(node.id, parent)
      if (node.children) walk(node.children, node.id)
    }
  }
  walk(scopeTree.value)
  return parents
}

function collapseScopeKeys(keys: string[]) {
  const selected = new Set(keys)
  const parents = scopeTreeParents()
  return keys.filter(key => {
    let parent = parents.get(key)
    while (parent) {
      if (selected.has(parent) && /^(workshop|area|unit):/.test(parent)) return false
      parent = parents.get(parent)
    }
    return key.startsWith('workshop:') || key.startsWith('area:') || key.startsWith('unit:') || key.startsWith('channel:')
  })
}

async function saveScopes() {
  if (!scopeTarget.value) return
  const checked = scopeTreeRef.value?.getCheckedKeys(false).map(String) ?? selectedScopeKeys.value
  const scopes: AccessScope[] = collapseScopeKeys(checked).map(key => {
    const [scopeType, scopeId] = key.split(':')
    return { scopeType: scopeType as AccessScope['scopeType'], scopeId: Number(scopeId) }
  })
  scopeLoading.value = true
  try {
    await updateAccessScopes(scopeTarget.value.target, scopeTarget.value.id, scopes)
    scopeDialog.value = false
    ElMessage.success('数据范围已保存')
  } catch (error) { ElMessage.error(error instanceof Error ? error.message : '数据范围保存失败') }
  finally { scopeLoading.value = false }
}

async function loadReleases() {
  if (!canManageReleases.value) return
  releaseLoading.value = true
  try { releases.value = await getDesktopReleases() }
  catch (error) { ElMessage.error(error instanceof Error ? error.message : '版本列表加载失败') }
  finally { releaseLoading.value = false }
}

function chooseReleaseFile(event: Event) {
  releaseFile.value = (event.target as HTMLInputElement).files?.[0] ?? null
}

async function submitRelease() {
  if (!releaseFile.value || !releaseForm.value.version.trim()) {
    ElMessage.warning('请选择安装包并填写版本号')
    return
  }
  releaseLoading.value = true
  try {
    await uploadDesktopRelease(releaseFile.value, releaseForm.value.version.trim(), releaseForm.value.releaseNotes.trim(), releaseForm.value.minimumVersion.trim(), releaseForm.value.forceUpdate)
    releaseDialog.value = false
    releaseFile.value = null
    releaseForm.value = { version: '', releaseNotes: '', minimumVersion: '', forceUpdate: false }
    await loadReleases()
    ElMessage.success('安装包已上传为草稿')
  } catch (error) { ElMessage.error(error instanceof Error ? error.message : '安装包上传失败') }
  finally { releaseLoading.value = false }
}

async function publishRelease(item: DesktopRelease) {
  try {
    await ElMessageBox.confirm(`确定发布版本 ${item.version} 吗？`, '发布桌面端版本', { type: 'warning' })
    await publishDesktopRelease(item.id, item.minimumVersion ?? '', item.forceUpdate)
    await loadReleases()
    ElMessage.success('版本已发布')
  } catch (error) {
    if (!isDialogCancel(error)) ElMessage.error(error instanceof Error ? error.message : '版本发布失败')
  }
}

async function revokeRelease(item: DesktopRelease) {
  try {
    await ElMessageBox.confirm(`确定撤回版本 ${item.version} 吗？`, '撤回桌面端版本', { type: 'warning' })
    await revokeDesktopRelease(item.id)
    await loadReleases()
    ElMessage.success('版本已撤回')
  } catch (error) {
    if (!isDialogCancel(error)) ElMessage.error(error instanceof Error ? error.message : '版本撤回失败')
  }
}

function openPermissionDialog(role: Role) {
  permissionRole.value = role
  permissionCodes.value = [...role.permissionCodes]
  permissionDialog.value = true
  void nextTick(() => permissionTreeRef.value?.setCheckedKeys(permissionCodes.value))
}

async function saveRolePermissions() {
  if (!permissionRole.value) return
  try {
    const selected = permissionTreeRef.value?.getCheckedKeys(true).map(String) ?? permissionCodes.value
    await updateRolePermissions(permissionRole.value.id, selected)
    permissionDialog.value = false
    await loadAccounts()
    ElMessage.success('角色权限已更新')
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '角色权限更新失败')
  }
}

function saveSettings() {
  try {
    const value = settingsForm.value.apiBase.trim()
    const parsed = new URL(value)
    if (!['http:', 'https:'].includes(parsed.protocol) || parsed.username || parsed.password || parsed.search || parsed.hash) throw new Error('平台网址必须是有效的 HTTP 或 HTTPS 地址，不能包含账号、密码、查询参数或片段')
    if (currentProtocol === 'https:' && parsed.protocol !== 'https:') throw new Error('当前页面使用 HTTPS，平台网址也必须使用 HTTPS')
    const normalized = parsed.toString().replace(/\/+$/, '')
    settingsForm.value.apiBase = normalized
    localStorage.setItem('video-platform-api-base', normalized)
    localStorage.setItem('video-platform-ssl-settings', JSON.stringify({ ...settingsForm.value }))
    ElMessage.success('连接与 SSL 配置已保存')
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '配置保存失败')
  }
}

function resetSettings() {
  settingsForm.value = { apiBase: window.location.origin, sslEnabled: currentProtocol === 'https:', sslDomain: window.location.hostname, certificatePath: '/etc/nginx/ssl/video-platform.crt', keyPath: '/etc/nginx/ssl/video-platform.key' }
  localStorage.removeItem('video-platform-api-base')
  localStorage.removeItem('video-platform-ssl-settings')
  ElMessage.success('配置已恢复默认值')
}

async function switchView(view: View) {
  await stopPtz()
  const previousView = activeView.value
  if (previousView === 'preview' && view !== 'preview') destroyPreviewPlayers()
  if (previousView === 'playback' && view !== 'playback') {
    playbackPlayer.value?.destroy()
    playbackPlayer.value = null
  }
  activeView.value = view
  if (view === 'preview') {
    await nextTick()
    for (const channel of selectedChannels.value) {
      const session = liveSessions.value[channel]
      if (session) mountPlayer(channel, session)
    }
  }
  if (view === 'playback' && playbackSession.value) {
    await nextTick()
    mountPlaybackPlayer()
  }
  if (view === 'business' && !workshops.value.length) await loadBusiness()
  if (view === 'alarms' && !alarms.value.length) await loadAlarms()
  if (view === 'accounts' && !managedUsers.value.length && (canManageUsers.value || canManageRoles.value)) await loadAccounts()
  if (view === 'releases' && !releases.value.length) await loadReleases()
  if (view === 'system-stats' && !systemStats.value) await loadSystemStats()
  if (view === 'device-stats' && !deviceStats.value) await loadDeviceStats()
  if (view === 'playback' && !playbackForm.value.start) defaultPlaybackRange()
}

async function toggleUserStatus(item: ManagedUser) {
  const status = item.status === 'active' ? 'disabled' : 'active'
  try {
    await ElMessageBox.confirm(`确定${status === 'active' ? '启用' : '停用'}账号“${item.username}”吗？`, '账号状态', { type: 'warning' })
    await updateUser(item.id, { username: item.username, displayName: item.displayName ?? '', phone: item.phone ?? '', status })
    await loadAccounts()
    ElMessage.success(status === 'active' ? '账号已启用' : '账号已停用')
  } catch (error) {
    if (!isDialogCancel(error)) ElMessage.error(error instanceof Error ? error.message : '账号状态更新失败')
  }
}

async function lockUser(item: ManagedUser) {
  try {
    await ElMessageBox.confirm(`确定锁定账号“${item.username}”吗？`, '锁定账号', { type: 'warning' })
    await updateUser(item.id, { username: item.username, displayName: item.displayName ?? '', phone: item.phone ?? '', status: 'locked' })
    await loadAccounts()
    ElMessage.success('账号已锁定')
  } catch (error) {
    if (!isDialogCancel(error)) ElMessage.error(error instanceof Error ? error.message : '账号锁定失败')
  }
}

async function resetUserPassword(item: ManagedUser) {
  try {
    const result = await ElMessageBox.prompt('输入新密码（至少 12 个字符）', `重置 ${item.username} 密码`, { inputType: 'password', inputPlaceholder: '新密码' })
    if (result.value.length < 12) {
      ElMessage.warning('密码至少需要 12 个字符')
      return
    }
    await updateUser(item.id, { username: item.username, displayName: item.displayName ?? '', phone: item.phone ?? '', status: item.status, password: result.value })
    ElMessage.success('密码已重置')
  } catch (error) {
    if (!isDialogCancel(error)) ElMessage.error(error instanceof Error ? error.message : '密码重置失败')
  }
}

function openAccountCreate() {
  accountForm.value = { id: null, username: '', displayName: '', phone: '', password: '', status: 'active', roleId: null }
  accountDialog.value = true
}

function openAccountEdit(item: ManagedUser) {
  accountForm.value = { id: item.id, username: item.username, displayName: item.displayName ?? '', phone: item.phone ?? '', password: '', status: item.status, roleId: item.roleIds[0] ?? null }
  accountDialog.value = true
}

async function saveAccount() {
  const form = accountForm.value
  if (!form.username.trim() || (form.id === null && form.password.length < 12) || (form.id !== null && form.password && form.password.length < 12)) {
    ElMessage.warning(form.id === null ? '请输入账号，密码至少需要 12 个字符' : '请输入账号，密码为空或至少需要 12 个字符')
    return
  }
  try {
    const item = form.id === null
      ? await createUser(form.username, form.password, form.displayName, form.phone)
      : await updateUser(form.id, { username: form.username, displayName: form.displayName, phone: form.phone, status: form.status, password: form.password || null })
    const currentRoleId = form.id === null ? null : managedUsers.value.find(candidate => candidate.id === form.id)?.roleIds[0] ?? null
    if (item?.id && form.roleId !== currentRoleId) await assignUserRole(item.id, form.roleId)
    accountDialog.value = false
    await loadAccounts()
    ElMessage.success(form.id === null ? '账号已创建' : '账号信息已更新')
  } catch (error) { ElMessage.error(error instanceof Error ? error.message : '账号保存失败') }
}

function openProfileDialog() {
  profileForm.value = { displayName: user.value?.displayName ?? '', phone: user.value?.phone ?? '', currentPassword: '', newPassword: '' }
  profileDialog.value = true
}

async function saveProfile() {
  const form = profileForm.value
  if (form.newPassword && (!form.currentPassword || form.newPassword.length < 12)) {
    ElMessage.warning('修改密码时请输入当前密码，且新密码至少 12 个字符')
    return
  }
  profileLoading.value = true
  try {
    const updated = await updateProfile(form.displayName, form.phone)
    user.value = { ...user.value!, ...updated }
    if (form.newPassword) await changePassword(form.currentPassword, form.newPassword)
    profileDialog.value = false
    ElMessage.success('个人资料已更新')
  } catch (error) { ElMessage.error(error instanceof Error ? error.message : '个人资料更新失败') }
  finally { profileLoading.value = false }
}

function openRoleCreate() {
  roleForm.value = { id: null, name: '', code: '', status: 'active' }
  roleDialog.value = true
}

function openRoleEdit(role: Role) {
  roleForm.value = { id: role.id, name: role.name, code: role.code, status: role.status as 'active' | 'disabled' }
  roleDialog.value = true
}

async function saveRole() {
  const form = roleForm.value
  if (!form.name.trim() || !form.code.trim()) { ElMessage.warning('角色名称和编码不能为空'); return }
  roleLoading.value = true
  try {
    if (form.id === null) await createRole(form.name, form.code, form.status)
    else await updateRole(form.id, form.name, form.code, form.status)
    roleDialog.value = false
    await loadAccounts()
    ElMessage.success(form.id === null ? '角色已创建' : '角色已更新')
  } catch (error) { ElMessage.error(error instanceof Error ? error.message : '角色保存失败') }
  finally { roleLoading.value = false }
}

async function removeRole(role: Role) {
  try {
    await ElMessageBox.confirm(`确定删除角色“${role.name}”吗？`, '删除角色', { type: 'warning' })
    await deleteRole(role.id)
    await loadAccounts()
    ElMessage.success('角色已删除')
  } catch (error) { if (!isDialogCancel(error)) ElMessage.error(error instanceof Error ? error.message : '删除角色失败') }
}

async function changeUserRole(item: ManagedUser, roleId: number | null) {
  if ((item.roleIds[0] ?? null) === roleId) return
  try {
    await assignUserRole(item.id, roleId)
    await loadAccounts()
    ElMessage.success('角色已更新')
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '角色更新失败')
  }
}

async function beginPreview(channel: number) {
  if (pendingPreviews.has(channel)) return
  pendingPreviews.add(channel)
  const generation = mediaGeneration
  try {
  if (liveSessions.value[channel] && liveSessions.value[channel].streamType === previewStreamType.value) {
    if (!selectedChannels.value.includes(channel)) selectedChannels.value = [...selectedChannels.value, channel].slice(-layout.value)
    await nextTick()
    if (!players.has(channel)) mountPlayer(channel, liveSessions.value[channel])
    return
  }
  if (liveSessions.value[channel]) await endPreview(channel)
  try {
    const session = await startLive(channel, previewStreamType.value)
    if (!authenticated.value || generation !== mediaGeneration) { await stopLive(session.id); return }
    if (!selectedChannels.value.includes(channel) && selectedChannels.value.length >= layout.value) await endPreview(selectedChannels.value[0])
    liveSessions.value = { ...liveSessions.value, [channel]: session }
    startLiveRenewal()
    selectedChannels.value = [...selectedChannels.value.filter(item => item !== channel), channel].slice(-layout.value)
    await nextTick()
    mountPlayer(channel, session)
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '实时预览启动失败')
  }
  } finally { pendingPreviews.delete(channel) }
}

function mountPlayer(channel: number, session: LiveSession) {
  const element = videoRefs.get(channel)
  if (!element || !mpegts.isSupported()) return
  const oldPlayer = players.get(channel)
  oldPlayer?.destroy()
  const player = mpegts.createPlayer({ type: 'flv', url: resolveMediaUrl(session.httpFlvUrl), isLive: true }, { enableStashBuffer: false, liveBufferLatencyChasing: true })
  player.attachMediaElement(element)
  player.on(mpegts.Events.MEDIA_INFO, () => { previewRetryCounts.delete(channel); delete previewErrors.value[channel] })
  player.on(mpegts.Events.ERROR, () => schedulePreviewRetry(channel))
  player.load()
  const playback = player.play()
  if (playback && typeof playback.then === 'function') playback.catch(() => undefined)
  players.set(channel, player)
}

function schedulePreviewRetry(channel: number) {
  if (!liveSessions.value[channel] || previewRetryTimers.has(channel)) return
  const attempt = previewRetryCounts.get(channel) ?? 0
  if (attempt >= 4) {
    previewErrors.value[channel] = '连接失败'
    ElMessage.warning(`通道 ${channel} 连接失败，请检查设备状态`)
    return
  }
  previewRetryCounts.set(channel, attempt + 1)
  previewErrors.value[channel] = '正在重连'
  const delay = Math.min(16000, 1000 * 2 ** attempt)
  ElMessage.info(`通道 ${channel} 连接中断，${Math.ceil(delay / 1000)} 秒后重试`)
  const timer = window.setTimeout(() => {
    previewRetryTimers.delete(channel)
    const session = liveSessions.value[channel]
    if (session) mountPlayer(channel, session)
  }, delay)
  previewRetryTimers.set(channel, timer)
}

function destroyPreviewPlayers() {
  for (const timer of previewRetryTimers.values()) window.clearTimeout(timer)
  previewRetryTimers.clear()
  previewRetryCounts.clear()
  for (const player of players.values()) player.destroy()
  players.clear()
  previewErrors.value = {}
}

function retryPreview(channel: number) {
  previewRetryCounts.delete(channel)
  const timer = previewRetryTimers.get(channel)
  if (timer !== undefined) window.clearTimeout(timer)
  previewRetryTimers.delete(channel)
  if (liveSessions.value[channel]) mountPlayer(channel, liveSessions.value[channel])
  else void beginPreview(channel)
}

async function endPreview(channel: number) {
  const session = liveSessions.value[channel]
  if (!session) return
  players.get(channel)?.destroy()
  players.delete(channel)
  const retryTimer = previewRetryTimers.get(channel)
  if (retryTimer !== undefined) window.clearTimeout(retryTimer)
  previewRetryTimers.delete(channel)
  previewRetryCounts.delete(channel)
  const nextSessions = { ...liveSessions.value }
  delete previewErrors.value[channel]
  delete nextSessions[channel]
  liveSessions.value = nextSessions
  selectedChannels.value = selectedChannels.value.filter(item => item !== channel)
  if (!Object.keys(nextSessions).length) stopLiveRenewal()
  try { await stopLive(session.id) }
  catch (error) { ElMessage.error(error instanceof Error ? error.message : '实时预览停止失败') }
}

async function stopAllPreviews() {
  mediaGeneration++
  await stopPtz()
  const liveStops: Promise<unknown>[] = []
  for (const [channel, session] of Object.entries(liveSessions.value)) {
    liveStops.push(stopLive(session.id).catch(() => undefined))
  }
  destroyPreviewPlayers()
  liveSessions.value = {}
  selectedChannels.value = []
  stopLiveRenewal()
  await Promise.all(liveStops)
  await stopPlaybackSession()
}

function startLiveRenewal() {
  if (liveRenewTimer.value !== null) window.clearInterval(liveRenewTimer.value)
  liveRenewTimer.value = window.setInterval(async () => {
    for (const [channel, session] of Object.entries(liveSessions.value)) {
      try {
        const renewed = await renewLive(session.id)
        if (liveSessions.value[Number(channel)]?.id !== session.id) continue
        liveSessions.value = { ...liveSessions.value, [Number(channel)]: renewed }
      } catch (error) {
        if (Date.parse(session.expiresAt) <= Date.now() || /（40[134]）/.test(String(error))) await endPreview(Number(channel))
      }
    }
  }, 60 * 1000)
}

function stopLiveRenewal() {
  if (liveRenewTimer.value !== null) window.clearInterval(liveRenewTimer.value)
  liveRenewTimer.value = null
}

async function toggleStreamType() {
  previewStreamType.value = previewStreamType.value === 2 ? 1 : 2
  const active = [...selectedChannels.value]
  await Promise.all(active.map(channel => endPreview(channel)))
  for (const channel of active) await beginPreview(channel)
}

async function toggleFullscreen(element: HTMLVideoElement | null) {
  const target = element?.parentElement
  if (!target) return
  try {
    if (document.fullscreenElement) await document.exitFullscreen()
    else await target.requestFullscreen()
  } catch { /* 浏览器拒绝全屏时保持当前画面 */ }
}

function captureVideo(element: HTMLVideoElement | null, channel: number) {
  if (!element || !element.videoWidth) {
    ElMessage.warning('当前视频尚未准备好')
    return
  }
  const canvas = document.createElement('canvas')
  canvas.width = element.videoWidth
  canvas.height = element.videoHeight
  canvas.getContext('2d')?.drawImage(element, 0, 0)
  canvas.toBlob(blob => {
    if (!blob) return
    const url = URL.createObjectURL(blob)
    const link = document.createElement('a')
    link.href = url
    link.download = `通道-${channel}-${new Date().toISOString().replaceAll(':', '-')}.png`
    link.click()
    URL.revokeObjectURL(url)
  }, 'image/png')
}

async function openPlayback(channel?: number) {
  if (!canViewPlayback.value) {
    ElMessage.warning('当前账号没有录像回放权限')
    return
  }
  await switchView('playback')
  if (!playbackForm.value.start || channel || !playbackForm.value.channel) defaultPlaybackRange(channel)
  else if (channel) playbackForm.value.channel = channel
}

async function searchPlayback() {
  if (recordingLoading.value) return
  playbackForm.value.channel = Number(playbackForm.value.channel)
  const form = playbackForm.value
  const start = new Date(form.start)
  const end = new Date(form.end)
  if (!form.channel || Number.isNaN(start.getTime()) || Number.isNaN(end.getTime()) || end <= start) {
    ElMessage.warning('请选择通道和有效的时间范围')
    return
  }
  if (end.getTime() - start.getTime() > 7 * 24 * 60 * 60 * 1000) {
    ElMessage.warning('录像检索时间范围不能超过 7 天')
    return
  }
  recordingLoading.value = true
  try {
    recordings.value = await searchRecordings(form.channel, toIsoDateTime(form.start), toIsoDateTime(form.end))
    recordingChannel = Number(form.channel)
    if (!recordings.value.length) ElMessage.info('该时间段没有找到录像')
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '录像检索失败')
  } finally {
    recordingLoading.value = false
  }
}

async function playRecording(record: Recording) {
  if (playbackLoading.value || !recordingChannel) return
  playbackForm.value.channel = Number(recordingChannel)
  const start = new Date(record.start)
  const end = new Date(record.end)
  if (Number.isNaN(start.getTime()) || Number.isNaN(end.getTime()) || end <= start) {
    ElMessage.warning('录像起止时间无效')
    return
  }
  if (end.getTime() - start.getTime() > 24 * 60 * 60 * 1000) {
    ElMessage.warning('单次回放时间不能超过 1 天')
    return
  }
  await stopPlaybackSession()
  const generation = playbackGeneration
  playbackLoading.value = true
  playbackChannel.value = recordingChannel
  playbackForm.value.start = toLocalDateTime(start)
  playbackForm.value.end = toLocalDateTime(end)
  try {
    const session = await startPlayback(recordingChannel, start.toISOString(), end.toISOString())
    if (!authenticated.value || generation !== playbackGeneration) { await stopPlayback(session.id); return }
    playbackSession.value = session
    playbackProgress.value = playbackSession.value.progress
    await nextTick()
    mountPlaybackPlayer()
    startPlaybackStatusPolling()
    startPlaybackRenewal()
  } catch (error) {
    playbackChannel.value = null
    ElMessage.error(error instanceof Error ? error.message : '回放启动失败')
  } finally {
    playbackLoading.value = false
  }
}

function mountPlaybackPlayer() {
  const element = playbackVideoRef.value
  const session = playbackSession.value
  if (!element || !session || !mpegts.isSupported()) return
  playbackPlayer.value?.destroy()
  const player = mpegts.createPlayer({ type: 'flv', url: resolveMediaUrl(session.httpFlvUrl), isLive: true }, { enableStashBuffer: true, stashInitialSize: 128 * 1024, liveBufferLatencyChasing: false })
  player.attachMediaElement(element)
  player.on(mpegts.Events.MEDIA_INFO, () => { playbackRetryCount.value = 0 })
  player.on(mpegts.Events.ERROR, () => schedulePlaybackRetry())
  player.load()
  const result = player.play()
  if (result && typeof result.then === 'function') result.catch(() => undefined)
  playbackPlayer.value = player
}

function schedulePlaybackRetry() {
  if (!playbackSession.value || playbackRetryTimer.value !== null) return
  if (playbackRetryCount.value >= 4) {
    ElMessage.warning('回放连接失败，请重新选择录像')
    return
  }
  const delay = Math.min(16000, 1000 * 2 ** playbackRetryCount.value)
  playbackRetryCount.value++
  ElMessage.info(`回放连接中断，${Math.ceil(delay / 1000)} 秒后重试`)
  playbackRetryTimer.value = window.setTimeout(() => {
    playbackRetryTimer.value = null
    if (playbackSession.value) mountPlaybackPlayer()
  }, delay)
}

function startPlaybackStatusPolling() {
  if (playbackStatusTimer.value !== null) window.clearInterval(playbackStatusTimer.value)
  playbackStatusTimer.value = window.setInterval(async () => {
    const session = playbackSession.value
    if (!session || playbackLoading.value) return
    const request = ++playbackStatusRequest
    try {
      const current = await getPlayback(session.id)
      if (playbackSession.value?.id !== session.id || request !== playbackStatusRequest) return
      playbackSession.value = current
      playbackProgress.value = current.progress
      if (current.state === 'completed') stopPlaybackPolling()
    } catch { /* 保留当前进度，下一次轮询重试 */ }
  }, 3000)
}

function stopPlaybackPolling() {
  if (playbackStatusTimer.value !== null) window.clearInterval(playbackStatusTimer.value)
  playbackStatusTimer.value = null
}

async function stopPlaybackSession(showMessage = false) {
  playbackGeneration++
  playbackStatusRequest++
  stopPlaybackPolling()
  if (playbackRenewTimer.value !== null) window.clearInterval(playbackRenewTimer.value)
  playbackRenewTimer.value = null
  if (playbackRetryTimer.value !== null) window.clearTimeout(playbackRetryTimer.value)
  playbackRetryTimer.value = null
  playbackRetryCount.value = 0
  const session = playbackSession.value
  playbackPlayer.value?.destroy()
  playbackPlayer.value = null
  playbackSession.value = null
  playbackChannel.value = null
  playbackProgress.value = 0
  if (!session) return
  try {
    await stopPlayback(session.id)
  } catch (error) {
    if (showMessage) ElMessage.error(error instanceof Error ? error.message : '回放停止失败')
  }
}

function startPlaybackRenewal() {
  if (playbackRenewTimer.value !== null) window.clearInterval(playbackRenewTimer.value)
  playbackRenewTimer.value = window.setInterval(async () => {
    const session = playbackSession.value
    if (!session) return
    try {
      const renewed = await renewPlayback(session.id)
      if (playbackSession.value?.id === session.id) playbackSession.value = renewed
    } catch (error) {
      if (Date.parse(session.expiresAt) <= Date.now() || /（40[134]）/.test(String(error))) await stopPlaybackSession()
    }
  }, 60 * 1000)
}

async function controlCurrentPlayback(action: 'pause' | 'resume' | 'fast' | 'slow' | 'normal') {
  const session = playbackSession.value
  if (!session || playbackLoading.value) return
  playbackLoading.value = true
  try {
    const current = await controlPlayback(session.id, action)
    if (playbackSession.value?.id !== session.id) return
    playbackSession.value = current
    playbackProgress.value = playbackSession.value.progress
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '回放控制失败')
  } finally {
    playbackLoading.value = false
  }
}

async function seekCurrentPlayback(value: number | number[]) {
  const session = playbackSession.value
  if (!session || Array.isArray(value) || playbackLoading.value) return
  const request = ++playbackStatusRequest
  playbackLoading.value = true
  try {
    const current = await controlPlayback(session.id, 'seek', value)
    if (playbackSession.value?.id !== session.id || request !== playbackStatusRequest) return
    playbackSession.value = current
    playbackProgress.value = current.progress
    await nextTick()
    mountPlaybackPlayer()
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '回放定位失败')
  } finally {
    playbackLoading.value = false
  }
}

async function startPtz(command: 'up' | 'down' | 'left' | 'right' | 'zoomIn' | 'zoomOut') {
  const channel = focusedChannelInfo.value?.channelNumber
  if (!channel || !canControlPtz.value || ptzBusy.value) return
  ptzBusy.value = command
  ptzChannel = channel
  try {
    ptzStarting = ptzStart(channel, command)
    await ptzStarting
    if (ptzChannel === channel) ptzKeepalive = window.setInterval(() => {
      if (ptzChannel !== channel) return
      ptzStarting = ptzStarting!.catch(() => undefined).then(() => ptzChannel === channel ? ptzStart(channel, command) : undefined)
      void ptzStarting.catch(() => stopPtz())
    }, 2000)
  } catch (error) {
    await stopPtz()
    ElMessage.error(error instanceof Error ? error.message : '云台控制失败')
  }
}

async function stopPtz() {
  const channel = ptzChannel
  if (!channel) return
  ptzChannel = null
  if (ptzKeepalive !== null) window.clearInterval(ptzKeepalive)
  ptzKeepalive = null
  const starting = ptzStarting
  try {
    await starting?.catch(() => undefined)
    await ptzStop(channel)
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '云台停止失败')
  } finally {
    ptzBusy.value = null
    ptzStarting = null
  }
}

async function callPtzPreset() {
  const channel = focusedChannelInfo.value?.channelNumber
  const preset = ptzPresetValue.value
  if (!channel || !preset || !canControlPtz.value) return
  try {
    await ptzPreset(channel, preset)
    ElMessage.success(`已调用预置位 ${preset}`)
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '预置位调用失败')
  }
}

async function openAlarmDetail(item: Alarm) {
  alarmDetailDialog.value = true
  alarmDetailLoading.value = true
  alarmDetail.value = null
  if (alarmImageUrl.value) URL.revokeObjectURL(alarmImageUrl.value)
  alarmImageUrl.value = ''
  try {
    const detail = await getAlarm(item.id)
    alarmDetail.value = detail
    if (detail.imageAvailable) {
      try { alarmImageUrl.value = await getAlarmImage(item.id) }
      catch { ElMessage.warning('报警图片读取失败') }
    }
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '报警详情加载失败')
    alarmDetailDialog.value = false
  } finally {
    alarmDetailLoading.value = false
  }
}

function closeAlarmDetail() {
  alarmDetailDialog.value = false
  if (alarmImageUrl.value) URL.revokeObjectURL(alarmImageUrl.value)
  alarmImageUrl.value = ''
  alarmDetail.value = null
}

async function acknowledge(alarm: Alarm) {
  try {
    const result = await ElMessageBox.prompt('处理备注（可选）', '确认报警', { inputPlaceholder: '填写处理记录' })
    await ackAlarm(alarm.id, result.value)
    alarm.state = 'acknowledged'
    ElMessage.success('报警已确认')
  } catch (error) {
    if (!isDialogCancel(error)) ElMessage.error(error instanceof Error ? error.message : '报警确认失败')
  }
}

function isDialogCancel(error: unknown) {
  return error === 'cancel' || (error instanceof Error && error.message === 'cancel')
}

function formatTime(value: string) {
  return new Date(value).toLocaleString('zh-CN', { hour12: false })
}

function channelSupportsPtz(channel: Pick<Channel, 'ptzCapable' | 'name' | 'model'>) {
  if (channel.ptzCapable != null) return channel.ptzCapable
  return /ptz|dome|2dc|球机|云台/i.test(`${channel.name ?? ''} ${channel.model ?? ''}`)
}

function alarmChannelNumber(alarm: Pick<Alarm, 'channelId' | 'channelNumber'>) {
  return alarm.channelNumber ?? (alarm.channelId == null ? null : channels.value.find(channel => channel.id === alarm.channelId)?.channelNumber ?? null)
}

function formatBytes(value: number | null | undefined) {
  if (value == null) return '—'
  if (value < 1024 * 1024) return `${(value / 1024).toFixed(0)} KB`
  if (value < 1024 * 1024 * 1024) return `${(value / 1024 / 1024).toFixed(1)} MB`
  return `${(value / 1024 / 1024 / 1024).toFixed(1)} GB`
}

function formatRate(value: number | null | undefined) {
  if (value == null) return '—'
  if (value < 1024) return `${value.toFixed(0)} B/s`
  if (value < 1024 * 1024) return `${(value / 1024).toFixed(2)} KB/s`
  return `${(value / 1024 / 1024).toFixed(2)} MB/s`
}

function formatDuration(seconds: number | null | undefined) {
  if (seconds == null) return '—'
  const days = Math.floor(seconds / 86400)
  const hours = Math.floor(seconds % 86400 / 3600)
  const minutes = Math.floor(seconds % 3600 / 60)
  return days ? `${days} 天 ${hours} 小时` : `${hours} 小时 ${minutes} 分钟`
}

function stateLabel(state: string) {
  return ({ new: '未确认', acknowledged: '已确认', resolved: '已恢复' } as Record<string, string>)[state] ?? state
}

function stateType(state: string) {
  return ({ new: 'danger', acknowledged: 'warning', resolved: 'success' } as Record<string, 'danger' | 'warning' | 'success'>)[state] ?? 'info'
}

async function chooseLayout(value: number) {
  const removed = selectedChannels.value.slice(0, Math.max(0, selectedChannels.value.length - value))
  await Promise.all(removed.map(endPreview))
  layout.value = value
  selectedChannels.value = selectedChannels.value.slice(-value)
  await nextTick()
  for (const channel of selectedChannels.value) if (liveSessions.value[channel]) mountPlayer(channel, liveSessions.value[channel])
}

function navigateToPreview(channel?: number) {
  void switchView('preview').then(() => channel ? beginPreview(channel) : undefined)
}

function resetStatsTimer() {
  if (statsTimer !== null) window.clearInterval(statsTimer)
  statsTimer = null
  if (!authenticated.value || !['system-stats', 'device-stats'].includes(activeView.value)) return
  statsTimer = window.setInterval(() => {
    if (activeView.value === 'system-stats') void loadSystemStats()
    if (activeView.value === 'device-stats') void loadDeviceStats()
  }, 15000)
}

watch([activeView, authenticated], resetStatsTimer)
watch([alarmFilter, alarmEventType, alarmChannelFilter, alarmFrom, alarmTo], () => {
  if (authenticated.value && activeView.value === 'alarms') void loadAlarms().catch(error => ElMessage.error(error instanceof Error ? error.message : '报警筛选失败'))
})

function handleHashChange() {
  if (!authenticated.value) showAdminLogin.value = window.location.hash === '#admin'
}

async function maintainLogin() {
  if (!authenticated.value || loggingOut.value) return
  try {
    await refreshSession()
    if (authenticated.value) { await loadCore(); await connectRealtime(); await connectDeviceRealtime() }
  } catch (error) { if (authenticated.value) ElMessage.warning(error instanceof Error ? error.message : '登录续期暂时不可用') }
}

async function reconnectAuthenticatedHubs() {
  await disconnectRealtime()
  if (authenticated.value) { await connectRealtime(); await connectDeviceRealtime() }
}

onMounted(() => {
  const savedSettings = localStorage.getItem('video-platform-ssl-settings')
  if (savedSettings) {
    try { settingsForm.value = { ...settingsForm.value, ...JSON.parse(savedSettings) } } catch { localStorage.removeItem('video-platform-ssl-settings') }
  }
  window.addEventListener('blur', stopPtz)
  window.addEventListener('pointerup', stopPtz)
  window.addEventListener('platform-auth-refreshed', reconnectAuthenticatedHubs)
  authTimer = window.setInterval(maintainLogin, 5 * 60 * 1000)
  window.addEventListener('platform-auth-expired', logout)
  window.addEventListener('hashchange', handleHashChange)
  void loadPublicRelease()
  if (authenticated.value) void maintainLogin()
})
onUnmounted(() => {
  window.removeEventListener('blur', stopPtz)
  window.removeEventListener('pointerup', stopPtz)
  window.removeEventListener('platform-auth-refreshed', reconnectAuthenticatedHubs)
  if (authTimer !== null) window.clearInterval(authTimer)
  window.removeEventListener('platform-auth-expired', logout)
  window.removeEventListener('hashchange', handleHashChange)
  void stopAllPreviews()
  void disconnectRealtime()
  closeAlarmDetail()
  if (statsTimer !== null) window.clearInterval(statsTimer)
  stopLiveRenewal()
  stopPlaybackPolling()
})
</script>

<template>
  <div v-if="!authenticated && !showAdminLogin" class="public-home">
    <header class="public-nav">
      <a class="public-brand" href="#top">
        <span class="brand-mark public-brand-mark"><img src="/brand.png" alt="京华安防平台" /></span>
        <span class="public-brand-copy"><strong>京华安防平台</strong><small>JINGHUA SECURITY PLATFORM</small></span>
      </a>
      <nav class="public-nav-links" aria-label="页面导航">
        <a href="#download">桌面端下载</a>
      </nav>
      <button class="public-admin-link" aria-label="打开管理端" title="管理端入口" @click="openAdminLogin"><Lock /></button>
    </header>

    <main id="top">
      <section class="public-hero">
        <div class="public-hero-copy">
          <p class="public-eyebrow">JINGHUA SECURITY PLATFORM / 01</p>
          <h1>让每一处现场，<br /><em>都被看见。</em></h1>
          <p class="public-hero-lead">连接录像机、视频通道与现场团队，统一完成预览、报警和协作。</p>
          <div class="public-hero-actions">
            <a class="public-primary-action" href="#download"><Download />下载桌面端<ArrowRight /></a>
          </div>
          <div class="public-proof-row"><span><i class="proof-dot"></i>安全会话</span><span><i class="proof-dot"></i>实时流媒体</span><span><i class="proof-dot"></i>分级权限</span></div>
        </div>
        <div class="public-hero-media">
          <img src="https://images.unsplash.com/photo-1557597774-9d273605dfa9?auto=format&fit=crop&w=1400&q=85" alt="安防摄像机现场" loading="eager" referrerpolicy="no-referrer" />
          <span class="hero-media-index">JH / 24</span>
        </div>
      </section>

      <section id="download" class="public-download-section">
        <div class="download-copy"><p class="public-eyebrow">DESKTOP WORKSPACE / 03</p><h2>把现场带到桌面上</h2><p>Windows 桌面端为长时间值守而生，支持多画面预览、录像回放和云台操作，连接平台后即可开始工作。</p><div class="download-platform"><span class="platform-mark">W</span><span><strong>Windows 桌面端</strong><small>Windows 10 / 11 · x64</small></span></div></div>
        <div class="download-panel">
          <div class="download-panel-head"><div><span class="download-label">LATEST RELEASE</span><h3>{{ publicRelease?.version ?? '准备就绪' }}</h3></div><span class="download-status"><i class="proof-dot"></i>{{ publicRelease ? '已发布' : '待发布' }}</span></div>
          <div v-if="publicReleaseLoading" class="download-state">正在检查最新版本…</div>
          <template v-else-if="publicRelease">
            <p class="download-notes">{{ publicRelease.releaseNotes || '包含稳定性优化与现场预览能力更新。' }}</p>
            <a class="download-button" :href="publicDownloadUrl"><Download />下载安装包<ArrowRight /></a>
            <div class="download-meta"><span>{{ formatBytes(publicRelease.fileSize) }}</span><span>SHA-256 {{ publicRelease.sha256.slice(0, 12) }}…</span></div>
          </template>
          <div v-else class="download-state"><strong>暂未发布公开安装包</strong><span>请联系平台管理员获取当前版本。</span></div>
        </div>
      </section>

    </main>

    <footer class="public-footer"><span>© 2026 京华安防平台</span><span>面向现场的安防视频协作平台</span><button class="public-footer-admin" @click="openAdminLogin">内部管理</button></footer>
  </div>

  <div v-else-if="!authenticated" class="login-page">
    <button class="login-back" @click="closeAdminLogin"><ArrowRight />返回首页</button>
    <div class="login-panel">
      <div class="brand-mark"><img src="/brand.png" alt="京华安防平台" /></div>
      <p class="eyebrow">JINGHUA SECURITY PLATFORM</p>
      <h1>管理端登录</h1>
      <p class="login-caption">使用部署时配置的管理员账号进入平台</p>
      <el-form class="login-form" @submit.prevent="submitLogin">
        <el-form-item>
          <el-input v-model="loginForm.username" size="large" placeholder="平台账号" :prefix-icon="Key" autocomplete="username" />
        </el-form-item>
        <el-form-item>
          <el-input v-model="loginForm.password" size="large" type="password" show-password placeholder="密码" :prefix-icon="SwitchButton" autocomplete="current-password" @keyup.enter="submitLogin" />
        </el-form-item>
        <p v-if="loginError" class="form-error">{{ loginError }}</p>
        <el-button class="login-button" type="primary" size="large" native-type="submit" :loading="loginLoading">进入平台</el-button>
      </el-form>
      <div class="login-foot"><span class="status-dot online"></span> 服务端已启用安全会话</div>
    </div>
  </div>

  <div v-else class="app-shell">
    <aside class="sidebar" :class="{ collapsed }">
      <div class="sidebar-head">
        <div class="brand-mark small"><img src="/brand.png" alt="京华安防平台" /></div>
        <div v-if="!collapsed" class="brand-copy"><strong>京华安防平台</strong><span>安全运营中心</span></div>
      </div>
      <nav class="nav-list">
        <p v-if="!collapsed" class="nav-section">工作台</p>
        <button class="nav-parent" :class="{ active: activeView === 'dashboard' }" title="运行总览" @click="switchView('dashboard')"><House /><span v-if="!collapsed">运行总览</span></button>
        <p v-if="!collapsed" class="nav-section">运营处置</p>
        <button :class="{ active: activeView === 'preview' }" title="实时预览" @click="switchView('preview')"><Monitor /><span v-if="!collapsed">实时预览</span><b v-if="!collapsed && sessionCount" class="nav-count">{{ sessionCount }}</b></button>
        <button v-if="canViewPlayback" :class="{ active: activeView === 'playback' }" title="录像回放" @click="switchView('playback')"><VideoPlay /><span v-if="!collapsed">录像回放</span></button>
        <button :class="{ active: activeView === 'alarms' }" title="报警中心" @click="switchView('alarms')"><Warning /><span v-if="!collapsed">报警中心</span><b v-if="!collapsed && newAlarmCount" class="nav-count danger">{{ newAlarmCount }}</b></button>
        <p v-if="!collapsed" class="nav-section">资源管理</p>
        <button :class="{ active: activeView === 'devices' }" title="设备与通道" @click="switchView('devices')"><VideoCamera /><span v-if="!collapsed">设备与通道</span></button>
        <button :class="{ active: activeView === 'business' }" title="业务结构" @click="switchView('business')"><Connection /><span v-if="!collapsed">业务结构</span></button>
        <p v-if="!collapsed" class="nav-section">系统管理</p>
        <button :class="{ active: activeView === 'system-stats' }" title="系统状态" @click="switchView('system-stats')"><DataLine /><span v-if="!collapsed">系统状态</span></button>
        <button :class="{ active: activeView === 'device-stats' }" title="设备统计" @click="switchView('device-stats')"><Camera /><span v-if="!collapsed">设备统计</span></button>
        <button v-if="canManageUsers || canManageRoles" :class="{ active: activeView === 'accounts' }" title="账号与权限" @click="switchView('accounts')"><UserFilled /><span v-if="!collapsed">账号与权限</span></button>
        <button v-if="canManageReleases" :class="{ active: activeView === 'releases' }" title="桌面端版本" @click="switchView('releases')"><Setting /><span v-if="!collapsed">桌面端版本</span></button>
        <button :class="{ active: activeView === 'settings' }" title="连接与安全" @click="switchView('settings')"><Setting /><span v-if="!collapsed">连接与安全</span></button>
      </nav>
      <div class="sidebar-foot">
        <div class="service-state"><span class="status-dot online"></span><span v-if="!collapsed">平台服务正常</span></div>
        <button class="collapse-button" :aria-label="collapsed ? '展开菜单' : '收起菜单'" @click="collapsed = !collapsed"> <Fold v-if="!collapsed" /><Expand v-else /> </button>
      </div>
    </aside>

    <main class="main-area">
      <header class="topbar">
        <div class="topbar-title"><p class="topbar-kicker">管理端 / {{ activeViewTitle }}</p><h2>{{ activeViewTitle }}</h2></div>
        <div class="topbar-actions">
          <span class="service-badge"><i class="status-dot online"></i>{{ device ? '设备在线' : '正在连接' }}</span>
          <el-button class="topbar-icon-button" text circle :icon="Refresh" :loading="refreshing" title="刷新" aria-label="刷新" @click="refreshAll" />
          <span class="topbar-divider"></span>
          <button class="user-chip" title="个人资料" @click="openProfileDialog"><span class="avatar">{{ user?.username?.slice(0, 1).toUpperCase() }}</span><span>{{ user?.displayName || user?.username }}</span></button>
          <el-button class="topbar-icon-button" text circle :icon="SwitchButton" title="退出登录" aria-label="退出登录" @click="logout" />
        </div>
      </header>

      <div v-if="errorText" class="error-banner"><WarningFilled />{{ errorText }}<el-button text @click="loadCore">重试</el-button></div>

      <section v-if="activeView === 'dashboard'" class="content dashboard-view">
        <div class="hero-row">
          <div><p class="section-kicker">{{ new Date().toLocaleDateString('zh-CN') }} · 实时态势</p><h1>早上好，{{ user?.username }}</h1><p class="muted">当前接入 {{ channels.length }} 路视频，{{ newAlarmCount ? `有 ${newAlarmCount} 条报警待处理` : '暂无待处理报警' }}。</p></div>
          <div class="hero-actions"><el-button :icon="Warning" @click="switchView('alarms')">查看报警</el-button><el-button v-if="canViewPlayback" :icon="VideoPlay" @click="switchView('playback')">录像回放</el-button><el-button type="primary" :icon="Monitor" @click="switchView('preview')">进入实时预览</el-button></div>
        </div>
        <div class="metric-grid">
          <button class="metric-card accent-blue" @click="switchView('devices')"><div class="metric-label"><span>接入通道</span><Camera /></div><strong>{{ channels.length || '—' }}</strong><small>查看设备资源 <ArrowRight /></small></button>
          <button class="metric-card accent-green" @click="switchView('device-stats')"><div class="metric-label"><span>在线通道</span><CircleCheckFilled /></div><strong>{{ onlineChannels.length || '—' }}</strong><small>{{ channels.length ? `${Math.round(onlineChannels.length / channels.length * 100)}% 在线率` : '等待同步' }} <ArrowRight /></small></button>
          <button class="metric-card accent-red" @click="switchView('alarms')"><div class="metric-label"><span>待处理报警</span><Bell /></div><strong>{{ newAlarmCount }}</strong><small>{{ newAlarmCount ? '立即进入处置' : '当前状态正常' }} <ArrowRight /></small></button>
          <button class="metric-card accent-amber" @click="switchView('preview')"><div class="metric-label"><span>预览会话</span><VideoPlay /></div><strong>{{ sessionCount }}</strong><small>打开监控画面 <ArrowRight /></small></button>
        </div>
        <div class="dashboard-grid">
          <div class="panel device-panel">
            <div class="panel-head"><div><p class="panel-kicker">核心设备</p><h3>录像机运行状态</h3></div><span class="plain-status" :class="device?.status === 'online' ? 'success' : ''"><i class="status-dot" :class="device?.status === 'online' ? 'online' : 'offline'"></i>{{ device?.status === 'online' ? '在线' : '离线' }}</span></div>
            <div class="device-identity"><div class="device-icon"><DataLine /></div><div><strong>{{ device?.model ?? device?.deviceKey ?? '录像机' }}</strong><span>{{ device?.serialNumber ?? '读取中' }}</span></div></div>
            <div class="spec-list"><div><span>数字通道</span><b>{{ device?.digitalChannels ?? '—' }}</b></div><div><span>存储硬盘</span><b>{{ device?.diskCount ?? '—' }} 块</b></div><div><span>RTSP 输出</span><b>{{ device?.supportsRtsp ? '已启用' : '不可用' }}</b></div><div><span>最近心跳</span><b>{{ device?.lastSeenAt ? formatTime(device.lastSeenAt) : '—' }}</b></div></div>
            <el-button text type="primary" :icon="ArrowRight" @click="switchView('devices')">查看设备详情</el-button>
          </div>
          <div class="panel alarm-panel"><div class="panel-head"><div><p class="panel-kicker">事件队列</p><h3>最近报警</h3></div><el-button text type="primary" :icon="ArrowRight" @click="switchView('alarms')">进入报警中心</el-button></div><div v-if="!alarms.length" class="empty-state"><Check /><strong>当前运行平稳</strong><span>暂无报警记录</span></div><div v-else class="event-list"><div v-for="alarm in alarms.slice(0, 5)" :key="alarm.id" class="event-item"><span class="event-icon" :class="alarm.state"><Warning /></span><div class="event-copy"><strong>{{ alarm.eventType }}</strong><span>{{ alarmChannelNumber(alarm) ? `通道 ${alarmChannelNumber(alarm)} · ` : '' }}{{ formatTime(alarm.occurredAt) }}</span></div><el-tag :type="stateType(alarm.state)" size="small">{{ stateLabel(alarm.state) }}</el-tag><el-button text type="primary" size="small" @click="openAlarmDetail(alarm)">详情</el-button></div></div></div>
        </div>
        <div class="dashboard-lower-grid"><div class="panel channel-summary"><div class="panel-head"><div><p class="panel-kicker">连接质量</p><h3>通道健康度</h3></div><el-button text type="primary" @click="switchView('devices')">管理通道</el-button></div><div class="health-overview"><strong>{{ channels.length ? Math.round(onlineChannels.length / channels.length * 100) : 0 }}%</strong><div><div class="health-bar"><span class="healthy" :style="{ width: `${channels.length ? onlineChannels.length / channels.length * 100 : 0}%` }"></span></div><div class="health-legend"><span><i class="legend-dot healthy"></i>在线 {{ onlineChannels.length }}</span><span><i class="legend-dot offline"></i>离线 {{ Math.max(channels.length - onlineChannels.length, 0) }}</span></div></div></div></div>
        <div class="panel system-summary"><div class="panel-head"><div><p class="panel-kicker">平台资源</p><h3>业务与账号</h3></div><span class="panel-count">实时数据</span></div><div class="summary-grid"><div><span>平台账号</span><strong>{{ stats?.users ?? '—' }}</strong></div><div><span>启用角色</span><strong>{{ stats?.roles ?? '—' }}</strong></div><div><span>车间</span><strong>{{ stats?.workshops ?? '—' }}</strong></div><div><span>区域 / 机组</span><strong>{{ stats ? `${stats.areas} / ${stats.units}` : '—' }}</strong></div></div></div></div>
      </section>

      <section v-else-if="activeView === 'system-stats'" class="content stats-view reference-view">
        <div class="view-toolbar reference-toolbar"><div><h1>系统统计</h1></div><el-button :icon="Refresh" @click="refreshAll">刷新</el-button></div>
        <div class="resource-grid">
          <div class="panel resource-card"><div class="panel-head"><h3>CPU 使用率</h3><DataLine /></div><div class="resource-body cpu-resource-body"><div class="cpu-progress"><el-progress type="circle" :percentage="systemCpuPercent" :width="138" :stroke-width="7" color="#67c23a" /><div class="cpu-loads"><span>1分钟负载 {{ systemStats?.loadAverage.one ?? '—' }}</span><span>5分钟负载 {{ systemStats?.loadAverage.five ?? '—' }}</span><span>15分钟负载 {{ systemStats?.loadAverage.fifteen ?? '—' }}</span></div></div><div class="resource-list"><div><span>核心数：</span><b>{{ systemStats?.processorCount ?? '—' }}</b></div></div></div></div>
          <div class="panel resource-card"><div class="panel-head"><h3>内存使用率</h3><DataLine /></div><div class="resource-body"><el-progress type="circle" :percentage="systemMemoryPercent" :width="138" :stroke-width="7" color="#eba53b" /><div class="resource-list"><div><span>总内存：</span><b>{{ formatBytes(systemStats?.memory.totalBytes) }}</b></div><div><span>已使用：</span><b>{{ formatBytes(systemStats?.memory.usedBytes) }}</b></div><div><span>可用：</span><b>{{ formatBytes(systemStats?.memory.availableBytes) }}</b></div></div></div></div>
          <div class="panel resource-card"><div class="panel-head"><h3>磁盘使用率</h3><DataLine /></div><div class="resource-body"><el-progress type="circle" :percentage="systemDiskPercent" :width="138" :stroke-width="7" color="#eba53b" /><div class="resource-list"><div><span>总容量：</span><b>{{ formatBytes(systemStats?.disk.totalBytes) }}</b></div><div><span>已使用：</span><b>{{ formatBytes(systemStats?.disk.usedBytes) }}</b></div><div><span>可用：</span><b>{{ formatBytes(systemStats?.disk.freeBytes) }}</b></div></div></div></div>
        </div>
        <div class="panel network-card"><div class="panel-head"><h3><Connection /> 网络流量</h3><span class="panel-count">实时速率</span></div><div class="network-grid"><div><span>实时上行</span><strong>{{ formatRate(systemStats?.network.transmittedBytesPerSecond) }}</strong><small>累计 {{ formatBytes(systemStats?.network.transmittedBytes) }}</small></div><div><span>实时下行</span><strong>{{ formatRate(systemStats?.network.receivedBytesPerSecond) }}</strong><small>累计 {{ formatBytes(systemStats?.network.receivedBytes) }}</small></div></div></div>
        <div class="panel system-info-card"><div class="panel-head"><h3>系统信息</h3></div><div class="system-info-grid"><div><span>操作系统：</span><b>{{ systemStats?.osDescription ?? '—' }}</b></div><div><span>系统版本：</span><b>{{ systemStats?.architecture ?? '—' }}</b></div><div><span>运行时间：</span><b>{{ formatDuration(systemStats?.uptimeSeconds) }}</b></div><div><span>服务器时间：</span><b>{{ systemStats?.serverTime ? formatTime(systemStats.serverTime) : '—' }}</b></div></div></div>
      </section>

      <section v-else-if="activeView === 'device-stats'" class="content stats-view reference-view">
        <div class="view-toolbar reference-toolbar"><div><h1>设备统计</h1></div><el-button :icon="Refresh" @click="refreshAll">刷新</el-button></div>
        <div class="device-summary-grid"><div class="panel device-overview"><div class="panel-head"><h3>设备概览</h3><Monitor /></div><div class="device-overview-grid"><div><span>设备总数</span><strong>{{ deviceStats?.deviceCount ?? '—' }}</strong></div><div><span>在线设备</span><strong class="green">{{ deviceStats?.onlineDevices ?? '—' }}</strong></div><div><span>离线设备</span><strong class="red">{{ deviceStats?.offlineDevices ?? '—' }}</strong></div><div><span>心跳超时</span><strong>—</strong></div><div><span>在线率</span><strong>{{ deviceStats?.deviceCount ? `${Math.round(deviceStats.onlineDevices / deviceStats.deviceCount * 100)}%` : '—' }}</strong></div></div></div><div class="panel type-card"><div class="panel-head"><h3>设备类型分布</h3><DataLine /></div><div class="type-row"><span>{{ deviceStats?.model ?? '录像机' }}</span><b>{{ deviceStats?.deviceCount ?? '—' }} 台</b></div><div class="type-track"><span :style="{ width: deviceStats?.deviceCount ? '100%' : '0%' }"></span></div></div><div class="panel channel-card"><div class="panel-head"><h3>通道统计</h3><Monitor /></div><div class="channel-stat-grid"><div><span>总通道数</span><strong>{{ deviceStats?.channels ?? '—' }}</strong></div><div><span>在线通道</span><strong class="green">{{ deviceStats?.onlineChannels ?? '—' }}</strong></div><div><span>已注册通道</span><strong class="red">{{ deviceStats?.offlineChannels ?? '—' }}</strong></div></div></div></div>
        <div class="panel vendor-card"><div class="panel-head"><h3>厂商分布</h3><span class="panel-count">当前设备</span></div><div class="vendor-row"><span>{{ deviceStats?.model?.split(' ')[0] ?? 'Hikvision' }}</span><b>{{ deviceStats?.deviceCount ?? '—' }} 台</b><em>{{ deviceStats?.deviceKey ?? '—' }}</em></div></div>
        <div class="panel activity-card"><div class="panel-head"><h3>最近活动</h3><span class="panel-count">最近同步 {{ deviceStats?.lastSeenAt ? formatTime(deviceStats.lastSeenAt) : '—' }}</span></div><div class="activity-row"><span>设备状态</span><b>{{ deviceStats?.status === 'online' ? '在线' : '离线' }}</b><span>今日报警 {{ deviceStats?.alarmsToday ?? '—' }} 条</span></div></div>
        <div class="panel trend-card"><div class="panel-head"><h3>24小时在线趋势</h3><span class="panel-count">每小时快照</span></div><div v-if="deviceStats?.onlineTrend?.length" class="trend-chart"><div v-for="point in deviceStats.onlineTrend" :key="point.timestamp" class="trend-column"><span class="trend-value">{{ point.online }}</span><i :style="{ height: `${Math.max(8, point.online / deviceTrendMax * 100)}%` }"></i><small>{{ new Date(point.timestamp).getHours().toString().padStart(2, '0') }}:00</small></div></div><el-empty v-else description="暂无趋势数据" /></div>
      </section>

      <section v-show="activeView === 'preview'" class="content preview-view">
        <div class="view-toolbar"><div><p class="section-kicker">LIVE MONITORING / {{ sessionCount }} ACTIVE</p><h1>实时预览</h1></div><div class="toolbar-controls"><div class="layout-switch"><button v-for="option in [1, 4, 9, 16]" :key="option" :class="{ active: layout === option }" :title="`${option} 分屏`" @click="chooseLayout(option)">{{ option }}</button></div><el-button :icon="Connection" @click="toggleStreamType">{{ previewStreamType === 2 ? '子码流' : '主码流' }}</el-button><el-input v-model="search" :prefix-icon="Search" placeholder="搜索通道" clearable style="width: 220px" /><el-button v-if="canViewPlayback" :icon="VideoPlay" @click="openPlayback(focusedChannel ?? undefined)">录像回放</el-button></div></div>
        <div class="preview-workspace">
          <div class="video-wall" :class="`grid-${layout}`">
            <div v-for="(channel, slot) in previewSlots" :key="`${slot}-${channel ?? 'empty'}`" class="video-tile">
              <template v-if="channel">
                <video :ref="el => setVideoRef(channel, el)" muted autoplay playsinline></video>
                <div v-if="previewErrors[channel]" class="tile-placeholder"><Warning /><span>{{ previewErrors[channel] }}</span></div>
                <div class="tile-overlay">
                  <span>CH {{ channel }}</span><strong>{{ channels.find(item => item.channelNumber === channel)?.name || `通道 ${channel}` }}</strong>
                  <button title="重试预览" @click.stop="retryPreview(channel)"><Refresh /></button>
                  <button title="截图" @click.stop="captureVideo(videoRefs.get(channel) ?? null, channel)"><Camera /></button>
                  <button title="全屏" @click.stop="toggleFullscreen(videoRefs.get(channel) ?? null)"><FullScreen /></button>
                  <button title="停止预览" @click.stop="endPreview(channel)"><Delete /></button>
                </div>
              </template>
              <div v-else class="tile-empty"><span>{{ String(slot + 1).padStart(2, '0') }}</span><p>选择通道加入预览</p></div>
            </div>
          </div>
          <div class="channel-picker panel">
            <div class="picker-head"><div><p class="panel-kicker">CHANNEL LIST</p><h3>通道选择</h3></div><span>{{ filteredChannels.length }} 路</span></div>
            <div class="picker-list">
              <button v-for="channel in filteredChannels" :key="channel.channelNumber" class="channel-row" :class="{ selected: selectedChannels.includes(channel.channelNumber), offline: !channel.online }" :disabled="!channel.online" @click="beginPreview(channel.channelNumber)">
                <span class="channel-number">{{ String(channel.channelNumber).padStart(2, '0') }}</span><span class="channel-info"><strong>{{ channel.name || `通道 ${channel.channelNumber}` }}</strong><small>{{ channel.model || '未知型号' }}</small></span><span class="status-dot" :class="channel.online ? 'online' : 'offline'"></span><Check v-if="selectedChannels.includes(channel.channelNumber)" class="selected-icon" />
              </button>
              <el-empty v-if="!filteredChannels.length" description="暂无可用通道" />
            </div>
          </div>
        </div>
        <div v-if="canControlPtz && focusedChannelInfo && channelSupportsPtz(focusedChannelInfo)" class="ptz-panel panel"><div class="panel-head"><div><p class="panel-kicker">PTZ CONTROL</p><h3>云台控制 · CH {{ focusedChannelInfo.channelNumber }}</h3></div><span class="panel-count">按住方向键移动</span></div><div class="ptz-content"><div class="ptz-pad"><button title="向上" @pointerdown="startPtz('up')" @pointerup="stopPtz" @pointerleave="stopPtz" @pointercancel="stopPtz"><ArrowUp /></button><button title="向左" @pointerdown="startPtz('left')" @pointerup="stopPtz" @pointerleave="stopPtz" @pointercancel="stopPtz"><ArrowLeft /></button><button title="向右" @pointerdown="startPtz('right')" @pointerup="stopPtz" @pointerleave="stopPtz" @pointercancel="stopPtz"><ArrowRight /></button><button title="向下" @pointerdown="startPtz('down')" @pointerup="stopPtz" @pointerleave="stopPtz" @pointercancel="stopPtz"><ArrowDown /></button></div><div class="ptz-actions"><el-button :icon="ZoomIn" @pointerdown="startPtz('zoomIn')" @pointerup="stopPtz" @pointerleave="stopPtz" @pointercancel="stopPtz">放大</el-button><el-button :icon="ZoomOut" @pointerdown="startPtz('zoomOut')" @pointerup="stopPtz" @pointerleave="stopPtz" @pointercancel="stopPtz">缩小</el-button><el-input-number v-model="ptzPresetValue" :min="1" :max="300" controls-position="right" placeholder="预置位" /><el-button type="primary" @click="callPtzPreset">调用</el-button></div></div></div>
      </section>

      <section v-if="activeView === 'playback'" class="content playback-view">
        <div class="view-toolbar"><div><p class="section-kicker">RECORDINGS / 7 DAYS</p><h1>录像回放</h1></div><el-button :icon="Refresh" @click="searchPlayback">重新检索</el-button></div>
         <div class="playback-layout"><div class="panel playback-search-panel"><div class="panel-head"><div><p class="panel-kicker">SEARCH</p><h3>选择录像</h3></div><span class="panel-count">{{ recordings.length }} 段</span></div><div class="playback-form"><label>通道<el-select v-model="playbackForm.channel" filterable placeholder="选择通道"><el-option v-for="channel in channels" :key="channel.channelNumber" :label="`CH ${channel.channelNumber} · ${channel.name || '未命名'}`" :value="Number(channel.channelNumber)" /></el-select></label><label>开始时间<input v-model="playbackForm.start" type="datetime-local" /></label><label>结束时间<input v-model="playbackForm.end" type="datetime-local" /></label><el-button type="primary" :loading="recordingLoading" :icon="Search" @click="searchPlayback">检索录像</el-button></div><div v-loading="recordingLoading" class="recording-list"><button v-for="record in recordings" :key="`${record.fileName}-${record.start}`" class="recording-row" @click="playRecording(record)"><span class="recording-time"><strong>{{ formatTime(record.start) }}</strong><small>至 {{ formatTime(record.end) }}</small></span><span class="recording-meta"><span>{{ formatBytes(record.fileSize) }}</span><span>{{ record.fileName }}</span></span><ArrowRight /></button><el-empty v-if="!recordingLoading && !recordings.length" description="请选择通道和时间后检索" /></div></div><div class="panel playback-player-panel"><div class="panel-head"><div><p class="panel-kicker">PLAYER</p><h3>{{ playbackChannelInfo ? `CH ${playbackChannelInfo.channelNumber} · ${playbackChannelInfo.name || '未命名'}` : '录像播放器' }}</h3></div><el-button v-if="playbackSession" text type="danger" @click="stopPlaybackSession(true)">停止回放</el-button></div><div class="playback-screen"><video ref="playbackVideoRef" muted autoplay playsinline></video><div v-if="!playbackSession" class="playback-empty"><VideoPlay /><strong>选择一段录像开始回放</strong><span>回放会话仅在当前页面保留</span></div></div><div v-if="playbackSession" class="playback-controls"><div class="playback-state"><span :class="`state-dot ${playbackSession.timelineState || playbackSession.state}`"></span>{{ (playbackSession.timelineState || playbackSession.state) === 'playing' ? '播放中' : (playbackSession.timelineState || playbackSession.state) === 'gap' ? '无录像，暂停等待' : (playbackSession.timelineState || playbackSession.state) === 'paused' ? '已暂停' : '已完成' }}<span v-if="playbackSession.currentTime" class="playback-current">当前 {{ formatTime(playbackSession.currentTime) }}</span><span class="playback-bytes">已接收 {{ formatBytes(playbackSession.bytes) }}</span></div><el-slider v-model="playbackProgress" :disabled="playbackSession.state === 'completed'" :format-tooltip="(value: number) => `${value}%`" @change="seekCurrentPlayback" /><div class="playback-control-buttons"><el-button circle :icon="VideoPause" :disabled="playbackSession.state !== 'playing'" title="暂停" @click="controlCurrentPlayback('pause')" /><el-button circle :icon="VideoPlay" :disabled="playbackSession.state === 'playing' || playbackSession.state === 'completed'" title="继续" @click="controlCurrentPlayback('resume')" /><el-button :icon="Promotion" @click="controlCurrentPlayback('slow')">慢速</el-button><el-button :icon="Promotion" @click="controlCurrentPlayback('normal')">正常</el-button><el-button :icon="Promotion" @click="controlCurrentPlayback('fast')">加速</el-button></div></div></div></div>
      </section>

        <section v-else-if="activeView === 'alarms'" class="content alarms-view"><div class="view-toolbar"><div><p class="section-kicker">EVENT CENTER / {{ newAlarmCount }} UNACKNOWLEDGED</p><h1>报警中心</h1></div><div class="toolbar-controls alarm-filters"><el-select v-model="alarmFilter" style="width: 140px"><el-option label="全部状态" value="all" /><el-option label="未确认" value="new" /><el-option label="已确认" value="acknowledged" /><el-option label="已恢复" value="resolved" /></el-select><el-input v-model="alarmEventType" clearable placeholder="事件类型" style="width: 150px" /><el-input-number v-model="alarmChannelFilter" :min="1" :max="9999" :controls="false" placeholder="通道" /><input v-model="alarmFrom" type="datetime-local" aria-label="报警开始时间" /><input v-model="alarmTo" type="datetime-local" aria-label="报警结束时间" /><el-button :icon="Refresh" @click="refreshAll">刷新事件</el-button></div></div><div class="panel table-panel"><el-table v-loading="alarmLoading" :data="visibleAlarms" empty-text="暂无报警记录" stripe><el-table-column prop="id" label="编号" width="90" /><el-table-column label="事件类型" min-width="220"><template #default="scope"><span class="alarm-name"><span class="alarm-signal" :class="scope.row.state"></span>{{ scope.row.eventType }}</span></template></el-table-column><el-table-column label="通道" width="110"><template #default="scope">{{ alarmChannelNumber(scope.row) ? `通道 ${alarmChannelNumber(scope.row)}` : '设备级' }}</template></el-table-column><el-table-column label="图片" width="90"><template #default="scope"><el-tag v-if="scope.row.imageAvailable" type="success" size="small">有</el-tag><span v-else>—</span></template></el-table-column><el-table-column label="发生时间" min-width="190"><template #default="scope">{{ formatTime(scope.row.occurredAt) }}</template></el-table-column><el-table-column label="状态" width="120"><template #default="scope"><el-tag :type="stateType(scope.row.state)">{{ stateLabel(scope.row.state) }}</el-tag></template></el-table-column><el-table-column label="操作" width="210" fixed="right"><template #default="scope"><el-button type="primary" link @click="openAlarmDetail(scope.row)">详情</el-button><el-button v-if="scope.row.state === 'new'" type="primary" link @click="acknowledge(scope.row)">确认处理</el-button><el-button type="primary" link @click="navigateToPreview(alarmChannelNumber(scope.row) ?? undefined)">打开监控</el-button></template></el-table-column></el-table></div></section>

        <section v-else-if="activeView === 'devices'" class="content devices-view"><div class="view-toolbar"><div><p class="section-kicker">DEVICE INVENTORY / {{ device ? 1 : 0 }} RECORDER</p><h1>设备与通道</h1></div><el-button :icon="Refresh" @click="refreshAll">同步状态</el-button></div><div class="device-detail-grid"><div class="panel recorder-card"><div class="panel-head"><div><p class="panel-kicker">RECORDER</p><h3>{{ device?.model ?? device?.deviceKey ?? '录像机' }}</h3></div><el-tag :type="device?.status === 'online' ? 'success' : 'info'">{{ device?.status === 'online' ? '在线' : device?.status === 'offline' ? '离线' : '未知' }}</el-tag></div><div class="recorder-serial">{{ device?.serialNumber ?? '—' }}</div><div class="detail-list"><div><span>IP 地址</span><b>{{ device?.ip ?? '—' }}</b></div><div><span>服务端口</span><b>{{ device?.servicePort ?? '—' }}</b></div><div><span>数字通道</span><b>{{ device?.digitalChannels ?? '—' }}</b></div><div><span>报警输入 / 输出</span><b>{{ device?.alarmInputCount ?? '—' }} / {{ device?.alarmOutputCount ?? '—' }}</b></div></div></div><div class="panel channel-table"><div class="panel-head"><div><p class="panel-kicker">CHANNELS</p><h3>通道状态</h3></div><span class="panel-count">{{ channels.length }} 路</span></div><div class="table-toolbar"><el-input v-model="search" :prefix-icon="Search" clearable placeholder="按编号、名称或型号筛选" /></div><el-table :data="filteredChannels" height="430" stripe><el-table-column prop="channelNumber" label="通道" width="90" /><el-table-column prop="name" label="名称" min-width="240" show-overflow-tooltip /><el-table-column prop="model" label="型号" min-width="190" show-overflow-tooltip /><el-table-column label="状态" width="110"><template #default="scope"><span class="table-state"><i class="status-dot" :class="scope.row.online ? 'online' : 'offline'"></i>{{ scope.row.online ? '在线' : '离线' }}</span></template></el-table-column><el-table-column label="操作" width="180"><template #default="scope"><el-button type="primary" link :disabled="!scope.row.online" @click="navigateToPreview(scope.row.channelNumber)">预览</el-button><el-button v-if="canViewPlayback" type="primary" link @click="openPlayback(scope.row.channelNumber)">回放</el-button></template></el-table-column></el-table></div></div></section>

      <section v-else-if="activeView === 'business'" class="content business-view">
        <div class="view-toolbar"><div><p class="section-kicker">BUSINESS TOPOLOGY / 3 LEVELS</p><h1>业务结构</h1></div><el-button v-if="canManageAreas" type="primary" :icon="Plus" @click="openBusinessCreate('workshop')">新增车间</el-button></div>
        <div class="business-grid"><div class="panel tree-panel"><div class="panel-head"><div><p class="panel-kicker">ORGANIZATION</p><h3>车间 / 区域 / 机组</h3></div></div><el-empty v-if="!businessTree.length" description="尚未创建业务结构" /><el-tree v-else class="business-tree" :data="businessTree" node-key="id" default-expand-all :expand-on-click-node="false"><template #default="{ data }"><div class="business-tree-node"><span class="business-tree-label"><strong>{{ data.label }}</strong><small v-if="data.source">{{ data.source[data.type === 'workshop' ? 2 : 3] }}</small><el-tag v-if="data.source && String(data.source[data.type === 'workshop' ? 3 : 4]) === 'disabled'" size="small" type="info">停用</el-tag></span><span v-if="canManageAreas" class="business-tree-actions"><el-button v-if="data.type !== 'unit'" text :icon="Plus" :title="data.type === 'workshop' ? '新增区域' : '新增机组'" @click="openBusinessCreate(data.type === 'workshop' ? 'area' : 'unit', Number(data.source?.[0]))" /><el-button text :icon="Edit" title="编辑" @click="openBusinessEdit(data.type!, data.source!)" /><el-button text type="danger" :icon="Delete" title="删除" @click="deleteBusinessNode(data.type!, data.source!)" /></span></div></template></el-tree></div><div class="panel unassigned-panel"><div class="panel-head"><div><p class="panel-kicker">UNASSIGNED CHANNELS</p><h3>待分配通道</h3></div><span class="panel-count">{{ unassigned.length }} 路</span></div><el-table :data="unassigned" height="480" stripe empty-text="所有通道已分配"><el-table-column label="编号" width="100"><template #default="scope">CH {{ scope.row[2] }}</template></el-table-column><el-table-column label="名称" min-width="220"><template #default="scope">{{ scope.row[3] || `通道 ${scope.row[2]}` }}</template></el-table-column><el-table-column label="型号" min-width="180"><template #default="scope">{{ scope.row[4] || '—' }}</template></el-table-column><el-table-column label="状态" width="100"><template #default="scope"><span class="table-state"><i class="status-dot" :class="scope.row[5] === 'online' ? 'online' : 'offline'"></i>{{ scope.row[5] === 'online' ? '在线' : '离线' }}</span></template></el-table-column><el-table-column v-if="canAssignChannels" label="分配到机组" min-width="150"><template #default="scope"><el-select size="small" placeholder="选择机组" @change="assignChannelToUnit(scope.row, $event)"><el-option v-for="row in units.filter(item => item[4] === 'active')" :key="String(row[0])" :label="String(row[2])" :value="Number(row[0])" /></el-select></template></el-table-column></el-table></div></div>
      </section>

      <section v-else-if="activeView === 'releases'" class="content releases-view">
        <div class="view-toolbar"><div><p class="section-kicker">DESKTOP RELEASES / {{ releases.length }} VERSIONS</p><h1>桌面端版本</h1></div><el-button type="primary" :icon="Plus" @click="releaseDialog = true">上传安装包</el-button></div>
        <div class="panel table-panel"><el-table v-loading="releaseLoading" :data="releases" stripe empty-text="暂无版本"><el-table-column prop="version" label="版本" width="130" /><el-table-column prop="fileName" label="安装包" min-width="220" show-overflow-tooltip /><el-table-column label="状态" width="110"><template #default="scope"><el-tag :type="scope.row.status === 'published' ? 'success' : scope.row.status === 'revoked' ? 'info' : 'warning'">{{ scope.row.status === 'published' ? '已发布' : scope.row.status === 'revoked' ? '已撤回' : '草稿' }}</el-tag></template></el-table-column><el-table-column label="强制更新" width="100"><template #default="scope">{{ scope.row.forceUpdate ? '是' : '否' }}</template></el-table-column><el-table-column label="下载次数" width="100" prop="downloadCount" /><el-table-column label="SHA-256" min-width="190" show-overflow-tooltip><template #default="scope">{{ scope.row.sha256 }}</template></el-table-column><el-table-column label="操作" width="160" fixed="right"><template #default="scope"><el-button v-if="scope.row.status === 'draft'" type="primary" link @click="publishRelease(scope.row)">发布</el-button><el-button v-if="scope.row.status === 'published'" type="warning" link @click="revokeRelease(scope.row)">撤回</el-button></template></el-table-column></el-table></div>
      </section>

      <section v-else-if="activeView === 'settings'" class="content settings-view">
        <div class="view-toolbar"><div><p class="section-kicker">CONNECTION & SECURITY</p><h1>连接与安全</h1></div><el-button :icon="Refresh" @click="resetSettings">恢复默认</el-button></div>
        <div class="settings-grid">
          <div class="panel settings-card"><div class="panel-head"><div><p class="panel-kicker">PLATFORM URL</p><h3>网址配置</h3></div></div><el-form label-position="top" @submit.prevent="saveSettings"><el-form-item label="平台 API 地址"><el-input v-model="settingsForm.apiBase" placeholder="https://10.37.200.74" /></el-form-item><p class="settings-help">填写 Web 端访问的平台 API 地址。保存后，登录续期、设备数据和报警图片都会使用该地址。</p><div class="settings-actions"><el-button type="primary" @click="saveSettings">保存网址</el-button></div></el-form></div>
          <div class="panel settings-card"><div class="panel-head"><div><p class="panel-kicker">TLS / SSL</p><h3>SSL 配置</h3></div><el-tag :type="settingsForm.sslEnabled ? 'success' : 'info'">{{ settingsForm.sslEnabled ? '已启用' : '未启用' }}</el-tag></div><el-form label-position="top" @submit.prevent="saveSettings"><el-form-item label="启用 HTTPS"><el-switch v-model="settingsForm.sslEnabled" /></el-form-item><el-form-item label="证书域名或 IP"><el-input v-model="settingsForm.sslDomain" placeholder="video.example.com 或 10.37.200.74" /></el-form-item><el-form-item label="证书公钥路径"><el-input v-model="settingsForm.certificatePath" /></el-form-item><el-form-item label="证书私钥路径"><el-input v-model="settingsForm.keyPath" /></el-form-item><p class="settings-help">此页保存部署参数，不会直接修改远程 Nginx。替换证书后执行：sudo nginx -t && sudo systemctl reload nginx</p><div class="settings-actions"><el-button type="primary" @click="saveSettings">保存 SSL 配置</el-button></div></el-form></div>
        </div>
        <el-alert :title="currentProtocol === 'https:' ? `当前页面已通过 HTTPS 访问（${currentHost}）` : `当前页面通过 HTTP 访问（${currentHost}），生产环境建议启用 HTTPS`" :type="currentProtocol === 'https:' ? 'success' : 'warning'" :closable="false" show-icon />
      </section>

      <section v-if="activeView === 'accounts'" class="content accounts-view">
        <div class="view-toolbar"><div><p class="section-kicker">ACCESS CONTROL / {{ managedUsers.length }} ACCOUNTS</p><h1>账号与权限</h1></div><div class="toolbar-controls"><el-button v-if="canManageRoles" type="primary" plain :icon="Plus" @click="openRoleCreate">新增角色</el-button><el-button v-if="canManageUsers" type="primary" :icon="Plus" @click="openAccountCreate">新增账号</el-button></div></div>
        <div class="account-grid">
          <div v-if="canManageUsers" class="panel account-table"><div class="panel-head"><div><p class="panel-kicker">USER DIRECTORY</p><h3>平台账号</h3></div><span class="panel-count">{{ managedUsers.length }} 个</span></div><el-table v-loading="accountLoading" :data="managedUsers" stripe empty-text="暂无账号"><el-table-column prop="username" label="账号" min-width="140" /><el-table-column prop="displayName" label="姓名" min-width="120"><template #default="scope">{{ scope.row.displayName || '—' }}</template></el-table-column><el-table-column prop="phone" label="手机号" min-width="140"><template #default="scope">{{ scope.row.phone || '—' }}</template></el-table-column><el-table-column v-if="canManageRoles" label="角色" min-width="150"><template #default="scope"><el-select :model-value="scope.row.roleIds[0] ?? null" size="small" clearable placeholder="未分配" @change="changeUserRole(scope.row, $event)"><el-option v-for="role in roles" :key="role.id" :label="role.name" :value="role.id" /></el-select></template></el-table-column><el-table-column label="状态" width="100"><template #default="scope"><el-tag :type="scope.row.status === 'active' ? 'success' : scope.row.status === 'locked' ? 'danger' : 'info'">{{ scope.row.status === 'active' ? '正常' : scope.row.status === 'locked' ? '已锁定' : '已停用' }}</el-tag></template></el-table-column><el-table-column label="最近登录" min-width="170"><template #default="scope">{{ scope.row.lastLoginAt ? formatTime(scope.row.lastLoginAt) : '从未登录' }}</template></el-table-column><el-table-column label="操作" width="390" fixed="right"><template #default="scope"><el-button type="primary" link @click="openAccountEdit(scope.row)">编辑</el-button><el-button type="primary" link @click="toggleUserStatus(scope.row)">{{ scope.row.status === 'active' ? '停用' : '启用' }}</el-button><el-button v-if="scope.row.status === 'active'" type="warning" link @click="lockUser(scope.row)">锁定</el-button><el-button type="primary" link @click="resetUserPassword(scope.row)">重置密码</el-button><el-button v-if="canManageUsers" type="primary" link @click="openScopeDialog('user', scope.row.id, scope.row.username)">数据范围</el-button></template></el-table-column></el-table></div>
          <div v-if="canManageRoles" class="panel role-table"><div class="panel-head"><div><p class="panel-kicker">ROLE CATALOG</p><h3>角色与授权</h3></div><span class="panel-count">{{ roles.length }} 个</span></div><el-table v-loading="accountLoading" :data="roles" stripe empty-text="暂无角色"><el-table-column prop="name" label="角色名称" min-width="130" /><el-table-column prop="code" label="编码" min-width="110" /><el-table-column label="状态" width="90"><template #default="scope"><el-tag :type="scope.row.status === 'active' ? 'success' : 'info'">{{ scope.row.status === 'active' ? '启用' : '停用' }}</el-tag></template></el-table-column><el-table-column prop="userCount" label="账号数" width="80" /><el-table-column label="权限与范围" min-width="230"><template #default="scope"><el-button type="primary" link @click="openPermissionDialog(scope.row)">权限（{{ scope.row.permissionCodes.length }}）</el-button><el-button type="primary" link @click="openScopeDialog('role', scope.row.id, scope.row.name)">数据范围</el-button><el-button type="primary" link @click="openRoleEdit(scope.row)">编辑</el-button><el-button type="danger" link :disabled="scope.row.userCount > 0" @click="removeRole(scope.row)">删除</el-button></template></el-table-column></el-table></div>
        </div>
        <el-empty v-if="!canManageUsers && !canManageRoles" description="当前账号没有账号管理权限" />
      </section>
    </main>
  </div>

  <el-dialog v-model="profileDialog" title="个人资料" width="460px" destroy-on-close>
    <el-form label-position="top" @submit.prevent="saveProfile">
      <el-form-item label="账号"><el-input :model-value="user?.username" disabled /></el-form-item>
      <el-form-item label="姓名"><el-input v-model="profileForm.displayName" maxlength="128" /></el-form-item>
      <el-form-item label="手机号"><el-input v-model="profileForm.phone" maxlength="32" /></el-form-item>
      <el-divider content-position="left">修改密码（可选）</el-divider>
      <el-form-item label="当前密码"><el-input v-model="profileForm.currentPassword" type="password" show-password autocomplete="current-password" /></el-form-item>
      <el-form-item label="新密码"><el-input v-model="profileForm.newPassword" type="password" show-password autocomplete="new-password" /></el-form-item>
      <div class="dialog-actions"><el-button @click="profileDialog = false">取消</el-button><el-button type="primary" :loading="profileLoading" @click="saveProfile">保存资料</el-button></div>
    </el-form>
  </el-dialog>

  <el-dialog v-model="accountDialog" :title="accountForm.id === null ? '新增平台账号' : '编辑平台账号'" width="460px" destroy-on-close>
    <el-form label-position="top" @submit.prevent="saveAccount">
      <el-form-item label="账号"><el-input v-model="accountForm.username" autocomplete="off" :disabled="accountForm.id === user?.id" /></el-form-item>
      <el-form-item label="姓名"><el-input v-model="accountForm.displayName" maxlength="128" /></el-form-item>
      <el-form-item label="手机号"><el-input v-model="accountForm.phone" maxlength="32" /></el-form-item>
      <el-form-item :label="accountForm.id === null ? '初始密码' : '重置密码（可选）'"><el-input v-model="accountForm.password" type="password" show-password autocomplete="new-password" /></el-form-item>
      <el-form-item v-if="accountForm.id !== null" label="状态"><el-select v-model="accountForm.status" style="width: 100%" :disabled="accountForm.id === user?.id"><el-option label="正常" value="active" /><el-option label="停用" value="disabled" /><el-option label="锁定" value="locked" /></el-select></el-form-item>
      <el-form-item v-if="canManageRoles" label="角色"><el-select v-model="accountForm.roleId" clearable style="width: 100%" placeholder="选择角色"><el-option v-for="role in roles.filter(item => item.status === 'active')" :key="role.id" :label="role.name" :value="role.id" /></el-select></el-form-item>
      <div class="dialog-actions"><el-button @click="accountDialog = false">取消</el-button><el-button type="primary" @click="saveAccount">保存账号</el-button></div>
    </el-form>
  </el-dialog>

  <el-dialog v-model="roleDialog" :title="roleForm.id === null ? '新增角色' : '编辑角色'" width="420px" destroy-on-close>
    <el-form label-position="top" @submit.prevent="saveRole">
      <el-form-item label="角色名称"><el-input v-model="roleForm.name" maxlength="64" /></el-form-item>
      <el-form-item label="角色编码"><el-input v-model="roleForm.code" maxlength="64" /></el-form-item>
      <el-form-item label="状态"><el-select v-model="roleForm.status" style="width: 100%"><el-option label="启用" value="active" /><el-option label="停用" value="disabled" /></el-select></el-form-item>
      <div class="dialog-actions"><el-button @click="roleDialog = false">取消</el-button><el-button type="primary" :loading="roleLoading" @click="saveRole">保存角色</el-button></div>
    </el-form>
  </el-dialog>

  <el-dialog v-model="businessDialog" :title="businessDialogTitle" width="420px" destroy-on-close>
    <el-form label-position="top" @submit.prevent="saveBusinessNode">
      <el-form-item label="名称"><el-input v-model="businessForm.name" autocomplete="off" /></el-form-item>
      <el-form-item label="编码"><el-input v-model="businessForm.code" autocomplete="off" /></el-form-item>
      <el-form-item v-if="businessForm.type === 'area'" label="所属车间"><el-select v-model="businessForm.parentId" style="width: 100%" placeholder="选择车间"><el-option v-for="row in workshops" :key="String(row[0])" :label="String(row[1])" :value="Number(row[0])" /></el-select></el-form-item>
      <el-form-item v-if="businessForm.type === 'unit'" label="所属区域"><el-select v-model="businessForm.parentId" style="width: 100%" placeholder="选择区域"><el-option v-for="row in areas" :key="String(row[0])" :label="String(row[2])" :value="Number(row[0])" /></el-select></el-form-item>
      <el-form-item v-if="businessForm.id" label="状态"><el-select v-model="businessForm.status" style="width: 100%"><el-option label="启用" value="active" /><el-option label="停用" value="disabled" /></el-select></el-form-item>
      <div class="dialog-actions"><el-button @click="businessDialog = false">取消</el-button><el-button type="primary" @click="saveBusinessNode">保存</el-button></div>
    </el-form>
  </el-dialog>

  <el-dialog v-model="permissionDialog" :title="`配置${permissionRole?.name ?? ''}权限`" width="560px" destroy-on-close>
    <el-tree ref="permissionTreeRef" class="authorization-tree" :data="permissionTree" node-key="id" show-checkbox default-expand-all :default-checked-keys="permissionCodes" :key="permissionRole?.id ?? 0" />
    <div class="dialog-actions"><el-button @click="permissionDialog = false">取消</el-button><el-button type="primary" @click="saveRolePermissions">保存权限</el-button></div>
  </el-dialog>

  <el-dialog v-model="scopeDialog" :title="scopeDialogTitle" width="560px" destroy-on-close>
    <div v-loading="scopeLoading" class="scope-picker"><el-tree ref="scopeTreeRef" class="authorization-tree" :data="scopeTree" node-key="id" show-checkbox default-expand-all :default-checked-keys="selectedScopeKeys" :key="`${scopeTarget?.target ?? ''}-${scopeTarget?.id ?? 0}`" /><el-empty v-if="!scopeTree.length" description="暂无可授权的业务范围" /></div>
    <div class="dialog-actions"><el-button @click="scopeDialog = false">取消</el-button><el-button type="primary" :loading="scopeLoading" @click="saveScopes">保存范围</el-button></div>
  </el-dialog>

  <el-dialog v-model="alarmDetailDialog" title="报警详情" width="620px" destroy-on-close @closed="closeAlarmDetail">
    <div v-loading="alarmDetailLoading" class="alarm-detail-dialog">
      <template v-if="alarmDetail">
        <div class="alarm-detail-summary"><div><span class="panel-kicker">{{ alarmDetail.eventType }}</span><strong>{{ formatTime(alarmDetail.occurredAt) }}</strong></div><el-tag :type="stateType(alarmDetail.state)">{{ stateLabel(alarmDetail.state) }}</el-tag></div>
        <div class="alarm-detail-grid"><div><span>关联通道</span><b>{{ alarmChannelNumber(alarmDetail) ? `通道 ${alarmChannelNumber(alarmDetail)}` : '设备级' }}</b></div><div><span>图片大小</span><b>{{ alarmDetail.imageAvailable ? formatBytes(alarmDetail.imageLength) : '无图片' }}</b></div></div>
        <img v-if="alarmImageUrl" class="alarm-detail-image" :src="alarmImageUrl" alt="报警现场图片" /><pre class="alarm-payload">{{ alarmDetail.payload }}</pre>
      </template>
    </div>
  </el-dialog>

  <el-dialog v-model="releaseDialog" title="上传桌面端安装包" width="520px" destroy-on-close>
    <el-form label-position="top" @submit.prevent="submitRelease">
      <el-form-item label="安装包"><input type="file" accept=".exe,.msi,.zip" @change="chooseReleaseFile" /><span v-if="releaseFile" class="muted release-file-name">{{ releaseFile.name }}</span></el-form-item>
      <el-form-item label="版本号"><el-input v-model="releaseForm.version" placeholder="例如 1.0.0" /></el-form-item>
      <el-form-item label="最低支持版本"><el-input v-model="releaseForm.minimumVersion" placeholder="可选" /></el-form-item>
      <el-form-item label="更新说明"><el-input v-model="releaseForm.releaseNotes" type="textarea" :rows="4" maxlength="4096" show-word-limit /></el-form-item>
      <el-checkbox v-model="releaseForm.forceUpdate">强制更新</el-checkbox>
      <div class="dialog-actions"><el-button @click="releaseDialog = false">取消</el-button><el-button type="primary" :loading="releaseLoading" @click="submitRelease">上传</el-button></div>
    </el-form>
  </el-dialog>
</template>
