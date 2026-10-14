import { createRouter, createWebHistory } from 'vue-router'
import { useAuth } from './stores/auth'
import { workspaceRoutes } from './navigation'
export const router = createRouter({
  history: createWebHistory(),
  scrollBehavior: () => ({ top: 0 }),
  routes: [
    { path: '/', component: () => import('./pages/HomePage.vue'), meta: { public: true, title: '客户端下载' } },
    { path: '/login', component: () => import('./pages/LoginPage.vue'), meta: { public: true, title: '登录' } },
    { path: '/app', component: () => import('./layouts/WorkspaceLayout.vue'), children: [
      ...workspaceRoutes,
      { path: 'organization-structure', redirect: '/app/organization' },
      { path: 'assets', redirect: '/app/organization' },
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
