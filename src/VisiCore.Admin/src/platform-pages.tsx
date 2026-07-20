import { useCallback, useEffect, useState, type FormEvent } from 'react'
import { AlertCircle, BellRing, Cpu, Download, KeyRound, LockKeyhole, PackagePlus, Plus, RefreshCw, RotateCw, Server, ShieldCheck, Trash2, Upload } from 'lucide-react'
import { api, formatTime } from './api'
import type { AgentCredentialEnvelope, AgentPublicKeyContract, DeviceCredential, EdgeAgent, EdgeAgentEnrollment, EdgeRelease, PlatformBackup, PlatformDeployment, PlatformDiagnosticCheck, PlatformDiagnosticResult, PlatformOperationsOverview, PlatformServiceStatus } from './types'
import { Badge, Button, Dialog, EmptyState, ErrorState, Field, Form, Input, LoadingState, PageHeader, Panel, Select, Textarea } from './ui'

type Notify = (message: string, tone?: 'good' | 'bad') => void

function useResource<T>(loader: () => Promise<T>, dependencies: readonly unknown[] = []) {
  const [data, setData] = useState<T | null>(null)
  const [error, setError] = useState('')
  const [loading, setLoading] = useState(true)
  const refresh = useCallback(async () => {
    setLoading(true)
    setError('')
    try {
      setData(await loader())
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : '加载失败')
    } finally {
      setLoading(false)
    }
  }, dependencies) // eslint-disable-line react-hooks/exhaustive-deps
  useEffect(() => { void refresh() }, [refresh])
  return { data, error, loading, refresh }
}

function statusKey(value: string | number | null | undefined) {
  return String(value ?? '').trim().toLowerCase()
}

function statusLabel(value: string | number | null | undefined) {
  const labels: Record<string, string> = {
    active: '可用', enabled: '已启用', ready: '就绪', healthy: '健康', online: '在线', connected: '已连接',
    pending: '等待中', enrolling: '配对中', running: '执行中', deploying: '发布中', upgrading: '升级中',
    completed: '已完成', succeeded: '已完成', applied: '已生效',
    disabled: '已停用', inactive: '未启用', offline: '离线', failed: '失败', error: '异常', revoked: '已撤销', stopped: '已停止'
  }
  const key = statusKey(value)
  return labels[key] ?? (value === null || value === undefined || value === '' ? '未上报' : String(value))
}

function statusTone(value: string | number | null | undefined): 'good' | 'bad' | 'warn' | 'neutral' {
  const key = statusKey(value)
  if (['active', 'enabled', 'ready', 'healthy', 'online', 'connected', 'completed', 'succeeded', 'applied'].includes(key)) return 'good'
  if (['disabled', 'inactive', 'offline', 'failed', 'error', 'revoked', 'stopped'].includes(key)) return 'bad'
  if (['pending', 'enrolling', 'running', 'deploying', 'upgrading'].includes(key)) return 'warn'
  return 'neutral'
}

function isActiveEdgeAgent(agent: EdgeAgent) {
  if (agent.disabledAt) return false
  return !['disabled', 'inactive', 'offline', 'failed', 'error', 'revoked', 'stopped'].includes(statusKey(agent.status))
}

function edgeAgentMatchesRelease(agent: EdgeAgent, release: EdgeRelease | null) {
  if (!release || agent.platform !== release.targetPlatform) return false
  const architecture = agent.architecture?.trim().toLowerCase()
  if (release.targetPlatform === 'linux' && release.targetArchitecture === 'amd64') return architecture === 'x64' || architecture === 'amd64'
  return architecture === release.targetArchitecture
}

function isActiveCredential(credential: DeviceCredential) {
  if (credential.disabledAt) return false
  return !['disabled', 'inactive', 'revoked', 'deleted'].includes(statusKey(credential.status))
}

function credentialVersionLabel(credential: DeviceCredential) {
  return credential.activeVersion ? `v${credential.activeVersion}` : credential.version ?? credential.keyVersion ?? '未标记版本'
}

function credentialStatus(credential: DeviceCredential) {
  return credential.disabledAt ? 'disabled' : credential.status ?? 'active'
}

function edgeAgentLastSeen(agent: EdgeAgent) {
  return agent.lastSeenAt ?? agent.lastHeartbeatAt ?? null
}

function edgeAgentStatus(agent: EdgeAgent) {
  if (agent.disabledAt) return 'disabled'
  if (agent.lastDiagnosticSucceeded === false) return 'failed'
  const lastSeen = edgeAgentLastSeen(agent)
  if (!lastSeen) return 'pending'
  return Date.now() - new Date(lastSeen).getTime() <= 120_000 ? 'online' : 'offline'
}

function edgeAgentCapabilities(agent: EdgeAgent) {
  let capabilities = agent.capabilities
  if (!capabilities && agent.capabilitiesJson) {
    try { capabilities = JSON.parse(agent.capabilitiesJson) as string[] | Record<string, unknown> } catch { return [] }
  }
  if (Array.isArray(capabilities)) return capabilities
  if (!capabilities) return []
  if (Array.isArray(capabilities.declared)) return capabilities.declared.filter((item): item is string => typeof item === 'string')
  return Object.entries(capabilities).filter(([, enabled]) => enabled === true).map(([name]) => name)
}

function readList<T>(value: T[] | { items?: T[] } | null | undefined) {
  return Array.isArray(value) ? value : value?.items ?? []
}

function bytesFromBase64(value: string) {
  const normalized = value.trim().replace(/-/g, '+').replace(/_/g, '/')
  const padded = normalized.padEnd(Math.ceil(normalized.length / 4) * 4, '=')
  const source = window.atob(padded)
  const bytes = new Uint8Array(source.length)
  for (let index = 0; index < source.length; index += 1) bytes[index] = source.charCodeAt(index)
  return bytes
}

function bytesToBase64(value: ArrayBuffer | Uint8Array) {
  const bytes = value instanceof Uint8Array ? value : new Uint8Array(value)
  let source = ''
  for (const byte of bytes) source += String.fromCharCode(byte)
  return window.btoa(source)
}

async function sealCredentialForAgent(agentId: string, publicKey: AgentPublicKeyContract, username: string, password: string): Promise<AgentCredentialEnvelope> {
  if (!window.crypto?.subtle || !window.crypto.randomUUID) throw new Error('当前浏览器不支持凭据加密所需的 WebCrypto 能力。')
  if (publicKey.keyEncryptionAlgorithm !== 'RSA-OAEP-256') throw new Error('边缘节点的公钥算法不受支持。')
  const normalizedAgentId = agentId.toLowerCase()
  if (publicKey.agentId.toLowerCase() !== normalizedAgentId) throw new Error('所选边缘节点的公钥不匹配。')
  const credentialVersionId = window.crypto.randomUUID().toLowerCase()
  const encoder = new TextEncoder()
  const additionalData = encoder.encode(JSON.stringify({ agentId: normalizedAgentId, credentialVersionId }))
  const publicCryptoKey = await window.crypto.subtle.importKey(
    'spki',
    bytesFromBase64(publicKey.subjectPublicKeyInfoBase64),
    { name: 'RSA-OAEP', hash: 'SHA-256' },
    false,
    ['encrypt']
  )
  const contentKey = await window.crypto.subtle.generateKey({ name: 'AES-GCM', length: 256 }, true, ['encrypt', 'decrypt']) as CryptoKey
  const initializationVector = window.crypto.getRandomValues(new Uint8Array(12))
  const plaintext = encoder.encode(JSON.stringify({ username, password }))
  const encryptedPayload = new Uint8Array(await window.crypto.subtle.encrypt(
    { name: 'AES-GCM', iv: initializationVector, additionalData, tagLength: 128 },
    contentKey,
    plaintext
  ))
  if (encryptedPayload.length <= 16) throw new Error('凭据加密结果无效。')
  const encryptedKey = await window.crypto.subtle.encrypt(
    { name: 'RSA-OAEP' },
    publicCryptoKey,
    await window.crypto.subtle.exportKey('raw', contentKey)
  )
  return {
    schemaVersion: 1,
    agentId: normalizedAgentId,
    credentialVersionId,
    keyId: publicKey.keyId,
    keyEncryptionAlgorithm: 'RSA-OAEP-256',
    contentEncryptionAlgorithm: 'A256GCM',
    encryptedKeyBase64: bytesToBase64(encryptedKey),
    initializationVectorBase64: bytesToBase64(initializationVector),
    ciphertextBase64: bytesToBase64(encryptedPayload.slice(0, -16)),
    authenticationTagBase64: bytesToBase64(encryptedPayload.slice(-16))
  }
}

export function CredentialsPage({ notify }: { notify: Notify }) {
  const [createOpen, setCreateOpen] = useState(false)
  const [rotating, setRotating] = useState<DeviceCredential | null>(null)
  const resource = useResource(async () => {
    const [credentialResponse, agentResponse] = await Promise.all([
      api.get<DeviceCredential[] | { items?: DeviceCredential[] }>('/api/v1/admin/device-credentials'),
      api.get<EdgeAgent[] | { items?: EdgeAgent[] }>('/api/v1/admin/edge-agents')
    ])
    return { credentials: readList(credentialResponse), agents: readList(agentResponse) }
  }, [])
  if (resource.loading) return <LoadingState />
  if (resource.error || !resource.data) return <ErrorState message={resource.error} retry={resource.refresh} />
  const { credentials, agents } = resource.data
  const agentById = new Map(agents.map(item => [item.id, item]))
  const changeStatus = async (credential: DeviceCredential) => {
    try {
      await api.patch(`/api/v1/admin/device-credentials/${credential.id}/status`, { disabled: isActiveCredential(credential) })
      notify(isActiveCredential(credential) ? '设备凭据已停用' : '设备凭据已启用')
      await resource.refresh()
    } catch (reason) {
      notify(reason instanceof Error ? reason.message : '状态更新失败', 'bad')
    }
  }
  return <>
    <PageHeader title="设备凭据" description="凭据以边缘节点公钥封装，平台不保存或展示可恢复的账号和密码。" actions={<div className="page-header__actions"><Button variant="secondary" icon={<RefreshCw size={16} />} onClick={() => void resource.refresh()}>刷新</Button><Button icon={<Plus size={16} />} onClick={() => setCreateOpen(true)}>新增凭据</Button></div>} />
    <Panel className="table-panel">
      {credentials.length === 0 ? <EmptyState title="尚未登记设备凭据" description="新增后可在直连摄像头中按名称选择。" /> : <div className="table-scroll"><table><thead><tr><th>状态</th><th>凭据名称</th><th>版本</th><th>边缘节点</th><th>引用数量</th><th>最近验证</th><th /></tr></thead><tbody>{credentials.map(credential => {
        const linkedAgents = (credential.agentIds ?? []).map(id => agentById.get(id)?.name ?? id.slice(0, 8))
        return <tr key={credential.id}><td><Badge tone={isActiveCredential(credential) ? 'good' : 'neutral'}>{statusLabel(credentialStatus(credential))}</Badge></td><td><strong>{credential.name}</strong><small>{credential.createdAt ? `创建于 ${formatTime(credential.createdAt)}` : '逻辑凭据'}</small></td><td>{credentialVersionLabel(credential)}</td><td>{linkedAgents.length ? linkedAgents.join('、') : '未绑定节点'}</td><td>{credential.usageCount ?? 0}</td><td>{formatTime(credential.lastVerifiedAt)}<small>{credential.lastVerificationError ? '最近验证异常' : ''}</small></td><td><div className="row-actions"><Button variant="ghost" icon={<RotateCw size={14} />} onClick={() => setRotating(credential)}>轮换</Button><Button variant={isActiveCredential(credential) ? 'danger' : 'ghost'} onClick={() => void changeStatus(credential)}>{isActiveCredential(credential) ? '停用' : '启用'}</Button></div></td></tr>
      })}</tbody></table></div>}
    </Panel>
    <CredentialDialog open={createOpen} agents={agents} onClose={() => setCreateOpen(false)} onSaved={async message => { setCreateOpen(false); notify(message); await resource.refresh() }} notify={notify} />
    <CredentialDialog open={!!rotating} credential={rotating} agents={agents} onClose={() => setRotating(null)} onSaved={async message => { setRotating(null); notify(message); await resource.refresh() }} notify={notify} />
  </>
}

function CredentialDialog({ open, credential, agents, onClose, onSaved, notify }: { open: boolean; credential?: DeviceCredential | null; agents: EdgeAgent[]; onClose: () => void; onSaved: (message: string) => Promise<void>; notify: Notify }) {
  const [agentId, setAgentId] = useState('')
  const [submitting, setSubmitting] = useState(false)
  const eligibleAgents = agents.filter(item => isActiveEdgeAgent(item) && !!item.publicKey)
  const eligibleAgentIds = eligibleAgents.map(item => item.id).join(',')
  useEffect(() => {
    if (!open) return
    const linkedAgentId = credential?.agentIds?.find(id => eligibleAgents.some(agent => agent.id === id))
    setAgentId(linkedAgentId ?? eligibleAgents[0]?.id ?? '')
    setSubmitting(false)
  }, [open, credential, eligibleAgentIds]) // eslint-disable-line react-hooks/exhaustive-deps
  const submit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    const formElement = event.currentTarget
    const form = new FormData(formElement)
    const name = String(form.get('name') ?? '').trim()
    const username = String(form.get('username') ?? '')
    const password = String(form.get('password') ?? '')
    const agent = eligibleAgents.find(item => item.id === agentId)
    if ((!credential && !name) || !username || !password || !agent?.publicKey) {
      notify('请完整填写名称、目标节点、账号和密码。', 'bad')
      return
    }
    setSubmitting(true)
    try {
      const envelope = await sealCredentialForAgent(agent.id, agent.publicKey, username, password)
      formElement.reset()
      const body = { envelope }
      if (credential) {
        await api.post(`/api/v1/admin/device-credentials/${credential.id}/rotate`, body)
        await onSaved('设备凭据已轮换')
      } else {
        await api.post('/api/v1/admin/device-credentials', { name, ...body })
        await onSaved('设备凭据已加密登记')
      }
    } catch (reason) {
      notify(reason instanceof Error ? reason.message : '凭据保存失败', 'bad')
    } finally {
      setSubmitting(false)
    }
  }
  return <Dialog open={open} title={credential ? `轮换设备凭据 · ${credential.name}` : '新增设备凭据'} description="账号和密码只用于生成目标节点可解封的加密信封，不会写入浏览器存储。" onClose={onClose}><Form onSubmit={submit}><div className="form-grid">{credential ? <Field label="凭据名称"><Input value={credential.name} readOnly /></Field> : <Field label="凭据名称"><Input name="name" placeholder="例如 camera-north-01" autoCapitalize="none" required /></Field>}<Field label="目标边缘节点"><Select value={agentId} onChange={event => setAgentId(event.target.value)} required><option value="" disabled>{eligibleAgents.length ? '请选择' : '没有可用节点'}</option>{eligibleAgents.map(agent => <option key={agent.id} value={agent.id}>{agent.name} · {agent.agentVersion ?? agent.version ?? '版本未上报'}</option>)}</Select></Field><Field label="账号"><Input name="username" autoComplete="username" required /></Field><Field label="密码"><Input name="password" type="password" autoComplete="new-password" required /></Field></div><div className="dialog__footer"><Button variant="secondary" type="button" onClick={onClose}>取消</Button><Button disabled={submitting || eligibleAgents.length === 0}>{submitting ? '正在加密' : credential ? '轮换凭据' : '加密登记'}</Button></div></Form></Dialog>
}

export function EdgeAgentsPage({ notify }: { notify: Notify }) {
  const [enrollmentOpen, setEnrollmentOpen] = useState(false)
  const [enrollment, setEnrollment] = useState<EdgeAgentEnrollment | null>(null)
  const [credentialView, setCredentialView] = useState<{ agent: EdgeAgent; credentials: DeviceCredential[] } | null>(null)
  const [diagnostic, setDiagnostic] = useState<{ agent: EdgeAgent; result: PlatformDiagnosticResult } | null>(null)
  const resource = useResource(async () => {
    const [agentResponse, credentialResponse] = await Promise.all([
      api.get<EdgeAgent[] | { items?: EdgeAgent[] }>('/api/v1/admin/edge-agents'),
      api.get<DeviceCredential[] | { items?: DeviceCredential[] }>('/api/v1/admin/device-credentials')
    ])
    return { agents: readList(agentResponse), credentials: readList(credentialResponse) }
  }, [])
  if (resource.loading) return <LoadingState />
  if (resource.error || !resource.data) return <ErrorState message={resource.error} retry={resource.refresh} />
  const { agents, credentials } = resource.data
  const viewCredentials = (agent: EdgeAgent) => setCredentialView({ agent, credentials: credentials.filter(credential => credential.agentIds?.includes(agent.id)) })
  const runDiagnostic = async (agent: EdgeAgent) => {
    try {
      const result = await api.post<PlatformDiagnosticResult>('/api/v1/admin/platform-operations/diagnostics', { edgeAgentId: agent.id, kind: 'host-health' })
      setDiagnostic({ agent, result })
      notify('节点诊断已进入队列')
    } catch (reason) {
      notify(reason instanceof Error ? reason.message : '节点诊断失败', 'bad')
    }
  }
  const enrollmentCode = enrollment?.enrollmentCode ?? enrollment?.enrollmentToken ?? enrollment?.pairingCode ?? enrollment?.token ?? ''
  return <>
    <PageHeader title="边缘节点" description="Docker 与 Windows Edge Agent 统一承载设备接入、预检、诊断和配置回执；现场转流仍由独立媒体部署承担。" actions={<div className="page-header__actions"><Button variant="secondary" icon={<RefreshCw size={16} />} onClick={() => void resource.refresh()}>刷新状态</Button><Button icon={<PackagePlus size={16} />} onClick={() => setEnrollmentOpen(true)}>配对节点</Button></div>} />
    <Panel className="table-panel">
      {agents.length === 0 ? <EmptyState title="尚未配对边缘节点" description="创建一次性注册码后，由已安装的 Linux Agent 完成安全配对。" /> : <div className="table-scroll"><table><thead><tr><th>状态</th><th>节点</th><th>版本 / 能力</th><th>最近心跳</th><th>配置 / 升级</th><th>凭据</th><th /></tr></thead><tbody>{agents.map(agent => {
        const capabilities = edgeAgentCapabilities(agent)
        const state = edgeAgentStatus(agent)
        const configurationStatus = agent.configurationStatus ?? '未上报'
        return <tr key={agent.id}><td><Badge tone={statusTone(state)}>{statusLabel(state)}</Badge></td><td><strong>{agent.name}</strong><small>{`${agent.platform ?? '未知平台'}${agent.architecture ? ` / ${agent.architecture}` : ''}`}</small><small>{agent.publicKey ? '节点公钥已登记' : '等待节点公钥'}</small></td><td>{agent.agentVersion ?? agent.version ?? '版本未上报'}<small>{capabilities.length ? capabilities.join('、') : '能力未上报'}</small></td><td>{formatTime(edgeAgentLastSeen(agent))}</td><td>{agent.configurationVersion ? `v${agent.configurationVersion}` : agent.appliedConfigVersion ?? agent.configVersion ?? '未上报'}<small>{`配置：${statusLabel(configurationStatus)}${agent.configurationFailureSummary ? ` · ${agent.configurationFailureSummary}` : ''}`}</small><small>{agent.lastDiagnosticAt ? `${agent.lastDiagnosticSucceeded === false ? '异常' : '最近诊断'} · ${formatTime(agent.lastDiagnosticAt)}` : '未执行诊断'}</small></td><td>{agent.credentialCount ?? 0}<small>{agent.assignmentCount ?? 0} 个设备分配</small></td><td><div className="row-actions"><Button variant="ghost" onClick={() => viewCredentials(agent)}>凭据</Button><Button variant="ghost" icon={<AlertCircle size={14} />} onClick={() => void runDiagnostic(agent)}>诊断</Button></div></td></tr>
      })}</tbody></table></div>}
    </Panel>
    <EdgeEnrollmentDialog open={enrollmentOpen} onClose={() => setEnrollmentOpen(false)} onCreated={async result => { setEnrollmentOpen(false); setEnrollment(result); await resource.refresh(); notify('一次性配对凭证已生成') }} notify={notify} />
    <EnrollmentCodeDialog code={enrollmentCode} expiresAt={enrollment?.expiresAt ?? null} onClose={() => setEnrollment(null)} notify={notify} />
    <AgentCredentialsDialog value={credentialView} onClose={() => setCredentialView(null)} />
    <DiagnosticResultDialog value={diagnostic} onClose={() => setDiagnostic(null)} />
  </>
}

function EdgeEnrollmentDialog({ open, onClose, onCreated, notify }: { open: boolean; onClose: () => void; onCreated: (result: EdgeAgentEnrollment) => Promise<void>; notify: Notify }) {
  const [submitting, setSubmitting] = useState(false)
  const submit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    const form = new FormData(event.currentTarget)
    setSubmitting(true)
    try {
      const result = await api.post<EdgeAgentEnrollment>('/api/v1/admin/edge-agents/enrollments', { name: form.get('name') })
      await onCreated(result)
    } catch (reason) {
      notify(reason instanceof Error ? reason.message : '配对凭证创建失败', 'bad')
    } finally {
      setSubmitting(false)
    }
  }
  return <Dialog open={open} title="配对边缘节点" description="创建后的凭证仅显示一次，并在过期后自动失效。" onClose={onClose}><Form onSubmit={submit}><Field label="节点名称"><Input name="name" placeholder="例如 warehouse-edge-01" autoCapitalize="none" required /></Field><div className="dialog__footer"><Button variant="secondary" type="button" onClick={onClose}>取消</Button><Button disabled={submitting}>{submitting ? '正在创建' : '创建配对凭证'}</Button></div></Form></Dialog>
}

function EnrollmentCodeDialog({ code, expiresAt, onClose, notify }: { code: string; expiresAt: string | null; onClose: () => void; notify: Notify }) {
  const [copied, setCopied] = useState(false)
  const copy = async () => {
    try {
      await navigator.clipboard.writeText(code)
      setCopied(true)
      notify('配对凭证已复制')
    } catch {
      notify('复制失败，请手动选择配对凭证。', 'bad')
    }
  }
  return <Dialog open={!!code} title="一次性配对凭证" description={expiresAt ? `有效至 ${formatTime(expiresAt)}` : '请在有效期内完成节点配对。'} onClose={onClose}><div className="token-box"><code>{code}</code><Button variant="secondary" icon={<KeyRound size={15} />} onClick={() => void copy()}>{copied ? '已复制' : '复制凭证'}</Button></div><div className="dialog__footer"><Button onClick={onClose}>关闭</Button></div></Dialog>
}

function AgentCredentialsDialog({ value, onClose }: { value: { agent: EdgeAgent; credentials: DeviceCredential[] } | null; onClose: () => void }) {
  return <Dialog open={!!value} title={`节点凭据 · ${value?.agent.name ?? ''}`} description="仅列出逻辑凭据和运行状态，不展示加密信封或任何登录信息。" onClose={onClose}>{value && <div className="dialog-table"><table><thead><tr><th>凭据</th><th>版本</th><th>状态</th><th>最近验证</th></tr></thead><tbody>{value.credentials.length === 0 ? <tr><td colSpan={4} className="dialog-table__empty">当前节点没有可用凭据</td></tr> : value.credentials.map(credential => <tr key={credential.id}><td>{credential.name}</td><td>{credentialVersionLabel(credential)}</td><td><Badge tone={isActiveCredential(credential) ? 'good' : 'neutral'}>{statusLabel(credentialStatus(credential))}</Badge></td><td>{formatTime(credential.lastVerifiedAt)}</td></tr>)}</tbody></table></div>}<div className="dialog__footer"><Button onClick={onClose}>关闭</Button></div></Dialog>
}

function DiagnosticResultDialog({ value, onClose }: { value: { agent: EdgeAgent; result: PlatformDiagnosticResult } | null; onClose: () => void }) {
  const checks = value?.result.checks ?? []
  return <Dialog open={!!value} title={`节点诊断 · ${value?.agent.name ?? ''}`} description={value?.result.summary ?? '诊断结果只包含已脱敏的运行态信息。'} onClose={onClose}>{value && <div className="diagnostic-result"><div className="diagnostic-result__summary"><Badge tone={statusTone(value.result.status)}>{statusLabel(value.result.status)}</Badge><span>{formatTime(value.result.completedAt ?? value.result.requestedAt)}</span></div>{checks.length === 0 ? <EmptyState title="诊断任务已提交" description="节点将在下一次状态上报时补充检查结果。" /> : <div className="compact-list">{checks.map((check: PlatformDiagnosticCheck, index) => <div className="compact-row" key={`${check.name ?? 'check'}-${index}`}><span className={`status-dot status-dot--${statusTone(check.status) === 'good' ? 'good' : statusTone(check.status) === 'bad' ? 'bad' : 'neutral'}`} /><div><strong>{check.name ?? '运行检查'}</strong><small>{check.message ?? '未返回说明'}</small></div><Badge tone={statusTone(check.status)}>{statusLabel(check.status)}</Badge></div>)}</div>}</div>}<div className="dialog__footer"><Button onClick={onClose}>关闭</Button></div></Dialog>
}

export function PlatformOperationsPage({ notify }: { notify: Notify }) {
  const [registerOpen, setRegisterOpen] = useState(false)
  const [deploymentRelease, setDeploymentRelease] = useState<EdgeRelease | null>(null)
  const resource = useResource(async () => {
    const [overview, deploymentResponse, releaseResponse, agentResponse] = await Promise.all([
      api.get<PlatformOperationsOverview>('/api/v1/admin/platform-operations/overview'),
      api.get<PlatformDeployment[] | { items?: PlatformDeployment[] }>('/api/v1/admin/platform-operations/deployments'),
      api.get<EdgeRelease[] | { items?: EdgeRelease[] }>('/api/v1/admin/edge-releases'),
      api.get<EdgeAgent[] | { items?: EdgeAgent[] }>('/api/v1/admin/edge-agents')
    ])
    return { overview, deployments: readList(deploymentResponse), releases: readList(releaseResponse), agents: readList(agentResponse) }
  }, [])
  if (resource.loading) return <LoadingState />
  if (resource.error || !resource.data) return <ErrorState message={resource.error} retry={resource.refresh} />
  const { overview, deployments, releases, agents: registeredEdgeAgents } = resource.data
  const services = (overview.recentOperations ?? []).map<PlatformServiceStatus>(operation => ({ name: operation.operationType ?? '运维任务', status: operation.status, detail: operation.summary, updatedAt: operation.requestedAt }))
  const agents = overview.edgeAgents ?? registeredEdgeAgents
  const registeredAgents = overview.edgeAgentCount ?? agents.length
  const onlineAgents = overview.onlineEdgeAgentCount ?? agents.filter(isActiveEdgeAgent).length
  const unhealthyAgents = overview.unhealthyEdgeAgentCount ?? 0
  const activeDeployments = overview.pendingOperationCount ?? overview.activeDeploymentCount ?? deployments.filter(item => ['pending', 'running', 'deploying', 'upgrading'].includes(statusKey(item.status))).length
  return <>
    <PageHeader title="平台运维" description="集中查看 Docker 与 Windows 边缘节点运行态、诊断任务和受控发布记录。" actions={<div className="page-header__actions"><Button variant="secondary" icon={<RefreshCw size={16} />} onClick={() => void resource.refresh()}>刷新状态</Button><Button icon={<PackagePlus size={16} />} onClick={() => setRegisterOpen(true)}>登记发行版本</Button></div>} />
    <div className="metric-grid operations-metrics"><article className="metric"><Server size={18} /><span>已登记节点</span><strong>{registeredAgents}</strong><small>Docker / Windows Edge Agent</small></article><article className="metric"><Cpu size={18} /><span>在线节点</span><strong>{onlineAgents}</strong><small>两分钟内有心跳</small></article><article className="metric metric--alert"><BellRing size={18} /><span>诊断异常</span><strong>{unhealthyAgents}</strong><small>待进一步处置</small></article><article className="metric"><PackagePlus size={18} /><span>待处理任务</span><strong>{activeDeployments}</strong><small>发布、诊断或连接预检</small></article></div>
    <div className="split-grid operations-grid"><Panel title="近期运维任务" actions={<Badge tone={services.length ? 'info' : 'neutral'}>{services.length}</Badge>}>{services.length === 0 ? <EmptyState title="暂无近期运维任务" description="诊断、预检和发布任务会在此汇总。" /> : <div className="compact-list">{services.map((service: PlatformServiceStatus, index) => <div className="compact-row" key={service.id ?? service.name ?? index}><span className={`status-dot status-dot--${statusTone(service.status) === 'good' ? 'good' : statusTone(service.status) === 'bad' ? 'bad' : 'neutral'}`} /><div><strong>{service.name ?? '未命名任务'}</strong><small>{service.detail ?? '未返回摘要'}</small></div><Badge tone={statusTone(service.status)}>{statusLabel(service.status)}</Badge></div>)}</div>}</Panel><Panel title="当前运行态" actions={<Badge tone={onlineAgents === registeredAgents && registeredAgents > 0 ? 'good' : registeredAgents ? 'warn' : 'neutral'}>{registeredAgents ? `${onlineAgents}/${registeredAgents} 在线` : '未上报'}</Badge>}><div className="operations-facts"><div><span>已登记节点</span><strong>{registeredAgents}</strong></div><div><span>在线节点</span><strong>{onlineAgents}</strong></div><div><span>待处理运维任务</span><strong>{activeDeployments}</strong></div></div></Panel></div>
    <Panel className="table-panel" title="发行版本" actions={<Badge tone={releases.length ? 'info' : 'neutral'}>{releases.length}</Badge>}>{releases.length === 0 ? <EmptyState title="尚未登记发行版本" description="登记包含 SHA-256 与离线签名的发行清单后才能向节点发布。" /> : <div className="table-scroll"><table><thead><tr><th>版本</th><th>目标</th><th>最小 Host Agent</th><th>有效期</th><th /></tr></thead><tbody>{releases.map(release => <tr key={release.id}><td><strong>{release.releaseId}</strong><small>{release.publicKeyId}</small></td><td>{release.targetPlatform} / {release.targetArchitecture}</td><td>{release.minimumHostAgentVersion}</td><td>{formatTime(release.expiresAt)}</td><td><Button variant="ghost" icon={<PackagePlus size={14} />} onClick={() => setDeploymentRelease(release)}>发布</Button></td></tr>)}</tbody></table></div>}</Panel>
    <Panel className="table-panel operation-readiness-panel" title="任务记录" actions={<Badge tone={deployments.length ? 'info' : 'neutral'}>{deployments.length}</Badge>}>{deployments.length === 0 ? <EmptyState title="暂无运维任务记录" description="Host Agent 完成诊断、预检或发布后将在此保留历史。" /> : <div className="table-scroll"><table><thead><tr><th>状态</th><th>任务类型</th><th>任务摘要</th><th>目标节点</th><th>提交时间</th><th>完成时间</th></tr></thead><tbody>{deployments.map(deployment => <tr key={deployment.id}><td><Badge tone={statusTone(deployment.status)}>{statusLabel(deployment.status)}</Badge></td><td>{deployment.operationType ?? deployment.version ?? '运维任务'}</td><td>{deployment.summary ?? deployment.detail ?? '—'}</td><td>{deployment.edgeAgentId?.slice(0, 8) ?? '中心服务'}</td><td>{formatTime(deployment.requestedAt ?? deployment.startedAt)}</td><td>{formatTime(deployment.completedAt)}</td></tr>)}</tbody></table></div>}</Panel>
    <ReleaseRegistrationDialog open={registerOpen} onClose={() => setRegisterOpen(false)} onSaved={async () => { setRegisterOpen(false); notify('发行版本已登记'); await resource.refresh() }} notify={notify} />
    <ReleaseDeploymentDialog release={deploymentRelease} agents={registeredEdgeAgents} onClose={() => setDeploymentRelease(null)} onSaved={async () => { setDeploymentRelease(null); notify('发布任务已进入队列'); await resource.refresh() }} notify={notify} />
  </>
}

function backupSize(value: number) {
  if (value < 1024) return `${value} B`
  if (value < 1024 * 1024) return `${(value / 1024).toFixed(1)} KB`
  if (value < 1024 * 1024 * 1024) return `${(value / 1024 / 1024).toFixed(1)} MB`
  return `${(value / 1024 / 1024 / 1024).toFixed(2)} GB`
}

export function BackupsPage({ notify }: { notify: Notify }) {
  const [uploading, setUploading] = useState(false)
  const [restoring, setRestoring] = useState<PlatformBackup | null>(null)
  const resource = useResource(() => api.get<PlatformBackup[]>('/api/v1/admin/backups'), [])
  const createBackup = async () => {
    try { await api.post('/api/v1/admin/backups'); notify('加密备份已创建'); await resource.refresh() } catch (reason) { notify(reason instanceof Error ? reason.message : '创建备份失败', 'bad') }
  }
  const download = async (backup: PlatformBackup) => {
    try {
      const { blob, fileName } = await api.download(`/api/v1/admin/backups/${backup.id}/download`)
      const url = URL.createObjectURL(blob); const anchor = document.createElement('a'); anchor.href = url; anchor.download = fileName || backup.fileName; anchor.click(); URL.revokeObjectURL(url)
      notify('备份下载已开始')
    } catch (reason) { notify(reason instanceof Error ? reason.message : '下载备份失败', 'bad') }
  }
  const upload = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault(); const file = new FormData(event.currentTarget).get('backup')
    if (!(file instanceof File) || file.size === 0) { notify('请选择有效备份文件。', 'bad'); return }
    const data = new FormData(); data.append('backup', file); setUploading(true)
    try { await api.upload('/api/v1/admin/backups/upload', data); notify('备份文件已上传'); event.currentTarget.reset(); await resource.refresh() } catch (reason) { notify(reason instanceof Error ? reason.message : '上传备份失败', 'bad') } finally { setUploading(false) }
  }
  const remove = async (backup: PlatformBackup) => {
    if (!window.confirm(`确认删除备份“${backup.fileName}”吗？此操作不能撤销。`)) return
    try { await api.delete(`/api/v1/admin/backups/${backup.id}`); notify('备份已删除'); await resource.refresh() } catch (reason) { notify(reason instanceof Error ? reason.message : '删除备份失败', 'bad') }
  }
  if (resource.loading) return <LoadingState />
  if (resource.error || !resource.data) return <ErrorState message={resource.error || '无法读取备份列表。'} retry={resource.refresh} />
  const backups = resource.data
  return <><PageHeader title="数据备份" description="数据库、运行配置与 TLS 证书会加密归档。自动备份每天 03:00 运行并保留最近 30 份。" actions={<div className="page-header__actions"><Button variant="secondary" icon={<RefreshCw size={16} />} onClick={() => void resource.refresh()}>刷新</Button><Button icon={<Plus size={16} />} onClick={() => void createBackup()}>立即备份</Button></div>} /><div className="split-grid"><Panel title="上传可恢复备份"><Form onSubmit={upload}><Field label="加密备份文件" hint="新服务器恢复时可上传此前下载的 .vcbackup 文件。"><Input name="backup" type="file" accept=".vcbackup,application/octet-stream" required /></Field><div className="dialog__footer"><Button icon={<Upload size={15} />} disabled={uploading}>{uploading ? '正在上传' : '上传备份'}</Button></div></Form></Panel><Panel title="恢复密钥"><div className="compact-list"><div className="compact-row"><KeyRound size={16} /><div><strong>离线保存</strong><small>恢复密钥不会写入备份文件，也不会在后台再次显示。</small></div></div><div className="compact-row"><ShieldCheck size={16} /><div><strong>受控恢复</strong><small>恢复前会自动创建当前数据保护点，并重启核心容器。</small></div></div></div></Panel></div><Panel className="table-panel" title="备份历史" actions={<Badge tone={backups.length ? 'info' : 'neutral'}>{backups.length}</Badge>}>{backups.length === 0 ? <EmptyState title="暂无可用备份" description="可立即创建备份，或上传来自其他服务器的加密备份。" /> : <div className="table-scroll"><table><thead><tr><th>类型</th><th>文件</th><th>大小</th><th>创建时间</th><th>保留至</th><th>操作</th></tr></thead><tbody>{backups.map(backup => <tr key={backup.id}><td><Badge tone={backup.kind === 'automatic' ? 'info' : backup.kind === 'uploaded' ? 'warn' : 'neutral'}>{backup.kind === 'automatic' ? '自动' : backup.kind === 'uploaded' ? '上传' : backup.kind === 'protection' ? '保护点' : '手动'}</Badge></td><td><strong>{backup.fileName}</strong><small>{backup.sha256.slice(0, 16)}...</small></td><td>{backupSize(backup.sizeBytes)}</td><td>{formatTime(backup.createdAt)}</td><td>{backup.retainUntil ? formatTime(backup.retainUntil) : '手动管理'}</td><td><div className="table-actions"><Button variant="ghost" icon={<Download size={15} />} onClick={() => void download(backup)}>下载</Button><Button variant="ghost" icon={<RotateCw size={15} />} onClick={() => setRestoring(backup)}>恢复</Button><Button variant="ghost" icon={<Trash2 size={15} />} onClick={() => void remove(backup)}>删除</Button></div></td></tr>)}</tbody></table></div>}</Panel><BackupRestoreDialog backup={restoring} onClose={() => setRestoring(null)} onSubmitted={() => notify('恢复任务已提交，核心容器将自动重启。')} notify={notify} /></>
}

function BackupRestoreDialog({ backup, onClose, onSubmitted, notify }: { backup: PlatformBackup | null; onClose: () => void; onSubmitted: () => void; notify: Notify }) {
  const [submitting, setSubmitting] = useState(false)
  const submit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault(); if (!backup) return
    const form = new FormData(event.currentTarget); setSubmitting(true)
    try { await api.post(`/api/v1/admin/backups/${backup.id}/restore`, { recoveryKey: form.get('recoveryKey'), confirmation: form.get('confirmation') }); onSubmitted(); onClose() } catch (reason) { notify(reason instanceof Error ? reason.message : '提交恢复失败', 'bad') } finally { setSubmitting(false) }
  }
  return <Dialog open={backup !== null} title="恢复平台备份" description="恢复会中断当前连接、替换数据库和核心配置，并使现有登录会话失效。" onClose={onClose}><Form onSubmit={submit}><Field label="恢复密钥"><Input name="recoveryKey" type="password" minLength={32} autoComplete="off" required /></Field><Field label={`确认文本：恢复 ${backup?.id ?? ''}`}><Input name="confirmation" autoComplete="off" required /></Field><div className="dialog__footer"><Button variant="secondary" type="button" onClick={onClose}>取消</Button><Button variant="danger" icon={<RotateCw size={15} />} disabled={submitting}>{submitting ? '正在提交' : '确认恢复'}</Button></div></Form></Dialog>
}

function ReleaseRegistrationDialog({ open, onClose, onSaved, notify }: { open: boolean; onClose: () => void; onSaved: () => Promise<void>; notify: Notify }) {
  const [submitting, setSubmitting] = useState(false)
  const submit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    const form = new FormData(event.currentTarget)
    setSubmitting(true)
    try {
      await api.post('/api/v1/admin/edge-releases', { manifestJson: form.get('manifestJson'), signatureBase64: form.get('signatureBase64'), publicKeyId: form.get('publicKeyId') })
      await onSaved()
    } catch (reason) {
      notify(reason instanceof Error ? reason.message : '发行版本登记失败', 'bad')
    } finally {
      setSubmitting(false)
    }
  }
  return <Dialog open={open} title="登记发行版本" description="只接受目标平台、架构、不可变 HTTPS 制品地址、SHA-256、有效期和离线签名完整匹配的清单。" onClose={onClose}><Form onSubmit={submit}><Field label="发行公钥标识"><Input name="publicKeyId" required /></Field><Field label="签名（Base64）"><Textarea name="signatureBase64" rows={3} required /></Field><Field label="发行清单"><Textarea name="manifestJson" rows={9} required /></Field><div className="dialog__footer"><Button variant="secondary" type="button" onClick={onClose}>取消</Button><Button disabled={submitting}>{submitting ? '正在登记' : '登记'}</Button></div></Form></Dialog>
}

function ReleaseDeploymentDialog({ release, agents, onClose, onSaved, notify }: { release: EdgeRelease | null; agents: EdgeAgent[]; onClose: () => void; onSaved: () => Promise<void>; notify: Notify }) {
  const [agentId, setAgentId] = useState('')
  const eligibleAgents = agents.filter(agent => isActiveEdgeAgent(agent) && edgeAgentMatchesRelease(agent, release))
  useEffect(() => { setAgentId(eligibleAgents[0]?.id ?? '') }, [release?.id]) // eslint-disable-line react-hooks/exhaustive-deps
  const submit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    if (!release || !agentId) return
    try {
      await api.post('/api/v1/admin/platform-operations/deployments', { edgeAgentId: agentId, releaseId: release.releaseId })
      await onSaved()
    } catch (reason) {
      notify(reason instanceof Error ? reason.message : '发布任务创建失败', 'bad')
    }
  }
  return <Dialog open={!!release} title={`发布 ${release?.releaseId ?? ''}`} description="Host Agent 会独立验证签名、有效期、平台、架构和制品摘要；未通过验证不会执行升级。" onClose={onClose}><Form onSubmit={submit}><Field label="目标边缘节点"><Select value={agentId} onChange={event => setAgentId(event.target.value)} required><option value="" disabled>{eligibleAgents.length ? '请选择' : '没有匹配平台与架构的在线节点'}</option>{eligibleAgents.map(agent => <option key={agent.id} value={agent.id}>{agent.name} · {agent.platform}/{agent.architecture ?? '未知架构'}</option>)}</Select></Field><div className="dialog__footer"><Button variant="secondary" type="button" onClick={onClose}>取消</Button><Button disabled={!agentId}>确认发布</Button></div></Form></Dialog>
}

type HttpsCertificateStatus = {
  source: 'uploaded' | 'deployment' | 'missing' | string
  present: boolean
  privateKeyMatches: boolean
  subject: string | null
  subjectAlternativeNames: string[]
  issuer: string | null
  fingerprintSha256: string | null
  notBefore: string | null
  notAfter: string | null
}

type HttpsConfigurationStatus = {
  currentEnabled: boolean
  currentPublicBaseUri: string | null
  pendingEnabled: boolean
  pendingPublicBaseUri: string | null
  currentCertificate: HttpsCertificateStatus
  pendingCertificate: HttpsCertificateStatus
  restartRequired: boolean
}

function certificateSourceLabel(source: string) {
  if (source === 'uploaded') return '核心配置卷'
  if (source === 'deployment') return '部署目录兼容证书'
  return '未配置'
}

function certificateExpiryLabel(value: string | null) {
  if (!value) return '未提供'
  const days = Math.ceil((new Date(value).getTime() - Date.now()) / 86_400_000)
  return days < 0 ? `已过期 ${Math.abs(days)} 天` : days <= 30 ? `${days} 天后到期` : formatTime(value)
}

function CertificateMetadata({ certificate, title }: { certificate: HttpsCertificateStatus; title: string }) {
  const warning = certificate.notAfter && new Date(certificate.notAfter).getTime() - Date.now() <= 30 * 86_400_000
  return <Panel title={title} actions={<Badge tone={!certificate.present ? 'neutral' : certificate.privateKeyMatches ? 'good' : 'bad'}>{certificateSourceLabel(certificate.source)}</Badge>}>
    <div className="compact-list https-certificate">
      <div className="compact-row"><ShieldCheck size={15} /><div><strong>{certificate.subject ?? '未检测到证书'}</strong><small>{certificate.issuer ? `颁发者：${certificate.issuer}` : '上传 PEM 证书链与私钥后显示元信息'}</small></div><Badge tone={certificate.privateKeyMatches ? 'good' : 'neutral'}>{certificate.privateKeyMatches ? '私钥匹配' : '未校验'}</Badge></div>
      <div className="compact-row"><LockKeyhole size={15} /><div><strong>SHA-256 指纹</strong><small className="https-fingerprint">{certificate.fingerprintSha256 ?? '—'}</small></div><Badge tone={warning ? 'warn' : certificate.present ? 'good' : 'neutral'}>{certificateExpiryLabel(certificate.notAfter)}</Badge></div>
      <div className="compact-row"><Server size={15} /><div><strong>主题备用名称</strong><small>{certificate.subjectAlternativeNames?.length ? certificate.subjectAlternativeNames.join('、') : '未声明'}</small></div><Badge tone="neutral">{certificate.notBefore ? `生效：${formatTime(certificate.notBefore)}` : '—'}</Badge></div>
    </div>
  </Panel>
}

export function HttpsConfigurationPage({ notify }: { notify: Notify }) {
  const resource = useResource(() => api.get<HttpsConfigurationStatus>('/api/v1/admin/https-configuration'), [])
  const [enabled, setEnabled] = useState(false)
  const [publicBaseUri, setPublicBaseUri] = useState('')
  const [certificateFile, setCertificateFile] = useState<File | null>(null)
  const [privateKeyFile, setPrivateKeyFile] = useState<File | null>(null)
  const [saving, setSaving] = useState(false)
  const [uploading, setUploading] = useState(false)
  const [confirmApply, setConfirmApply] = useState(false)
  const [restarting, setRestarting] = useState(false)
  const [restartError, setRestartError] = useState('')

  useEffect(() => {
    if (!resource.data) return
    setEnabled(resource.data.pendingEnabled)
    setPublicBaseUri(resource.data.pendingPublicBaseUri ?? '')
  }, [resource.data])

  useEffect(() => {
    if (!restarting) return
    const target = resource.data?.pendingPublicBaseUri ?? window.location.origin
    const deadline = Date.now() + 90_000
    const timer = window.setInterval(() => {
      void fetch('/readyz', { cache: 'no-store' })
        .then(response => {
          if (!response.ok) return
          window.clearInterval(timer)
          const destination = new URL('/admin', target).toString()
          window.location.assign(destination)
        })
        .catch(() => {
          // 容器退出和 Docker 拉起期间会短暂断连，继续轮询即可。
        })
      if (Date.now() >= deadline) {
        window.clearInterval(timer)
        setRestartError('中心服务在预期时间内没有恢复健康，请检查容器日志和证书文件。')
        setRestarting(false)
      }
    }, 1500)
    return () => window.clearInterval(timer)
  }, [restarting, resource.data?.pendingPublicBaseUri])

  if (resource.loading) return <LoadingState />
  if (resource.error || !resource.data) return <ErrorState message={resource.error || '无法读取 HTTPS 配置。'} retry={resource.refresh} />
  const status = resource.data

  const save = async () => {
    setSaving(true)
    try {
      await api.put<HttpsConfigurationStatus>('/api/v1/admin/https-configuration', { enabled, publicBaseUri: enabled ? publicBaseUri : null })
      await resource.refresh()
      notify('HTTPS 配置已保存为待应用项')
    } catch (reason) {
      notify(reason instanceof Error ? reason.message : 'HTTPS 配置保存失败', 'bad')
    } finally {
      setSaving(false)
    }
  }

  const uploadCertificate = async () => {
    if (!certificateFile || !privateKeyFile) {
      notify('请选择 PEM 证书链和未加密 PEM 私钥。', 'bad')
      return
    }
    const form = new FormData()
    form.append('certificate', certificateFile)
    form.append('privateKey', privateKeyFile)
    setUploading(true)
    try {
      await api.upload<HttpsConfigurationStatus>('/api/v1/admin/https-configuration/certificate', form)
      setCertificateFile(null)
      setPrivateKeyFile(null)
      await resource.refresh()
      notify('证书已校验并保存为待应用项')
    } catch (reason) {
      notify(reason instanceof Error ? reason.message : '证书上传失败', 'bad')
    } finally {
      setUploading(false)
    }
  }

  const apply = async () => {
    setConfirmApply(false)
    setRestartError('')
    try {
      await api.post<{ state: string }>('/api/v1/admin/https-configuration/apply')
      setRestarting(true)
      notify('中心容器正在重启并应用 HTTPS 配置')
    } catch (reason) {
      notify(reason instanceof Error ? reason.message : 'HTTPS 配置应用失败', 'bad')
    }
  }

  return <>
    <PageHeader title="中心 HTTPS" description="配置公网 HTTPS 地址与中心证书；所有改动保存为待应用项，确认后才会重启核心容器。" actions={<Button variant="secondary" icon={<RefreshCw size={16} />} onClick={() => void resource.refresh()}>刷新状态</Button>} />
    {restarting && <div className="form-notice">正在等待核心容器恢复健康，恢复后将跳转到新的 HTTPS 管理地址。</div>}
    {restartError && <div className="form-notice form-notice--error">{restartError}</div>}
    <div className="split-grid">
      <Panel title="监听与待应用状态" actions={<Badge tone={status.restartRequired ? 'warn' : status.currentEnabled ? 'good' : 'neutral'}>{status.restartRequired ? '待应用' : status.currentEnabled ? '已启用' : '未启用'}</Badge>}>
        <div className="compact-list">
          <div className="compact-row"><LockKeyhole size={15} /><div><strong>当前监听</strong><small>{status.currentEnabled ? status.currentPublicBaseUri : 'HTTP 模式'}</small></div><Badge tone={status.currentEnabled ? 'good' : 'neutral'}>{status.currentEnabled ? 'HTTPS' : 'HTTP'}</Badge></div>
          <div className="compact-row"><Server size={15} /><div><strong>待应用公网地址</strong><small>{status.pendingEnabled ? status.pendingPublicBaseUri : '禁用后恢复首次安装公网地址'}</small></div><Badge tone={status.pendingEnabled ? 'warn' : 'neutral'}>{status.pendingEnabled ? 'HTTPS' : '回退'}</Badge></div>
          <div className="compact-row"><ShieldCheck size={15} /><div><strong>流网关地址</strong><small>{status.pendingEnabled && status.pendingPublicBaseUri ? new URL('stream/', status.pendingPublicBaseUri).toString() : '使用首次安装地址'}</small></div><Badge tone={status.pendingEnabled ? 'good' : 'neutral'}>{status.pendingEnabled ? '受保护' : '默认'}</Badge></div>
        </div>
      </Panel>
      <Panel title="HTTPS 配置" actions={<Badge tone={enabled ? 'good' : 'neutral'}>{enabled ? '准备启用' : '准备禁用'}</Badge>}>
        <Form onSubmit={event => { event.preventDefault(); void save() }}>
          <label className="check"><input type="checkbox" checked={enabled} onChange={event => setEnabled(event.target.checked)} />启用 HTTPS 公网访问</label>
          <Field label="HTTPS 公网基础地址" hint="仅接受根路径 HTTPS 地址，例如 https://center.example.com/"><Input value={publicBaseUri} onChange={event => setPublicBaseUri(event.target.value)} placeholder="https://center.example.com/" disabled={!enabled} required={enabled} inputMode="url" /></Field>
          <div className="dialog__footer"><Button disabled={saving || restarting}>{saving ? '正在保存' : '保存待应用配置'}</Button></div>
        </Form>
      </Panel>
      <CertificateMetadata title="当前证书" certificate={status.currentCertificate} />
      <CertificateMetadata title="待应用证书" certificate={status.pendingCertificate} />
    </div>
    <div className="split-grid https-actions">
      <Panel title="上传 PEM 证书">
        <Form onSubmit={event => { event.preventDefault(); void uploadCertificate() }}>
          <Field label="证书链 tls.crt" hint="PEM 完整证书链，最大 1 MiB。"><Input type="file" accept=".crt,.pem,application/x-pem-file" onChange={event => setCertificateFile(event.target.files?.[0] ?? null)} /></Field>
          <Field label="私钥 tls.key" hint="未加密 PEM 私钥，最大 1 MiB。"><Input type="file" accept=".key,.pem,application/x-pem-file" onChange={event => setPrivateKeyFile(event.target.files?.[0] ?? null)} /></Field>
          <div className="selected-file">{certificateFile && privateKeyFile ? `已选择：${certificateFile.name}，${privateKeyFile.name}` : '尚未选择证书文件'}</div>
          <div className="dialog__footer"><Button icon={<Upload size={16} />} disabled={uploading || restarting}>{uploading ? '正在校验并保存' : '上传为待应用证书'}</Button></div>
        </Form>
      </Panel>
      <Panel title="应用变更">
        <div className="compact-list">
          <div className="compact-row"><ShieldCheck size={15} /><div><strong>重启前复核</strong><small>{status.pendingEnabled ? '将重新校验公网地址、证书有效期与私钥匹配关系。' : '将移除 HTTPS 公网覆盖并恢复首次安装地址。'}</small></div><Badge tone={status.restartRequired ? 'warn' : 'neutral'}>{status.restartRequired ? '需要重启' : '无变更'}</Badge></div>
          <div className="compact-row"><LockKeyhole size={15} /><div><strong>私钥保护</strong><small>私钥不会出现在响应、审计记录、数据库或浏览器本地存储中。</small></div><Badge tone="good">受限保存</Badge></div>
        </div>
        <div className="panel-action-footer"><Button variant="danger" disabled={!status.restartRequired || restarting} onClick={() => setConfirmApply(true)}>{restarting ? '正在等待重启' : '应用并重启中心'}</Button></div>
      </Panel>
    </div>
    <Dialog open={confirmApply} title="应用 HTTPS 配置并重启中心" description="核心容器将停止并由 Docker 自动重启。恢复健康后，当前会话会跳转到待应用的管理地址。" onClose={() => setConfirmApply(false)}>
      <div className="dialog__footer"><Button variant="secondary" onClick={() => setConfirmApply(false)}>取消</Button><Button variant="danger" onClick={() => void apply()}>确认应用并重启</Button></div>
    </Dialog>
  </>
}
