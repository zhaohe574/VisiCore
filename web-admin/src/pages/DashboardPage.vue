<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref } from 'vue'
import { Bell, Connection, Monitor, Refresh, VideoCamera } from '@element-plus/icons-vue'
import { managementApi, workflowApi, type Dashboard, type SystemStatus, type Alarm } from '../api'
import { bytes, dateTime, errorMessage, percent } from '../lib/format'
import { useEvents } from '../stores/events'
import { useAuth } from '../stores/auth'
import PageHeader from '../components/PageHeader.vue'
import StatusBadge from '../components/StatusBadge.vue'
const auth = useAuth(), stats = ref<Dashboard>(), system = ref<SystemStatus>(), alarms = ref<Alarm[]>([]), error = ref(''), loading = ref(false)
let disposed = false
async function load() {
  if (loading.value) return
  loading.value = true; error.value = ''
  const results = await Promise.allSettled([managementApi.dashboard(), managementApi.system(), auth.can('alarm.read') ? workflowApi.alarms({ page: 1, pageSize: 6, state: 'new' }) : Promise.resolve(null)])
  if (!disposed) {
    if (results[0].status === 'fulfilled') stats.value = results[0].value
    if (results[1].status === 'fulfilled') system.value = results[1].value
    if (results[2].status === 'fulfilled') alarms.value = results[2].value?.items || []
    error.value = results.filter(item => item.status === 'rejected').map(item => errorMessage(item.reason)).join('；')
    loading.value = false
  }
}
const unsubscribe = useEvents().subscribe(['alarm.changed', 'device.changed', 'media.changed', 'reconnected'], load)
let timer: ReturnType<typeof setInterval>
onMounted(() => { void load(); timer = setInterval(() => void load(), 30000) })
onBeforeUnmount(() => { disposed = true; unsubscribe(); clearInterval(timer) })
</script>
<template><div><PageHeader title="运行总览"><el-button :icon="Refresh" :loading="loading" @click="load">刷新</el-button></PageHeader><el-alert v-if="error" class="page-alert" type="error" :title="error" :closable="false" show-icon />
  <div v-loading="loading && !stats" class="metrics-row"><article class="metric"><div><span>设备在线</span><el-icon class="teal"><Connection /></el-icon></div><strong>{{ stats?.onlineDevices ?? '—' }}<small>/ {{ stats?.devices ?? '—' }}</small></strong><span>已接入录像机</span></article><article class="metric"><div><span>通道在线</span><el-icon class="blue"><VideoCamera /></el-icon></div><strong>{{ stats?.onlineChannels ?? '—' }}<small>/ {{ stats?.channels ?? '—' }}</small></strong><span>视频通道</span></article><article class="metric"><div><span>待处理报警</span><el-icon class="red"><Bell /></el-icon></div><strong>{{ stats?.pendingAlarms ?? '—' }}</strong><span>今日 {{ stats?.alarmsToday ?? '—' }} 条</span></article><article class="metric"><div><span>在线会话</span><el-icon class="amber"><Monitor /></el-icon></div><strong>{{ stats?.onlineSessions ?? '—' }}</strong><span>账号 {{ stats?.users ?? '—' }} · 角色 {{ stats?.roles ?? '—' }}</span></article></div>
  <section class="page-section"><div class="section-heading"><h2>系统运行</h2><span v-if="system" class="muted">版本 {{ system.version }} · 已运行 {{ Math.floor(system.uptimeSeconds / 3600) }} 小时</span></div><div class="resource-grid"><div class="resource-item"><span>CPU 使用率</span><strong>{{ system?.cpuPercent != null ? `${Math.round(system.cpuPercent)}%` : '—' }}</strong><el-progress :percentage="system?.cpuPercent != null ? Math.round(system.cpuPercent) : 0" :show-text="false" color="#3478de" /></div><div class="resource-item"><span>内存</span><strong>{{ system ? bytes(system.memoryUsedBytes) : '—' }}<small>/ {{ bytes(system?.memoryTotalBytes) }}</small></strong><el-progress :percentage="system ? percent(system.memoryUsedBytes, system.memoryTotalBytes) : 0" :show-text="false" color="#16857a" /></div><div class="resource-item"><span>磁盘可用</span><strong>{{ bytes(system?.diskFreeBytes) }}<small>/ {{ bytes(system?.diskTotalBytes) }}</small></strong><el-progress :percentage="system ? percent(system.diskFreeBytes, system.diskTotalBytes) : 0" :show-text="false" color="#b88720" /></div></div><div v-if="system" class="service-strip"><span v-for="service in system.services" :key="service.name">{{ service.name }}<StatusBadge :value="service.status" /></span></div></section>
  <div class="dashboard-columns"><section class="page-section"><div class="section-heading"><h2>当前媒体负载</h2></div><dl class="data-list"><div><dt>实时预览</dt><dd>{{ stats?.liveSessions ?? '—' }} 路</dd></div><div><dt>录像回放</dt><dd>{{ stats?.playbackSessions ?? '—' }} 路</dd></div><div><dt>兼容转码</dt><dd>{{ system?.media.transcodes ?? '—' }} 路</dd></div></dl></section><section v-if="auth.can('alarm.read')" class="page-section"><div class="section-heading"><h2>待处理报警</h2><router-link to="/app/alarms">全部报警</router-link></div><el-table :data="alarms" empty-text="暂无待处理报警"><el-table-column prop="channelName" label="通道" min-width="130" show-overflow-tooltip /><el-table-column prop="eventType" label="事件" min-width="130" /><el-table-column label="发生时间" min-width="168"><template #default="{ row }">{{ dateTime(row.occurredAt) }}</template></el-table-column><el-table-column width="72"><template #default="{ row }"><router-link :to="{ path: '/app/alarms', query: { id: row.id } }">处理</router-link></template></el-table-column></el-table></section></div>
</div></template>
