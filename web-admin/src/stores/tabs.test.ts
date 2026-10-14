import { beforeEach, describe, expect, it } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { useTabsStore, HOME_TAB } from './tabs'
import type { RouteLocationNormalized } from 'vue-router'

function mockRoute(path: string, title = '测试页面', query: Record<string, string> = {}): RouteLocationNormalized {
  const queryString = new URLSearchParams(query).toString()
  return {
    path,
    fullPath: queryString ? `${path}?${queryString}` : path,
    name: undefined,
    params: {},
    query,
    hash: '',
    matched: [],
    meta: { title },
    redirectedFrom: undefined
  }
}

describe('tabs store', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
  })

  it('initializes with HOME_TAB by default', () => {
    const store = useTabsStore()
    expect(store.tabs.length).toBe(1)
    expect(store.tabs[0].path).toBe('/app')
    expect(store.tabs[0].affix).toBe(true)
    expect(store.cachedViews).toContain('DashboardPage')
  })

  it('initTabs initializes HOME_TAB and current non-home route', () => {
    const store = useTabsStore()
    store.initTabs(mockRoute('/app/devices', '设备管理'))
    expect(store.tabs.length).toBe(2)
    expect(store.tabs[0].path).toBe('/app')
    expect(store.tabs[1].path).toBe('/app/devices')
    expect(store.tabs[1].title).toBe('设备管理')
    expect(store.cachedViews).toEqual(expect.arrayContaining(['DashboardPage', 'DevicesPage']))
  })

  it('does not duplicate tabs on repeated addTab and updates query', () => {
    const store = useTabsStore()
    store.addTab(mockRoute('/app/organization', '组织与通道', { channelId: '1' }))
    expect(store.tabs.length).toBe(2)
    expect(store.tabs[1].fullPath).toBe('/app/organization?channelId=1')

    // Navigate to same path with different query
    store.addTab(mockRoute('/app/organization', '组织与通道', { channelId: '2' }))
    expect(store.tabs.length).toBe(2)
    expect(store.tabs[1].fullPath).toBe('/app/organization?channelId=2')
  })

  it('cannot close affix tab', () => {
    const store = useTabsStore()
    const result = store.closeTab('/app', '/app')
    expect(result).toBeNull()
    expect(store.tabs.length).toBe(1)
    expect(store.tabs[0].path).toBe('/app')
  })

  it('closes active tab and activates right tab when available', () => {
    const store = useTabsStore()
    store.addTab(mockRoute('/app/organization', '组织与通道'))
    store.addTab(mockRoute('/app/alarms', '报警中心'))
    // tabs: ['/app', '/app/organization', '/app/alarms']

    const nextPath = store.closeTab('/app/organization', '/app/organization')
    expect(nextPath).toBe('/app/alarms')
    expect(store.tabs.map(t => t.path)).toEqual(['/app', '/app/alarms'])
  })

  it('closes active tab at the end and activates left tab', () => {
    const store = useTabsStore()
    store.addTab(mockRoute('/app/organization', '组织与通道'))
    store.addTab(mockRoute('/app/alarms', '报警中心'))
    // tabs: ['/app', '/app/organization', '/app/alarms']

    const nextPath = store.closeTab('/app/alarms', '/app/alarms')
    expect(nextPath).toBe('/app/organization')
    expect(store.tabs.map(t => t.path)).toEqual(['/app', '/app/organization'])
  })

  it('closeOthers retains affix tab and current tab', () => {
    const store = useTabsStore()
    store.addTab(mockRoute('/app/organization', '组织与通道'))
    store.addTab(mockRoute('/app/alarms', '报警中心'))
    store.addTab(mockRoute('/app/devices', '设备管理'))

    store.closeOthers('/app/alarms')
    expect(store.tabs.map(t => t.path)).toEqual(['/app', '/app/alarms'])
    expect(store.cachedViews).toEqual(expect.arrayContaining(['DashboardPage', 'AlarmsPage']))
    expect(store.cachedViews).not.toContain('DevicesPage')
  })

  it('closeRight closes only tabs to the right', () => {
    const store = useTabsStore()
    store.addTab(mockRoute('/app/organization', '组织与通道'))
    store.addTab(mockRoute('/app/alarms', '报警中心'))
    store.addTab(mockRoute('/app/devices', '设备管理'))
    // ['/app', '/app/organization', '/app/alarms', '/app/devices']

    store.closeRight('/app/organization')
    expect(store.tabs.map(t => t.path)).toEqual(['/app', '/app/organization'])
  })

  it('closeAll closes everything except affix tabs', () => {
    const store = useTabsStore()
    store.addTab(mockRoute('/app/organization', '组织与通道'))
    store.addTab(mockRoute('/app/alarms', '报警中心'))

    const fallback = store.closeAll()
    expect(fallback).toBe('/app')
    expect(store.tabs.length).toBe(1)
    expect(store.tabs[0].path).toBe('/app')
  })

  it('refreshTab updates refreshKey and temporarily clears component from cachedViews', async () => {
    const store = useTabsStore()
    store.addTab(mockRoute('/app/devices', '设备管理'))
    expect(store.cachedViews).toContain('DevicesPage')

    const refreshPromise = store.refreshTab('/app/devices')
    // During refresh, refreshingPath is set
    expect(store.refreshingPath).toBe('/app/devices')
    expect(store.cachedViews).not.toContain('DevicesPage')

    await refreshPromise
    // After nextTick, cachedViews includes it again
    expect(store.refreshingPath).toBeNull()
    expect(store.cachedViews).toContain('DevicesPage')
    expect(store.getComponentKey(mockRoute('/app/devices'))).toContain('/app/devices__')
  })
})
