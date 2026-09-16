<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import {
  Bell,
  BottomLeft,
  Coin,
  Connection,
  Cpu,
  Files,
  Monitor,
  QuestionFilled,
  Refresh,
  Right,
  TopRight,
  VideoCamera,
  VideoPlay
} from '@element-plus/icons-vue'
import { managementApi, workflowApi, type Dashboard, type SystemStatus, type Alarm } from '../api'
import { bytes, dateTime, duration, errorMessage, formatLinkSpeed, networkSpeed, percent } from '../lib/format'
import { useEvents } from '../stores/events'
import { useAuth } from '../stores/auth'
import PageHeader from '../components/PageHeader.vue'
import StatusBadge from '../components/StatusBadge.vue'
import MiniSparkline from '../components/MiniSparkline.vue'

const auth = useAuth()
const stats = ref<Dashboard>()
const system = ref<SystemStatus>()
const alarms = ref<Alarm[]>([])
const error = ref('')
const loading = ref(false)

const refreshInterval = ref<number>(3000)
const rxHistory = ref<number[]>([])
const txHistory = ref<number[]>([])
const cpuHistory = ref<number[]>([])
const memHistory = ref<number[]>([])
const showNicsDialog = ref(false)

let disposed = false
let timer: ReturnType<typeof setInterval> | null = null

function updateHistory(rx: number, tx: number, cpu: number, mem: number) {
  const maxPoints = 25
  rxHistory.value = [...rxHistory.value.slice(-(maxPoints - 1)), rx]
  txHistory.value = [...txHistory.value.slice(-(maxPoints - 1)), tx]
  cpuHistory.value = [...cpuHistory.value.slice(-(maxPoints - 1)), cpu]
  memHistory.value = [...memHistory.value.slice(-(maxPoints - 1)), mem]
}

async function load() {
  if (loading.value) return
  loading.value = true
  error.value = ''
  const results = await Promise.allSettled([
    managementApi.dashboard(),
    managementApi.system(),
    auth.can('alarm.read') ? workflowApi.alarms({ page: 1, pageSize: 6, state: 'new' }) : Promise.resolve(null)
  ])
  if (!disposed) {
    if (results[0].status === 'fulfilled') stats.value = results[0].value
    if (results[1].status === 'fulfilled') {
      system.value = results[1].value
      const rx = system.value.network?.rxBytesPerSecond ?? 0
      const tx = system.value.network?.txBytesPerSecond ?? 0
      const cpu = system.value.cpuPercent != null ? Math.round(system.value.cpuPercent) : 0
      const mem = percent(system.value.memoryUsedBytes, system.value.memoryTotalBytes)
      updateHistory(rx, tx, cpu, mem)
    }
    if (results[2].status === 'fulfilled') alarms.value = results[2].value?.items || []
    error.value = results
      .filter(item => item.status === 'rejected')
      .map(item => errorMessage(item.reason))
      .join('；')
    loading.value = false
  }
}

function resetTimer() {
  if (timer) {
    clearInterval(timer)
    timer = null
  }
  if (refreshInterval.value > 0) {
    timer = setInterval(() => void load(), refreshInterval.value)
  }
}

watch(refreshInterval, () => {
  resetTimer()
})

const deviceOnlineRate = computed(() => {
  if (!stats.value || !stats.value.devices) return 0
  return Math.round((stats.value.onlineDevices / stats.value.devices) * 100)
})

const channelOnlineRate = computed(() => {
  if (!stats.value || !stats.value.channels) return 0
  return Math.round((stats.value.onlineChannels / stats.value.channels) * 100)
})

const unsubscribe = useEvents().subscribe(['alarm.changed', 'device.changed', 'media.changed', 'reconnected'], load)

onMounted(() => {
  void load()
  resetTimer()
})

onBeforeUnmount(() => {
  disposed = true
  unsubscribe()
  if (timer) clearInterval(timer)
})
</script>

<template>
  <div>
    <PageHeader title="运行总览" description="实时汇聚音视频设备接入状态、吞吐流量、硬件健康指标与待办报警">
      <div style="display: flex; align-items: center; gap: 10px;">
        <span class="muted" style="font-size: 12px;">自动刷新：</span>
        <el-select v-model="refreshInterval" size="small" style="width: 120px;" aria-label="监控刷新频率">
          <el-option :value="3000" label="3秒 (极速)" />
          <el-option :value="5000" label="5秒 (标准)" />
          <el-option :value="10000" label="10秒" />
          <el-option :value="30000" label="30秒" />
          <el-option :value="0" label="暂停刷新" />
        </el-select>
        <el-button :icon="Refresh" :loading="loading" @click="load">刷新</el-button>
      </div>
    </PageHeader>

    <el-alert v-if="error" class="page-alert" type="error" :title="error" :closable="false" show-icon style="margin-bottom: 16px;" />

    <!-- 待办警报横幅提示 -->
    <div
      v-if="stats?.pendingAlarms && stats.pendingAlarms > 0"
      class="alarm-banner"
      style="background: #fef2f2; border: 1px solid #fecaca; border-radius: var(--radius-lg); padding: 12px 18px; display: flex; align-items: center; justify-content: space-between; margin-bottom: 18px; box-shadow: var(--shadow-sm);"
    >
      <div style="display: flex; align-items: center; gap: 10px;">
        <el-icon class="red" style="font-size: 20px;"><Bell /></el-icon>
        <span style="font-size: 13.5px; font-weight: 600; color: #991b1b;">
          平台当前有 {{ stats.pendingAlarms }} 条待处理报警事件需值守人员核实！
        </span>
      </div>
      <router-link to="/app/alarms">
        <el-button type="danger" size="small" :icon="Right">立即处置</el-button>
      </router-link>
    </div>

    <!-- 快捷操作入口通道卡片 -->
    <div class="shortcuts-row" style="display: grid; grid-template-columns: repeat(4, 1fr); gap: 14px; margin-bottom: 20px;">
      <router-link to="/app/live" class="shortcut-card">
        <div class="sc-icon blue"><el-icon><VideoCamera /></el-icon></div>
        <div class="sc-meta">
          <strong>实时视频预览</strong>
          <small>多路分屏联动与巡检</small>
        </div>
      </router-link>

      <router-link to="/app/playback" class="shortcut-card">
        <div class="sc-icon teal"><el-icon><VideoPlay /></el-icon></div>
        <div class="sc-meta">
          <strong>录像检索回放</strong>
          <small>时间轴回溯与片段提取</small>
        </div>
      </router-link>

      <router-link to="/app/devices" class="shortcut-card">
        <div class="sc-icon purple"><el-icon><Connection /></el-icon></div>
        <div class="sc-meta">
          <strong>设备接入管理</strong>
          <small>NVR/IPC 录入与通道同步</small>
        </div>
      </router-link>

      <router-link to="/app/alarms" class="shortcut-card">
        <div class="sc-icon amber"><el-icon><Bell /></el-icon></div>
        <div class="sc-meta">
          <strong>报警处置中心</strong>
          <small>移动侦测与异常上报</small>
        </div>
      </router-link>
    </div>

    <!-- 顶部四项业务 KPI 指标 -->
    <div class="kpi-grid">
      <div class="kpi-card">
        <div class="kpi-icon-wrap teal"><el-icon><Connection /></el-icon></div>
        <div class="kpi-content">
          <div class="kpi-label">设备在线状态</div>
          <div class="kpi-value">
            {{ stats?.onlineDevices ?? '—' }}<small>/ {{ stats?.devices ?? '—' }}</small>
          </div>
          <div class="kpi-sub">
            在线率 {{ deviceOnlineRate }}% · {{ stats?.devices ?? 0 }} 台录像设备
          </div>
        </div>
      </div>

      <div class="kpi-card">
        <div class="kpi-icon-wrap blue"><el-icon><VideoCamera /></el-icon></div>
        <div class="kpi-content">
          <div class="kpi-label">监控通道在线</div>
          <div class="kpi-value">
            {{ stats?.onlineChannels ?? '—' }}<small>/ {{ stats?.channels ?? '—' }}</small>
          </div>
          <div class="kpi-sub">
            在线率 {{ channelOnlineRate }}% · {{ stats?.channels ?? 0 }} 路接入点
          </div>
        </div>
      </div>

      <div class="kpi-card">
        <div class="kpi-icon-wrap red"><el-icon><Bell /></el-icon></div>
        <div class="kpi-content">
          <div class="kpi-label">待处理报警</div>
          <div class="kpi-value">{{ stats?.pendingAlarms ?? '—' }}</div>
          <div class="kpi-sub">
            今日上报 {{ stats?.alarmsToday ?? '—' }} 条安全事件
          </div>
        </div>
      </div>

      <div class="kpi-card">
        <div class="kpi-icon-wrap amber"><el-icon><Monitor /></el-icon></div>
        <div class="kpi-content">
          <div class="kpi-label">在线业务会话</div>
          <div class="kpi-value">{{ stats?.onlineSessions ?? '—' }}</div>
          <div class="kpi-sub">
            总账号 {{ stats?.users ?? '—' }} · 权限角色 {{ stats?.roles ?? '—' }}
          </div>
        </div>
      </div>
    </div>

    <!-- 实时网络吞吐模块 -->
    <section class="filter-card" style="margin-bottom: 20px;">
      <div style="display: flex; align-items: center; justify-content: space-between; margin-bottom: 14px;">
        <div style="display: flex; align-items: baseline; gap: 10px;">
          <h2 style="margin: 0; font-size: 15px;">全平台实时网络吞吐</h2>
          <span class="muted" style="font-size: 12px;">实时视频上行推流与客户端下行拉流速率监视</span>
        </div>
        <el-button
          v-if="system?.network?.interfaces?.length"
          size="small"
          text
          type="primary"
          @click="showNicsDialog = true"
        >
          查看物理与虚拟网卡明细 ({{ system.network.interfaces.length }})
        </el-button>
      </div>

      <div style="display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 16px;">
        <!-- 下行接收 -->
        <div class="net-card rx-card">
          <div style="display: flex; justify-content: space-between; align-items: flex-start;">
            <div style="display: flex; align-items: center; gap: 10px;">
              <span class="kpi-icon-wrap teal" style="width: 38px; height: 38px; font-size: 20px;"><el-icon><BottomLeft /></el-icon></span>
              <div>
                <strong style="font-size: 13.5px; color: var(--text-primary); display: block;">实时下行速率 (接收 Rx)</strong>
                <small class="muted">客户端与设备数据流入</small>
              </div>
            </div>
            <div style="text-align: right;">
              <span class="muted" style="font-size: 11px;">累计总接收</span>
              <strong style="display: block; font-size: 13px; color: var(--text-regular);">{{ bytes(system?.network?.totalBytesReceived) }}</strong>
            </div>
          </div>
          <div style="margin: 14px 0 10px;">
            <span style="font-size: 32px; font-weight: 700; color: #0f172a; font-family: Consolas, monospace;">
              {{ networkSpeed(system?.network?.rxBytesPerSecond) }}
            </span>
          </div>
          <MiniSparkline :data="rxHistory" color="#0d9488" :height="44" />
        </div>

        <!-- 上行发送 -->
        <div class="net-card tx-card">
          <div style="display: flex; justify-content: space-between; align-items: flex-start;">
            <div style="display: flex; align-items: center; gap: 10px;">
              <span class="kpi-icon-wrap blue" style="width: 38px; height: 38px; font-size: 20px;"><el-icon><TopRight /></el-icon></span>
              <div>
                <strong style="font-size: 13.5px; color: var(--text-primary); display: block;">实时上行速率 (发送 Tx)</strong>
                <small class="muted">视频流分发与对外输出</small>
              </div>
            </div>
            <div style="text-align: right;">
              <span class="muted" style="font-size: 11px;">累计总发送</span>
              <strong style="display: block; font-size: 13px; color: var(--text-regular);">{{ bytes(system?.network?.totalBytesSent) }}</strong>
            </div>
          </div>
          <div style="margin: 14px 0 10px;">
            <span style="font-size: 32px; font-weight: 700; color: #0f172a; font-family: Consolas, monospace;">
              {{ networkSpeed(system?.network?.txBytesPerSecond) }}
            </span>
          </div>
          <MiniSparkline :data="txHistory" color="#2563eb" :height="44" />
        </div>
      </div>
    </section>

    <!-- 服务器性能监视模块 -->
    <section class="filter-card" style="margin-bottom: 20px;">
      <div style="display: flex; align-items: center; justify-content: space-between; margin-bottom: 14px; flex-wrap: wrap; gap: 8px;">
        <div style="display: flex; align-items: baseline; gap: 10px; flex-wrap: wrap;">
          <h2 style="margin: 0; font-size: 15px;">服务器硬件与微内核健康</h2>
          <span v-if="system?.host" class="muted" style="font-size: 12px;">
            {{ system.host.machineName }} · {{ system.host.osDescription }} ({{ system.host.framework }})
          </span>
        </div>
        <span v-if="system" class="muted" style="font-size: 12px;">
          系统开机 {{ duration(system.host?.systemUptimeSeconds) }} · 服务连续运行 {{ duration(system.uptimeSeconds) }}
        </span>
      </div>

      <div style="display: grid; grid-template-columns: repeat(3, minmax(0, 1fr)); gap: 16px;">
        <!-- CPU 卡片 -->
        <div class="perf-metric-card">
          <div style="display: flex; justify-content: space-between; align-items: center; margin-bottom: 6px;">
            <div style="display: flex; align-items: center; gap: 8px; font-weight: 600; font-size: 13px;">
              <el-icon class="blue"><Cpu /></el-icon>
              <span>CPU 计算负载</span>
            </div>
            <strong style="font-size: 20px; font-weight: 700; color: #1e293b;">
              {{ system?.cpuPercent != null ? `${Math.round(system.cpuPercent)}%` : '—' }}
            </strong>
          </div>
          <MiniSparkline :data="cpuHistory" color="#3b82f6" :height="36" :min="0" :max="100" />
          <el-progress :percentage="system?.cpuPercent != null ? Math.round(system.cpuPercent) : 0" :show-text="false" color="#3b82f6" style="margin-top: 8px;" />
          <div class="perf-stats-list">
            <div><span>物理/逻辑核心</span><strong>{{ system?.performance?.cpuCores ?? '—' }} 核</strong></div>
            <div><span>服务自身 CPU</span><strong>{{ system?.performance?.processCpuPercent != null ? `${system.performance.processCpuPercent}%` : '—' }}</strong></div>
            <div><span>活跃工作线程</span><strong>{{ system?.performance?.processThreads ?? '—' }} 个</strong></div>
          </div>
        </div>

        <!-- 内存卡片 -->
        <div class="perf-metric-card">
          <div style="display: flex; justify-content: space-between; align-items: center; margin-bottom: 6px;">
            <div style="display: flex; align-items: center; gap: 8px; font-weight: 600; font-size: 13px;">
              <el-icon class="teal"><Coin /></el-icon>
              <span>物理内存分配</span>
            </div>
            <strong style="font-size: 18px; font-weight: 700; color: #1e293b;">
              {{ system ? bytes(system.memoryUsedBytes) : '—' }}
              <small class="muted" style="font-size: 12px; font-weight: 400;">/ {{ bytes(system?.memoryTotalBytes) }}</small>
            </strong>
          </div>
          <MiniSparkline :data="memHistory" color="#0d9488" :height="36" :min="0" :max="100" />
          <el-progress :percentage="system ? percent(system.memoryUsedBytes, system.memoryTotalBytes) : 0" :show-text="false" color="#0d9488" style="margin-top: 8px;" />
          <div class="perf-stats-list">
            <div><span>空闲可用内存</span><strong>{{ system ? bytes((system.memoryTotalBytes || 0) - (system.memoryUsedBytes || 0)) : '—' }}</strong></div>
            <div><span>分页交换空间</span><strong>{{ bytes(system?.performance?.swapUsedBytes) }}</strong></div>
            <div><span>GC托管堆/工作集</span><strong>{{ bytes(system?.performance?.gcHeapBytes) }}</strong></div>
          </div>
        </div>

        <!-- 存储空间卡片 -->
        <div class="perf-metric-card">
          <div style="display: flex; justify-content: space-between; align-items: center; margin-bottom: 6px;">
            <div style="display: flex; align-items: center; gap: 8px; font-weight: 600; font-size: 13px;">
              <el-icon class="amber"><Files /></el-icon>
              <span>存储卷与磁盘</span>
            </div>
            <span class="count-badge">{{ system?.disks?.length || 1 }} 个挂载卷</span>
          </div>

          <div v-if="system?.disks?.length" style="display: flex; flex-direction: column; gap: 10px; margin-top: 8px; max-height: 180px; overflow-y: auto;">
            <div v-for="disk in system.disks" :key="disk.name">
              <div style="display: flex; justify-content: space-between; font-size: 12px; margin-bottom: 3px;">
                <strong>{{ disk.name }} <span v-if="disk.isDataPath" style="font-size: 10px; color: var(--teal); background: #e6fffa; padding: 1px 4px; border-radius: 2px;">主录像盘</span></strong>
                <span class="muted">{{ bytes(disk.usedBytes) }} / {{ bytes(disk.totalBytes) }} ({{ disk.usedPercent }}%)</span>
              </div>
              <el-progress :percentage="disk.usedPercent ?? 0" :show-text="false" :color="disk.usedPercent && disk.usedPercent > 90 ? '#ef4444' : '#f59e0b'" />
              <div style="text-align: right; font-size: 11px; color: var(--text-light); margin-top: 2px;">剩余可用 {{ bytes(disk.freeBytes) }}</div>
            </div>
          </div>
          <div v-else style="margin-top: 14px;">
            <div style="font-size: 20px; font-weight: 700; margin-bottom: 8px;">
              {{ bytes(system?.diskFreeBytes) }} 可用 <small class="muted" style="font-size: 13px;">/ {{ bytes(system?.diskTotalBytes) }}</small>
            </div>
            <el-progress :percentage="system ? percent(system.diskFreeBytes, system.diskTotalBytes) : 0" :show-text="false" color="#f59e0b" />
          </div>
        </div>
      </div>

      <!-- 核心微服务守护状态 -->
      <div v-if="system?.services" style="display: flex; flex-wrap: wrap; gap: 20px; margin-top: 16px; padding-top: 14px; border-top: 1px solid var(--border-light);">
        <div v-for="svc in system.services" :key="svc.name" style="display: flex; align-items: center; gap: 8px; font-size: 12.5px;">
          <span style="color: var(--text-regular); font-weight: 500;">{{ svc.name }}</span>
          <StatusBadge :value="svc.status" />
        </div>
      </div>
    </section>

    <!-- 下方并列：当前媒体负载与待办报警 -->
    <div style="display: grid; grid-template-columns: 340px minmax(0, 1fr); gap: 16px;">
      <!-- 媒体负载 -->
      <div class="filter-card" style="margin-bottom: 0;">
        <h2 style="font-size: 15px; margin-bottom: 12px;">实时媒体流负载</h2>
        <dl class="data-list">
          <div>
            <dt>实时预览会话</dt>
            <dd><span style="color: var(--primary); font-weight: 700; font-size: 16px;">{{ stats?.liveSessions ?? '—' }}</span> 路</dd>
          </div>
          <div>
            <dt>录像回放会话</dt>
            <dd><span style="color: var(--teal); font-weight: 700; font-size: 16px;">{{ stats?.playbackSessions ?? '—' }}</span> 路</dd>
          </div>
          <div>
            <dt>实时兼容转码</dt>
            <dd><span style="color: var(--amber); font-weight: 700; font-size: 16px;">{{ system?.media.transcodes ?? '—' }}</span> 路</dd>
          </div>
        </dl>
      </div>

      <!-- 最新待处理报警 -->
      <div v-if="auth.can('alarm.read')" class="table-card">
        <div style="padding: 14px 18px; display: flex; align-items: center; justify-content: space-between; border-bottom: 1px solid var(--border-light);">
          <h2 style="margin: 0; font-size: 15px;">待处理报警事件</h2>
          <router-link to="/app/alarms" style="font-size: 12.5px;">查看全部报警 →</router-link>
        </div>
        <el-table :data="alarms" empty-text="当前暂无待处理报警">
          <el-table-column prop="channelName" label="触发通道" min-width="140" show-overflow-tooltip />
          <el-table-column prop="eventType" label="事件类型" min-width="130" />
          <el-table-column label="发生时间" min-width="168">
            <template #default="{ row }">{{ dateTime(row.occurredAt) }}</template>
          </el-table-column>
          <el-table-column width="80" fixed="right">
            <template #default="{ row }">
              <router-link :to="{ path: '/app/alarms', query: { id: row.id } }">
                <el-button link type="primary">处置</el-button>
              </router-link>
            </template>
          </el-table-column>
        </el-table>
      </div>
    </div>

    <!-- 网卡详情弹窗 -->
    <el-dialog v-model="showNicsDialog" title="服务器网络接口明细" width="860px">
      <el-table :data="system?.network?.interfaces || []" empty-text="未探测到活跃网卡">
        <el-table-column prop="name" label="网卡名称" min-width="130" show-overflow-tooltip />
        <el-table-column prop="ipAddress" label="IPv4 / IP" min-width="130">
          <template #default="{ row }">{{ row.ipAddress || '—' }}</template>
        </el-table-column>
        <el-table-column prop="type" label="接口类型" width="110" />
        <el-table-column label="协商速率" min-width="168">
          <template #header>
            <span style="display: inline-flex; align-items: center; gap: 4px;">
              <span>协商速率</span>
              <el-tooltip content="物理网卡显示网线/光纤协商后的物理工作带宽；虚拟网卡无物理PHY限制" placement="top">
                <el-icon style="cursor: pointer;"><QuestionFilled /></el-icon>
              </el-tooltip>
            </span>
          </template>
          <template #default="{ row }">{{ formatLinkSpeed(row.speed) }}</template>
        </el-table-column>
        <el-table-column label="累计接收" min-width="110">
          <template #default="{ row }">{{ bytes(row.bytesReceived) }}</template>
        </el-table-column>
        <el-table-column label="累计发送" min-width="110">
          <template #default="{ row }">{{ bytes(row.bytesSent) }}</template>
        </el-table-column>
        <el-table-column prop="status" label="状态" width="80">
          <template #default="{ row }">
            <StatusBadge :value="row.status.toLowerCase() === 'up' ? 'online' : 'offline'" />
          </template>
        </el-table-column>
      </el-table>
    </el-dialog>
  </div>
</template>

<style scoped>
.shortcut-card {
  background: #ffffff;
  border: 1px solid var(--border);
  border-radius: var(--radius-lg);
  padding: 14px 18px;
  display: flex;
  align-items: center;
  gap: 14px;
  box-shadow: var(--shadow-card);
  transition: all 0.2s ease;
  text-decoration: none;
}
.shortcut-card:hover {
  transform: translateY(-2px);
  border-color: var(--border-dark);
  box-shadow: var(--shadow-md);
  text-decoration: none;
}
.sc-icon {
  width: 40px;
  height: 40px;
  border-radius: var(--radius-md);
  display: grid;
  place-items: center;
  font-size: 20px;
}
.sc-icon.blue { background: #eff6ff; color: #2563eb; }
.sc-icon.teal { background: #f0fdfa; color: #0d9488; }
.sc-icon.purple { background: #f5f3ff; color: #8b5cf6; }
.sc-icon.amber { background: #fffbeb; color: #f59e0b; }

.sc-meta strong {
  display: block;
  font-size: 13.5px;
  color: var(--text-primary);
}
.sc-meta small {
  font-size: 11.5px;
  color: var(--text-muted);
}

.net-card {
  background: #ffffff;
  border: 1px solid var(--border);
  border-radius: var(--radius-lg);
  padding: 18px 20px;
  display: flex;
  flex-direction: column;
}

.perf-metric-card {
  background: #f8fafc;
  border: 1px solid var(--border);
  border-radius: var(--radius-md);
  padding: 16px;
  display: flex;
  flex-direction: column;
}

.perf-stats-list {
  margin-top: 14px;
  padding-top: 10px;
  border-top: 1px solid var(--border);
  display: flex;
  flex-direction: column;
  gap: 6px;
  font-size: 12px;
}
.perf-stats-list > div {
  display: flex;
  justify-content: space-between;
}
.perf-stats-list span { color: var(--text-muted); }
.perf-stats-list strong { color: var(--text-regular); font-weight: 500; }

@media (max-width: 1024px) {
  .shortcuts-row {
    grid-template-columns: repeat(2, 1fr) !important;
  }
}
</style>
