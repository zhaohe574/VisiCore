import { describe, it, expect, beforeAll } from 'vitest'
import { setActivePinia, createPinia } from 'pinia'

beforeAll(() => {
  setActivePinia(createPinia())
  // Mock minimal window/document needed for createWebHistory
  const win = {
    location: { pathname: '/', search: '', hash: '' },
    history: { state: null, pushState() {}, replaceState() {} },
    addEventListener() {},
    removeEventListener() {},
    dispatchEvent() { return true }
  }
  Object.defineProperty(globalThis, 'window', { value: win, configurable: true, writable: true })
  Object.defineProperty(globalThis, 'location', { value: win.location, configurable: true, writable: true })
  Object.defineProperty(globalThis, 'history', { value: win.history, configurable: true, writable: true })
  Object.defineProperty(globalThis, 'document', {
    value: {
      title: '',
      createElement() { return { setAttribute() {}, getAttribute() {} } },
      querySelector() { return null },
      addEventListener() {},
      removeEventListener() {}
    },
    configurable: true,
    writable: true
  })
})

describe('Router Configuration', () => {
  it('should resolve /app/organization without fallback to home', async () => {
    const { router } = await import('./router')
    const resolved = router.resolve('/app/organization')
    expect(resolved.matched.length).toBeGreaterThan(0)
    expect(resolved.matched.some(m => m.path === '/app/organization')).toBe(true)
    expect(resolved.path).toBe('/app/organization')
  })

  it('should resolve /app/accounts without fallback to home', async () => {
    const { router } = await import('./router')
    const resolved = router.resolve('/app/accounts')
    expect(resolved.matched.length).toBeGreaterThan(0)
    expect(resolved.matched.some(m => m.path === '/app/accounts')).toBe(true)
    expect(resolved.path).toBe('/app/accounts')
  })

  it('保留实时预览和回放页面入口', async () => {
    const { router } = await import('./router')
    const appRoute = router.options.routes.find(r => r.path === '/app')
    const children = appRoute && 'children' in appRoute ? appRoute.children : []
    const liveRoute = children?.find(r => r.path === 'live')
    const playbackRoute = children?.find(r => r.path === 'playback')

    expect(liveRoute).toBeDefined()
    expect(playbackRoute).toBeDefined()

    expect(liveRoute?.redirect).toBeUndefined()
    expect(playbackRoute?.redirect).toBeUndefined()
    expect(liveRoute?.component).toBeDefined()
    expect(playbackRoute?.component).toBeDefined()
  })

  it('should resolve all workspace layout navigation links', async () => {
    const { router } = await import('./router')
    const requiredLinks = [
      '/app',
      '/app/twin',
      '/app/alarms',
      '/app/live',
      '/app/playback',
      '/app/exports',
      '/app/layouts',
      '/app/organization',
      '/app/devices',
      '/app/computers',
      '/app/terminal-boxes',
      '/app/pon',
      '/app/onus',
      '/app/network-devices',
      '/app/ip-pools',
      '/app/accounts',
      '/app/plugins',
      '/app/sessions',
      '/app/audit',
      '/app/ssl',
      '/app/releases',
      '/app/settings'
    ]

    for (const path of requiredLinks) {
      const resolved = router.resolve(path)
      expect(resolved.matched.length).toBeGreaterThan(0)
      expect(resolved.path).toBe(path)
    }
  })

  it('should redirect /app/organization-structure to /app/organization', async () => {
    const { router } = await import('./router')
    const appRoute = router.options.routes.find(r => r.path === '/app')
    const children = appRoute && 'children' in appRoute ? appRoute.children : []
    const orgStructRoute = children?.find(r => r.path === 'organization-structure')
    expect(orgStructRoute).toBeDefined()
    expect(orgStructRoute?.redirect).toBe('/app/organization')
  })
})

