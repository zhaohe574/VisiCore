<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { Bell, Camera, Collection, Connection, Cpu, Download, Expand, FolderOpened, House, List, Lock, Monitor, Setting, SwitchButton, User, VideoCamera, VideoPlay } from '@element-plus/icons-vue'
import { ElMessage } from 'element-plus'
import { useAuth } from '../stores/auth'
import { useEvents } from '../stores/events'
import { errorMessage } from '../lib/format'
import StatusBadge from '../components/StatusBadge.vue'
const auth = useAuth(), events = useEvents(), route = useRoute(), router = useRouter()
const mobileOpen = ref(false), leaving = ref(false)
const sections = [
  { name: '值守中心', links: [{ path: '/app', title: '运行总览', icon: House, permission: 'statistics.read' }, { path: '/app/live', title: '实时预览', icon: VideoCamera, permission: 'live.view' }, { path: '/app/playback', title: '录像回放', icon: VideoPlay, permission: 'playback.view' }, { path: '/app/alarms', title: '报警中心', icon: Bell, permission: 'alarm.read' }, { path: '/app/exports', title: '录像导出', icon: Download, permission: 'export.create' }, { path: '/app/layouts', title: '收藏与轮巡', icon: Collection, permission: 'live.view' }] },
  { name: '平台管理', links: [{ path: '/app/devices', title: '设备管理', icon: Camera, permission: 'device.read' }, { path: '/app/plugins', title: '插件管理', icon: Cpu, permission: 'plugin.read' }, { path: '/app/organization', title: '组织与通道', icon: FolderOpened, permission: 'area.read' }, { path: '/app/accounts', title: '账号与角色', icon: User, permission: 'user.read' }, { path: '/app/sessions', title: '在线会话', icon: Connection, permission: 'session.manage' }, { path: '/app/audit', title: '操作审计', icon: List, permission: 'audit.read' }, { path: '/app/releases', title: '版本发布', icon: Monitor, permission: 'desktop.release.manage' }, { path: '/app/settings', title: '系统设置', icon: Setting, permission: 'settings.manage' }] },
]
const visibleSections = computed(() => sections.map(section => ({ ...section, links: section.links.filter(link => link.path === '/app/accounts' ? auth.can('user.read') || auth.can('role.read') : auth.can(link.permission)) })).filter(section => section.links.length))
async function logout() {
  leaving.value = true
  try { await auth.logout(); await router.replace('/login') } catch (e) { ElMessage.error(errorMessage(e)) } finally { leaving.value = false }
}
const unsubscribe = events.subscribe(['access.changed', 'reconnected'], async event => {
  if (event) window.dispatchEvent(new Event('platform-access-changed'))
  try { await auth.loadUser(); if (!auth.canRoute(route.meta)) await router.replace('/app/forbidden') }
  catch (e) { ElMessage.error(errorMessage(e)) }
})
watch(() => route.fullPath, () => { mobileOpen.value = false })
watch(() => auth.authenticated, loggedIn => { if (!loggedIn) { void events.stop(); void router.replace({ path: '/login', query: { redirect: route.fullPath } }) } })
onMounted(() => void events.start())
onBeforeUnmount(() => { unsubscribe(); void events.stop() })
</script>
<template>
  <div class="workspace-shell">
    <button v-if="mobileOpen" class="sidebar-overlay" aria-label="关闭导航" @click="mobileOpen = false" />
    <aside :class="['sidebar', { open: mobileOpen }]">
      <router-link class="workspace-brand" to="/app"><img src="/visicore.svg" alt="VisiCore（视枢）标识" /><span><strong>VisiCore</strong><small>视枢 · 管理工作区</small></span></router-link>
      <nav aria-label="主导航"><section v-for="section in visibleSections" :key="section.name" class="nav-section"><h2>{{ section.name }}</h2><router-link v-for="link in section.links" :key="link.path" :to="link.path" :class="{ selected: route.path === link.path }"><el-icon><component :is="link.icon" /></el-icon><span>{{ link.title }}</span></router-link></section></nav>
      <footer class="sidebar-footer"><router-link to="/"><el-icon><Download /></el-icon>客户端下载</router-link><small>版本 2.0.0</small></footer>
    </aside>
    <div class="workspace-main">
      <header class="workspace-topbar"><div class="topbar-location"><el-button class="mobile-menu" text :icon="Expand" aria-label="展开导航" @click="mobileOpen = !mobileOpen" /><span>工作区</span><span class="separator">/</span><strong>{{ route.meta.title }}</strong></div><div class="topbar-actions"><span class="connection-indicator"><StatusBadge :value="events.state" /></span><router-link class="user-link" to="/app/profile"><el-icon><Lock /></el-icon><span>{{ auth.user?.displayName || auth.user?.username }}</span></router-link><el-tooltip content="退出登录"><el-button text :icon="SwitchButton" :loading="leaving" aria-label="退出登录" @click="logout" /></el-tooltip></div></header>
      <main class="workspace-content"><router-view v-slot="{ Component }"><component :is="Component" :key="route.path" /></router-view></main>
    </div>
  </div>
</template>
