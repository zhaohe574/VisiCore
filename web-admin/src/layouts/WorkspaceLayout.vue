<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import {
  Bell,
  Camera,
  Collection,
  Connection,
  Cpu,
  Download,
  Expand,
  Fold,
  FolderOpened,
  FullScreen,
  House,
  List,
  Lock,
  Monitor,
  Search,
  Setting,
  SwitchButton,
  User,
  VideoCamera,
  VideoPlay,
  Right
} from '@element-plus/icons-vue'
import { ElMessage } from 'element-plus'
import { useAuth } from '../stores/auth'
import { useEvents } from '../stores/events'
import { errorMessage } from '../lib/format'
import { workflowApi } from '../api'

const auth = useAuth()
const events = useEvents()
const route = useRoute()
const router = useRouter()

// 侧边栏折叠与移动端控制
const isCollapsed = ref(localStorage.getItem('visicore-sidebar-collapsed') === 'true')
const mobileOpen = ref(false)
const leaving = ref(false)

// 待处理报警数量统计
const pendingAlarmCount = ref(0)

// 全局快速搜索弹窗状态
const searchDialogOpen = ref(false)
const searchKeyword = ref('')

function toggleCollapse() {
  isCollapsed.value = !isCollapsed.value
  localStorage.setItem('visicore-sidebar-collapsed', String(isCollapsed.value))
}

const sections = [
  {
    name: '值守中心',
    links: [
      { path: '/app', title: '运行总览', icon: House, permission: 'statistics.read' },
      { path: '/app/live', title: '实时预览', icon: VideoCamera, permission: 'live.view' },
      { path: '/app/playback', title: '录像回放', icon: VideoPlay, permission: 'playback.view' },
      { path: '/app/alarms', title: '报警中心', icon: Bell, permission: 'alarm.read', badge: () => pendingAlarmCount.value },
      { path: '/app/exports', title: '录像导出', icon: Download, permission: 'export.create' },
      { path: '/app/layouts', title: '收藏与轮巡', icon: Collection, permission: 'live.view' }
    ]
  },
  {
    name: '平台管理',
    links: [
      { path: '/app/devices', title: '设备管理', icon: Camera, permission: 'device.read' },
      { path: '/app/plugins', title: '插件管理', icon: Cpu, permission: 'plugin.read' },
      { path: '/app/organization', title: '组织与通道', icon: FolderOpened, permission: 'area.read' },
      { path: '/app/accounts', title: '账号与角色', icon: User, permission: 'user.read' },
      { path: '/app/sessions', title: '在线会话', icon: Connection, permission: 'session.manage' },
      { path: '/app/audit', title: '操作审计', icon: List, permission: 'audit.read' },
      { path: '/app/releases', title: '版本发布', icon: Monitor, permission: 'desktop.release.manage' },
      { path: '/app/ssl', title: 'SSL管控', icon: Lock, permission: 'ssl.read' },
      { path: '/app/settings', title: '系统设置', icon: Setting, permission: 'settings.manage' }
    ]
  }
]

const visibleSections = computed(() =>
  sections
    .map(section => ({
      ...section,
      links: section.links.filter(link =>
        link.path === '/app/accounts'
          ? auth.can('user.read') || auth.can('role.read')
          : auth.can(link.permission)
      )
    }))
    .filter(section => section.links.length > 0)
)

// 获取待处理报警数量
async function refreshPendingAlarms() {
  if (!auth.can('alarm.read')) return
  try {
    const res = await workflowApi.alarms({ page: 1, pageSize: 1, state: 'new' })
    pendingAlarmCount.value = res.total || 0
  } catch {
    // 忽略轮询或初始报错
  }
}

async function logout() {
  leaving.value = true
  try {
    await auth.logout()
    await router.replace('/login')
  } catch (e) {
    ElMessage.error(errorMessage(e))
  } finally {
    leaving.value = false
  }
}

function toggleFullScreen() {
  if (!document.fullscreenElement) {
    document.documentElement.requestFullscreen().catch(() => {})
  } else {
    document.exitFullscreen().catch(() => {})
  }
}

// 快速搜索跳转逻辑
const allNavOptions = computed(() => {
  const list: { title: string; path: string; group: string }[] = []
  for (const s of visibleSections.value) {
    for (const link of s.links) {
      list.push({ title: link.title, path: link.path, group: s.name })
    }
  }
  return list
})

const filteredNavOptions = computed(() => {
  const kw = searchKeyword.value.trim().toLowerCase()
  if (!kw) return allNavOptions.value
  return allNavOptions.value.filter(o => o.title.toLowerCase().includes(kw) || o.group.toLowerCase().includes(kw))
})

function navigateTo(path: string) {
  searchDialogOpen.value = false
  searchKeyword.value = ''
  void router.push(path)
}

// 全局快捷键监听 (Ctrl+K / Cmd+K)
function handleGlobalKeydown(e: KeyboardEvent) {
  if ((e.ctrlKey || e.metaKey) && e.key === 'k') {
    e.preventDefault()
    searchDialogOpen.value = true
  }
}

const unsubscribers = [
  events.subscribe(['alarm.changed'], () => {
    void refreshPendingAlarms()
  }),
  events.subscribe(['access.changed', 'reconnected'], async event => {
    if (event) window.dispatchEvent(new Event('platform-access-changed'))
    try {
      await auth.loadUser()
      if (!auth.canRoute(route.meta)) await router.replace('/app/forbidden')
    } catch (e) {
      ElMessage.error(errorMessage(e))
    }
  })
]

watch(() => route.fullPath, () => {
  mobileOpen.value = false
})

watch(() => auth.authenticated, loggedIn => {
  if (!loggedIn) {
    void events.stop()
    void router.replace({ path: '/login', query: { redirect: route.fullPath } })
  }
})

onMounted(() => {
  void events.start()
  void refreshPendingAlarms()
  window.addEventListener('keydown', handleGlobalKeydown)
})

onBeforeUnmount(() => {
  unsubscribers.forEach(fn => fn())
  void events.stop()
  window.removeEventListener('keydown', handleGlobalKeydown)
})
</script>

<template>
  <div class="workspace-shell">
    <!-- 移动端遮罩层 -->
    <div
      v-if="mobileOpen"
      class="sidebar-overlay"
      aria-label="关闭导航"
      style="position: fixed; inset: 0; background: rgba(15, 23, 42, 0.6); z-index: 49;"
      @click="mobileOpen = false"
    />

    <!-- 左侧主导航侧边栏 -->
    <aside :class="['sidebar', { collapsed: isCollapsed, open: mobileOpen }]">
      <router-link class="workspace-brand" to="/app">
        <img src="/visicore.svg" alt="VisiCore 标识" />
        <div class="brand-text">
          <strong>VisiCore</strong>
          <small>视枢 · 视频管理平台</small>
        </div>
      </router-link>

      <nav aria-label="主导航">
        <section v-for="section in visibleSections" :key="section.name" class="nav-section">
          <h2>{{ isCollapsed ? section.name.slice(0, 2) : section.name }}</h2>
          <router-link
            v-for="link in section.links"
            :key="link.path"
            :to="link.path"
            :class="['nav-link', { selected: route.path === link.path }]"
            :title="isCollapsed ? link.title : undefined"
          >
            <el-icon><component :is="link.icon" /></el-icon>
            <span class="nav-title">{{ link.title }}</span>
            <span v-if="link.badge && link.badge() > 0" class="nav-badge">
              {{ link.badge() > 99 ? '99+' : link.badge() }}
            </span>
          </router-link>
        </section>
      </nav>

      <footer class="sidebar-footer">
        <button
          class="sidebar-collapse-btn"
          :title="isCollapsed ? '展开侧边栏' : '收起侧边栏'"
          aria-label="切换侧边栏折叠"
          @click="toggleCollapse"
        >
          <el-icon><Fold v-if="!isCollapsed" /><Expand v-else /></el-icon>
        </button>
        <router-link class="sidebar-download-link" to="/" title="客户端下载">
          <el-icon><Download /></el-icon>
          <span>客户端下载</span>
        </router-link>
      </footer>
    </aside>

    <!-- 右侧主体内容容器 -->
    <div :class="['workspace-main', { 'sidebar-collapsed': isCollapsed }]">
      <!-- 现代化顶部导航栏 -->
      <header class="workspace-topbar">
        <div class="topbar-left">
          <el-button
            class="mobile-menu-btn"
            style="display: none;"
            text
            :icon="Expand"
            aria-label="展开导航"
            @click="mobileOpen = !mobileOpen"
          />

          <div class="topbar-breadcrumb">
            <el-icon style="font-size: 14px;"><House /></el-icon>
            <span>工作区</span>
            <span class="separator">/</span>
            <strong>{{ route.meta.title }}</strong>
          </div>
        </div>

        <div class="topbar-right">
          <!-- 全局快速搜索触发器 -->
          <div class="global-search-trigger" @click="searchDialogOpen = true">
            <el-icon><Search /></el-icon>
            <span>快速导航搜索...</span>
            <kbd class="search-shortcut-badge">Ctrl K</kbd>
          </div>

          <!-- SignalR 服务端长连接状态 -->
          <el-tooltip :content="`通信通道：${events.state === 'connected' ? '服务已连接 (实时)' : '连接中断或重连中'}`">
            <div :class="['connection-pill', events.state === 'connected' ? 'connected' : 'disconnected']">
              <span :class="['pulse-dot', { pulse: events.state === 'connected' }]" />
              <span>{{ events.state === 'connected' ? '在线通信' : '连接中断' }}</span>
            </div>
          </el-tooltip>

          <!-- 全屏切换 -->
          <el-tooltip content="全屏/窗口模式切换">
            <el-button class="topbar-tool-btn" text :icon="FullScreen" aria-label="全屏切换" @click="toggleFullScreen" />
          </el-tooltip>

          <!-- 报警快捷通知铃铛 -->
          <el-tooltip :content="pendingAlarmCount > 0 ? `有 ${pendingAlarmCount} 条待处理报警` : '暂无待处理报警'">
            <router-link to="/app/alarms">
              <el-badge :value="pendingAlarmCount" :hidden="pendingAlarmCount === 0" :max="99" style="display: flex;">
                <el-button class="topbar-tool-btn" text :icon="Bell" aria-label="报警通知" />
              </el-badge>
            </router-link>
          </el-tooltip>

          <!-- 用户头像与菜单下拉 -->
          <el-dropdown trigger="click">
            <div class="user-dropdown-trigger">
              <div class="user-avatar-circle">
                {{ (auth.user?.displayName || auth.user?.username || 'U').slice(0, 1).toUpperCase() }}
              </div>
              <div class="user-meta">
                <span class="user-name">{{ auth.user?.displayName || auth.user?.username }}</span>
                <span class="user-role-tag">已认证</span>
              </div>
            </div>
            <template #dropdown>
              <el-dropdown-menu>
                <el-dropdown-item :icon="User" @click="router.push('/app/profile')">个人账号</el-dropdown-item>
                <el-dropdown-item v-if="auth.can('settings.manage')" :icon="Setting" @click="router.push('/app/settings')">系统设置</el-dropdown-item>
                <el-dropdown-item divided :icon="SwitchButton" style="color: #ef4444;" @click="logout">退出登录</el-dropdown-item>
              </el-dropdown-menu>
            </template>
          </el-dropdown>
        </div>
      </header>

      <!-- 主要路由视口 -->
      <main class="workspace-content">
        <router-view v-slot="{ Component }">
          <transition name="fade-transform" mode="out-in">
            <component :is="Component" :key="route.path" />
          </transition>
        </router-view>
      </main>
    </div>

    <!-- 全局快捷跳转搜索弹窗 (Ctrl + K) -->
    <el-dialog
      v-model="searchDialogOpen"
      title="快速导航与检索"
      width="560px"
      :show-close="true"
      destroy-on-close
    >
      <div style="margin-bottom: 16px;">
        <el-input
          v-model="searchKeyword"
          placeholder="输入页面功能关键词快速跳转..."
          :prefix-icon="Search"
          size="large"
          clearable
          autofocus
        />
      </div>

      <div style="max-height: 360px; overflow-y: auto;">
        <div v-if="!filteredNavOptions.length" class="muted" style="text-align: center; padding: 24px;">
          未搜索到匹配的功能模块
        </div>
        <div
          v-for="item in filteredNavOptions"
          :key="item.path"
          class="channel-entry"
          style="padding: 10px 14px; cursor: pointer; border-radius: 8px;"
          @click="navigateTo(item.path)"
        >
          <div style="flex: 1;">
            <strong style="font-size: 13.5px; color: var(--text-primary);">{{ item.title }}</strong>
            <span class="muted" style="font-size: 12px; margin-left: 8px;">{{ item.group }} · {{ item.path }}</span>
          </div>
          <el-icon class="muted"><Right /></el-icon>
        </div>
      </div>
    </el-dialog>
  </div>
</template>

<style scoped>
@media (max-width: 1024px) {
  .mobile-menu-btn {
    display: inline-flex !important;
    margin-right: 4px;
  }
  .global-search-trigger {
    min-width: 0;
    padding: 6px;
  }
  .global-search-trigger span,
  .global-search-trigger .search-shortcut-badge {
    display: none;
  }
  .user-meta {
    display: none;
  }
}

.fade-transform-leave-active,
.fade-transform-enter-active {
  transition: all 0.18s ease-out;
}

.fade-transform-enter-from {
  opacity: 0;
  transform: translateY(4px);
}

.fade-transform-leave-to {
  opacity: 0;
  transform: translateY(-4px);
}
</style>
