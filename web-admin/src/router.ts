import { createRouter, createWebHistory } from 'vue-router'
import { useAuth } from './stores/auth'
export const router = createRouter({
  history: createWebHistory(),
  scrollBehavior: () => ({ top: 0 }),
  routes: [
    { path: '/', component: () => import('./pages/HomePage.vue'), meta: { public: true, title: '客户端下载' } },
    { path: '/login', component: () => import('./pages/LoginPage.vue'), meta: { public: true, title: '登录' } },
    { path: '/app', component: () => import('./layouts/WorkspaceLayout.vue'), children: [
      { path: '', component: () => import('./pages/DashboardPage.vue'), meta: { title: '运行总览', permission: 'statistics.read' } },
      { path: 'live', component: () => import('./pages/MonitorPage.vue'), meta: { title: '实时预览', permission: 'live.view' } },
      { path: 'playback', component: () => import('./pages/MonitorPage.vue'), meta: { title: '录像回放', permission: 'playback.view' } },
      { path: 'alarms', component: () => import('./pages/AlarmsPage.vue'), meta: { title: '报警中心', permission: 'alarm.read' } },
      { path: 'exports', component: () => import('./pages/ExportsPage.vue'), meta: { title: '录像导出', permission: 'export.create' } },
      { path: 'layouts', component: () => import('./pages/LayoutsPage.vue'), meta: { title: '收藏与轮巡', permission: 'live.view' } },
      { path: 'devices', component: () => import('./pages/DevicesPage.vue'), meta: { title: '设备管理', permission: 'device.read' } },
      { path: 'plugins', component: () => import('./pages/PluginsPage.vue'), meta: { title: '插件管理', permission: 'plugin.read' } },
      { path: 'organization', component: () => import('./pages/OrganizationPage.vue'), meta: { title: '组织与通道', permission: 'area.read' } },
      { path: 'accounts', component: () => import('./pages/AccountsPage.vue'), meta: { title: '账号与角色', permissions: ['user.read', 'role.read'] } },
      { path: 'sessions', component: () => import('./pages/SessionsPage.vue'), meta: { title: '在线会话', permission: 'session.manage' } },
      { path: 'audit', component: () => import('./pages/AuditPage.vue'), meta: { title: '操作审计', permission: 'audit.read' } },
      { path: 'releases', component: () => import('./pages/ReleasesPage.vue'), meta: { title: '版本发布', permission: 'desktop.release.manage' } },
      { path: 'settings', component: () => import('./pages/SettingsPage.vue'), meta: { title: '系统设置', permission: 'settings.manage' } },
      { path: 'profile', component: () => import('./pages/ProfilePage.vue'), meta: { title: '个人账号' } },
      { path: 'forbidden', component: () => import('./pages/ForbiddenPage.vue'), meta: { title: '访问受限' } },
    ] },
    { path: '/:pathMatch(.*)*', redirect: '/' },
  ],
})
router.beforeEach(async to => {
  document.title = `${String(to.meta.title || '工作区')} · VisiCore（视枢）`
  if (to.path === '/' && to.hash === '#admin') return '/login'
  if (to.meta.public) return
  const auth = useAuth()
  await auth.initialize()
  if (!auth.authenticated) return { path: '/login', query: { redirect: to.fullPath } }
  if (!auth.canRoute(to.meta)) {
    if (to.path === '/app') {
      const candidate = router.getRoutes().find(route => route.path.startsWith('/app/') && (route.meta.permission || route.meta.permissions) && auth.canRoute(route.meta))
      return candidate?.path || '/app/profile'
    }
    return '/app/forbidden'
  }
})
