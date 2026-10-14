import { defineStore } from 'pinia'
import { computed, nextTick, ref } from 'vue'
import type { RouteLocationNormalized } from 'vue-router'
import { workspacePages } from '../navigation'

export interface TabItem {
  title: string
  path: string
  fullPath: string
  affix?: boolean
}

export const pathToComponentName: Record<string, string> = Object.fromEntries(workspacePages.map(page => [page.path, page.componentName]))

export const HOME_TAB: TabItem = {
  path: '/app',
  fullPath: '/app',
  title: '运行总览',
  affix: true
}

export const useTabsStore = defineStore('tabs', () => {
  const tabs = ref<TabItem[]>([{ ...HOME_TAB }])
  const refreshingPath = ref<string | null>(null)
  const refreshKeys = ref<Record<string, number>>({})

  // 计算当前应该保活的组件列表
  const cachedViews = computed<string[]>(() => {
    const names = new Set<string>()
    for (const tab of tabs.value) {
      const compName = pathToComponentName[tab.path]
      if (compName) {
        // 视频流巡查页面不保活，切换时释放媒体流和资源；刷新中的页面也不保活
        if (compName === 'InspectionPage' || refreshingPath.value === tab.path) {
          continue
        }
        names.add(compName)
      }
    }
    return Array.from(names)
  })

  // 获取组件在 router-view 中的动态 Key
  function getComponentKey(route: RouteLocationNormalized): string {
    const refreshKey = refreshKeys.value[route.path]
    return refreshKey ? `${route.path}__${refreshKey}` : route.path
  }

  // 初始化页签：仅保留“运行总览”和当前正在访问的页面（满足轻量级非持久化设计）
  function initTabs(currentRoute: RouteLocationNormalized) {
    tabs.value = [{ ...HOME_TAB }]
    if (
      currentRoute.path.startsWith('/app') &&
      currentRoute.path !== '/app' &&
      currentRoute.path !== '/app/forbidden'
    ) {
      addTab(currentRoute)
    }
  }

  // 添加或更新页签（按 path 唯一，更新 fullPath 与 title）
  function addTab(route: RouteLocationNormalized) {
    if (!route.path.startsWith('/app')) return

    const existing = tabs.value.find(t => t.path === route.path)
    const title = (route.meta.title as string) || (route.path === '/app' ? '运行总览' : '未命名')

    if (existing) {
      existing.fullPath = route.fullPath
      existing.title = title
    } else {
      tabs.value.push({
        path: route.path,
        fullPath: route.fullPath,
        title,
        affix: route.path === '/app'
      })
    }
  }

  /**
   * 关闭指定页签
   * @param path 要关闭的页签路径
   * @param currentPath 当前激活的路由路径
   * @returns 若关闭的是当前页，则返回下一个需要跳转的目标 path；否则返回 null
   */
  function closeTab(path: string, currentPath: string): string | null {
    const index = tabs.value.findIndex(t => t.path === path)
    if (index === -1) return null

    const targetTab = tabs.value[index]
    if (targetTab.affix) return null // 常驻标签不可关闭

    let nextPath: string | null = null
    if (currentPath === path) {
      // 优先右侧，无右侧则激活左侧，都无则回退首页
      if (index < tabs.value.length - 1) {
        nextPath = tabs.value[index + 1].fullPath
      } else if (index > 0) {
        nextPath = tabs.value[index - 1].fullPath
      } else {
        nextPath = HOME_TAB.path
      }
    }

    tabs.value.splice(index, 1)
    delete refreshKeys.value[path]

    return nextPath
  }

  // 关闭其他标签（保留当前标签与常驻标签）
  function closeOthers(path: string): string {
    tabs.value = tabs.value.filter(t => t.affix || t.path === path)
    // 清理无用 refreshKeys
    const activePaths = new Set(tabs.value.map(t => t.path))
    for (const key of Object.keys(refreshKeys.value)) {
      if (!activePaths.has(key)) delete refreshKeys.value[key]
    }
    return path
  }

  // 关闭右侧标签
  function closeRight(path: string): void {
    const index = tabs.value.findIndex(t => t.path === path)
    if (index === -1) return
    tabs.value = tabs.value.filter((t, i) => i <= index || t.affix)
    const activePaths = new Set(tabs.value.map(t => t.path))
    for (const key of Object.keys(refreshKeys.value)) {
      if (!activePaths.has(key)) delete refreshKeys.value[key]
    }
  }

  // 全部关闭（仅保留常驻标签）
  function closeAll(): string {
    tabs.value = tabs.value.filter(t => t.affix)
    refreshKeys.value = {}
    return HOME_TAB.path
  }

  // 刷新指定页签：更新 refreshKey 并短暂移出保活列表
  async function refreshTab(path: string) {
    refreshingPath.value = path
    refreshKeys.value[path] = Date.now()
    await nextTick()
    // 下一轮事件循环重新纳入缓存保活
    refreshingPath.value = null
  }

  return {
    tabs,
    cachedViews,
    refreshingPath,
    refreshKeys,
    getComponentKey,
    initTabs,
    addTab,
    closeTab,
    closeOthers,
    closeRight,
    closeAll,
    refreshTab
  }
})
