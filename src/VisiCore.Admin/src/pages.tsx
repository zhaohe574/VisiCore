import { Fragment, useEffect, useMemo, useState, type FormEvent } from 'react'
import { AlertCircle, BellRing, Camera, Cpu, Download, FileDown, KeyRound, PackagePlus, Pencil, Plus, Power, RefreshCw, RotateCw, Send, Server, ShieldCheck, Trash2, UserRoundCog, X } from 'lucide-react'
import { api, connectivityLabel, formatTime } from './api'
import type { AlertIncident, AlertRule, AuditLog, Camera as CameraType, DeadLetter, DeviceCredential, DevicePlugin, DeviceWorker, DeviceWorkerOperationStatus, EdgeAgent, ExportCamera, Id, NotificationChannel, NotificationDelivery, PlaybackExport, RecorderResponse, Region, Role, User } from './types'
import { Badge, Button, Dialog, EmptyState, ErrorState, Field, Form, Input, LoadingState, PageHeader, Panel, Select, StatusBadge, Textarea } from './ui'
import { useResource } from './features/shared/use-resource'

type Notify = (message: string, tone?: 'good' | 'bad') => void

const systemPermissionOptions = [
  [1, '资产管理'],
  [2, '边缘节点'],
  [4, '通知配置'],
  [8, '运维处置'],
  [16, '审计查看'],
  [32, '录像导出'],
  [64, '设备凭据']
] as const

function systemPermissionLabels(value: number) {
  return systemPermissionOptions.filter(([flag]) => (value & flag) !== 0).map(([, label]) => label)
}

function readSystemPermissions(form: FormData) {
  return form.getAll('systemPermissions').reduce((value, current) => value | Number(current), 0)
}

function connectivityTone(value: number | string): 'good' | 'bad' | 'warn' | 'neutral' {
  const label = connectivityLabel(value)
  return label === '在线' ? 'good' : label === '离线' ? 'bad' : label.includes('疑似') || label.includes('恢复') ? 'warn' : 'neutral'
}

function clockSynchronization(value: number | string): { label: string; tone: 'good' | 'bad' | 'neutral' } {
  const key = String(value)
  if (key === '1' || key === 'Synchronized') return { label: '已同步', tone: 'good' }
  if (key === '2' || key === 'Drifted') return { label: '偏差', tone: 'bad' }
  return { label: '未检测', tone: 'neutral' }
}

function clockOffsetLabel(value: number | null) {
  if (value === null) return '—'
  const seconds = value / 1000
  return `${seconds > 0 ? '+' : ''}${Math.abs(seconds) < 1 ? seconds.toFixed(3) : seconds.toFixed(1)} 秒`
}

function incidentTypeLabel(value: string) { return value === 'clock_skew' ? '时钟偏差' : '离线' }

const operationLabels: Record<string, string> = {
  'onvif.recording-search': '检索',
  'onvif.playback-relay': '回放',
  'onvif.ptz': 'PTZ',
  'plugin.recording-search': '检索',
  'plugin.playback-relay': '回放',
  'plugin.playback-export': '导出',
  'plugin.ptz': 'PTZ'
}

function operationTypeForRecorder(runtimeType: string | null, adapterType: string, capability: 'search' | 'playback' | 'export' | 'ptz') {
  if (runtimeType === 'onvif') {
    return capability === 'search' ? 'onvif.recording-search' : capability === 'playback' ? 'onvif.playback-relay' : capability === 'ptz' ? 'onvif.ptz' : null
  }
  if (runtimeType === 'external-edge') {
    return capability === 'search' ? 'plugin.recording-search' : capability === 'playback' ? 'plugin.playback-relay' : capability === 'export' ? 'plugin.playback-export' : 'plugin.ptz'
  }
  return null
}

function operationFailureLabel(value: string | null) {
  if (!value) return ''
  const labels: Record<string, string> = {
    disabled_by_policy: '策略关闭',
    validation_required: '待设备验收',
    configuration_invalid: '配置无效',
    camera_missing: '缺少摄像头',
    unsupported_plugin: '插件不支持'
  }
  return labels[value] ?? value
}

function operationBadge(status: DeviceWorkerOperationStatus | undefined) {
  if (!status) return <Badge tone="neutral">未上报</Badge>
  if (status.isEffective) return <Badge tone="good">可用</Badge>
  if (!status.isReady) return <Badge tone="bad">{operationFailureLabel(status.failureKind) || '不可用'}</Badge>
  if (status.workerDisabledAt) return <Badge tone="neutral">节点停用</Badge>
  return <Badge tone="warn">状态过期</Badge>
}

function isActiveEdgeAgent(agent: EdgeAgent) {
  if (agent.disabledAt) return false
  const status = String(agent.status ?? '').trim().toLowerCase()
  return !['disabled', 'inactive', 'offline', 'failed', 'error', 'revoked', 'stopped'].includes(status)
}

function isActiveCredential(credential: DeviceCredential) {
  if (credential.disabledAt) return false
  const status = String(credential.status ?? '').trim().toLowerCase()
  return !['disabled', 'inactive', 'revoked', 'deleted'].includes(status)
}

function credentialVersionLabel(credential: DeviceCredential) {
  return credential.version ?? credential.keyVersion ?? '未标记版本'
}

export function OverviewPage({ notify }: { notify: Notify }) {
  const resource = useResource(async () => {
    const [cameras, recorders, workers, incidents, deliveries] = await Promise.all([
      api.get<CameraType[]>('/api/v1/admin/cameras'), api.get<RecorderResponse[]>('/api/v1/admin/recorders'),
      api.get<DeviceWorker[]>('/api/v1/admin/device-workers'), api.get<AlertIncident[]>('/api/v1/admin/alert-incidents?openOnly=true'),
      api.get<NotificationDelivery[]>('/api/v1/admin/notification-deliveries')
    ])
    return { cameras, recorders, workers, incidents, deliveries }
  }, [])
  if (resource.loading) return <LoadingState />
  if (resource.error || !resource.data) return <ErrorState message={resource.error} retry={resource.refresh} />
  const { cameras, recorders, workers, incidents, deliveries } = resource.data
  const offline = cameras.filter(item => connectivityLabel(item.connectivity) === '离线').length
  const onlineWorkers = workers.filter(item => !item.disabledAt && item.lastSeenAt && Date.now() - new Date(item.lastSeenAt).getTime() < 180_000).length
  const failedDeliveries = deliveries.filter(item => ['Failed', 'DeadLettered', '2', '3'].includes(String(item.status))).length
  return <>
    <PageHeader title="运行总览" description="设备、边缘节点和通知链路的当前状态。" actions={<Button variant="secondary" icon={<RefreshCw size={16} />} onClick={() => void resource.refresh()}>刷新</Button>} />
    <div className="metric-grid">
      <article className="metric"><Camera size={18} /><span>摄像头</span><strong>{cameras.length}</strong><small>{offline} 路确认离线</small></article>
      <article className="metric"><Server size={18} /><span>接入设备</span><strong>{recorders.length}</strong><small>{recorders.filter(item => connectivityLabel(item.recorder.connectivity) !== '在线').length} 台待确认</small></article>
      <article className="metric"><Cpu size={18} /><span>设备 Worker</span><strong>{onlineWorkers}/{workers.length}</strong><small>三分钟内有心跳</small></article>
      <article className="metric metric--alert"><BellRing size={18} /><span>未恢复事件</span><strong>{incidents.length}</strong><small>{failedDeliveries} 条投递失败</small></article>
    </div>
    <div className="split-grid">
      <Panel title="未恢复事件" actions={<Badge tone={incidents.length ? 'bad' : 'good'}>{incidents.length}</Badge>}>
        {incidents.length === 0 ? <EmptyState title="当前没有未恢复事件" /> : <div className="compact-list">{incidents.slice(0, 8).map(item => <div className="compact-row" key={item.id}><span className="status-dot status-dot--bad" /><div><strong>{item.resourceName}</strong><small>{item.resourceType} · {formatTime(item.openedAt)}</small></div><Badge tone="bad">{incidentTypeLabel(item.incidentType)}</Badge></div>)}</div>}
      </Panel>
      <Panel title="边缘节点">
        {workers.length === 0 ? <EmptyState title="尚未登记 Worker" /> : <div className="compact-list">{workers.map(item => {
          const active = !item.disabledAt && !!item.lastSeenAt && Date.now() - new Date(item.lastSeenAt).getTime() < 180_000
          return <div className="compact-row" key={item.id}><span className={`status-dot ${active ? 'status-dot--good' : 'status-dot--neutral'}`} /><div><strong>{item.name}</strong><small>{item.assignments.length} 台接入设备 · {formatTime(item.lastSeenAt)}</small></div><Badge tone={active ? 'good' : 'neutral'}>{active ? '在线' : item.disabledAt ? '已停用' : '未连接'}</Badge></div>
        })}</div>}
      </Panel>
    </div>
  </>
}

type AssetTab = 'cameras' | 'recorders' | 'regions'

const deviceKindOptions = [
  ['camera', '摄像头'],
  ['recorder', '录像机'],
  ['matrix', '录像矩阵'],
  ['encoder', '编码器'],
  ['decoder', '解码器'],
  ['gateway', '视频网关'],
  ['other', '其他设备']
] as const

const protocolNames = ['Rtsp', 'Onvif'] as const

function protocolName(value: number | string) {
  if (typeof value !== 'number') return String(value)
  return protocolNames[value] ?? `未知协议（${value}）`
}

function protocolValue(value: string) {
  const index = protocolNames.findIndex(item => item.toLowerCase() === value.toLowerCase())
  if (index < 0) throw new Error(`当前管理后台不支持端点协议“${value}”。`)
  return index
}

function deviceKindLabel(value: string) {
  return deviceKindOptions.find(([key]) => key === value)?.[1] ?? value
}

function parseStreamMap(value: string) {
  try {
    const map = JSON.parse(value) as { main?: string | number; sub?: string | number }
    return { main: String(map.main ?? ''), sub: String(map.sub ?? '') }
  } catch {
    return { main: '', sub: '' }
  }
}

function mappingValue(value: FormDataEntryValue | null) {
  const text = String(value ?? '').trim()
  return /^\d+$/.test(text) ? Number(text) : text
}

function defaultChannelStreamMap(channel: string | number) {
  const inputChannel = Number(channel)
  if (!Number.isSafeInteger(inputChannel) || inputChannel < 1 || inputChannel > 21_474_836) {
    return { main: '', sub: '' }
  }
  return { main: String(inputChannel * 100 + 1), sub: String(inputChannel * 100 + 2) }
}

function hasCustomChannelStreamMap(map: { main: string; sub: string }, channel: string | number) {
  const defaults = defaultChannelStreamMap(channel)
  return map.main !== defaults.main || map.sub !== defaults.sub
}

function streamHostLabel(value: string | null) {
  if (!value) return '地址未登记'
  try { return new URL(value).host } catch { return '地址格式异常' }
}

function hasEmbeddedUrlCredentials(value: string) {
  if (!value.trim()) return false
  try {
    const url = new URL(value)
    return !!url.username || !!url.password
  } catch {
    return false
  }
}

export function AssetsPage({ notify }: { notify: Notify }) {
  const [tab, setTab] = useState<AssetTab>('cameras')
  const [query, setQuery] = useState('')
  const [dialog, setDialog] = useState<'camera' | 'recorder' | 'region' | null>(null)
  const [editingCamera, setEditingCamera] = useState<CameraType | null>(null)
  const [editingRecorder, setEditingRecorder] = useState<RecorderResponse | null>(null)
  const [editingRegion, setEditingRegion] = useState<Region | null>(null)
  const resource = useResource(async () => {
    const [cameras, recorders, regions, plugins, edgeAgents, credentials] = await Promise.all([
      api.get<CameraType[]>('/api/v1/admin/cameras'),
      api.get<RecorderResponse[]>('/api/v1/admin/recorders'),
      api.get<Region[]>('/api/v1/admin/regions'),
      api.get<DevicePlugin[]>('/api/v1/admin/device-plugins'),
      api.get<EdgeAgent[]>('/api/v1/admin/edge-agents'),
      api.get<DeviceCredential[]>('/api/v1/admin/device-credentials')
    ])
    return { cameras, recorders, regions, plugins, edgeAgents, credentials }
  }, [])

  const closeDialog = () => { setDialog(null); setEditingCamera(null); setEditingRecorder(null); setEditingRegion(null) }
  const saved = async (message: string) => { closeDialog(); notify(message); await resource.refresh() }
  if (resource.loading) return <LoadingState />
  if (resource.error || !resource.data) return <ErrorState message={resource.error} retry={resource.refresh} />
  const { cameras, recorders, regions, plugins, edgeAgents, credentials } = resource.data
  const q = query.trim().toLowerCase()
  const visibleCameras = cameras.filter(item => !q || `${item.code} ${item.alias}`.toLowerCase().includes(q))
  const directDeviceIds = new Set(cameras.filter(item => item.sourceType === 'direct').map(item => item.recorderId))
  const managedRecorders = recorders.filter(item => !directDeviceIds.has(item.recorder.id))
  const visibleRecorders = managedRecorders.filter(item => !q || `${item.recorder.code} ${item.recorder.name} ${item.recorder.vendor}`.toLowerCase().includes(q))
  const visibleRegions = regions.filter(item => !q || `${item.code} ${item.name}`.toLowerCase().includes(q))
  const pluginById = new Map(plugins.map(item => [item.id, item]))
  return <>
    <PageHeader title="资产与区域" description="维护业务编号、别名、区域归属和多协议设备端点。" actions={<Button icon={<Plus size={16} />} onClick={() => setDialog(tab === 'cameras' ? 'camera' : tab === 'recorders' ? 'recorder' : 'region')}>新增</Button>} />
    <div className="toolbar"><div className="tabs"><button className={tab === 'cameras' ? 'active' : ''} onClick={() => setTab('cameras')}>摄像头 <span>{cameras.length}</span></button><button className={tab === 'recorders' ? 'active' : ''} onClick={() => setTab('recorders')}>接入设备 <span>{managedRecorders.length}</span></button><button className={tab === 'regions' ? 'active' : ''} onClick={() => setTab('regions')}>区域 <span>{regions.length}</span></button></div><Input className="input search-input" placeholder="搜索编号或名称" value={query} onChange={event => setQuery(event.target.value)} /></div>
    <Panel className="table-panel">
      {tab === 'cameras' && (visibleCameras.length === 0 ? <EmptyState title="没有匹配的摄像头" /> : <div className="table-scroll"><table><thead><tr><th>状态</th><th>编号 / 别名</th><th>区域</th><th>接入来源</th><th>厂商 / 型号</th><th>PTZ</th><th>最近检测</th><th /></tr></thead><tbody>{visibleCameras.map(item => {
        const parent = recorders.find(recorder => recorder.recorder.id === item.recorderId)
        return <tr key={item.id}><td><Badge tone={connectivityTone(item.connectivity)}>{connectivityLabel(item.connectivity)}</Badge></td><td><strong>{item.code}</strong><small>{item.alias}</small></td><td>{regions.find(region => region.id === item.regionId)?.name ?? '—'}</td><td>{item.sourceType === 'direct' ? <><Badge tone="info">RTSP 直连</Badge><small>{streamHostLabel(item.mainStreamUrl)}</small></> : <><strong>{parent?.recorder.name ?? '—'} / {item.inputChannelNumber}</strong><small>{item.provisioningMode === 'manual' ? '人工维护' : '自动发现'}</small></>}</td><td>{item.manufacturer ?? parent?.recorder.vendor ?? '—'}<small>{item.model ?? '型号未登记'}</small></td><td>{item.supportsPtz ? <Badge tone="info">支持</Badge> : '—'}</td><td>{formatTime(item.lastVerifiedAt)}</td><td><Button variant="ghost" onClick={() => { setEditingCamera(item); setDialog('camera') }}>编辑</Button></td></tr>
      })}</tbody></table></div>)}
      {tab === 'recorders' && (visibleRecorders.length === 0 ? <EmptyState title="没有匹配的接入设备" /> : <div className="table-scroll"><table><thead><tr><th>状态</th><th>编号 / 名称</th><th>类型 / 插件</th><th>厂商 / 型号</th><th>协议端点</th><th>时钟</th><th /></tr></thead><tbody>{visibleRecorders.map(item => { const clock = clockSynchronization(item.recorder.clockSynchronization); const plugin = item.recorder.devicePluginId ? pluginById.get(item.recorder.devicePluginId) : null; return <tr key={item.recorder.id}><td><Badge tone={connectivityTone(item.recorder.connectivity)}>{connectivityLabel(item.recorder.connectivity)}</Badge></td><td><strong>{item.recorder.code}</strong><small>{item.recorder.name}</small></td><td><strong>{deviceKindLabel(item.recorder.deviceKind)}</strong><small>{plugin?.name ?? item.recorder.adapterType}</small></td><td>{item.recorder.vendor}<small>{item.recorder.model ?? '型号未登记'}</small></td><td><div className="inline-badges">{item.endpoints.map(endpoint => <Badge key={endpoint.id}>{protocolName(endpoint.protocol)}:{endpoint.port}</Badge>)}</div></td><td><Badge tone={clock.tone}>{clock.label}</Badge><small>{clockOffsetLabel(item.recorder.lastClockOffsetMilliseconds)} · {formatTime(item.recorder.lastClockObservedAt)}</small></td><td><Button variant="ghost" onClick={() => { setEditingRecorder(item); setDialog('recorder') }}>编辑</Button></td></tr> })}</tbody></table></div>)}
      {tab === 'regions' && (visibleRegions.length === 0 ? <EmptyState title="没有匹配的区域" /> : <div className="table-scroll"><table><thead><tr><th>区域编号</th><th>名称</th><th>上级区域</th><th>摄像头数量</th><th /></tr></thead><tbody>{visibleRegions.map(item => <tr key={item.id}><td><strong>{item.code}</strong></td><td>{item.name}</td><td>{regions.find(parent => parent.id === item.parentId)?.name ?? '根区域'}</td><td>{cameras.filter(camera => camera.regionId === item.id).length}</td><td><Button variant="ghost" onClick={() => { setEditingRegion(item); setDialog('region') }}>编辑</Button></td></tr>)}</tbody></table></div>)}
    </Panel>
    <CameraDialog open={dialog === 'camera'} camera={editingCamera} recorders={recorders} regions={regions} plugins={plugins} edgeAgents={edgeAgents} credentials={credentials} onClose={closeDialog} onSaved={saved} notify={notify} />
    <RecorderDialog open={dialog === 'recorder'} value={editingRecorder} plugins={plugins} onClose={closeDialog} onSaved={saved} notify={notify} />
    <RegionDialog open={dialog === 'region'} value={editingRegion} regions={regions} onClose={closeDialog} onSaved={saved} notify={notify} />
  </>
}

function CameraDialog({ open, camera, recorders, regions, plugins, edgeAgents, credentials, onClose, onSaved, notify }: { open: boolean; camera: CameraType | null; recorders: RecorderResponse[]; regions: Region[]; plugins: DevicePlugin[]; edgeAgents: EdgeAgent[]; credentials: DeviceCredential[]; onClose: () => void; onSaved: (message: string) => Promise<void>; notify: Notify }) {
  const [saving, setSaving] = useState(false)
  const [sourceType, setSourceType] = useState<'recorder-channel' | 'direct'>('recorder-channel')
  const [selectedAgentId, setSelectedAgentId] = useState('')
  const [selectedCredentialId, setSelectedCredentialId] = useState('')
  const [mainStreamUrl, setMainStreamUrl] = useState('')
  const [subStreamUrl, setSubStreamUrl] = useState('')
  const [inputChannel, setInputChannel] = useState('1')
  const [useCustomChannelStreamMap, setUseCustomChannelStreamMap] = useState(false)
  const [mainMapping, setMainMapping] = useState('')
  const [subMapping, setSubMapping] = useState('')
  const [useCustomSubStreamUrl, setUseCustomSubStreamUrl] = useState(false)
  const [preflighting, setPreflighting] = useState(false)
  const channelStreamDefaults = useMemo(() => defaultChannelStreamMap(inputChannel), [inputChannel])
  const resolvedChannelStreamMap = useCustomChannelStreamMap
    ? { main: mainMapping, sub: subMapping }
    : channelStreamDefaults
  const directPlugins = plugins.filter(item => (item.enabled || item.id === camera?.devicePluginId) && item.runtimeType === 'direct-rtsp' && item.manifest.supportedDeviceKinds.includes('camera'))
  const availableAgents = edgeAgents.filter(item => isActiveEdgeAgent(item) || item.id === camera?.edgeAgentId || item.id === camera?.agentId)
  const selectedCredential = credentials.find(item => item.id === selectedCredentialId)
  const selectableCredentials = credentials.filter(item => (isActiveCredential(item) && (!selectedAgentId || !item.agentIds?.length || item.agentIds.includes(selectedAgentId))) || item.id === selectedCredentialId)
  const directReady = directPlugins.some(item => item.enabled) && availableAgents.some(item => isActiveEdgeAgent(item)) && credentials.some(isActiveCredential)
  useEffect(() => {
    if (open) {
      setSaving(false)
      setSourceType(camera?.sourceType === 'direct' ? 'direct' : 'recorder-channel')
      const initialAgentId = camera?.edgeAgentId ?? camera?.agentId ?? ''
      const hasCurrentAgent = edgeAgents.some(item => item.id === initialAgentId)
      setSelectedAgentId(hasCurrentAgent ? initialAgentId : '')
      setSelectedCredentialId(camera?.credentialId ?? (hasCurrentAgent ? credentials.find(item => item.name === camera?.credentialReference)?.id ?? '' : ''))
      setMainStreamUrl(camera?.mainStreamUrl ?? '')
      const initialChannel = String(camera?.inputChannelNumber ?? 1)
      const initialStreamMap = parseStreamMap(camera?.streamingChannelMap ?? '{}')
      const customChannelMap = camera?.sourceType === 'recorder-channel' && hasCustomChannelStreamMap(initialStreamMap, initialChannel)
      const customSubStreamUrl = camera?.sourceType === 'direct' &&
        !!camera.mainStreamUrl && !!camera.subStreamUrl && camera.mainStreamUrl !== camera.subStreamUrl
      setInputChannel(initialChannel)
      setUseCustomChannelStreamMap(customChannelMap)
      setMainMapping(customChannelMap ? initialStreamMap.main : defaultChannelStreamMap(initialChannel).main)
      setSubMapping(customChannelMap ? initialStreamMap.sub : defaultChannelStreamMap(initialChannel).sub)
      setUseCustomSubStreamUrl(customSubStreamUrl)
      setSubStreamUrl(customSubStreamUrl ? camera?.subStreamUrl ?? '' : '')
      setPreflighting(false)
    }
  }, [open, camera, edgeAgents, credentials])

  const submit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault(); setSaving(true)
    const form = new FormData(event.currentTarget)
    const common = {
      code: form.get('code'), alias: form.get('alias'), regionId: form.get('regionId'),
      manufacturer: form.get('manufacturer'), model: form.get('model'), serialNumber: form.get('serialNumber'),
      description: form.get('description')
    }
    try {
      const submittedMainStreamUrl = String(form.get('mainStreamUrl') ?? '')
      const submittedSubStreamUrl = useCustomSubStreamUrl ? String(form.get('subStreamUrl') ?? '') : ''
      if (sourceType === 'direct' && (hasEmbeddedUrlCredentials(submittedMainStreamUrl) || hasEmbeddedUrlCredentials(submittedSubStreamUrl))) {
        throw new Error('码流地址不能包含账号或密码，请使用已登记的设备凭据。')
      }
      if (sourceType === 'direct' && ((!camera && (!selectedAgentId || !selectedCredential)) || (selectedAgentId && !selectedCredential))) {
        throw new Error('请选择可用的边缘节点和设备凭据。')
      }
      const directConnection = {
        credentialId: selectedCredential?.id ?? null,
        credentialReference: selectedCredential?.name ?? camera?.credentialReference ?? null,
        edgeAgentId: selectedAgentId || null,
        workerId: camera?.workerId ?? null
      }
      if (camera) {
        const body = camera.sourceType === 'direct'
          ? {
            ...common, mainStreamUrl: form.get('mainStreamUrl'), subStreamUrl: submittedSubStreamUrl,
            ...directConnection, devicePluginId: form.get('devicePluginId'), timeZoneId: form.get('timeZoneId')
          }
          : { ...common, inputChannelNumber: Number(form.get('channel')), supportsPtz: form.get('ptz') === 'on', streamingChannelMap: JSON.stringify({ main: mappingValue(resolvedChannelStreamMap.main), sub: mappingValue(resolvedChannelStreamMap.sub) }) }
        await api.patch(`/api/v1/admin/cameras/${camera.id}`, body)
      } else if (sourceType === 'direct') {
        await api.post('/api/v1/admin/cameras', {
          ...common, sourceType: 'direct', recorderId: null, inputChannelNumber: 1, streamingChannelMap: null, supportsPtz: false,
          devicePluginId: form.get('devicePluginId'), ...directConnection, mainStreamUrl: form.get('mainStreamUrl'),
          subStreamUrl: submittedSubStreamUrl, timeZoneId: form.get('timeZoneId')
        })
      } else {
        await api.post('/api/v1/admin/cameras', {
          ...common, sourceType: 'recorder-channel', recorderId: form.get('recorderId'), inputChannelNumber: Number(form.get('channel')),
          supportsPtz: form.get('ptz') === 'on', streamingChannelMap: JSON.stringify({ main: mappingValue(resolvedChannelStreamMap.main), sub: mappingValue(resolvedChannelStreamMap.sub) })
        })
      }
      await onSaved(camera ? '摄像头信息已更新' : '摄像头已创建')
    } catch (reason) { notify(reason instanceof Error ? reason.message : '保存失败', 'bad') } finally { setSaving(false) }
  }

  const runPreflight = async () => {
    if (!selectedAgentId || !selectedCredential || !mainStreamUrl.trim()) {
      notify('请先选择边缘节点和设备凭据，并填写主码流地址。', 'bad')
      return
    }
    if (hasEmbeddedUrlCredentials(mainStreamUrl) || (useCustomSubStreamUrl && hasEmbeddedUrlCredentials(subStreamUrl))) {
      notify('码流地址不能包含账号或密码，请使用已登记的设备凭据。', 'bad')
      return
    }
    setPreflighting(true)
    try {
      await api.post('/api/v1/admin/camera-preflights', {
        edgeAgentId: selectedAgentId,
        credentialId: selectedCredential.id,
        mainStreamUrl: mainStreamUrl.trim(),
        subStreamUrl: useCustomSubStreamUrl ? subStreamUrl.trim() || null : null
      })
      notify('连接预检已提交，结果将由边缘节点回传。')
    } catch (reason) {
      notify(reason instanceof Error ? reason.message : '连接预检提交失败', 'bad')
    } finally {
      setPreflighting(false)
    }
  }

  return <Dialog open={open} title={camera ? '编辑摄像头' : '新增摄像头'} description={camera ? '修改码流或通道会立即撤销现有播放和 PTZ 会话。' : '可从接入设备通道建立，也可直接填写 RTSP 地址并先执行连接预检。'} onClose={onClose}>
    <Form onSubmit={submit}>
      {!camera && <div className="segmented segmented--form"><button type="button" className={sourceType === 'recorder-channel' ? 'active' : ''} onClick={() => setSourceType('recorder-channel')}>设备通道</button><button type="button" className={sourceType === 'direct' ? 'active' : ''} onClick={() => setSourceType('direct')}>直连地址</button></div>}
      <div className="form-grid">
        <Field label="平台编号"><Input name="code" defaultValue={camera?.code} required /></Field>
        <Field label="业务别名"><Input name="alias" defaultValue={camera?.alias} required /></Field>
        <Field label="区域"><Select name="regionId" required defaultValue={camera?.regionId ?? ''}><option value="" disabled>请选择</option>{regions.map(item => <option key={item.id} value={item.id}>{item.name}</option>)}</Select></Field>
        <Field label="厂商"><Input name="manufacturer" defaultValue={camera?.manufacturer ?? ''} placeholder="可填写任意品牌" /></Field>
        <Field label="型号"><Input name="model" defaultValue={camera?.model ?? ''} /></Field>
        <Field label="序列号"><Input name="serialNumber" defaultValue={camera?.serialNumber ?? ''} /></Field>

        {sourceType === 'recorder-channel' ? <Fragment key="recorder-channel">
          {!camera && <Field label="所属接入设备"><Select name="recorderId" required defaultValue=""><option value="" disabled>请选择</option>{recorders.filter(item => item.recorder.deviceKind !== 'camera').map(item => <option key={item.recorder.id} value={item.recorder.id}>{item.recorder.name}</option>)}</Select></Field>}
          <Field label="输入通道"><Input name="channel" type="number" min="1" value={inputChannel} onChange={event => setInputChannel(event.target.value)} required /></Field>
          <Field label="自定义码流映射" hint={`默认主码流 ${channelStreamDefaults.main || '—'}，子码流 ${channelStreamDefaults.sub || '—'}`}><label className="check"><input type="checkbox" checked={useCustomChannelStreamMap} onChange={event => setUseCustomChannelStreamMap(event.target.checked)} />启用自定义映射</label></Field>
          <Field label="主码流映射" hint="仅在设备默认流号不适用时修改"><Input name="mainMapping" value={resolvedChannelStreamMap.main} readOnly={!useCustomChannelStreamMap} onChange={event => setMainMapping(event.target.value)} required /></Field>
          <Field label="子码流映射"><Input name="subMapping" value={resolvedChannelStreamMap.sub} readOnly={!useCustomChannelStreamMap} onChange={event => setSubMapping(event.target.value)} required /></Field>
          <Field label="云台能力"><label className="check"><input name="ptz" type="checkbox" defaultChecked={camera?.supportsPtz} />支持 PTZ</label></Field>
        </Fragment> : <Fragment key="direct">
          {!directReady && !camera && <div className="form-notice field-wide">需先启用支持摄像头的 direct-rtsp 插件，并配对至少一个可用边缘节点和设备凭据。</div>}
          <Field label="协议插件"><Select name="devicePluginId" required defaultValue={camera?.devicePluginId ?? ''}><option value="" disabled>请选择</option>{directPlugins.map(item => <option key={item.id} value={item.id}>{item.name} · {item.version}</option>)}</Select></Field>
          <Field label="边缘节点"><Select name="edgeAgentId" required={!camera || !!selectedAgentId} value={selectedAgentId} onChange={event => setSelectedAgentId(event.target.value)}><option value="">{camera?.workerId && !camera?.edgeAgentId && !camera?.agentId ? '保留原节点分配' : '请选择'}</option>{availableAgents.map(item => <option key={item.id} value={item.id}>{item.name}{isActiveEdgeAgent(item) ? '' : ' · 不可用'}</option>)}</Select></Field>
          <Field label="主码流地址"><Input name="mainStreamUrl" type="url" value={mainStreamUrl} onChange={event => setMainStreamUrl(event.target.value)} placeholder="rtsp://10.0.0.20:554/live/main" required /></Field>
          <Field label="子码流地址" hint={useCustomSubStreamUrl ? '使用与主码流相同的主机、端口和协议' : '默认复用主码流地址'}><label className="check"><input type="checkbox" checked={useCustomSubStreamUrl} onChange={event => setUseCustomSubStreamUrl(event.target.checked)} />启用独立子码流地址</label><Input name="subStreamUrl" type="url" value={useCustomSubStreamUrl ? subStreamUrl : mainStreamUrl} readOnly={!useCustomSubStreamUrl} onChange={event => setSubStreamUrl(event.target.value)} placeholder="rtsp://10.0.0.20:554/live/sub" required={useCustomSubStreamUrl} /></Field>
          <Field label="设备凭据" hint="仅显示凭据名称，码流地址中禁止包含用户名和密码"><Select name="credentialId" required={!camera} value={selectedCredentialId} onChange={event => setSelectedCredentialId(event.target.value)}><option value="">{camera?.credentialReference ? `保留旧凭据引用 · ${camera.credentialReference}` : '请选择已登记凭据'}</option>{selectableCredentials.map(item => <option key={item.id} value={item.id}>{item.name} · {credentialVersionLabel(item)}</option>)}</Select></Field>
          <Field label="设备时区"><Input name="timeZoneId" defaultValue={camera?.timeZoneId ?? 'Asia/Shanghai'} required /></Field>
        </Fragment>}
        <div className="field-wide"><Field label="备注"><Textarea name="description" rows={3} defaultValue={camera?.description ?? ''} /></Field></div>
      </div>
      <div className="dialog__footer">{sourceType === 'direct' && <Button variant="secondary" type="button" icon={<AlertCircle size={15} />} disabled={preflighting || !selectedAgentId || !selectedCredential || !mainStreamUrl.trim()} onClick={() => void runPreflight()}>{preflighting ? '预检中' : '连接预检'}</Button>}<Button variant="secondary" type="button" onClick={onClose}>取消</Button><Button disabled={saving || (!camera && sourceType === 'direct' && !directReady)}>{saving ? '保存中' : '保存'}</Button></div>
    </Form>
  </Dialog>
}

function RecorderDialog({ open, value, plugins, onClose, onSaved, notify }: { open: boolean; value: RecorderResponse | null; plugins: DevicePlugin[]; onClose: () => void; onSaved: (message: string) => Promise<void>; notify: Notify }) {
  const [saving, setSaving] = useState(false)
  const [deviceKind, setDeviceKind] = useState(value?.recorder.deviceKind ?? 'recorder')
  const [pluginId, setPluginId] = useState(value?.recorder.devicePluginId ?? '')
  useEffect(() => {
    if (!open) return
    setSaving(false)
    const initialKind = value?.recorder.deviceKind ?? 'recorder'
    setDeviceKind(initialKind)
    setPluginId(value
      ? value.recorder.devicePluginId ?? ''
      : plugins.find(item => item.enabled && item.manifest.supportedDeviceKinds.includes(initialKind))?.id ?? '')
  }, [open, value, plugins])
  const compatiblePlugins = plugins.filter(item => (item.enabled || item.id === value?.recorder.devicePluginId) && item.manifest.supportedDeviceKinds.includes(deviceKind))
  const selectedPlugin = plugins.find(item => item.id === pluginId)
  const endpointDefinitions = selectedPlugin?.manifest.endpoints ?? []

  const changeDeviceKind = (next: string) => {
    setDeviceKind(next)
    const current = plugins.find(item => item.id === pluginId)
    if (!current?.manifest.supportedDeviceKinds.includes(next)) {
      setPluginId(plugins.find(item => item.enabled && item.manifest.supportedDeviceKinds.includes(next))?.id ?? '')
    }
  }

  const submit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault(); setSaving(true)
    const form = new FormData(event.currentTarget)
    try {
      if (!selectedPlugin) throw new Error('请选择可用的协议插件。')
      const endpoints = endpointDefinitions.flatMap(endpoint => {
        const key = endpoint.protocol.toLowerCase()
        const host = String(form.get(`endpoint-${key}-host`) ?? '').trim()
        const credentialReference = String(form.get(`endpoint-${key}-credential`) ?? '').trim()
        const useTls = form.get(`endpoint-${key}-tls`) === 'on'
        const certificateThumbprint = String(form.get(`endpoint-${key}-thumbprint`) ?? '').trim()
        if (!endpoint.required && !host && !credentialReference && !useTls && !certificateThumbprint) return []
        if (!host || !credentialReference) throw new Error(`${endpoint.label}的主机地址和凭据引用必须同时填写。`)
        return [{
          protocol: protocolValue(endpoint.protocol), host, port: Number(form.get(`endpoint-${key}-port`)),
          useTls,
          certificateThumbprint: certificateThumbprint || null,
          credentialReference
        }]
      })
      const body = {
        code: form.get('code'), name: form.get('name'), vendor: form.get('vendor'), model: form.get('model'),
        adapterType: selectedPlugin.adapterType, timeZoneId: form.get('timeZone'), endpoints,
        devicePluginId: selectedPlugin.id, deviceKind, serialNumber: form.get('serialNumber'),
        firmwareVersion: form.get('firmwareVersion'), description: form.get('description'),
        configurationJson: form.get('configurationJson')
      }
      if (value) await api.patch(`/api/v1/admin/recorders/${value.recorder.id}`, body)
      else await api.post('/api/v1/admin/recorders', body)
      await onSaved(value ? '接入设备信息已更新' : '接入设备已创建')
    } catch (reason) { notify(reason instanceof Error ? reason.message : '保存失败', 'bad') } finally { setSaving(false) }
  }

  return <Dialog open={open} title={value ? '编辑接入设备' : '新增接入设备'} description="厂商和型号仅作为资产信息；实际接入能力由协议插件决定。" onClose={onClose}>
    <Form onSubmit={submit}>
      <div className="form-grid">
        <Field label="设备编号"><Input name="code" defaultValue={value?.recorder.code} required /></Field>
        <Field label="设备名称"><Input name="name" defaultValue={value?.recorder.name} required /></Field>
        <Field label="设备类型"><Select name="deviceKind" value={deviceKind} onChange={event => changeDeviceKind(event.target.value)}>{deviceKindOptions.map(([key, label]) => <option key={key} value={key}>{label}</option>)}</Select></Field>
        <Field label="协议插件"><Select name="devicePluginId" value={pluginId} onChange={event => setPluginId(event.target.value)} required><option value="" disabled>请选择</option>{compatiblePlugins.map(item => <option key={item.id} value={item.id}>{item.name} · {item.version}</option>)}</Select></Field>
        <Field label="厂商"><Input name="vendor" defaultValue={value?.recorder.vendor ?? selectedPlugin?.vendor ?? ''} placeholder="可填写任意品牌" required /></Field>
        <Field label="型号"><Input name="model" defaultValue={value?.recorder.model ?? ''} /></Field>
        <Field label="序列号"><Input name="serialNumber" defaultValue={value?.recorder.serialNumber ?? ''} /></Field>
        <Field label="固件版本"><Input name="firmwareVersion" defaultValue={value?.recorder.firmwareVersion ?? ''} /></Field>
        <Field label="设备时区"><Input name="timeZone" defaultValue={value?.recorder.timeZoneId ?? 'Asia/Shanghai'} required /></Field>
        <div className="field-wide"><Field label="备注"><Textarea name="description" rows={3} defaultValue={value?.recorder.description ?? ''} /></Field></div>
        <div className="form-section-title field-wide"><strong>协议端点</strong><span>{selectedPlugin?.description ?? '选择插件后配置端点'}</span></div>
        {endpointDefinitions.map(endpoint => {
          const key = endpoint.protocol.toLowerCase()
          const current = value?.endpoints.find(item => protocolName(item.protocol).toLowerCase() === key)
          return <div className="endpoint-group field-wide" key={`${selectedPlugin?.id ?? 'plugin'}-${endpoint.protocol}`}>
            <div className="endpoint-group__title"><strong>{endpoint.label}</strong><Badge tone={endpoint.required ? 'info' : 'neutral'}>{endpoint.required ? '必填' : '可选'}</Badge></div>
            <div className="form-grid">
              <Field label="主机地址"><Input name={`endpoint-${key}-host`} defaultValue={current?.host ?? ''} placeholder="IP 或 DNS 主机名" required={endpoint.required} /></Field>
              <Field label="端口"><Input name={`endpoint-${key}-port`} type="number" min="1" max="65535" defaultValue={current?.port ?? endpoint.defaultPort} required={endpoint.required} /></Field>
              <Field label="凭据引用"><Input name={`endpoint-${key}-credential`} defaultValue={current?.credentialReference ?? ''} required={endpoint.required} /></Field>
              {endpoint.supportsTls && <Field label="TLS"><label className="check"><input name={`endpoint-${key}-tls`} type="checkbox" defaultChecked={current?.useTls} />启用 TLS</label></Field>}
              {endpoint.supportsTls && <div className="field-wide"><Field label="SHA-256 证书指纹" hint="启用 ONVIF / ISAPI TLS 时必填"><Input name={`endpoint-${key}-thumbprint`} defaultValue={current?.certificateThumbprint ?? ''} /></Field></div>}
            </div>
          </div>
        })}
        <div className="field-wide"><Field label="高级配置（JSON）"><Textarea name="configurationJson" rows={4} defaultValue={value?.recorder.configurationJson ?? '{}'} spellCheck={false} /></Field></div>
      </div>
      <div className="dialog__footer"><Button variant="secondary" type="button" onClick={onClose}>取消</Button><Button disabled={saving || !selectedPlugin}>{saving ? '保存中' : '保存'}</Button></div>
    </Form>
  </Dialog>
}

function RegionDialog({ open, value, regions, onClose, onSaved, notify }: { open: boolean; value: Region | null; regions: Region[]; onClose: () => void; onSaved: (message: string) => Promise<void>; notify: Notify }) {
  const submit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault(); const form = new FormData(event.currentTarget); const body = { code: form.get('code'), name: form.get('name'), parentId: form.get('parentId') || null }
    try { value ? await api.patch(`/api/v1/admin/regions/${value.id}`, body) : await api.post('/api/v1/admin/regions', body); await onSaved(value ? '区域已更新' : '区域已创建') } catch (reason) { notify(reason instanceof Error ? reason.message : '保存失败', 'bad') }
  }
  return <Dialog open={open} title={value ? '编辑区域' : '新增区域'} onClose={onClose}><Form onSubmit={submit}><div className="form-grid"><Field label="区域编号"><Input name="code" defaultValue={value?.code} required /></Field><Field label="区域名称"><Input name="name" defaultValue={value?.name} required /></Field><Field label="上级区域"><Select name="parentId" defaultValue={value?.parentId ?? ''}><option value="">根区域</option>{regions.filter(item => item.id !== value?.id).map(item => <option key={item.id} value={item.id}>{item.name}</option>)}</Select></Field></div><div className="dialog__footer"><Button variant="secondary" type="button" onClick={onClose}>取消</Button><Button>保存</Button></div></Form></Dialog>
}

function pluginCapabilityLabels(plugin: DevicePlugin) {
  const capability = plugin.manifest.capabilities
  return [
    capability.liveView && '实时预览',
    capability.channelDiscovery && '通道发现',
    capability.playback && '回放',
    capability.ptz && 'PTZ',
    capability.export && '导出',
    capability.clockSynchronization && '时钟'
  ].filter((item): item is string => !!item)
}

export function PluginsPage({ notify }: { notify: Notify }) {
  const [installOpen, setInstallOpen] = useState(false)
  const resource = useResource(() => api.get<DevicePlugin[]>('/api/v1/admin/device-plugins'), [])
  if (resource.loading) return <LoadingState />
  if (resource.error || !resource.data) return <ErrorState message={resource.error} retry={resource.refresh} />

  const setEnabled = async (plugin: DevicePlugin) => {
    try {
      await api.patch(`/api/v1/admin/device-plugins/${plugin.id}/status`, { enabled: !plugin.enabled })
      notify(plugin.enabled ? '协议插件已停用' : '协议插件已启用')
      await resource.refresh()
    } catch (reason) { notify(reason instanceof Error ? reason.message : '更新插件状态失败', 'bad') }
  }
  const remove = async (plugin: DevicePlugin) => {
    if (!window.confirm(`确认卸载“${plugin.name}”？`)) return
    try {
      await api.delete(`/api/v1/admin/device-plugins/${plugin.id}`)
      notify('协议插件已卸载')
      await resource.refresh()
    } catch (reason) { notify(reason instanceof Error ? reason.message : '卸载插件失败', 'bad') }
  }

  return <>
    <PageHeader title="设备插件" description="按协议运行时管理设备接入能力、端点结构和支持的资产类型。" actions={<Button icon={<PackagePlus size={16} />} onClick={() => setInstallOpen(true)}>安装插件</Button>} />
    <Panel className="table-panel">
      {resource.data.length === 0 ? <EmptyState title="尚未安装设备插件" /> : <div className="table-scroll"><table><thead><tr><th>状态</th><th>插件 / 版本</th><th>协议运行时</th><th>支持设备</th><th>能力</th><th>占用</th><th>包校验</th><th /></tr></thead><tbody>{resource.data.map(plugin => <tr key={plugin.id}>
        <td><StatusBadge value={plugin.enabled} />{plugin.isBuiltIn && <small>内置</small>}</td>
        <td><strong>{plugin.name}</strong><small>{plugin.key} · {plugin.version}</small></td>
        <td><Badge tone="info">{plugin.protocolType}</Badge><small>{plugin.runtimeType}</small></td>
        <td><div className="inline-badges">{plugin.manifest.supportedDeviceKinds.map(item => <Badge key={item}>{deviceKindLabel(item)}</Badge>)}</div></td>
        <td><div className="inline-badges">{pluginCapabilityLabels(plugin).map(item => <Badge key={item} tone="good">{item}</Badge>)}</div></td>
        <td><strong>{plugin.usageCount}</strong><small>台接入设备</small></td>
        <td><code>{plugin.packageHash.slice(0, 12)}</code><small>{formatTime(plugin.updatedAt)}</small></td>
        <td><div className="row-actions"><Button variant="ghost" icon={<Power size={15} />} disabled={plugin.enabled && plugin.usageCount > 0} title={plugin.enabled && plugin.usageCount > 0 ? '插件仍被设备使用' : undefined} onClick={() => void setEnabled(plugin)}>{plugin.enabled ? '停用' : '启用'}</Button>{!plugin.isBuiltIn && <Button variant="danger" icon={<Trash2 size={15} />} disabled={plugin.usageCount > 0} onClick={() => void remove(plugin)}>卸载</Button>}</div></td>
      </tr>)}</tbody></table></div>}
    </Panel>
    <PluginInstallDialog open={installOpen} onClose={() => setInstallOpen(false)} onInstalled={async () => { setInstallOpen(false); notify('协议插件已安装'); await resource.refresh() }} notify={notify} />
  </>
}

function PluginInstallDialog({ open, onClose, onInstalled, notify }: { open: boolean; onClose: () => void; onInstalled: () => Promise<void>; notify: Notify }) {
  const [manifestText, setManifestText] = useState('')
  const [fileName, setFileName] = useState('')
  const [saving, setSaving] = useState(false)
  useEffect(() => {
    setSaving(false)
    if (!open) { setManifestText(''); setFileName('') }
  }, [open])
  const selectFile = async (file: File | undefined) => {
    if (!file) return
    setFileName(file.name)
    setManifestText(await file.text())
  }
  const submit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault(); setSaving(true)
    try {
      const manifest = JSON.parse(manifestText) as unknown
      await api.post('/api/v1/admin/device-plugins/install', { manifest })
      await onInstalled()
    } catch (reason) {
      notify(reason instanceof SyntaxError ? '插件 manifest 不是有效 JSON。' : reason instanceof Error ? reason.message : '安装插件失败', 'bad')
    } finally { setSaving(false) }
  }
  return <Dialog open={open} title="安装设备插件" description="安装包使用声明式 JSON manifest，不执行插件提供的前端脚本。" onClose={onClose}>
    <Form onSubmit={submit}>
      <Field label="Manifest 文件"><Input type="file" accept="application/json,.json" onChange={event => void selectFile(event.target.files?.[0])} /></Field>
      {fileName && <div className="selected-file"><PackagePlus size={16} /><span>{fileName}</span></div>}
      <Field label="Manifest 内容"><Textarea rows={14} value={manifestText} onChange={event => setManifestText(event.target.value)} spellCheck={false} placeholder={'{\n  "key": "vendor-onvif",\n  "runtimeType": "onvif"\n}'} required /></Field>
      <div className="dialog__footer"><Button variant="secondary" type="button" onClick={onClose}>取消</Button><Button disabled={saving || !manifestText.trim()}>{saving ? '正在安装' : '安装'}</Button></div>
    </Form>
  </Dialog>
}

export function AccessPage({ notify }: { notify: Notify }) {
  const [tab, setTab] = useState<'users' | 'roles'>('users')
  const [dialog, setDialog] = useState<'user' | 'role' | 'userRoles' | 'resetPassword' | 'scope' | 'systemPermissions' | null>(null)
  const [selectedUser, setSelectedUser] = useState<User | null>(null)
  const [selectedRole, setSelectedRole] = useState<Role | null>(null)
  const resource = useResource(async () => {
    const [users, roles, regions, cameras] = await Promise.all([
      api.get<User[]>('/api/v1/admin/users'), api.get<Role[]>('/api/v1/admin/roles'),
      api.get<Region[]>('/api/v1/admin/regions'), api.get<CameraType[]>('/api/v1/admin/cameras')
    ])
    return { users, roles, regions, cameras }
  }, [])
  const close = () => { setDialog(null); setSelectedUser(null); setSelectedRole(null) }
  const saved = async (message: string) => { close(); notify(message); await resource.refresh() }
  if (resource.loading) return <LoadingState />
  if (resource.error || !resource.data) return <ErrorState message={resource.error} retry={resource.refresh} />
  const { users, roles, regions, cameras } = resource.data
  const setUserStatus = async (user: User) => {
    try { await api.patch(`/api/v1/admin/users/${user.id}/status`, { disabled: !user.disabledAt }); notify(user.disabledAt ? '账号已启用' : '账号已停用'); await resource.refresh() } catch (reason) { notify(reason instanceof Error ? reason.message : '操作失败', 'bad') }
  }
  return <>
    <PageHeader title="账号与权限" description="角色权限取并集，摄像头范围由服务端强制执行。" actions={<Button icon={<Plus size={16} />} onClick={() => setDialog(tab === 'users' ? 'user' : 'role')}>新增{tab === 'users' ? '账号' : '角色'}</Button>} />
    <div className="toolbar"><div className="tabs"><button className={tab === 'users' ? 'active' : ''} onClick={() => setTab('users')}>账号 <span>{users.length}</span></button><button className={tab === 'roles' ? 'active' : ''} onClick={() => setTab('roles')}>角色 <span>{roles.length}</span></button></div></div>
    <Panel className="table-panel">
      {tab === 'users' && <div className="table-scroll"><table><thead><tr><th>状态</th><th>用户名</th><th>密码状态</th><th>账号类型</th><th>角色</th><th /></tr></thead><tbody>{users.map(user => <tr key={user.id}><td><Badge tone={user.disabledAt ? 'neutral' : 'good'}>{user.disabledAt ? '停用' : '启用'}</Badge></td><td><strong>{user.username}</strong></td><td>{user.requiresPasswordChange ? <Badge tone="warn">登录后需修改</Badge> : <Badge tone="good">已设置</Badge>}</td><td>{user.isSystemAdministrator ? <Badge tone="warn">平台管理员</Badge> : '业务用户'}</td><td><div className="inline-badges">{user.roleIds.length ? user.roleIds.map(id => <Badge key={id}>{roles.find(role => role.id === id)?.name ?? '已删除角色'}</Badge>) : '—'}</div></td><td><div className="row-actions"><Button variant="ghost" onClick={() => { setSelectedUser(user); setDialog('userRoles') }}>分配角色</Button><Button variant="ghost" icon={<KeyRound size={15} />} onClick={() => { setSelectedUser(user); setDialog('resetPassword') }}>重置密码</Button><Button variant={user.disabledAt ? 'ghost' : 'danger'} onClick={() => void setUserStatus(user)}>{user.disabledAt ? '启用' : '停用'}</Button></div></td></tr>)}</tbody></table></div>}
      {tab === 'roles' && <div className="table-scroll"><table><thead><tr><th>角色编号</th><th>名称</th><th>系统权限</th><th>授权范围</th><th>使用账号</th><th /></tr></thead><tbody>{roles.map(role => { const permissions = systemPermissionLabels(role.systemPermissions); return <tr key={role.id}><td><strong>{role.code}</strong></td><td>{role.name}</td><td><div className="inline-badges">{permissions.length ? permissions.map(permission => <Badge key={permission}>{permission}</Badge>) : '—'}</div></td><td>{role.cameraScopes.length} 项</td><td>{users.filter(user => user.roleIds.includes(role.id)).length}</td><td><div className="row-actions"><Button variant="ghost" onClick={() => { setSelectedRole(role); setDialog('systemPermissions') }}>配置系统权限</Button><Button variant="ghost" onClick={() => { setSelectedRole(role); setDialog('scope') }}>配置摄像头权限</Button></div></td></tr> })}</tbody></table></div>}
    </Panel>
    <UserDialog open={dialog === 'user'} roles={roles} onClose={close} onSaved={saved} notify={notify} />
    <RoleDialog open={dialog === 'role'} onClose={close} onSaved={saved} notify={notify} />
    <UserRolesDialog open={dialog === 'userRoles'} user={selectedUser} roles={roles} onClose={close} onSaved={saved} notify={notify} />
    <ResetPasswordDialog open={dialog === 'resetPassword'} user={selectedUser} onClose={close} onSaved={saved} notify={notify} />
    <RoleSystemPermissionsDialog key={`system-${selectedRole?.id ?? 'none'}`} open={dialog === 'systemPermissions'} role={selectedRole} onClose={close} onSaved={saved} notify={notify} />
    <RoleScopeDialog key={`scope-${selectedRole?.id ?? 'none'}`} open={dialog === 'scope'} role={selectedRole} regions={regions} cameras={cameras} onClose={close} onSaved={saved} notify={notify} />
  </>
}

function UserDialog({ open, roles, onClose, onSaved, notify }: { open: boolean; roles: Role[]; onClose: () => void; onSaved: (message: string) => Promise<void>; notify: Notify }) {
  const submit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault(); const form = new FormData(event.currentTarget)
    try { await api.post('/api/v1/admin/users', { username: form.get('username'), isSystemAdministrator: form.get('admin') === 'on', roleIds: form.getAll('roles') }); await onSaved('账号已创建，初始密码与账号相同') } catch (reason) { notify(reason instanceof Error ? reason.message : '创建失败', 'bad') }
  }
  return <Dialog open={open} title="新增账号" description="初始密码与账号相同，首次登录必须修改密码。" onClose={onClose}><Form onSubmit={submit}><div className="form-grid"><Field label="用户名"><Input name="username" required autoComplete="off" /></Field><Field label="账号类型"><label className="check"><input type="checkbox" name="admin" />平台管理员</label></Field><Field label="角色"><div className="check-grid">{roles.map(role => <label className="check" key={role.id}><input type="checkbox" name="roles" value={role.id} />{role.name}</label>)}</div></Field></div><div className="dialog__footer"><Button variant="secondary" type="button" onClick={onClose}>取消</Button><Button>创建</Button></div></Form></Dialog>
}

function RoleDialog({ open, onClose, onSaved, notify }: { open: boolean; onClose: () => void; onSaved: (message: string) => Promise<void>; notify: Notify }) {
  const submit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault(); const form = new FormData(event.currentTarget)
    try { await api.post('/api/v1/admin/roles', { code: form.get('code'), name: form.get('name'), systemPermissions: readSystemPermissions(form) }); await onSaved('角色已创建') } catch (reason) { notify(reason instanceof Error ? reason.message : '创建失败', 'bad') }
  }
  return <Dialog open={open} title="新增角色" onClose={onClose}><Form onSubmit={submit}><div className="form-grid"><Field label="角色编号"><Input name="code" required /></Field><Field label="角色名称"><Input name="name" required /></Field><Field label="系统权限"><div className="check-grid">{systemPermissionOptions.map(([flag, label]) => <label className="check" key={flag}><input type="checkbox" name="systemPermissions" value={flag} />{label}</label>)}</div></Field></div><div className="dialog__footer"><Button variant="secondary" type="button" onClick={onClose}>取消</Button><Button>创建</Button></div></Form></Dialog>
}

function RoleSystemPermissionsDialog({ open, role, onClose, onSaved, notify }: { open: boolean; role: Role | null; onClose: () => void; onSaved: (message: string) => Promise<void>; notify: Notify }) {
  const submit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault(); if (!role) return; const form = new FormData(event.currentTarget)
    try { await api.patch(`/api/v1/admin/roles/${role.id}/system-permissions`, { systemPermissions: readSystemPermissions(form) }); await onSaved('系统权限已更新') } catch (reason) { notify(reason instanceof Error ? reason.message : '保存失败', 'bad') }
  }
  return <Dialog open={open} title={`系统权限 · ${role?.name ?? ''}`} onClose={onClose}><Form onSubmit={submit}><div className="check-grid check-grid--wide">{systemPermissionOptions.map(([flag, label]) => <label className="check" key={flag}><input type="checkbox" name="systemPermissions" value={flag} defaultChecked={!!role && (role.systemPermissions & flag) !== 0} />{label}</label>)}</div><div className="dialog__footer"><Button variant="secondary" type="button" onClick={onClose}>取消</Button><Button>保存</Button></div></Form></Dialog>
}

function UserRolesDialog({ open, user, roles, onClose, onSaved, notify }: { open: boolean; user: User | null; roles: Role[]; onClose: () => void; onSaved: (message: string) => Promise<void>; notify: Notify }) {
  const submit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault(); if (!user) return; const form = new FormData(event.currentTarget)
    try { await api.put(`/api/v1/admin/users/${user.id}/roles`, { roleIds: form.getAll('roles') }); await onSaved('账号角色已更新，现有播放会话已撤销') } catch (reason) { notify(reason instanceof Error ? reason.message : '保存失败', 'bad') }
  }
  return <Dialog open={open} title={`分配角色 · ${user?.username ?? ''}`} onClose={onClose}><Form onSubmit={submit}><div className="check-grid check-grid--wide">{roles.map(role => <label className="check" key={role.id}><input type="checkbox" name="roles" value={role.id} defaultChecked={user?.roleIds.includes(role.id)} />{role.name}</label>)}</div><div className="dialog__footer"><Button variant="secondary" type="button" onClick={onClose}>取消</Button><Button>保存</Button></div></Form></Dialog>
}

function ResetPasswordDialog({ open, user, onClose, onSaved, notify }: { open: boolean; user: User | null; onClose: () => void; onSaved: (message: string) => Promise<void>; notify: Notify }) {
  const [saving, setSaving] = useState(false)
  const reset = async () => {
    if (!user || saving) return
    setSaving(true)
    try { await api.put(`/api/v1/admin/users/${user.id}/password`, {}); await onSaved('密码已重置为账号，用户下次登录必须修改密码') } catch (reason) { notify(reason instanceof Error ? reason.message : '重置失败', 'bad') } finally { setSaving(false) }
  }
  return <Dialog open={open} title={`重置密码 · ${user?.username ?? ''}`} description="重置后，密码将与账号相同，当前登录会话会立即失效。" onClose={onClose}><div className="dialog__footer"><Button variant="secondary" type="button" disabled={saving} onClick={onClose}>取消</Button><Button variant="danger" icon={<KeyRound size={15} />} disabled={saving} onClick={() => void reset()}>{saving ? '正在重置' : '确认重置'}</Button></div></Dialog>
}

interface ScopeDraft { targetType: 'region' | 'camera'; targetId: Id; permissions: number }

function RoleScopeDialog({ open, role, regions, cameras, onClose, onSaved, notify }: { open: boolean; role: Role | null; regions: Region[]; cameras: CameraType[]; onClose: () => void; onSaved: (message: string) => Promise<void>; notify: Notify }) {
  const [scopes, setScopes] = useState<ScopeDraft[]>(() => role?.cameraScopes.map(scope => ({ targetType: scope.regionId ? 'region' : 'camera', targetId: scope.regionId ?? scope.cameraId ?? '', permissions: scope.permissions })) ?? [])
  const add = () => setScopes(value => [...value, { targetType: 'region', targetId: regions[0]?.id ?? '', permissions: 1 }])
  const save = async () => {
    if (!role || scopes.some(scope => !scope.targetId || scope.permissions === 0)) return notify('每项授权必须选择范围和权限', 'bad')
    try { await api.put(`/api/v1/admin/roles/${role.id}/camera-scopes`, { scopes: scopes.map(scope => ({ regionId: scope.targetType === 'region' ? scope.targetId : null, cameraId: scope.targetType === 'camera' ? scope.targetId : null, permissions: scope.permissions })) }); await onSaved('角色摄像头权限已更新，现有播放会话已撤销') } catch (reason) { notify(reason instanceof Error ? reason.message : '保存失败', 'bad') }
  }
  const update = (index: number, patch: Partial<ScopeDraft>) => setScopes(value => value.map((scope, current) => current === index ? { ...scope, ...patch } : scope))
  return <Dialog open={open} title={`摄像头权限 · ${role?.name ?? ''}`} description="区域授权自动包含其全部下级区域。" onClose={onClose}><div className="scope-list">{scopes.map((scope, index) => <div className="scope-row" key={index}><Select value={scope.targetType} onChange={event => update(index, { targetType: event.target.value as 'region' | 'camera', targetId: '' })}><option value="region">区域</option><option value="camera">摄像头</option></Select><Select value={scope.targetId} onChange={event => update(index, { targetId: event.target.value })}><option value="">请选择</option>{(scope.targetType === 'region' ? regions : cameras).map(item => <option key={item.id} value={item.id}>{'alias' in item ? `${item.code} · ${item.alias}` : `${item.code} · ${item.name}`}</option>)}</Select><div className="permission-set">{[[1, '实时'], [2, '回放'], [4, 'PTZ'], [8, '导出']].map(([flag, label]) => <label className="check" key={flag}><input type="checkbox" checked={(scope.permissions & Number(flag)) !== 0} onChange={event => update(index, { permissions: event.target.checked ? scope.permissions | Number(flag) : scope.permissions & ~Number(flag) })} />{label}</label>)}</div><Button variant="ghost" onClick={() => setScopes(value => value.filter((_, current) => current !== index))}>移除</Button></div>)}</div><Button variant="secondary" icon={<Plus size={15} />} onClick={add}>添加授权范围</Button><div className="dialog__footer"><Button variant="secondary" onClick={onClose}>取消</Button><Button onClick={() => void save()}>保存权限</Button></div></Dialog>
}

export function WorkersPage({ notify }: { notify: Notify }) {
  const [createOpen, setCreateOpen] = useState(false)
  const [assignWorker, setAssignWorker] = useState<DeviceWorker | null>(null)
  const [token, setToken] = useState('')
  const resource = useResource(async () => {
    const [workers, recorders, regions, operationStatuses, plugins] = await Promise.all([
      api.get<DeviceWorker[]>('/api/v1/admin/device-workers'),
      api.get<RecorderResponse[]>('/api/v1/admin/recorders'),
      api.get<Region[]>('/api/v1/admin/regions'),
      api.get<DeviceWorkerOperationStatus[]>('/api/v1/admin/device-worker-operation-statuses'),
      api.get<DevicePlugin[]>('/api/v1/admin/device-plugins')
    ])
    return { workers, recorders, regions, operationStatuses, plugins }
  }, [])
  if (resource.loading) return <LoadingState />
  if (resource.error || !resource.data) return <ErrorState message={resource.error} retry={resource.refresh} />
  const { workers, recorders, regions, operationStatuses, plugins } = resource.data
  const pluginsById = new Map(plugins.map(item => [item.id, item]))
  const workerByRecorder = new Map<string, DeviceWorker>()
  workers.forEach(worker => worker.assignments.forEach(assignment => workerByRecorder.set(assignment.recorderId, worker)))
  const statusByWorkerRecorderOperation = new Map(operationStatuses.map(item => [`${item.workerId}:${item.recorderId}:${item.operationType}`, item]))
  const capabilities = ['search', 'playback', 'export', 'ptz'] as const
  const runtimeFor = (recorder: RecorderResponse) => recorder.recorder.devicePluginId ? pluginsById.get(recorder.recorder.devicePluginId)?.runtimeType ?? null : null
  const expectedOperationCount = recorders.reduce((count, recorder) => count + capabilities.filter(capability => operationTypeForRecorder(runtimeFor(recorder), recorder.recorder.adapterType, capability)).length, 0)
  const effectiveOperationCount = operationStatuses.filter(item => item.isEffective).length
  const statusFor = (recorder: RecorderResponse, capability: 'search' | 'playback' | 'export' | 'ptz') => {
    const worker = workerByRecorder.get(recorder.recorder.id)
    const operationType = operationTypeForRecorder(runtimeFor(recorder), recorder.recorder.adapterType, capability)
    return worker && operationType ? statusByWorkerRecorderOperation.get(`${worker.id}:${recorder.recorder.id}:${operationType}`) : undefined
  }
  const changeStatus = async (worker: DeviceWorker) => { try { await api.patch(`/api/v1/admin/device-workers/${worker.id}/status`, { disabled: !worker.disabledAt }); notify(worker.disabledAt ? 'Worker 已启用' : 'Worker 已停用'); await resource.refresh() } catch (reason) { notify(reason instanceof Error ? reason.message : '操作失败', 'bad') } }
  const rotate = async (worker: DeviceWorker) => { try { const result = await api.post<{ enrollmentToken: string }>(`/api/v1/admin/device-workers/${worker.id}/rotate-token`); setToken(result.enrollmentToken); notify('新令牌已生成，旧令牌立即失效') } catch (reason) { notify(reason instanceof Error ? reason.message : '轮换失败', 'bad') } }
  return <>
    <PageHeader title="设备 Worker" description="受控 Windows 节点通过一次性令牌接收设备分配，并持续上报可执行操作。" actions={<div className="page-header__actions"><Button variant="secondary" icon={<RefreshCw size={16} />} onClick={() => void resource.refresh()}>刷新状态</Button><Button icon={<Plus size={16} />} onClick={() => setCreateOpen(true)}>登记 Worker</Button></div>} />
    <Panel className="table-panel">{workers.length === 0 ? <EmptyState title="尚未登记设备 Worker" /> : <div className="table-scroll"><table><thead><tr><th>状态</th><th>节点名称</th><th>最近心跳</th><th>设备分配</th><th /></tr></thead><tbody>{workers.map(worker => <tr key={worker.id}><td><Badge tone={worker.disabledAt ? 'neutral' : 'good'}>{worker.disabledAt ? '停用' : '启用'}</Badge></td><td><strong>{worker.name}</strong></td><td>{formatTime(worker.lastSeenAt)}</td><td>{worker.assignments.length ? worker.assignments.map(item => recorders.find(recorder => recorder.recorder.id === item.recorderId)?.recorder.name ?? item.recorderId.slice(0, 8)).join('、') : '—'}</td><td><div className="row-actions"><Button variant="ghost" onClick={() => setAssignWorker(worker)}>分配设备</Button><Button variant="ghost" icon={<RotateCw size={14} />} onClick={() => void rotate(worker)}>轮换令牌</Button><Button variant={worker.disabledAt ? 'ghost' : 'danger'} onClick={() => void changeStatus(worker)}>{worker.disabledAt ? '启用' : '停用'}</Button></div></td></tr>)}</tbody></table></div>}</Panel>
    <Panel className="table-panel operation-readiness-panel" title="设备操作运行态" actions={<Badge tone={expectedOperationCount === 0 ? 'neutral' : effectiveOperationCount === expectedOperationCount ? 'good' : 'warn'}>{effectiveOperationCount}/{expectedOperationCount} 项可用</Badge>}>
      {recorders.length === 0 ? <EmptyState title="尚未登记接入设备" /> : <div className="table-scroll"><table><thead><tr><th>接入设备 / Worker</th><th>检索</th><th>回放中继</th><th>导出</th><th>PTZ</th><th>最后报告</th></tr></thead><tbody>{recorders.map(recorder => {
        const worker = workerByRecorder.get(recorder.recorder.id)
        const statuses = capabilities.map(capability => statusFor(recorder, capability))
        const latest = statuses.filter((item): item is DeviceWorkerOperationStatus => !!item).sort((left, right) => new Date(right.reportedAt).getTime() - new Date(left.reportedAt).getTime())[0]
        return <tr key={recorder.recorder.id}><td><strong>{recorder.recorder.name}</strong><small>{worker?.name ?? '未分配 Worker'} · {pluginsById.get(recorder.recorder.devicePluginId ?? '')?.name ?? recorder.recorder.adapterType}</small></td>{statuses.map((status, index) => <td key={capabilities[index]}>{operationTypeForRecorder(runtimeFor(recorder), recorder.recorder.adapterType, capabilities[index]) === null ? <Badge tone="neutral">不支持</Badge> : <div className="operation-state">{operationBadge(status)}{status && <small>{operationLabels[status.operationType] ?? status.operationType}</small>}</div>}</td>)}<td>{latest ? <><strong>{formatTime(latest.reportedAt)}</strong><small>{latest.isEffective ? '当前有效' : operationFailureLabel(latest.failureKind) || '需处理'}</small></> : '—'}</td></tr>
      })}</tbody></table></div>}
    </Panel>
    <WorkerCreateDialog open={createOpen} onClose={() => setCreateOpen(false)} onCreated={async value => { setCreateOpen(false); setToken(value); await resource.refresh() }} notify={notify} />
    <WorkerAssignmentDialog open={!!assignWorker} worker={assignWorker} recorders={recorders} regions={regions} onClose={() => setAssignWorker(null)} onSaved={async () => { setAssignWorker(null); notify('接入设备已分配'); await resource.refresh() }} notify={notify} />
    <TokenDialog token={token} onClose={() => setToken('')} />
  </>
}

function WorkerCreateDialog({ open, onClose, onCreated, notify }: { open: boolean; onClose: () => void; onCreated: (token: string) => Promise<void>; notify: Notify }) {
  const submit = async (event: FormEvent<HTMLFormElement>) => { event.preventDefault(); const form = new FormData(event.currentTarget); try { const result = await api.post<{ enrollmentToken: string }>('/api/v1/admin/device-workers', { name: form.get('name') }); await onCreated(result.enrollmentToken) } catch (reason) { notify(reason instanceof Error ? reason.message : '登记失败', 'bad') } }
  return <Dialog open={open} title="登记设备 Worker" onClose={onClose}><Form onSubmit={submit}><Field label="节点名称"><Input name="name" placeholder="例如 warehouse-edge-01" required /></Field><div className="dialog__footer"><Button variant="secondary" type="button" onClick={onClose}>取消</Button><Button>登记</Button></div></Form></Dialog>
}

function WorkerAssignmentDialog({ open, worker, recorders, regions, onClose, onSaved, notify }: { open: boolean; worker: DeviceWorker | null; recorders: RecorderResponse[]; regions: Region[]; onClose: () => void; onSaved: () => Promise<void>; notify: Notify }) {
  const submit = async (event: FormEvent<HTMLFormElement>) => { event.preventDefault(); if (!worker) return; const form = new FormData(event.currentTarget); try { await api.post(`/api/v1/admin/device-workers/${worker.id}/assignments`, { recorderId: form.get('recorderId'), defaultRegionId: form.get('regionId') }); await onSaved() } catch (reason) { notify(reason instanceof Error ? reason.message : '分配失败', 'bad') } }
  return <Dialog open={open} title={`分配接入设备 · ${worker?.name ?? ''}`} onClose={onClose}><Form onSubmit={submit}><div className="form-grid"><Field label="接入设备"><Select name="recorderId" required defaultValue=""><option value="" disabled>请选择</option>{recorders.filter(item => !worker?.assignments.some(assignment => assignment.recorderId === item.recorder.id)).map(item => <option key={item.recorder.id} value={item.recorder.id}>{item.recorder.name}</option>)}</Select></Field><Field label="新通道默认区域"><Select name="regionId" required defaultValue=""><option value="" disabled>请选择</option>{regions.map(item => <option key={item.id} value={item.id}>{item.name}</option>)}</Select></Field></div><div className="dialog__footer"><Button variant="secondary" type="button" onClick={onClose}>取消</Button><Button>保存分配</Button></div></Form></Dialog>
}

function TokenDialog({ token, onClose }: { token: string; onClose: () => void }) {
  const [copied, setCopied] = useState(false)
  const copy = async () => { await navigator.clipboard.writeText(token); setCopied(true) }
  return <Dialog open={!!token} title="一次性 Worker 令牌" description="关闭后无法再次查看；请立即配置到受控 Windows 服务。" onClose={onClose}><div className="token-box"><code>{token}</code><Button variant="secondary" icon={<KeyRound size={15} />} onClick={() => void copy()}>{copied ? '已复制' : '复制令牌'}</Button></div><div className="dialog__footer"><Button onClick={onClose}>我已妥善保存</Button></div></Dialog>
}

function exportStatusLabel(value: string) {
  const labels: Record<string, string> = { Queued: '等待执行', Running: '正在导出', Completed: '已完成', Failed: '失败', Cancelled: '已取消', Expired: '已过期' }
  return labels[value] ?? value
}

function exportStatusTone(value: string): 'good' | 'bad' | 'warn' | 'neutral' {
  if (value === 'Completed') return 'good'
  if (value === 'Failed') return 'bad'
  if (value === 'Queued' || value === 'Running') return 'warn'
  return 'neutral'
}

function formatExportDuration(startedAt: string, endedAt: string) {
  const minutes = Math.max(0, Math.round((new Date(endedAt).getTime() - new Date(startedAt).getTime()) / 60_000))
  const hours = Math.floor(minutes / 60)
  return hours > 0 ? `${hours} 小时 ${minutes % 60} 分` : `${minutes} 分`
}

function formatFileSize(sizeBytes: number) {
  if (sizeBytes < 1024 * 1024) return `${Math.ceil(sizeBytes / 1024)} KB`
  if (sizeBytes < 1024 * 1024 * 1024) return `${(sizeBytes / (1024 * 1024)).toFixed(1)} MB`
  return `${(sizeBytes / (1024 * 1024 * 1024)).toFixed(2)} GB`
}

function toDateTimeLocal(value: Date) {
  const local = new Date(value.getTime() - value.getTimezoneOffset() * 60_000)
  return local.toISOString().slice(0, 16)
}

export function ExportsPage({ notify }: { notify: Notify }) {
  const [query, setQuery] = useState('')
  const [createOpen, setCreateOpen] = useState(false)
  const resource = useResource(async () => {
    const [exports, cameras] = await Promise.all([
      api.get<PlaybackExport[]>('/api/v1/admin/playback-exports?limit=500'),
      api.get<ExportCamera[]>('/api/v1/admin/playback-exports/cameras')
    ])
    return { exports, cameras }
  }, [])
  if (resource.loading) return <LoadingState />
  if (resource.error || !resource.data) return <ErrorState message={resource.error} retry={resource.refresh} />
  const { exports, cameras } = resource.data
  const cameraById = new Map(cameras.map(item => [item.id, item]))
  const q = query.trim().toLowerCase()
  const visibleExports = exports.filter(item => {
    const camera = cameraById.get(item.cameraId)
    return !q || `${camera?.code ?? item.cameraId} ${camera?.alias ?? ''} ${item.status}`.toLowerCase().includes(q)
  })
  const downloadArtifact = async (item: PlaybackExport) => {
    if (!item.artifact) return
    try {
      const { blob, fileName } = await api.download(`/api/v1/admin/playback-exports/${item.id}/artifact`)
      const anchor = document.createElement('a')
      const objectUrl = URL.createObjectURL(blob)
      anchor.href = objectUrl
      anchor.download = fileName
      anchor.click()
      URL.revokeObjectURL(objectUrl)
      notify('导出文件下载已开始')
    } catch (reason) {
      notify(reason instanceof Error ? reason.message : '下载导出文件失败', 'bad')
    }
  }
  const cancelExport = async (item: PlaybackExport) => {
    try {
      await api.post<void>(`/api/v1/admin/playback-exports/${item.id}/cancel`)
      notify('录像导出已取消')
      await resource.refresh()
    } catch (reason) {
      notify(reason instanceof Error ? reason.message : '取消录像导出失败', 'bad')
    }
  }
  return <>
    <PageHeader title="录像导出" description="受控任务由所属边缘 Worker 执行，文件将统一进入中心管理端。" actions={<Button icon={<FileDown size={16} />} onClick={() => setCreateOpen(true)}>新建导出</Button>} />
    <div className="toolbar"><Input className="input search-input" placeholder="搜索摄像头或任务状态" value={query} onChange={event => setQuery(event.target.value)} /><Button variant="secondary" icon={<RefreshCw size={16} />} onClick={() => void resource.refresh()}>刷新</Button></div>
    <Panel className="table-panel">
      {visibleExports.length === 0 ? <EmptyState title={exports.length ? '没有匹配的导出任务' : '暂无录像导出任务'} description={cameras.length ? '创建后将在这里跟踪队列和执行状态。' : '当前账号没有具备导出权限的摄像头。'} /> : <div className="table-scroll"><table><thead><tr><th>状态</th><th>摄像头</th><th>录像范围</th><th>时长</th><th>归档文件</th><th>提交时间</th><th /></tr></thead><tbody>{visibleExports.map(item => {
        const camera = cameraById.get(item.cameraId)
        const cancellable = item.status === 'Queued' || item.status === 'Running'
        return <tr key={item.id}><td><Badge tone={exportStatusTone(item.status)}>{exportStatusLabel(item.status)}</Badge><small>{item.failureCode ?? ''}</small></td><td><strong>{camera?.code ?? item.cameraId.slice(0, 8)}</strong><small>{camera?.alias ?? '摄像头权限已变更或设备已删除'}</small></td><td>{formatTime(item.startedAt)}<small>{formatTime(item.endedAt)}</small></td><td>{formatExportDuration(item.startedAt, item.endedAt)}</td><td>{item.artifact ? <><strong>{formatFileSize(item.artifact.sizeBytes)} · MP4</strong><small>有效至 {formatTime(item.artifact.expiresAt)}</small></> : <small>等待归档</small>}</td><td>{formatTime(item.requestedAt)}</td><td>{item.artifact && <Button variant="ghost" icon={<Download size={15} />} onClick={() => void downloadArtifact(item)}>下载</Button>}{cancellable && <Button variant="ghost" icon={<X size={15} />} onClick={() => void cancelExport(item)}>取消</Button>}</td></tr>
      })}</tbody></table></div>}
    </Panel>
    <PlaybackExportDialog open={createOpen} cameras={cameras} onClose={() => setCreateOpen(false)} onCreated={async () => { setCreateOpen(false); notify('导出任务已进入队列'); await resource.refresh() }} notify={notify} />
  </>
}

function PlaybackExportDialog({ open, cameras, onClose, onCreated, notify }: { open: boolean; cameras: ExportCamera[]; onClose: () => void; onCreated: () => Promise<void>; notify: Notify }) {
  const [submitting, setSubmitting] = useState(false)
  const now = new Date()
  const submit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    const form = new FormData(event.currentTarget)
    const startedAt = new Date(String(form.get('startedAt')))
    const endedAt = new Date(String(form.get('endedAt')))
    if (Number.isNaN(startedAt.getTime()) || Number.isNaN(endedAt.getTime()) || startedAt >= endedAt) {
      notify('请填写有效的起止时间，结束时间必须晚于开始时间', 'bad')
      return
    }
    if (endedAt.getTime() - startedAt.getTime() > 31 * 24 * 60 * 60 * 1000) {
      notify('单次导出范围不能超过 31 天', 'bad')
      return
    }
    setSubmitting(true)
    try {
      await api.post<PlaybackExport>('/api/v1/admin/playback-exports', { cameraId: form.get('cameraId'), startedAt: startedAt.toISOString(), endedAt: endedAt.toISOString(), container: 'mp4' })
      await onCreated()
    } catch (reason) {
      notify(reason instanceof Error ? reason.message : '创建导出任务失败', 'bad')
    } finally {
      setSubmitting(false)
    }
  }
  return <Dialog open={open} title="新建录像导出" description="导出命令将发送到摄像头所属的受控边缘 Worker，最终文件由中心管理端统一保存和审计。" onClose={onClose}><Form onSubmit={submit}><div className="form-grid"><Field label="摄像头"><Select name="cameraId" required defaultValue=""><option value="" disabled>请选择可导出的摄像头</option>{cameras.map(item => <option key={item.id} value={item.id}>{item.code} · {item.alias}</option>)}</Select></Field><Field label="文件格式"><Input value="MP4" readOnly /></Field><Field label="开始时间"><Input name="startedAt" type="datetime-local" defaultValue={toDateTimeLocal(new Date(now.getTime() - 60 * 60 * 1000))} required /></Field><Field label="结束时间"><Input name="endedAt" type="datetime-local" defaultValue={toDateTimeLocal(now)} required /></Field></div><div className="dialog__footer"><Button variant="secondary" type="button" onClick={onClose}>取消</Button><Button disabled={submitting || cameras.length === 0}>{submitting ? '正在提交' : '创建任务'}</Button></div></Form></Dialog>
}

type AlertTab = 'incidents' | 'rules' | 'channels' | 'deliveries' | 'deadLetters'

function isEmailChannelType(value: number | string) { return String(value) === '0' || String(value) === 'Email' }
function channelTypeLabel(value: number | string) { return isEmailChannelType(value) ? '邮件' : '企业微信群机器人' }
function weComMessageFormatLabel(configuration: Record<string, unknown>) { const hasUserMentions = Array.isArray(configuration.mentionedList) && configuration.mentionedList.length > 0; const base = configuration.messageType === 'text' ? '文本 @提醒' : hasUserMentions ? 'Markdown @成员' : 'Markdown'; return typeof configuration.securityKeyword === 'string' && configuration.securityKeyword.trim() ? `${base} · 关键词` : base }
function channelConfigurationLabel(channel: NotificationChannel) { return isEmailChannelType(channel.type) ? 'SMTP' : weComMessageFormatLabel(channel.configuration) }
function splitFormList(value: FormDataEntryValue | null) { return [...new Set(String(value ?? '').split(/[,，;；\s]+/).map(item => item.trim()).filter(Boolean))] }
function isWeComWebhookAddress(value: string) {
  try {
    const address = new URL(value)
    return address.protocol === 'https:' && address.hostname === 'qyapi.weixin.qq.com' && !address.port && address.pathname === '/cgi-bin/webhook/send' && !address.username && !address.password && !address.hash && Boolean(address.searchParams.get('key'))
  } catch {
    return false
  }
}
function configurationStringList(configuration: Record<string, unknown>, property: string) { const value = configuration[property]; return Array.isArray(value) ? value.filter((item): item is string => typeof item === 'string').map(item => item.trim()).filter(Boolean) : [] }
function configurationString(configuration: Record<string, unknown>, property: string) { const value = configuration[property]; return typeof value === 'string' ? value : '' }
function deliveryStatus(value: number | string) { const key = String(value); return key === '0' || key === 'Pending' ? 'Pending' : key === '1' || key === 'Sent' ? 'Sent' : key === '3' || key === 'DeadLettered' ? 'DeadLettered' : 'Failed' }
function deliveryEvent(value: number | string) { return String(value) === '0' || String(value) === 'Opened' ? '告警' : '恢复' }

export function AlertsPage({ notify }: { notify: Notify }) {
  const [tab, setTab] = useState<AlertTab>('incidents')
  const [channelOpen, setChannelOpen] = useState(false)
  const [editingChannel, setEditingChannel] = useState<NotificationChannel | null>(null)
  const [ruleOpen, setRuleOpen] = useState(false)
  const [testingChannelId, setTestingChannelId] = useState<Id | null>(null)
  const resource = useResource(async () => {
    const [incidents, rules, channels, deliveries, deadLetters, regions] = await Promise.all([
      api.get<AlertIncident[]>('/api/v1/admin/alert-incidents'), api.get<AlertRule[]>('/api/v1/admin/alert-rules'),
      api.get<NotificationChannel[]>('/api/v1/admin/notification-channels'), api.get<NotificationDelivery[]>('/api/v1/admin/notification-deliveries'),
      api.get<DeadLetter[]>('/api/v1/admin/outbox/dead-letters'), api.get<Region[]>('/api/v1/admin/regions')
    ])
    return { incidents, rules, channels, deliveries, deadLetters, regions }
  }, [])
  if (resource.loading) return <LoadingState />
  if (resource.error || !resource.data) return <ErrorState message={resource.error} retry={resource.refresh} />
  const { incidents, rules, channels, deliveries, deadLetters, regions } = resource.data
  const openChannelCreate = () => { setEditingChannel(null); setChannelOpen(true) }
  const closeChannelDialog = () => { setChannelOpen(false); setEditingChannel(null) }
  const action = tab === 'rules' ? <Button icon={<Plus size={16} />} onClick={() => setRuleOpen(true)}>新增规则</Button> : tab === 'channels' ? <Button icon={<Plus size={16} />} onClick={openChannelCreate}>新增渠道</Button> : <Button variant="secondary" icon={<RefreshCw size={16} />} onClick={() => void resource.refresh()}>刷新</Button>
  const toggleChannel = async (channel: NotificationChannel) => { try { await api.patch(`/api/v1/admin/notification-channels/${channel.id}/status`, { enabled: !channel.enabled }); notify(channel.enabled ? '渠道已停用' : '渠道已启用'); await resource.refresh() } catch (reason) { notify(reason instanceof Error ? reason.message : '操作失败', 'bad') } }
  const testChannel = async (channel: NotificationChannel) => { setTestingChannelId(channel.id); try { await api.post(`/api/v1/admin/notification-channels/${channel.id}/test`); notify('测试消息已进入投递队列，尚未确认送达；请留意目标邮箱或企业微信群。') } catch (reason) { notify(reason instanceof Error ? reason.message : '测试消息提交失败', 'bad') } finally { setTestingChannelId(null) } }
  const toggleRule = async (rule: AlertRule) => { try { await api.patch(`/api/v1/admin/alert-rules/${rule.id}/status`, { enabled: !rule.enabled }); notify(rule.enabled ? '规则已停用' : '规则已启用'); await resource.refresh() } catch (reason) { notify(reason instanceof Error ? reason.message : '操作失败', 'bad') } }
  const retryDelivery = async (id: Id) => { try { await api.post(`/api/v1/admin/notification-deliveries/${id}/retry`); notify('投递已重新进入队列'); await resource.refresh() } catch (reason) { notify(reason instanceof Error ? reason.message : '重试失败', 'bad') } }
  const retryOutbox = async (id: Id) => { try { await api.post(`/api/v1/admin/outbox/${id}/retry`); notify('死信已重新进入 Outbox'); await resource.refresh() } catch (reason) { notify(reason instanceof Error ? reason.message : '重试失败', 'bad') } }
  return <>
    <PageHeader title="告警与通知" description="离线与时钟偏差事件去重、区域路由、恢复通知与投递审计。" actions={action} />
    <div className="toolbar"><div className="tabs tabs--scroll"><button className={tab === 'incidents' ? 'active' : ''} onClick={() => setTab('incidents')}>事件 <span>{incidents.filter(item => !item.resolvedAt).length}</span></button><button className={tab === 'rules' ? 'active' : ''} onClick={() => setTab('rules')}>规则 <span>{rules.length}</span></button><button className={tab === 'channels' ? 'active' : ''} onClick={() => setTab('channels')}>渠道 <span>{channels.length}</span></button><button className={tab === 'deliveries' ? 'active' : ''} onClick={() => setTab('deliveries')}>投递 <span>{deliveries.length}</span></button><button className={tab === 'deadLetters' ? 'active' : ''} onClick={() => setTab('deadLetters')}>死信 <span>{deadLetters.length}</span></button></div></div>
    <Panel className="table-panel">
      {tab === 'incidents' && (incidents.length === 0 ? <EmptyState title="暂无告警事件" /> : <div className="table-scroll"><table><thead><tr><th>状态</th><th>类型</th><th>资源</th><th>区域</th><th>发生时间</th><th>恢复时间</th></tr></thead><tbody>{incidents.map(item => <tr key={item.id}><td><Badge tone={item.resolvedAt ? 'good' : 'bad'}>{item.resolvedAt ? '已恢复' : '未恢复'}</Badge></td><td>{incidentTypeLabel(item.incidentType)}</td><td><strong>{item.resourceName}</strong><small>{item.resourceType}</small></td><td>{regions.find(region => region.id === item.regionId)?.name ?? '全局'}</td><td>{formatTime(item.openedAt)}</td><td>{formatTime(item.resolvedAt)}</td></tr>)}</tbody></table></div>)}
      {tab === 'rules' && (rules.length === 0 ? <EmptyState title="尚未配置告警规则" /> : <div className="table-scroll"><table><thead><tr><th>状态</th><th>规则名称</th><th>资源类型</th><th>区域</th><th>渠道</th><th>恢复通知</th><th /></tr></thead><tbody>{rules.map(item => <tr key={item.id}><td><StatusBadge value={item.enabled} /></td><td><strong>{item.name}</strong></td><td>{item.resourceType === '*' ? '全部' : item.resourceType}</td><td>{regions.find(region => region.id === item.regionId)?.name ?? '全部区域'}</td><td>{item.notificationChannelIds.map(id => channels.find(channel => channel.id === id)?.name ?? '已删除').join('、')}</td><td>{item.notifyOnRecovery ? '发送' : '不发送'}</td><td><Button variant={item.enabled ? 'danger' : 'ghost'} onClick={() => void toggleRule(item)}>{item.enabled ? '停用' : '启用'}</Button></td></tr>)}</tbody></table></div>)}
      {tab === 'channels' && (channels.length === 0 ? <EmptyState title="尚未配置通知渠道" /> : <div className="table-scroll"><table><thead><tr><th>状态</th><th>渠道名称</th><th>类型</th><th>投递格式</th><th>投递凭据</th><th>创建时间</th><th /></tr></thead><tbody>{channels.map(item => <tr key={item.id}><td><StatusBadge value={item.enabled} /></td><td><strong>{item.name}</strong></td><td>{channelTypeLabel(item.type)}</td><td>{channelConfigurationLabel(item)}</td><td>{isEmailChannelType(item.type) ? <code>{item.secretReference}</code> : <Badge tone={item.webhookConfigured ? 'good' : 'warn'}>{item.webhookConfigured ? 'Webhook 已保存' : '待保存地址'}</Badge>}</td><td>{formatTime(item.createdAt)}</td><td><div className="row-actions"><Button variant="ghost" icon={<Pencil size={15} />} onClick={() => { setEditingChannel(item); setChannelOpen(true) }}>编辑</Button><Button variant="secondary" icon={<Send size={15} />} disabled={testingChannelId === item.id} title={item.enabled ? '发送测试消息' : '发送一次测试消息，不会启用该渠道'} onClick={() => void testChannel(item)}>{testingChannelId === item.id ? '正在提交' : '发送测试'}</Button><Button variant={item.enabled ? 'danger' : 'ghost'} onClick={() => void toggleChannel(item)}>{item.enabled ? '停用' : '启用'}</Button></div></td></tr>)}</tbody></table></div>)}
      {tab === 'deliveries' && (deliveries.length === 0 ? <EmptyState title="暂无通知投递" /> : <div className="table-scroll"><table><thead><tr><th>状态</th><th>事件</th><th>渠道</th><th>尝试次数</th><th>创建 / 发送</th><th>失败类别</th><th /></tr></thead><tbody>{deliveries.map(item => { const status = deliveryStatus(item.status); return <tr key={item.id}><td><Badge tone={status === 'Sent' ? 'good' : status === 'Pending' ? 'warn' : 'bad'}>{status === 'Sent' ? '已发送' : status === 'DeadLettered' ? '死信' : status === 'Failed' ? '失败' : '等待'}</Badge></td><td>{deliveryEvent(item.eventType)}</td><td>{channels.find(channel => channel.id === item.notificationChannelId)?.name ?? '—'}</td><td>{item.attempts}</td><td>{formatTime(item.createdAt)}<small>{formatTime(item.sentAt)}</small></td><td>{item.lastError ?? '—'}</td><td>{(status === 'Failed' || status === 'DeadLettered') && <Button variant="ghost" onClick={() => void retryDelivery(item.id)}>重试</Button>}</td></tr> })}</tbody></table></div>)}
      {tab === 'deadLetters' && (deadLetters.length === 0 ? <EmptyState title="没有 Outbox 死信" /> : <div className="table-scroll"><table><thead><tr><th>事件类型</th><th>资源</th><th>尝试次数</th><th>进入死信</th><th>失败类别</th><th /></tr></thead><tbody>{deadLetters.map(item => <tr key={item.id}><td><strong>{item.eventType}</strong></td><td>{item.aggregateType} · {item.aggregateId.slice(0, 8)}</td><td>{item.attempts}</td><td>{formatTime(item.deadLetteredAt)}</td><td>{item.lastError ?? '—'}</td><td><Button variant="ghost" onClick={() => void retryOutbox(item.id)}>重新处理</Button></td></tr>)}</tbody></table></div>)}
    </Panel>
    <NotificationChannelDialog open={channelOpen} channel={editingChannel} onClose={closeChannelDialog} onSaved={async () => { const message = editingChannel ? '通知渠道已更新' : '通知渠道已创建'; closeChannelDialog(); notify(message); await resource.refresh() }} notify={notify} />
    <AlertRuleDialog open={ruleOpen} channels={channels} regions={regions} onClose={() => setRuleOpen(false)} onSaved={async () => { setRuleOpen(false); notify('告警规则已创建'); await resource.refresh() }} notify={notify} />
  </>
}

function NotificationChannelDialog({ open, channel, onClose, onSaved, notify }: { open: boolean; channel: NotificationChannel | null; onClose: () => void; onSaved: () => Promise<void>; notify: Notify }) {
  const editing = channel !== null
  const configuration = channel?.configuration ?? {}
  const [type, setType] = useState<'email' | 'wecom'>('email')
  const [weComMessageType, setWeComMessageType] = useState<'markdown' | 'text'>('markdown')
  const [weComMentionMode, setWeComMentionMode] = useState<'none' | 'all' | 'userIds' | 'mobiles'>('none')
  useEffect(() => {
    if (!open) return
    const isWeCom = channel !== null && !isEmailChannelType(channel.type)
    setType(isWeCom ? 'wecom' : 'email')
    const messageType = configuration.messageType === 'text' ? 'text' : 'markdown'
    setWeComMessageType(messageType)
    const mentionedList = configurationStringList(configuration, 'mentionedList')
    const mentionedMobileList = configurationStringList(configuration, 'mentionedMobileList')
    setWeComMentionMode(messageType === 'markdown' ? 'none' : mentionedMobileList.length > 0 ? 'mobiles' : mentionedList.includes('@all') ? 'all' : mentionedList.length > 0 ? 'userIds' : 'none')
  }, [channel, open])
  const selectedType = editing ? (isEmailChannelType(channel.type) ? 'email' : 'wecom') : type
  const mentionedList = configurationStringList(configuration, 'mentionedList')
  const mentionedMobileList = configurationStringList(configuration, 'mentionedMobileList')
  const savedWebhook = selectedType === 'wecom' && channel?.webhookConfigured === true
  const submit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault(); const form = new FormData(event.currentTarget)
    const name = String(form.get('name') ?? '').trim()
    const secretReference = String(form.get('secretReference') ?? '').trim()
    const webhookUrl = String(form.get('webhookUrl') ?? '').trim()
    if (selectedType === 'email' && !/^[A-Za-z0-9._-]{1,128}$/.test(secretReference)) {
      notify('密钥引用只能使用字母、数字、连字符、下划线或点。', 'bad')
      return
    }
    if (selectedType === 'wecom' && !webhookUrl && !savedWebhook) {
      notify('请填写完整的企业微信群机器人 Webhook 地址。', 'bad')
      return
    }
    if (selectedType === 'wecom' && webhookUrl && !isWeComWebhookAddress(webhookUrl)) {
      notify('Webhook 地址必须为企业微信机器人提供的完整 HTTPS 地址，并包含 key 参数。', 'bad')
      return
    }
    const nextConfiguration = selectedType === 'email'
      ? { host: form.get('host'), port: Number(form.get('port')), useSsl: form.get('ssl') === 'on', from: form.get('from'), recipients: splitFormList(form.get('recipients')), subjectPrefix: form.get('prefix') }
      : { messageType: weComMessageType, mentionedList: weComMessageType === 'markdown' ? splitFormList(form.get('mentionedList')) : weComMentionMode === 'all' ? ['@all'] : weComMentionMode === 'userIds' ? splitFormList(form.get('mentionedList')) : [], mentionedMobileList: weComMessageType === 'text' && weComMentionMode === 'mobiles' ? splitFormList(form.get('mentionedMobileList')) : [], securityKeyword: String(form.get('securityKeyword') ?? '').trim() || null }
    const request = selectedType === 'email'
      ? { name, configuration: nextConfiguration, secretReference }
      : { name, configuration: nextConfiguration, ...(webhookUrl ? { webhookUrl } : {}) }
    try {
      if (channel) {
        await api.patch('/api/v1/admin/notification-channels/' + channel.id, request)
      } else {
        await api.post('/api/v1/admin/notification-channels', { ...request, type: selectedType === 'email' ? 0 : 1 })
      }
      await onSaved()
    } catch (reason) {
      notify(reason instanceof Error ? reason.message : editing ? '更新失败' : '创建失败', 'bad')
    }
  }
  return <Dialog open={open} title={editing ? '编辑通知渠道' : '新增通知渠道'} description="SMTP 密码从部署环境读取；完整企业微信群机器人 Webhook 地址会加密保存到平台数据库，且不会回显。" onClose={onClose}>
    <Form key={channel?.id ?? 'new'} onSubmit={submit}>
      {editing
        ? <div className="form-notice">编辑时不能变更渠道类型；真实 SMTP 密码和完整 Webhook 地址均不会显示或返回。</div>
        : <div className="segmented"><button type="button" className={selectedType === 'email' ? 'active' : ''} onClick={() => setType('email')}>SMTP 邮件</button><button type="button" className={selectedType === 'wecom' ? 'active' : ''} onClick={() => setType('wecom')}>企业微信群机器人</button></div>}
      <div className="form-grid">
        <Field label="渠道名称"><Input name="name" defaultValue={channel?.name ?? ''} required /></Field>
        {selectedType === 'email'
          ? <Field label="密钥引用"><Input name="secretReference" defaultValue={channel?.secretReference ?? ''} placeholder="smtp-main" autoCapitalize="none" spellCheck={false} maxLength={128} required /></Field>
          : <Field label={savedWebhook ? '替换 Webhook 地址' : 'Webhook 地址'} hint={savedWebhook ? '已保存地址不会回显；留空保持原地址。' : '请粘贴群机器人提供的完整 HTTPS Webhook 地址。'}><Input name="webhookUrl" type="url" placeholder="https://qyapi.weixin.qq.com/cgi-bin/webhook/send?key=..." autoComplete="off" autoCapitalize="none" spellCheck={false} maxLength={2048} required={!savedWebhook} /></Field>}
        {selectedType === 'email' && <>
          <Field label="SMTP 主机"><Input name="host" defaultValue={configurationString(configuration, 'host')} required /></Field>
          <Field label="端口"><Input name="port" type="number" min="1" max="65535" defaultValue={typeof configuration.port === 'number' ? configuration.port : 587} required /></Field>
          <Field label="发件地址"><Input name="from" type="email" defaultValue={configurationString(configuration, 'from')} required /></Field>
          <Field label="收件地址"><Input name="recipients" defaultValue={configurationStringList(configuration, 'recipients').join(', ')} placeholder="多个地址用逗号分隔" required /></Field>
          <Field label="主题前缀"><Input name="prefix" defaultValue={configurationString(configuration, 'subjectPrefix') || '[视频平台]'} /></Field>
          <Field label="传输安全"><label className="check"><input name="ssl" type="checkbox" defaultChecked={editing ? configuration.useSsl === true : true} />启用 SSL/TLS</label></Field>
        </>}
        {selectedType === 'wecom' && <>
          <div className="form-notice field-wide">在目标企业微信群中添加群机器人后，复制完整 Webhook 地址并粘贴到此处。完整 Webhook 地址本身就是凭据，无需额外填写“Webhook 密钥”。地址将使用 Windows DPAPI 加密保存到平台数据库，管理后台和接口均不会回显。中心 API 与通知 Worker 必须位于同一台受控 Windows 主机；迁移到新主机后请重新保存地址。机器人名称和头像在企业微信客户端内维护。若企业微信设置了 IP 白名单，应放行通知 Worker 的出口 IP。</div>
          <Field label="消息格式"><Select value={weComMessageType} onChange={event => { const next = event.target.value as 'markdown' | 'text'; setWeComMessageType(next); if (next === 'markdown') setWeComMentionMode('none') }}><option value="markdown">Markdown</option><option value="text">文本消息（支持 @提醒）</option></Select></Field>
          <Field label="安全关键词" hint="仅在目标群机器人启用关键词安全时填写；平台会将该关键词附加到每条消息。"><Input name="securityKeyword" defaultValue={configurationString(configuration, 'securityKeyword')} maxLength={128} /></Field>
          {weComMessageType === 'markdown'
            ? <Field label="提醒成员 UserID" hint="多个成员用逗号、分号或换行分隔；系统会在 Markdown 消息中使用 <@UserID> 提醒指定成员。"><Textarea name="mentionedList" defaultValue={mentionedList.join('\n')} placeholder={'zhangsan\nlisi'} /></Field>
            : <>
              <Field label="@ 提醒方式"><Select value={weComMentionMode} onChange={event => setWeComMentionMode(event.target.value as 'none' | 'all' | 'userIds' | 'mobiles')}><option value="none">不提醒</option><option value="all">@全体成员</option><option value="userIds">按成员 UserID</option><option value="mobiles">按成员手机号</option></Select></Field>
              {weComMentionMode === 'all' && <div className="form-notice field-wide">仅当该群机器人在企业微信侧允许 @全体成员时，此提醒才会生效。</div>}
              {weComMentionMode === 'userIds' && <Field label="成员 UserID" hint="多个成员用逗号、分号或换行分隔，按企业微信成员账号填写。"><Textarea name="mentionedList" defaultValue={mentionedList.filter(item => item !== '@all').join('\n')} placeholder={'zhangsan\nlisi'} /></Field>}
              {weComMentionMode === 'mobiles' && <Field label="成员手机号" hint="多个手机号用逗号、分号或换行分隔，仅用于文本消息 @提醒。"><Textarea name="mentionedMobileList" defaultValue={mentionedMobileList.join('\n')} inputMode="numeric" placeholder={'13800000000\n13900000000'} /></Field>}
            </>}
        </>}
      </div>
      <div className="dialog__footer"><Button variant="secondary" type="button" onClick={onClose}>取消</Button><Button>{editing ? '保存修改' : '创建渠道'}</Button></div>
    </Form>
  </Dialog>
}

function AlertRuleDialog({ open, channels, regions, onClose, onSaved, notify }: { open: boolean; channels: NotificationChannel[]; regions: Region[]; onClose: () => void; onSaved: () => Promise<void>; notify: Notify }) {
  const submit = async (event: FormEvent<HTMLFormElement>) => { event.preventDefault(); const form = new FormData(event.currentTarget); try { await api.post('/api/v1/admin/alert-rules', { name: form.get('name'), resourceType: form.get('resourceType'), regionId: form.get('regionId') || null, notifyOnRecovery: form.get('recovery') === 'on', notificationChannelIds: form.getAll('channels') }); await onSaved() } catch (reason) { notify(reason instanceof Error ? reason.message : '创建失败', 'bad') } }
  return <Dialog open={open} title="新增告警规则" onClose={onClose}><Form onSubmit={submit}><div className="form-grid"><Field label="规则名称"><Input name="name" required /></Field><Field label="资源类型"><Select name="resourceType"><option value="*">接入设备和摄像头</option><option value="recorder">仅接入设备</option><option value="camera">仅摄像头</option></Select></Field><Field label="区域范围"><Select name="regionId"><option value="">全部区域</option>{regions.map(item => <option key={item.id} value={item.id}>{item.name}</option>)}</Select></Field><Field label="恢复策略"><label className="check"><input name="recovery" type="checkbox" defaultChecked />发送恢复通知</label></Field><Field label="通知渠道"><div className="check-grid">{channels.map(channel => <label className="check" key={channel.id}><input type="checkbox" name="channels" value={channel.id} />{channel.name}</label>)}</div></Field></div><div className="dialog__footer"><Button variant="secondary" type="button" onClick={onClose}>取消</Button><Button disabled={!channels.length}>创建规则</Button></div></Form></Dialog>
}

export function AuditPage({ notify }: { notify: Notify }) {
  const [query, setQuery] = useState('')
  const resource = useResource(() => api.get<AuditLog[]>('/api/v1/admin/audit-logs?limit=1000'), [])
  if (resource.loading) return <LoadingState />
  if (resource.error || !resource.data) return <ErrorState message={resource.error} retry={resource.refresh} />
  const q = query.toLowerCase().trim()
  const logs = resource.data.filter(item => !q || `${item.action} ${item.resourceType} ${item.resourceId}`.toLowerCase().includes(q))
  return <>
    <PageHeader title="操作审计" description="管理员、Worker 与系统任务的关键状态变更记录。" actions={<Button variant="secondary" icon={<RefreshCw size={16} />} onClick={() => void resource.refresh()}>刷新</Button>} />
    <div className="toolbar"><Input className="input search-input" placeholder="搜索动作或资源" value={query} onChange={event => setQuery(event.target.value)} /></div>
    <Panel className="table-panel">{logs.length === 0 ? <EmptyState title="没有匹配的审计记录" /> : <div className="table-scroll"><table><thead><tr><th>时间</th><th>动作</th><th>资源类型</th><th>资源标识</th><th>操作者</th><th>详情</th></tr></thead><tbody>{logs.map(item => <tr key={item.id}><td>{formatTime(item.occurredAt)}</td><td><strong>{item.action}</strong></td><td>{item.resourceType}</td><td><code>{item.resourceId.slice(0, 18)}</code></td><td>{item.actorUserId?.slice(0, 8) ?? '系统'}</td><td><details><summary>查看</summary><pre>{JSON.stringify(JSON.parse(item.detailsJson), null, 2)}</pre></details></td></tr>)}</tbody></table></div>}</Panel>
  </>
}
