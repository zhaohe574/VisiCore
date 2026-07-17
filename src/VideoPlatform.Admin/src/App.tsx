import { useEffect, useState, type FormEvent } from 'react'
import { Activity, BellRing, Boxes, ChevronRight, CircleUserRound, Cpu, FileDown, KeyRound, LogOut, Menu, PlugZap, ShieldCheck, UsersRound, Wrench, X } from 'lucide-react'
import { api, session } from './api'
import type { Section } from './types'
import { Button, Input } from './ui'
import { AccessPage, AlertsPage, AssetsPage, AuditPage, ExportsPage, OverviewPage, PluginsPage } from './pages'
import { CredentialsPage, EdgeAgentsPage, PlatformOperationsPage } from './platform-pages'
import { PublicOfflineDevicesPage } from './public-offline-devices'

const navigation: Array<{ id: Section; label: string; icon: typeof Activity }> = [
  { id: 'overview', label: '运行总览', icon: Activity },
  { id: 'credentials', label: '设备凭据', icon: KeyRound },
  { id: 'edgeAgents', label: '边缘节点', icon: Cpu },
  { id: 'operations', label: '平台运维', icon: Wrench },
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
  const [active, setActive] = useState<Section>('overview')
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
  const notify = (message: string, tone: 'good' | 'bad' = 'good') => setToast({ message, tone })
  const select = (section: Section) => { setActive(section); setMenuOpen(false) }
  const adminPath = isAdminPath(window.location.pathname)
  const publicOfflinePath = isPublicOfflinePath(window.location.pathname)
  const logout = async () => {
    try { await api.logout() } catch { /* 本地会话仍需清除。 */ }
    session.clear(); setAuthenticated(false); setPasswordChangeRequired(false)
  }
  if (publicOfflinePath) return <PublicOfflineDevicesPage />
  if (!adminPath) return <NotFoundPage />
  if (passwordChangeRequired) return <RequiredPasswordChangePage onFinished={() => { session.clear(); setPasswordChangeRequired(false); setAuthenticated(false) }} />
  if (!authenticated) return <LoginPage onLogin={required => { setPasswordChangeRequired(required); setAuthenticated(!required) }} />
  const page = active === 'overview' ? <OverviewPage notify={notify} />
    : active === 'credentials' ? <CredentialsPage notify={notify} />
      : active === 'edgeAgents' ? <EdgeAgentsPage notify={notify} />
        : active === 'operations' ? <PlatformOperationsPage notify={notify} />
          : active === 'assets' ? <AssetsPage notify={notify} />
            : active === 'plugins' ? <PluginsPage notify={notify} />
              : active === 'access' ? <AccessPage notify={notify} />
                : active === 'exports' ? <ExportsPage notify={notify} />
                  : active === 'alerts' ? <AlertsPage notify={notify} />
                    : <AuditPage notify={notify} />
  return <div className="app-shell">
    <aside className={`sidebar ${menuOpen ? 'sidebar--open' : ''}`}>
      <div className="brand"><div className="brand__mark"><Activity size={20} /></div><div><strong>企业视频平台</strong><span>CONTROL PLANE</span></div><button className="sidebar-close" aria-label="关闭导航" onClick={() => setMenuOpen(false)}><X size={19} /></button></div>
      <nav>{navigation.map(item => { const Icon = item.icon; return <button key={item.id} className={active === item.id ? 'active' : ''} onClick={() => select(item.id)}><Icon size={18} /><span>{item.label}</span>{active === item.id && <ChevronRight size={15} />}</button> })}</nav>
      <div className="sidebar__footer"><div className="environment"><span className="status-dot status-dot--good" /><div><strong>中心 API</strong><small>统一运维控制面</small></div></div><button onClick={() => void logout()}><LogOut size={17} />退出登录</button></div>
    </aside>
    {menuOpen && <button className="sidebar-scrim" aria-label="关闭导航" onClick={() => setMenuOpen(false)} />}
    <main className="main-shell"><header className="topbar"><button className="menu-trigger" aria-label="打开导航" onClick={() => setMenuOpen(true)}><Menu size={20} /></button><div className="breadcrumb"><span>控制平面</span><ChevronRight size={14} /><strong>{navigation.find(item => item.id === active)?.label}</strong></div><div className="operator"><CircleUserRound size={18} /><span>{session.username()}</span></div></header><div className="page-content">{page}</div></main>
    {toast && <div className={`toast toast--${toast.tone}`} role="status">{toast.tone === 'good' ? <ShieldCheck size={18} /> : <BellRing size={18} />}{toast.message}</div>}
  </div>
}

function NotFoundPage() {
  return <main className="login-page"><section className="login-brand"><div className="login-brand__line"><Activity size={22} /><span>VIDEO OPERATIONS</span></div><h1>企业视频<br />统一管理平台</h1></section><section className="login-panel"><header><ShieldCheck size={22} /><div><h2>页面不存在</h2><p>请检查访问地址后重试。</p></div></header><Button type="button" onClick={() => window.location.assign('/')}>返回掉线设备页</Button></section></main>
}

function LoginPage({ onLogin }: { onLogin: (passwordChangeRequired: boolean) => void }) {
  const [error, setError] = useState('')
  const [loading, setLoading] = useState(false)
  const submit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault(); setLoading(true); setError(''); const form = new FormData(event.currentTarget)
    try { const result = await api.login(String(form.get('username')), String(form.get('password'))); session.save(result); onLogin(result.requiresPasswordChange) } catch (reason) { setError(reason instanceof Error ? reason.message : '登录失败') } finally { setLoading(false) }
  }
  return <main className="login-page"><section className="login-brand"><div className="login-brand__line"><Activity size={22} /><span>VIDEO OPERATIONS</span></div><h1>企业视频<br />统一管理平台</h1><div className="login-status"><span className="status-dot status-dot--good" />中心控制面</div></section><section className="login-panel"><form onSubmit={submit}><header><ShieldCheck size={22} /><div><h2>管理员登录</h2><p>授权账号访问</p></div></header><label><span>用户名</span><Input name="username" autoComplete="username" autoFocus required /></label><label><span>密码</span><Input name="password" type="password" autoComplete="current-password" required /></label>{error && <div className="login-error">{error}</div>}<Button disabled={loading}>{loading ? '正在验证' : '登录控制台'}</Button></form><footer>局域网管理入口 · 会话有效期 12 小时</footer></section></main>
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
  return <main className="login-page"><section className="login-brand"><div className="login-brand__line"><Activity size={22} /><span>VIDEO OPERATIONS</span></div><h1>企业视频<br />统一管理平台</h1><div className="login-status"><span className="status-dot status-dot--good" />账号安全验证</div></section><section className="login-panel"><form onSubmit={submit}><header><KeyRound size={22} /><div><h2>必须修改密码</h2><p>首次登录或密码重置后需先设置新密码</p></div></header><label><span>当前密码</span><Input name="currentPassword" type="password" autoComplete="current-password" autoFocus required /></label><label><span>新密码</span><Input name="newPassword" type="password" minLength={12} maxLength={256} autoComplete="new-password" required /></label><label><span>确认新密码</span><Input name="confirmPassword" type="password" minLength={12} maxLength={256} autoComplete="new-password" required /></label>{error && <div className="login-error">{error}</div>}<Button disabled={loading}>{loading ? '正在修改' : '修改密码'}</Button><Button variant="secondary" type="button" disabled={loading} onClick={() => void exit()}>返回登录</Button></form><footer>修改成功后请使用新密码重新登录</footer></section></main>
}
