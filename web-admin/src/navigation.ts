import type { Component } from 'vue'
import type { RouteRecordRaw } from 'vue-router'
import { Bell, Box, Camera, Connection, Cpu, DataLine, Download, FolderOpened, House, List, Lock, Monitor, OfficeBuilding, Platform, Setting, User, VideoCamera } from '@element-plus/icons-vue'

export interface WorkspacePage {
  path: string
  title: string
  component: NonNullable<RouteRecordRaw['component']>
  componentName: string
  permission?: string
  permissions?: string[]
  group?: string
  icon?: Component
}

// 路由、菜单与页签共用同一份功能及权限定义。
export const workspacePages: WorkspacePage[] = [
  // 巡查工作台
  { path: '/app', component: () => import('./pages/DashboardPage.vue'), componentName: 'DashboardPage', title: '运行总览', permission: 'statistics.read', group: '巡查工作台', icon: House },
  { path: '/app/live', component: () => import('./pages/InspectionPage.vue'), componentName: 'InspectionPage', title: '设备巡查', permission: 'live.view', group: '巡查工作台', icon: Monitor },
  { path: '/app/playback', component: () => import('./pages/InspectionPage.vue'), componentName: 'InspectionPage', title: '设备巡查', permission: 'playback.view' },
  { path: '/app/alarms', component: () => import('./pages/AlarmsPage.vue'), componentName: 'AlarmsPage', title: '报警中心', permission: 'alarm.read', group: '巡查工作台', icon: Bell },
  { path: '/app/twin', component: () => import('./pages/DigitalTwinPage.vue'), componentName: 'DigitalTwinPage', title: '空间数字孪生', permission: 'map.read', group: '巡查工作台', icon: Platform },

  // 资产中心
  { path: '/app/organization', component: () => import('./pages/OrganizationPage.vue'), componentName: 'OrganizationPage', title: '组织架构拓扑', permission: 'area.read', group: '资产中心', icon: OfficeBuilding },
  { path: '/app/channels', component: () => import('./pages/ChannelsPage.vue'), componentName: 'ChannelsPage', title: '监控通道台账', permission: 'device.read', group: '资产中心', icon: VideoCamera },
  { path: '/app/terminal-boxes', component: () => import('./pages/TerminalBoxesPage.vue'), componentName: 'TerminalBoxesPage', title: '网络终端箱', permission: 'pon.read', group: '资产中心', icon: Box },
  { path: '/app/pon', component: () => import('./pages/PonPage.vue'), componentName: 'PonPage', title: '全光网络', permission: 'pon.read', group: '资产中心', icon: Connection },
  { path: '/app/onus', component: () => import('./pages/OnuDevicesPage.vue'), componentName: 'OnuDevicesPage', title: 'ONU设备台账', permission: 'pon.read', group: '资产中心', icon: Cpu },
  { path: '/app/computers', component: () => import('./pages/ComputersPage.vue'), componentName: 'ComputersPage', title: '工位电脑', permission: 'computer.read', group: '资产中心', icon: Monitor },
  { path: '/app/ip-pools', component: () => import('./pages/IpPoolsPage.vue'), componentName: 'IpPoolsPage', title: 'IP池管理', permission: 'ip.read', group: '资产中心', icon: DataLine },
  { path: '/app/network-devices', component: () => import('./pages/NetworkDevicesPage.vue'), componentName: 'NetworkDevicesPage', title: '网络核心设备', permission: 'network_device.read', group: '资产中心', icon: Platform },
  { path: '/app/devices', component: () => import('./pages/DevicesPage.vue'), componentName: 'DevicesPage', title: '设备管理', permission: 'device.read', group: '资产中心', icon: Camera },

  // 视频方案
  { path: '/app/layouts', component: () => import('./pages/LayoutsPage.vue'), componentName: 'LayoutsPage', title: '收藏与轮巡', permission: 'live.view', group: '视频方案', icon: Platform },
  { path: '/app/exports', component: () => import('./pages/ExportsPage.vue'), componentName: 'ExportsPage', title: '导出任务查询', permission: 'export.create', group: '视频方案', icon: Download },

  // 系统安全
  { path: '/app/accounts', component: () => import('./pages/AccountsPage.vue'), componentName: 'AccountsPage', title: '账号与角色', permissions: ['user.read', 'role.read'], group: '系统安全', icon: User },
  { path: '/app/sessions', component: () => import('./pages/SessionsPage.vue'), componentName: 'SessionsPage', title: '在线会话', permission: 'session.manage', group: '系统安全', icon: Connection },
  { path: '/app/audit', component: () => import('./pages/AuditPage.vue'), componentName: 'AuditPage', title: '操作审计', permission: 'audit.read', group: '系统安全', icon: List },
  { path: '/app/releases', component: () => import('./pages/ReleasesPage.vue'), componentName: 'ReleasesPage', title: '版本发布', permission: 'desktop.release.manage', group: '系统安全', icon: Monitor },
  { path: '/app/ssl', component: () => import('./pages/SslPage.vue'), componentName: 'SslPage', title: 'SSL管控', permission: 'ssl.read', group: '系统安全', icon: Lock },
  { path: '/app/plugins', component: () => import('./pages/PluginsPage.vue'), componentName: 'PluginsPage', title: '插件管理', permission: 'plugin.read', group: '系统安全', icon: Cpu },
  { path: '/app/settings', component: () => import('./pages/SettingsPage.vue'), componentName: 'SettingsPage', title: '系统设置', permission: 'settings.manage', group: '系统安全', icon: Setting },

  // 个人与辅助
  { path: '/app/profile', component: () => import('./pages/ProfilePage.vue'), componentName: 'ProfilePage', title: '个人账号' },
  { path: '/app/forbidden', component: () => import('./pages/ForbiddenPage.vue'), componentName: 'ForbiddenPage', title: '访问受限' },
]
export const workspaceGroups = ['巡查工作台', '资产中心', '视频方案', '系统安全']
export const workspaceRoutes: RouteRecordRaw[] = workspacePages.map(page => ({
  path: page.path === '/app' ? '' : page.path.slice(5), component: page.component,
  meta: { title: page.title, permission: page.permission, permissions: page.permissions },
  ...(page.path === '/app/pon' ? { beforeEnter: (to: any) => {
    if (['onus', 'history'].includes(to.query.tab)) return { path: '/app/onus', query: to.query }
    if (['kanban', 'boxes'].includes(to.query.tab)) return { path: '/app/terminal-boxes', query: to.query }
  } } : {})
}))
