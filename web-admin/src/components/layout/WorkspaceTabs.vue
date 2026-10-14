<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import {
  ArrowDown,
  ArrowLeft,
  ArrowRight,
  Bell,
  Box,
  Camera,
  CircleClose,
  Close,
  Connection,
  Cpu,
  DataLine,
  Download,
  FolderOpened,
  House,
  List,
  Lock,
  Monitor,
  Platform,
  Refresh,
  Remove,
  Right,
  Setting,
  User
} from '@element-plus/icons-vue'
import { useTabsStore, type TabItem } from '../../stores/tabs'

const route = useRoute()
const router = useRouter()
const tabsStore = useTabsStore()

const scrollContainer = ref<HTMLElement | null>(null)
const tabRefs = ref<Record<string, HTMLElement | null>>({})

// 路由对应的图标映射
const iconMap: Record<string, any> = {
  '/app': House,
  '/app/live': Monitor,
  '/app/alarms': Bell,
  '/app/twin': Platform,
  '/app/organization': FolderOpened,
  '/app/terminal-boxes': Box,
  '/app/pon': Connection,
  '/app/onus': Cpu,
  '/app/computers': Monitor,
  '/app/ip-pools': DataLine,
  '/app/network-devices': Platform,
  '/app/devices': Camera,
  '/app/layouts': Platform,
  '/app/exports': Download,
  '/app/accounts': User,
  '/app/sessions': Connection,
  '/app/audit': List,
  '/app/releases': Monitor,
  '/app/ssl': Lock,
  '/app/plugins': Cpu,
  '/app/settings': Setting,
  '/app/profile': User,
  '/app/forbidden': Lock
}

function getTabIcon(path: string) {
  return iconMap[path] || House
}

// 右键菜单状态
const contextMenuVisible = ref(false)
const contextMenuPosition = ref({ x: 0, y: 0 })
const contextMenuTab = ref<TabItem | null>(null)

// 是否可以向右/向左滚动检测
const canScrollLeft = ref(false)
const canScrollRight = ref(false)

function checkScrollOverflow() {
  const el = scrollContainer.value
  if (!el) return
  canScrollLeft.value = el.scrollLeft > 4
  canScrollRight.value = el.scrollLeft + el.clientWidth < el.scrollWidth - 4
}

function scrollHorizontally(distance: number) {
  if (!scrollContainer.value) return
  scrollContainer.value.scrollBy({ left: distance, behavior: 'smooth' })
}

function handleWheel(e: WheelEvent) {
  if (!scrollContainer.value) return
  if (Math.abs(e.deltaY) > Math.abs(e.deltaX)) {
    scrollContainer.value.scrollLeft += e.deltaY * 0.8
  }
}

// 自动滚动让当前激活的页签出现在可视区域
function scrollToActiveTab() {
  void nextTick(() => {
    const activeEl = tabRefs.value[route.path]
    if (activeEl && scrollContainer.value) {
      activeEl.scrollIntoView({ behavior: 'smooth', block: 'nearest', inline: 'nearest' })
    }
    checkScrollOverflow()
  })
}

// 点击切换标签
function handleSelectTab(tab: TabItem) {
  if (route.fullPath === tab.fullPath) return
  void router.push(tab.fullPath)
}

// 关闭单个标签
function handleCloseTab(tab: TabItem, e?: Event) {
  if (e) e.stopPropagation()
  if (tab.affix) return

  const nextPath = tabsStore.closeTab(tab.path, route.path)
  if (nextPath) {
    void router.push(nextPath)
  }
}

// 鼠标中键直接关闭标签
function handleMiddleClick(tab: TabItem) {
  if (!tab.affix) {
    handleCloseTab(tab)
  }
}

// 打开右键上下文菜单
function openContextMenu(tab: TabItem, e: MouseEvent) {
  contextMenuTab.value = tab
  contextMenuPosition.value = {
    x: Math.min(e.clientX, window.innerWidth - 170),
    y: Math.min(e.clientY + 8, window.innerHeight - 200)
  }
  contextMenuVisible.value = true
}

function closeContextMenu() {
  contextMenuVisible.value = false
}

// 菜单交互操作
async function handleRefreshCurrent(targetTab?: TabItem) {
  closeContextMenu()
  const tab = targetTab || contextMenuTab.value || tabsStore.tabs.find(t => t.path === route.path)
  if (!tab) return
  if (route.path !== tab.path) {
    await router.push(tab.fullPath)
  }
  await tabsStore.refreshTab(tab.path)
}

function handleCloseCurrent(targetTab?: TabItem) {
  closeContextMenu()
  const tab = targetTab || contextMenuTab.value || tabsStore.tabs.find(t => t.path === route.path)
  if (tab && !tab.affix) {
    handleCloseTab(tab)
  }
}

function handleCloseOthers(targetTab?: TabItem) {
  closeContextMenu()
  const tab = targetTab || contextMenuTab.value || tabsStore.tabs.find(t => t.path === route.path)
  if (!tab) return
  tabsStore.closeOthers(tab.path)
  if (route.path !== tab.path) {
    void router.push(tab.fullPath)
  }
}

function handleCloseRight(targetTab?: TabItem) {
  closeContextMenu()
  const tab = targetTab || contextMenuTab.value || tabsStore.tabs.find(t => t.path === route.path)
  if (!tab) return
  tabsStore.closeRight(tab.path)
  // 如果当前激活的标签在右侧被关掉了，切到该 tab
  const stillExists = tabsStore.tabs.some(t => t.path === route.path)
  if (!stillExists) {
    void router.push(tab.fullPath)
  }
}

function handleCloseAll() {
  closeContextMenu()
  const homePath = tabsStore.closeAll()
  void router.push(homePath)
}

// 监听路由变化，自动滚动
watch(
  () => route.path,
  () => {
    scrollToActiveTab()
  }
)

onMounted(() => {
  window.addEventListener('click', closeContextMenu)
  window.addEventListener('resize', checkScrollOverflow)
  scrollToActiveTab()
})

onBeforeUnmount(() => {
  window.removeEventListener('click', closeContextMenu)
  window.removeEventListener('resize', checkScrollOverflow)
})
</script>

<template>
  <div class="workspace-tabs-bar">
    <!-- 左侧滚动辅助按钮（当内容溢出时可用） -->
    <button
      v-if="canScrollLeft"
      class="tab-scroll-btn tab-scroll-left"
      title="向左滚动"
      aria-label="向左滚动标签"
      @click="scrollHorizontally(-200)"
    >
      <el-icon><ArrowLeft /></el-icon>
    </button>

    <!-- 标签滚动展示主体 -->
    <div
      ref="scrollContainer"
      class="tabs-scroll-container"
      @wheel.passive="handleWheel"
      @scroll.passive="checkScrollOverflow"
    >
      <div
        v-for="tab in tabsStore.tabs"
        :key="tab.path"
        :ref="el => { tabRefs[tab.path] = el as HTMLElement | null }"
        :class="['workspace-tab-item', { active: route.path === tab.path, affix: tab.affix }]"
        :title="tab.title"
        @click="handleSelectTab(tab)"
        @auxclick.prevent="handleMiddleClick(tab)"
        @contextmenu.prevent="openContextMenu(tab, $event)"
      >
        <el-icon class="tab-icon"><component :is="getTabIcon(tab.path)" /></el-icon>
        <span class="tab-label">{{ tab.title }}</span>

        <!-- 刷新中状态轻微呼吸动效指示 -->
        <span
          v-if="tabsStore.refreshingPath === tab.path"
          class="tab-refreshing-spinner"
          title="重新加载中..."
        />

        <!-- 关闭小按钮（常驻标签不显示） -->
        <button
          v-if="!tab.affix"
          class="tab-close-icon"
          title="关闭页签 (或鼠标中键)"
          aria-label="关闭标签"
          @click="handleCloseTab(tab, $event)"
        >
          <el-icon><Close /></el-icon>
        </button>
      </div>
    </div>

    <!-- 右侧滚动辅助按钮 -->
    <button
      v-if="canScrollRight"
      class="tab-scroll-btn tab-scroll-right"
      title="向右滚动"
      aria-label="向右滚动标签"
      @click="scrollHorizontally(200)"
    >
      <el-icon><ArrowRight /></el-icon>
    </button>

    <!-- 右侧快捷操作下拉菜单 -->
    <div class="tabs-actions-wrapper">
      <el-dropdown trigger="click" popper-class="tabs-action-popper">
        <button class="tabs-action-btn" title="页签操作菜单" aria-label="页签快捷操作">
          <el-icon><ArrowDown /></el-icon>
        </button>
        <template #dropdown>
          <el-dropdown-menu>
            <el-dropdown-item :icon="Refresh" @click="handleRefreshCurrent()">
              刷新当前页
            </el-dropdown-item>
            <el-dropdown-item
              :icon="CircleClose"
              :disabled="route.path === '/app'"
              @click="handleCloseCurrent()"
            >
              关闭当前页
            </el-dropdown-item>
            <el-dropdown-item
              :icon="Remove"
              :disabled="tabsStore.tabs.length <= 1"
              @click="handleCloseOthers()"
            >
              关闭其他页
            </el-dropdown-item>
            <el-dropdown-item
              :icon="Right"
              @click="handleCloseRight()"
            >
              关闭右侧页
            </el-dropdown-item>
            <el-dropdown-item
              divided
              :icon="CircleClose"
              :disabled="tabsStore.tabs.length <= 1"
              style="color: #ef4444;"
              @click="handleCloseAll()"
            >
              全部关闭
            </el-dropdown-item>
          </el-dropdown-menu>
        </template>
      </el-dropdown>
    </div>

    <!-- 右键上下文弹出菜单 -->
    <Teleport to="body">
      <div
        v-if="contextMenuVisible && contextMenuTab"
        class="tab-context-menu"
        :style="{ left: `${contextMenuPosition.x}px`, top: `${contextMenuPosition.y}px` }"
        @click.stop
      >
        <div class="context-menu-item" @click="handleRefreshCurrent(contextMenuTab)">
          <el-icon><Refresh /></el-icon>
          <span>刷新当前</span>
        </div>
        <div
          v-if="!contextMenuTab.affix"
          class="context-menu-item"
          @click="handleCloseCurrent(contextMenuTab)"
        >
          <el-icon><Close /></el-icon>
          <span>关闭当前</span>
        </div>
        <div
          v-if="tabsStore.tabs.length > 1"
          class="context-menu-item"
          @click="handleCloseOthers(contextMenuTab)"
        >
          <el-icon><Remove /></el-icon>
          <span>关闭其他</span>
        </div>
        <div class="context-menu-item" @click="handleCloseRight(contextMenuTab)">
          <el-icon><Right /></el-icon>
          <span>关闭右侧</span>
        </div>
        <div
          v-if="tabsStore.tabs.length > 1"
          class="context-menu-item danger"
          @click="handleCloseAll()"
        >
          <el-icon><CircleClose /></el-icon>
          <span>全部关闭</span>
        </div>
      </div>
    </Teleport>
  </div>
</template>

<style scoped>
.workspace-tabs-bar {
  display: flex;
  align-items: center;
  height: 42px;
  background: rgba(255, 255, 255, 0.92);
  backdrop-filter: blur(10px);
  -webkit-backdrop-filter: blur(10px);
  border-bottom: 1px solid var(--border);
  padding: 0 16px;
  position: sticky;
  top: 64px;
  z-index: 25;
  user-select: none;
}

.tabs-scroll-container {
  display: flex;
  align-items: center;
  gap: 6px;
  flex: 1;
  overflow-x: auto;
  overflow-y: hidden;
  scrollbar-width: none;
  -ms-overflow-style: none;
  padding: 4px 2px;
}

.tabs-scroll-container::-webkit-scrollbar {
  display: none;
}

/* 现代化卡片胶囊风格 */
.workspace-tab-item {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  height: 30px;
  padding: 0 12px;
  background: #f1f5f9;
  color: var(--text-secondary);
  border: 1px solid transparent;
  border-radius: 6px;
  font-size: 13px;
  font-weight: 500;
  cursor: pointer;
  white-space: nowrap;
  flex-shrink: 0;
  transition: all 0.18s cubic-bezier(0.4, 0, 0.2, 1);
}

.workspace-tab-item:hover {
  background: #e2e8f0;
  color: var(--text-primary);
}

.workspace-tab-item.active {
  background: #eff6ff;
  color: var(--primary);
  border-color: rgba(25, 118, 210, 0.28);
  box-shadow: 0 1px 4px rgba(25, 118, 210, 0.12);
  font-weight: 600;
}

.tab-icon {
  font-size: 13.5px;
  flex-shrink: 0;
  transition: transform 0.18s ease;
}

.workspace-tab-item:hover .tab-icon {
  transform: scale(1.08);
}

.tab-label {
  line-height: 1;
}

.tab-close-icon {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 16px;
  height: 16px;
  border-radius: 50%;
  border: none;
  background: transparent;
  color: inherit;
  opacity: 0.65;
  cursor: pointer;
  padding: 0;
  margin-left: 2px;
  transition: all 0.15s ease;
}

.tab-close-icon:hover {
  background: rgba(0, 0, 0, 0.1);
  opacity: 1;
  color: #ef4444;
}

.workspace-tab-item.active .tab-close-icon:hover {
  background: rgba(25, 118, 210, 0.15);
  color: #ef4444;
}

.tab-refreshing-spinner {
  width: 10px;
  height: 10px;
  border: 2px solid var(--primary);
  border-top-color: transparent;
  border-radius: 50%;
  animation: spin 0.7s linear infinite;
}

@keyframes spin {
  to {
    transform: rotate(360deg);
  }
}

.tab-scroll-btn {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 26px;
  height: 26px;
  border: none;
  background: transparent;
  color: var(--text-secondary);
  border-radius: 4px;
  cursor: pointer;
  flex-shrink: 0;
  transition: all 0.15s;
}

.tab-scroll-btn:hover {
  background: #f1f5f9;
  color: var(--text-primary);
}

.tabs-actions-wrapper {
  margin-left: 6px;
  flex-shrink: 0;
}

.tabs-action-btn {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 28px;
  height: 28px;
  border: 1px solid var(--border);
  background: #ffffff;
  color: var(--text-secondary);
  border-radius: 6px;
  cursor: pointer;
  transition: all 0.15s;
}

.tabs-action-btn:hover {
  background: #f8fafc;
  color: var(--primary);
  border-color: rgba(25, 118, 210, 0.4);
}

/* 右键上下文菜单浮层 */
.tab-context-menu {
  position: fixed;
  z-index: 3000;
  min-width: 130px;
  background: #ffffff;
  border-radius: 8px;
  box-shadow: 0 4px 20px rgba(15, 23, 42, 0.15), 0 0 0 1px rgba(15, 23, 42, 0.05);
  padding: 5px;
  font-size: 13px;
  color: var(--text-primary);
  user-select: none;
  animation: menuIn 0.12s ease-out;
}

@keyframes menuIn {
  from {
    opacity: 0;
    transform: scale(0.96);
  }
  to {
    opacity: 1;
    transform: scale(1);
  }
}

.context-menu-item {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 7px 10px;
  border-radius: 5px;
  cursor: pointer;
  transition: all 0.12s;
}

.context-menu-item:hover {
  background: #f1f5f9;
  color: var(--primary);
}

.context-menu-item.danger:hover {
  background: #fef2f2;
  color: #ef4444;
}


</style>
