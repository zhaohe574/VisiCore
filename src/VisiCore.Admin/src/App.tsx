import { useEffect, useRef, useState, type FormEvent } from 'react'
import { Activity, AlertTriangle, BellRing, Boxes, ChevronRight, CircleUserRound, Cpu, Database, FileDown, Gauge, KeyRound, LoaderCircle, LockKeyhole, LogOut, Menu, PlugZap, ShieldAlert, ShieldCheck, UsersRound, Wrench, X } from 'lucide-react'
import { api, session, type SetupDefaults, type SetupRequest } from './api'
import type { Section } from './types'
import { Button, Input, Select } from './ui'
import { AccessPage, AlertsPage, AssetsPage, AuditPage, ExportsPage, OverviewPage, PluginsPage } from './pages'
import { BackupsPage, CredentialsPage, EdgeAgentsPage, HttpsConfigurationPage, PlatformOperationsPage } from './platform-pages'
import { PublicOfflineDevicesPage } from './public-offline-devices'
import { ObservabilityPage } from './features/observability/ObservabilityPage'
import { sectionFromPath, sectionPath } from './routes'

const navigation: Array<{ id: Section; label: string; icon: typeof Activity }> = [
  { id: 'overview', label: '运行总览', icon: Activity },
  { id: 'observability', label: '运行指标', icon: Gauge },
  { id: 'credentials', label: '设备凭据', icon: KeyRound },
  { id: 'edgeAgents', label: '边缘节点', icon: Cpu },
  { id: 'operations', label: '平台运维', icon: Wrench },
  { id: 'backups', label: '数据备份', icon: Database },
  { id: 'https', label: '中心 HTTPS', icon: LockKeyhole },
  { id: 'assets', label: '资产与区域', icon: Boxes },
  { id: 'plugins', label: '设备插件', icon: PlugZap },
  { id: 'access', label: '账号与权限', icon: UsersRound },
  { id: 'exports', label: '录像导出', icon: FileDown },
  { id: 'alerts', label: '告警与通知', icon: BellRing },
  { id: 'audit', label: '操作审计', icon: ShieldCheck }
]

function isAdminPath(pathname: string) {
  const normalizedPath = pathname.replace(/\/+$/, '') || '/'
  return normalizedPath === '/admin' || normalizedPath.startsWith('/admin/')
}

function isPublicOfflinePath(pathname: string) {
  const normalizedPath = pathname.replace(/\/+$/, '') || '/'
  return normalizedPath === '/' || normalizedPath === '/offline-devices'
}

export default function App() {
  const [authenticated, setAuthenticated] = useState(!!session.token() && !session.requiresPasswordChange())
  const [passwordChangeRequired, setPasswordChangeRequired] = useState(!!session.token() && session.requiresPasswordChange())
  const [setupState, setSetupState] = useState<'loading' | 'unconfigured' | 'initializing' | 'completed' | 'error'>('loading')
  const [setupDefaults, setSetupDefaults] = useState<SetupDefaults | null>(null)
  const [setupError, setSetupError] = useState('')
  const [setupRecoveryKey, setSetupRecoveryKey] = useState<string | null>(null)
  const [active, setActive] = useState<Section>(() => sectionFromPath(window.location.pathname))
  const [canManageHttps, setCanManageHttps] = useState(false)
  const [menuOpen, setMenuOpen] = useState(false)
  const [toast, setToast] = useState<{ message: string; tone: 'good' | 'bad' } | null>(null)
  useEffect(() => {
    const expired = () => { setAuthenticated(false); setPasswordChangeRequired(false) }
    window.addEventListener('platform-auth-expired', expired)
    return () => window.removeEventListener('platform-auth-expired', expired)
  }, [])
  useEffect(() => {
    if (!toast) return
    const timer = window.setTimeout(() => setToast(null), 3600)
    return () => window.clearTimeout(timer)
  }, [toast])
  useEffect(() => {
    const onPopState = () => setActive(sectionFromPath(window.location.pathname))
    window.addEventListener('popstate', onPopState)
    return () => window.removeEventListener('popstate', onPopState)
  }, [])
  const loadSetupStatus = async () => {
    try {
      const status = await api.setupStatus()
      setSetupDefaults(status.defaults)
      setSetupState(status.state)
      setSetupError('')
    } catch (reason) {
      setSetupState('error')
      setSetupError(reason instanceof Error ? reason.message : '无法读取初始化状态。')
    }
  }
  useEffect(() => { void loadSetupStatus() }, [])
  useEffect(() => {
    if (!authenticated || passwordChangeRequired) {
      setCanManageHttps(false)
      return
    }
    let disposed = false
    void api.get('/api/v1/admin/https-configuration')
      .then(() => { if (!disposed) setCanManageHttps(true) })
      .catch(() => { if (!disposed) setCanManageHttps(false) })
    return () => { disposed = true }
  }, [authenticated, passwordChangeRequired])
  useEffect(() => {
    if (setupState !== 'initializing') return
    const timer = window.setInterval(() => {
      void api.setupStatus()
        .then(status => {
          if (status.state === 'completed') window.location.assign('/admin')
        })
        .catch(() => {
          // API 主动退出期间会短暂断开，等待 Docker 重启后继续轮询。
        })
    }, 1500)
    return () => window.clearInterval(timer)
  }, [setupState])
  const notify = (message: string, tone: 'good' | 'bad' = 'good') => setToast({ message, tone })
  const select = (section: Section) => {
    const nextPath = sectionPath(section)
    if (window.location.pathname !== nextPath) window.history.pushState({}, '', nextPath)
    setActive(section)
    setMenuOpen(false)
  }
  const adminPath = isAdminPath(window.location.pathname)
  const publicOfflinePath = isPublicOfflinePath(window.location.pathname)
  const logout = async () => {
    try { await api.logout() } catch { /* 本地会话仍需清除。 */ }
    session.clear(); setAuthenticated(false); setPasswordChangeRequired(false)
  }
  if (setupState === 'loading') return <BootstrapLoadingPage />
  if (setupState === 'error') return <BootstrapErrorPage message={setupError} retry={() => void loadSetupStatus()} />
  if (setupState === 'initializing') return <BootstrapRestartPage recoveryKey={setupRecoveryKey} />
  if (setupState === 'unconfigured' && setupDefaults) return <SetupPage defaults={setupDefaults} onSubmitted={key => { setSetupRecoveryKey(key); setSetupState('initializing') }} />
  if (publicOfflinePath) return <PublicOfflineDevicesPage />
  if (!adminPath) return <NotFoundPage />
  if (passwordChangeRequired) return <RequiredPasswordChangePage onFinished={() => { session.clear(); setPasswordChangeRequired(false); setAuthenticated(false) }} />
  if (!authenticated) return <LoginPage onLogin={required => { setPasswordChangeRequired(required); setAuthenticated(!required) }} />
  const page = active === 'overview' ? <OverviewPage notify={notify} />
    : active === 'observability' ? <ObservabilityPage />
    : active === 'credentials' ? <CredentialsPage notify={notify} />
      : active === 'edgeAgents' ? <EdgeAgentsPage notify={notify} />
        : active === 'operations' ? <PlatformOperationsPage notify={notify} />
          : active === 'backups' ? <BackupsPage notify={notify} />
            : active === 'https' && canManageHttps ? <HttpsConfigurationPage notify={notify} />
            : active === 'assets' ? <AssetsPage notify={notify} />
            : active === 'plugins' ? <PluginsPage notify={notify} />
              : active === 'access' ? <AccessPage notify={notify} />
                : active === 'exports' ? <ExportsPage notify={notify} />
                  : active === 'alerts' ? <AlertsPage notify={notify} />
                    : <AuditPage notify={notify} />
  return <div className="app-shell">
    <aside className={`sidebar ${menuOpen ? 'sidebar--open' : ''}`}>
      <div className="brand"><div className="brand__mark"><Activity size={20} /></div><div><strong>视枢</strong><span>CONTROL PLANE</span></div><button className="sidebar-close" aria-label="关闭导航" onClick={() => setMenuOpen(false)}><X size={19} /></button></div>
      <nav>{navigation.filter(item => item.id !== 'https' || canManageHttps).map(item => { const Icon = item.icon; return <button key={item.id} className={active === item.id ? 'active' : ''} onClick={() => select(item.id)}><Icon size={18} /><span>{item.label}</span>{active === item.id && <ChevronRight size={15} />}</button> })}</nav>
      <div className="sidebar__footer"><div className="environment"><span className="status-dot status-dot--good" /><div><strong>中心 API</strong><small>统一运维控制面</small></div></div><button onClick={() => void logout()}><LogOut size={17} />退出登录</button></div>
    </aside>
    {menuOpen && <button className="sidebar-scrim" aria-label="关闭导航" onClick={() => setMenuOpen(false)} />}
    <main className="main-shell"><header className="topbar"><button className="menu-trigger" aria-label="打开导航" onClick={() => setMenuOpen(true)}><Menu size={20} /></button><div className="breadcrumb"><span>控制平面</span><ChevronRight size={14} /><strong>{navigation.find(item => item.id === active)?.label ?? '运行总览'}</strong></div><div className="operator"><CircleUserRound size={18} /><span>{session.username()}</span></div></header><div className="page-content">{page}</div></main>
    {toast && <div className={`toast toast--${toast.tone}`} role="status">{toast.tone === 'good' ? <ShieldCheck size={18} /> : <BellRing size={18} />}{toast.message}</div>}
  </div>
}

function BootstrapLoadingPage() {
  return <main className="setup-page setup-page--loading"><LoaderCircle className="spin" size={24} /><span>正在检查视枢运行状态</span></main>
}

function BootstrapErrorPage({ message, retry }: { message: string; retry: () => void }) {
  return <main className="setup-page setup-page--loading"><AlertTriangle size={25} /><strong>无法连接初始化服务</strong><span>{message}</span><Button variant="secondary" type="button" onClick={retry}>重新检查</Button></main>
}

function BootstrapRestartPage({ recoveryKey }: { recoveryKey: string | null }) {
  return <main className="setup-page setup-page--loading"><LoaderCircle className="spin" size={26} /><strong>正在应用视枢配置</strong><span>核心容器会自动重启。请保持此页面打开，完成后将进入管理员登录页。</span>{recoveryKey && <section className="setup-recovery-key"><strong>恢复密钥</strong><code>{recoveryKey}</code><span>此密钥只显示一次。请离线保存；新服务器恢复备份时必须输入。</span></section>}</main>
}

function SetupPage({ defaults, onSubmitted }: { defaults: SetupDefaults; onSubmitted: (recoveryKey: string | null) => void }) {
  const [mode, setMode] = useState<'install' | 'restore'>('install')
  const [publicBaseUri, setPublicBaseUri] = useState(`${window.location.origin}/`)
  const [httpAcknowledged, setHttpAcknowledged] = useState(false)
  const [archive, setArchive] = useState<File | null>(null)
  const [recoveryKey, setRecoveryKey] = useState('')
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState('')
  const currentOriginUsesHttp = window.location.protocol === 'http:'
  const submitInstall = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    const form = new FormData(event.currentTarget)
    const password = String(form.get('platformAdministratorPassword') ?? '')
    if (password !== String(form.get('platformAdministratorPasswordConfirmation') ?? '')) { setError('两次输入的系统管理员密码不一致。'); return }
    if (currentOriginUsesHttp && !httpAcknowledged && publicBaseUri.startsWith('http:')) { setError('请确认局域网 HTTP 的明文传输风险，或改用 HTTPS 访问地址。'); return }
    setLoading(true); setError('')
    try {
      const result = await api.initialize({ publicBaseUri, platformAdministratorUsername: String(form.get('platformAdministratorUsername') ?? ''), platformAdministratorPassword: password, allowInsecureLanHttp: currentOriginUsesHttp && httpAcknowledged })
      onSubmitted(result.recoveryKey ?? null)
    } catch (reason) { setError(reason instanceof Error ? reason.message : '初始化失败，请检查核心容器状态后重试。') } finally { setLoading(false) }
  }
  const submitRestore = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    if (!archive || recoveryKey.trim().length < 32) { setError('请选择有效备份文件并输入恢复密钥。'); return }
    const data = new FormData(); data.append('backup', archive); data.append('recoveryKey', recoveryKey.trim())
    setLoading(true); setError('')
    try { await api.restoreSetup(data); onSubmitted(null) } catch (reason) { setError(reason instanceof Error ? reason.message : '备份恢复请求失败。') } finally { setLoading(false) }
  }
  return <main className="setup-page"><section className="setup-brand"><div className="login-brand__line"><Activity size={22} /><span>VISICORE</span></div><h1>视枢初始化</h1><p>核心容器已内置 PostgreSQL 与 MediaMTX。完成管理员配置后即可进入控制台。</p><div className="setup-brand__status"><span className="status-dot status-dot--good" />核心容器引导态</div></section><section className="setup-content"><div className="setup-form"><header><Database size={23} /><div><h2>{mode === 'install' ? '首次安装配置' : '从加密备份恢复'}</h2><p>{mode === 'install' ? '数据库和媒体服务由核心容器自动配置。' : '恢复会写入数据库、运行配置和 TLS 证书，并重启核心容器。'}</p></div></header><div className="setup-mode" role="group" aria-label="初始化方式"><button type="button" className={mode === 'install' ? 'active' : ''} onClick={() => { setMode('install'); setError('') }}>全新安装</button><button type="button" className={mode === 'restore' ? 'active' : ''} onClick={() => { setMode('restore'); setError('') }}>恢复备份</button></div>{mode === 'install' ? <form className="form setup-section" onSubmit={submitInstall}><label><span>视枢地址</span><Input type="url" value={publicBaseUri} onChange={event => setPublicBaseUri(event.target.value)} autoComplete="url" required /></label>{currentOriginUsesHttp && <label className="setup-risk"><input type="checkbox" checked={httpAcknowledged} onChange={event => setHttpAcknowledged(event.target.checked)} /><span><ShieldAlert size={17} />我理解局域网 HTTP 会明文传输登录密码和会话，仅在受控网络中使用。</span></label>}<div className="setup-grid"><label><span>首个管理员账号</span><Input name="platformAdministratorUsername" defaultValue={defaults.platformAdministratorUsername} autoComplete="username" required /><small>支持邮箱，邮箱登录不区分大小写；普通账号可使用数字开头。</small></label><label><span>管理员密码</span><Input name="platformAdministratorPassword" type="password" minLength={12} maxLength={256} autoComplete="new-password" required /></label><label><span>确认密码</span><Input name="platformAdministratorPasswordConfirmation" type="password" minLength={12} maxLength={256} autoComplete="new-password" required /></label></div><div className="setup-actions"><span>恢复密钥会在提交成功后仅展示一次。</span><Button disabled={loading}>{loading ? '正在初始化' : '完成初始化'}</Button></div></form> : <form className="form setup-section" onSubmit={submitRestore}><label><span>加密备份文件</span><Input type="file" accept=".vcbackup,application/octet-stream" onChange={event => setArchive(event.target.files?.[0] ?? null)} required /></label><label><span>恢复密钥</span><Input type="password" value={recoveryKey} onChange={event => setRecoveryKey(event.target.value)} autoComplete="off" minLength={32} required /></label><div className="setup-actions"><span>恢复成功后使用备份中的管理员账号登录。</span><Button disabled={loading}>{loading ? '正在提交恢复' : '恢复到此核心容器'}</Button></div></form>}{error && <div className="setup-error"><AlertTriangle size={17} />{error}</div>}<footer><span>内置服务仅监听核心容器回环地址；Docker 只发布管理端 HTTP 与 HTTPS 端口。</span></footer></div></section></main>
}

function LegacySetupPage({ defaults, onSubmitted }: { defaults: SetupDefaults; onSubmitted: () => void }) {
  return null
}
/*
  const formRef = useRef<HTMLFormElement>(null)
  const [step, setStep] = useState<1 | 2 | 3>(1)
  const [postgresTested, setPostgresTested] = useState(false)
  const [mediaTested, setMediaTested] = useState(false)
  const [mediaMode, setMediaMode] = useState<'same-host' | 'remote'>(defaults.mediaMode)
  const [mediaApiBaseUri, setMediaApiBaseUri] = useState(defaults.mediaApiBaseUri)
  const [mediaHlsBaseUri, setMediaHlsBaseUri] = useState(defaults.mediaHlsBaseUri)
  const [publicBaseUri, setPublicBaseUri] = useState(`${window.location.origin}/`)
  const [httpAcknowledged, setHttpAcknowledged] = useState(false)
  const [error, setError] = useState('')
  const [message, setMessage] = useState('')
  const [testing, setTesting] = useState<'postgres' | 'media' | null>(null)
  const [loading, setLoading] = useState(false)
  const currentOriginUsesHttp = window.location.protocol === 'http:'

  const readForm = () => new FormData(formRef.current ?? undefined)
  const invalidatePostgres = () => { setPostgresTested(false); setMediaTested(false); setMessage(''); setError('') }
  const invalidateMedia = () => { setMediaTested(false); setMessage(''); setError('') }

  const selectMediaMode = (mode: 'same-host' | 'remote') => {
    setMediaMode(mode)
    invalidateMedia()
    if (mode === 'same-host') {
      setMediaApiBaseUri(defaults.mediaApiBaseUri)
      setMediaHlsBaseUri(defaults.mediaHlsBaseUri)
    } else {
      setMediaApiBaseUri('')
      setMediaHlsBaseUri('')
    }
  }

  const testPostgreSql = async () => {
    const form = readForm()
    setTesting('postgres'); setError(''); setMessage('')
    try {
      const result = await api.testPostgreSql({
        databaseHost: String(form.get('databaseHost') ?? ''),
        databasePort: Number(form.get('databasePort') ?? 0),
        postgresTlsMode: String(form.get('postgresTlsMode') ?? ''),
        databaseAdministratorUsername: String(form.get('databaseAdministratorUsername') ?? ''),
        databaseAdministratorPassword: String(form.get('databaseAdministratorPassword') ?? ''),
        databaseName: String(form.get('databaseName') ?? '')
      })
      setPostgresTested(true); setMessage(result.message)
    } catch (reason) {
      setPostgresTested(false); setError(reason instanceof Error ? reason.message : 'PostgreSQL 测试失败。')
    } finally {
      setTesting(null)
    }
  }

  const testMediaMtx = async () => {
    setTesting('media'); setError(''); setMessage('')
    try {
      const result = await api.testMediaMtx({ mediaMode, mediaApiBaseUri, mediaHlsBaseUri })
      setMediaTested(true); setMessage(result.message)
    } catch (reason) {
      setMediaTested(false); setError(reason instanceof Error ? reason.message : 'MediaMTX 测试失败。')
    } finally {
      setTesting(null)
    }
  }

  const submit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    if (!postgresTested || !mediaTested) return
    setError('')
    const form = new FormData(event.currentTarget)
    const platformAdministratorPassword = String(form.get('platformAdministratorPassword') ?? '')
    if (platformAdministratorPassword !== String(form.get('platformAdministratorPasswordConfirmation') ?? '')) {
      setError('两次输入的系统管理员密码不一致。')
      return
    }
    if (currentOriginUsesHttp && !httpAcknowledged && publicBaseUri.startsWith('http:')) {
      setError('请确认局域网 HTTP 的明文传输风险，或改用 HTTPS 访问地址。')
      return
    }

    const request: SetupRequest = {
      databaseHost: String(form.get('databaseHost') ?? ''),
      databasePort: Number(form.get('databasePort') ?? 0),
      postgresTlsMode: String(form.get('postgresTlsMode') ?? ''),
      databaseAdministratorUsername: String(form.get('databaseAdministratorUsername') ?? ''),
      databaseAdministratorPassword: String(form.get('databaseAdministratorPassword') ?? ''),
      databaseName: String(form.get('databaseName') ?? ''),
      publicBaseUri,
      mediaMode,
      mediaApiBaseUri,
      mediaHlsBaseUri,
      platformAdministratorUsername: String(form.get('platformAdministratorUsername') ?? ''),
      platformAdministratorPassword,
      allowInsecureLanHttp: currentOriginUsesHttp && httpAcknowledged
    }

    setLoading(true)
    try {
      await api.initialize(request)
      onSubmitted()
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : '初始化失败，请检查配置后重试。')
    } finally {
      setLoading(false)
    }
  }

  return <main className="setup-page">
    <section className="setup-brand"><div className="login-brand__line"><Activity size={22} /><span>VISICORE</span></div><h1>视枢初始化</h1><p>逐步验证 PostgreSQL 与 MediaMTX，创建首个管理员后自动进入控制台。</p><div className="setup-brand__status"><span className="status-dot status-dot--good" />核心容器引导态</div></section>
    <section className="setup-content"><form ref={formRef} className="setup-form" onSubmit={submit}>
      <header><Database size={23} /><div><h2>首次安装配置</h2><p>连接测试不写入外部服务；密码仅保留在当前页面内存和最终初始化请求中。</p></div></header>
      <nav className="setup-steps" aria-label="安装步骤"><span className={step === 1 ? 'active' : postgresTested ? 'complete' : ''}>1. PostgreSQL</span><span className={step === 2 ? 'active' : mediaTested ? 'complete' : ''}>2. MediaMTX</span><span className={step === 3 ? 'active' : ''}>3. 系统设置</span></nav>
      <section className="setup-section" hidden={step !== 1}><h3>连接 PostgreSQL</h3><p className="setup-hint">使用管理账号测试 TLS、目标数据库状态和创建数据库权限。核心运行期也使用此账号。</p><div className="setup-grid"><label><span>主机</span><Input name="databaseHost" autoComplete="off" placeholder="postgres.example.internal" required maxLength={255} onChange={invalidatePostgres} /></label><label><span>端口</span><Input name="databasePort" type="number" min={1} max={65535} defaultValue={defaults.databasePort} required onChange={invalidatePostgres} /></label><label><span>TLS 模式</span><Select name="postgresTlsMode" defaultValue={defaults.postgresTlsMode} onChange={invalidatePostgres}><option value="disable">不使用 TLS</option><option value="require">要求 TLS</option><option value="verify-full">校验服务器证书</option></Select></label><label><span>管理账号</span><Input name="databaseAdministratorUsername" autoComplete="username" required onChange={invalidatePostgres} /></label><label><span>管理密码</span><Input name="databaseAdministratorPassword" type="password" autoComplete="current-password" required onChange={invalidatePostgres} /></label><label><span>目标数据库</span><Input name="databaseName" defaultValue={defaults.databaseName} autoComplete="off" required onChange={invalidatePostgres} /></label></div><div className="setup-actions"><Button type="button" variant="secondary" disabled={testing !== null} onClick={() => void testPostgreSql()}>{testing === 'postgres' ? '正在测试' : '测试 PostgreSQL'}</Button><Button type="button" disabled={!postgresTested || testing !== null} onClick={() => { setStep(2); setMessage(''); setError('') }}>下一步</Button></div></section>
      <section className="setup-section" hidden={step !== 2}><h3>连接 MediaMTX</h3><p className="setup-hint">测试 Control API 与 HLS 地址的网络、TLS 和响应可用性，不触发真实设备流。</p><div className="setup-mode" role="group" aria-label="MediaMTX 部署方式"><button type="button" className={mediaMode === 'same-host' ? 'active' : ''} aria-pressed={mediaMode === 'same-host'} onClick={() => selectMediaMode('same-host')}>同机 Docker 网络</button><button type="button" className={mediaMode === 'remote' ? 'active' : ''} aria-pressed={mediaMode === 'remote'} onClick={() => selectMediaMode('remote')}>远程 HTTPS 服务</button></div><div className="setup-grid"><label><span>Control API 地址</span><Input type="url" value={mediaApiBaseUri} onChange={event => { setMediaApiBaseUri(event.target.value); invalidateMedia() }} placeholder={mediaMode === 'remote' ? 'https://media.example.com/api/' : undefined} required /></label><label><span>HLS 地址</span><Input type="url" value={mediaHlsBaseUri} onChange={event => { setMediaHlsBaseUri(event.target.value); invalidateMedia() }} placeholder={mediaMode === 'remote' ? 'https://media.example.com/hls/' : undefined} required /></label></div>{mediaMode === 'remote' && <p className="setup-hint">远程模式仅接受 HTTPS 地址，并必须允许 MediaMTX 回调视枢的内部鉴权地址。</p>}<div className="setup-actions"><Button type="button" variant="secondary" disabled={testing !== null} onClick={() => void testMediaMtx()}>{testing === 'media' ? '正在测试' : '测试 MediaMTX'}</Button><div><Button type="button" variant="secondary" disabled={testing !== null} onClick={() => { setStep(1); setMessage(''); setError('') }}>上一步</Button><Button type="button" disabled={!mediaTested || testing !== null} onClick={() => { setStep(3); setMessage(''); setError('') }}>下一步</Button></div></div></section>
      <section className="setup-section" hidden={step !== 3}><h3>系统设置</h3><label><span>视枢地址</span><Input type="url" value={publicBaseUri} onChange={event => setPublicBaseUri(event.target.value)} autoComplete="url" required /></label>{currentOriginUsesHttp && <label className="setup-risk"><input type="checkbox" checked={httpAcknowledged} onChange={event => setHttpAcknowledged(event.target.checked)} /><span><ShieldAlert size={17} />我理解局域网 HTTP 会明文传输登录密码和会话，仅在受控网络中使用。</span></label>}<div className="setup-grid"><label><span>首个管理员账号</span><Input name="platformAdministratorUsername" defaultValue={defaults.platformAdministratorUsername} autoComplete="username" required /><small>支持邮箱，邮箱登录不区分大小写；普通账号可使用数字开头。</small></label><label><span>管理员密码</span><Input name="platformAdministratorPassword" type="password" minLength={12} maxLength={256} autoComplete="new-password" required /></label><label><span>确认密码</span><Input name="platformAdministratorPasswordConfirmation" type="password" minLength={12} maxLength={256} autoComplete="new-password" required /></label></div><div className="setup-actions"><Button type="button" variant="secondary" disabled={loading} onClick={() => { setStep(2); setMessage(''); setError('') }}>上一步</Button><Button disabled={loading}>{loading ? '正在初始化' : '完成初始化'}</Button></div></section>
      {message && <div className="setup-success"><ShieldCheck size={17} />{message}</div>}
      {error && <div className="setup-error"><AlertTriangle size={17} />{error}</div>}
      <footer><span>完成初始化时会再次校验两项连接，创建全新数据库、迁移和首个管理员，并自动重启核心容器。</span></footer>
    </form></section>
  </main>
}
*/

function NotFoundPage() {
  return <main className="login-page"><section className="login-brand"><div className="login-brand__line"><Activity size={22} /><span>VISICORE</span></div><h1>企业视频<br />统一管理平台</h1></section><section className="login-panel"><header><ShieldCheck size={22} /><div><h2>页面不存在</h2><p>请检查访问地址后重试。</p></div></header><Button type="button" onClick={() => window.location.assign('/')}>返回掉线设备页</Button></section></main>
}

function LoginPage({ onLogin }: { onLogin: (passwordChangeRequired: boolean) => void }) {
  const [error, setError] = useState('')
  const [loading, setLoading] = useState(false)
  const submit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault(); setLoading(true); setError(''); const form = new FormData(event.currentTarget)
    try { const result = await api.login(String(form.get('username')), String(form.get('password'))); session.save(result); onLogin(result.requiresPasswordChange) } catch (reason) { setError(reason instanceof Error ? reason.message : '登录失败') } finally { setLoading(false) }
  }
  return <main className="login-page"><section className="login-brand"><div className="login-brand__line"><Activity size={22} /><span>VISICORE</span></div><h1>企业视频<br />统一管理平台</h1><div className="login-status"><span className="status-dot status-dot--good" />中心控制面</div></section><section className="login-panel"><form onSubmit={submit}><header><ShieldCheck size={22} /><div><h2>管理员登录</h2><p>授权账号访问</p></div></header><label><span>用户名</span><Input name="username" autoComplete="username" autoFocus required /></label><label><span>密码</span><Input name="password" type="password" autoComplete="current-password" required /></label>{error && <div className="login-error">{error}</div>}<Button disabled={loading}>{loading ? '正在验证' : '登录控制台'}</Button></form><footer>局域网管理入口 · 会话有效期 12 小时</footer></section></main>
}

function RequiredPasswordChangePage({ onFinished }: { onFinished: () => void }) {
  const [error, setError] = useState('')
  const [loading, setLoading] = useState(false)
  const submit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault(); setError(''); const form = new FormData(event.currentTarget)
    const currentPassword = String(form.get('currentPassword'))
    const newPassword = String(form.get('newPassword'))
    const confirmPassword = String(form.get('confirmPassword'))
    if (newPassword !== confirmPassword) return setError('两次输入的新密码不一致。')
    if (newPassword.length < 12 || newPassword.length > 256) return setError('新密码长度必须为 12 至 256 位。')
    if (newPassword === currentPassword) return setError('新密码不能与当前密码相同。')
    setLoading(true)
    try { await api.changePassword(currentPassword, newPassword); onFinished() } catch (reason) { setError(reason instanceof Error ? reason.message : '修改密码失败') } finally { setLoading(false) }
  }
  const exit = async () => { try { await api.logout() } catch { /* 本地会话仍需清除。 */ } onFinished() }
  return <main className="login-page"><section className="login-brand"><div className="login-brand__line"><Activity size={22} /><span>VISICORE</span></div><h1>企业视频<br />统一管理平台</h1><div className="login-status"><span className="status-dot status-dot--good" />账号安全验证</div></section><section className="login-panel"><form onSubmit={submit}><header><KeyRound size={22} /><div><h2>必须修改密码</h2><p>首次登录或密码重置后需先设置新密码</p></div></header><label><span>当前密码</span><Input name="currentPassword" type="password" autoComplete="current-password" autoFocus required /></label><label><span>新密码</span><Input name="newPassword" type="password" minLength={12} maxLength={256} autoComplete="new-password" required /></label><label><span>确认新密码</span><Input name="confirmPassword" type="password" minLength={12} maxLength={256} autoComplete="new-password" required /></label>{error && <div className="login-error">{error}</div>}<Button disabled={loading}>{loading ? '正在修改' : '修改密码'}</Button><Button variant="secondary" type="button" disabled={loading} onClick={() => void exit()}>返回登录</Button></form><footer>修改成功后请使用新密码重新登录</footer></section></main>
}
