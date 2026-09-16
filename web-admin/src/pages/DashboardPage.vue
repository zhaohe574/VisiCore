<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { Bell, BottomLeft, Coin, Connection, Cpu, Files, Monitor, QuestionFilled, Refresh, TopRight, VideoCamera } from '@element-plus/icons-vue'
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
    auth.can('alarm.read') ? workflowApi.alarms({ page: 1, pageSize: 6, state: 'new' }) : Promise.resolve(null),
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
    error.value = results.filter(item => item.status === 'rejected').map(item => errorMessage(item.reason)).join('；')
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
    <PageHeader title="运行总览">
      <div class="header-refresh-bar">
        <span class="refresh-tip">刷新频率：</span>
        <el-select v-model="refreshInterval" size="small" class="interval-select" aria-label="监控刷新频率">
          <el-option :value="3000" label="3秒 (实时)" />
          <el-option :value="5000" label="5秒" />
          <el-option :value="10000" label="10秒" />
          <el-option :value="30000" label="30秒" />
          <el-option :value="0" label="暂停自动刷新" />
        </el-select>
        <el-button :icon="Refresh" :loading="loading" @click="load">刷新</el-button>
      </div>
    </PageHeader>

    <el-alert v-if="error" class="page-alert" type="error" :title="error" :closable="false" show-icon />

    <!-- 顶部四项业务指标 -->
    <div v-loading="loading && !stats" class="metrics-row">
      <article class="metric">
        <div><span>设备在线</span><el-icon class="teal"><Connection /></el-icon></div>
        <strong>{{ stats?.onlineDevices ?? '—' }}<small>/ {{ stats?.devices ?? '—' }}</small></strong>
        <span>已接入录像机</span>
      </article>
      <article class="metric">
        <div><span>通道在线</span><el-icon class="blue"><VideoCamera /></el-icon></div>
        <strong>{{ stats?.onlineChannels ?? '—' }}<small>/ {{ stats?.channels ?? '—' }}</small></strong>
        <span>视频通道</span>
      </article>
      <article class="metric">
        <div><span>待处理报警</span><el-icon class="red"><Bell /></el-icon></div>
        <strong>{{ stats?.pendingAlarms ?? '—' }}</strong>
        <span>今日 {{ stats?.alarmsToday ?? '—' }} 条</span>
      </article>
      <article class="metric">
        <div><span>在线会话</span><el-icon class="amber"><Monitor /></el-icon></div>
        <strong>{{ stats?.onlineSessions ?? '—' }}</strong>
        <span>账号 {{ stats?.users ?? '—' }} · 角色 {{ stats?.roles ?? '—' }}</span>
      </article>
    </div>

    <!-- 实时网速监控模块 -->
    <section class="page-section">
      <div class="section-heading">
        <div class="heading-title-group">
          <h2>实时网络吞吐</h2>
          <span class="muted">全平台网卡实时传输流量与动态速率</span>
        </div>
        <div class="heading-extra">
          <el-button v-if="system?.network?.interfaces?.length" size="small" text type="primary" @click="showNicsDialog = true">
            查看网卡明细 ({{ system.network.interfaces.length }})
          </el-button>
        </div>
      </div>

      <div class="network-cards-grid">
        <div class="network-card rx-card">
          <div class="net-card-header">
            <div class="net-card-title">
              <span class="net-icon-wrap rx"><el-icon><BottomLeft /></el-icon></span>
              <div>
                <strong>实时下行网速 (接收)</strong>
                <small>Rx Transfer Rate</small>
              </div>
            </div>
            <div class="net-total-badge">
              <span>累计接收</span>
              <strong>{{ bytes(system?.network?.totalBytesReceived) }}</strong>
            </div>
          </div>
          <div class="net-speed-value">
            <span class="speed-num">{{ networkSpeed(system?.network?.rxBytesPerSecond) }}</span>
          </div>
          <div class="net-chart-wrap">
            <MiniSparkline :data="rxHistory" color="#16857a" :height="44" />
          </div>
        </div>

        <div class="network-card tx-card">
          <div class="net-card-header">
            <div class="net-card-title">
              <span class="net-icon-wrap tx"><el-icon><TopRight /></el-icon></span>
              <div>
                <strong>实时上行网速 (发送)</strong>
                <small>Tx Transfer Rate</small>
              </div>
            </div>
            <div class="net-total-badge">
              <span>累计发送</span>
              <strong>{{ bytes(system?.network?.totalBytesSent) }}</strong>
            </div>
          </div>
          <div class="net-speed-value">
            <span class="speed-num">{{ networkSpeed(system?.network?.txBytesPerSecond) }}</span>
          </div>
          <div class="net-chart-wrap">
            <MiniSparkline :data="txHistory" color="#246ec4" :height="44" />
          </div>
        </div>
      </div>
    </section>

    <!-- 服务器深度性能监视模块 -->
    <section class="page-section">
      <div class="section-heading">
        <div class="heading-title-group">
          <h2>服务器性能监视</h2>
          <span v-if="system?.host" class="muted">
            主机：{{ system.host.machineName }} · {{ system.host.osDescription }} ({{ system.host.osArchitecture }}) · {{ system.host.framework }}
          </span>
        </div>
        <span v-if="system" class="muted">
          系统开机 {{ duration(system.host?.systemUptimeSeconds) }} · 服务运行 {{ duration(system.uptimeSeconds) }}
        </span>
      </div>

      <div class="performance-grid">
        <!-- CPU 性能 -->
        <div class="perf-card">
          <div class="perf-card-header">
            <div class="perf-card-title">
              <el-icon class="blue"><Cpu /></el-icon>
              <span>CPU 占用率</span>
            </div>
            <strong class="perf-main-val">{{ system?.cpuPercent != null ? `${Math.round(system.cpuPercent)}%` : '—' }}</strong>
          </div>
          <div class="perf-chart">
            <MiniSparkline :data="cpuHistory" color="#3478de" :height="36" :min="0" :max="100" />
          </div>
          <el-progress :percentage="system?.cpuPercent != null ? Math.round(system.cpuPercent) : 0" :show-text="false" color="#3478de" />
          <div class="perf-details">
            <div><span>逻辑核心数</span><strong>{{ system?.performance?.cpuCores ?? '—' }} 核</strong></div>
            <div><span>服务自身 CPU</span><strong>{{ system?.performance?.processCpuPercent != null ? `${system.performance.processCpuPercent}%` : '—' }}</strong></div>
            <div><span>服务活跃线程</span><strong>{{ system?.performance?.processThreads ?? '—' }} 个</strong></div>
          </div>
        </div>

        <!-- 内存资源 -->
        <div class="perf-card">
          <div class="perf-card-header">
            <div class="perf-card-title">
              <el-icon class="teal"><Coin /></el-icon>
              <span>物理内存</span>
            </div>
            <strong class="perf-main-val">{{ system ? bytes(system.memoryUsedBytes) : '—' }}<small>/ {{ bytes(system?.memoryTotalBytes) }}</small></strong>
          </div>
          <div class="perf-chart">
            <MiniSparkline :data="memHistory" color="#16857a" :height="36" :min="0" :max="100" />
          </div>
          <el-progress :percentage="system ? percent(system.memoryUsedBytes, system.memoryTotalBytes) : 0" :show-text="false" color="#16857a" />
          <div class="perf-details">
            <div><span>空闲物理内存</span><strong>{{ system ? bytes((system.memoryTotalBytes || 0) - (system.memoryUsedBytes || 0)) : '—' }}</strong></div>
            <div><span>虚拟分页文件</span><strong>{{ bytes(system?.performance?.swapUsedBytes) }}<small v-if="system?.performance?.swapTotalBytes"> / {{ bytes(system?.performance?.swapTotalBytes) }}</small></strong></div>
            <div><span>服务工作集/堆</span><strong>{{ bytes(system?.performance?.processWorkingSetBytes) }}<small v-if="system?.performance?.gcHeapBytes"> (堆 {{ bytes(system?.performance?.gcHeapBytes) }})</small></strong></div>
          </div>
        </div>

        <!-- 存储空间与磁盘 -->
        <div class="perf-card disk-card">
          <div class="perf-card-header">
            <div class="perf-card-title">
              <el-icon class="amber"><Files /></el-icon>
              <span>磁盘与存储卷</span>
            </div>
            <span class="muted-tag">{{ system?.disks?.length ? `${system.disks.length} 个可用卷` : '存储空间' }}</span>
          </div>

          <div v-if="system?.disks?.length" class="disk-list">
            <div v-for="disk in system.disks" :key="disk.name" class="disk-row">
              <div class="disk-row-header">
                <div class="disk-title">
                  <strong>{{ disk.name }}</strong>
                  <span v-if="disk.label" class="disk-label">({{ disk.label }})</span>
                  <span v-if="disk.isDataPath" class="data-tag">主数据盘</span>
                </div>
                <span class="disk-stat">{{ bytes(disk.usedBytes) }} / {{ bytes(disk.totalBytes) }} ({{ disk.usedPercent }}%)</span>
              </div>
              <el-progress :percentage="disk.usedPercent ?? 0" :show-text="false" :color="disk.usedPercent && disk.usedPercent > 90 ? '#cd4456' : '#b88720'" />
              <div class="disk-free-hint">剩余可用 {{ bytes(disk.freeBytes) }}</div>
            </div>
          </div>
          <div v-else class="single-disk-fallback">
            <strong>{{ bytes(system?.diskFreeBytes) }} 可用<small>/ {{ bytes(system?.diskTotalBytes) }}</small></strong>
            <el-progress :percentage="system ? percent(system.diskFreeBytes, system.diskTotalBytes) : 0" :show-text="false" color="#b88720" />
          </div>
        </div>
      </div>

      <!-- 服务组件健康状态条 -->
      <div v-if="system" class="service-strip">
        <span v-for="service in system.services" :key="service.name">
          {{ service.name }}<StatusBadge :value="service.status" />
        </span>
      </div>
    </section>

    <!-- 媒体负载与报警信息 -->
    <div class="dashboard-columns">
      <section class="page-section">
        <div class="section-heading">
          <h2>当前媒体负载</h2>
        </div>
        <dl class="data-list">
          <div><dt>实时预览</dt><dd>{{ stats?.liveSessions ?? '—' }} 路</dd></div>
          <div><dt>录像回放</dt><dd>{{ stats?.playbackSessions ?? '—' }} 路</dd></div>
          <div><dt>兼容转码</dt><dd>{{ system?.media.transcodes ?? '—' }} 路</dd></div>
        </dl>
      </section>

      <section v-if="auth.can('alarm.read')" class="page-section">
        <div class="section-heading">
          <h2>待处理报警</h2>
          <router-link to="/app/alarms">全部报警</router-link>
        </div>
        <el-table :data="alarms" empty-text="暂无待处理报警">
          <el-table-column prop="channelName" label="通道" min-width="130" show-overflow-tooltip />
          <el-table-column prop="eventType" label="事件" min-width="130" />
          <el-table-column label="发生时间" min-width="168">
            <template #default="{ row }">{{ dateTime(row.occurredAt) }}</template>
          </el-table-column>
          <el-table-column width="72">
            <template #default="{ row }">
              <router-link :to="{ path: '/app/alarms', query: { id: row.id } }">处理</router-link>
            </template>
          </el-table-column>
        </el-table>
      </section>
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
              <el-tooltip content="物理网卡显示网线/光纤协商后的物理工作带宽（如 1 Gbps、100 Mbps）；虚拟网卡（如 KVM virtio、Tailscale 等）走宿主机总线直接转发，无物理 PHY 协商上限" placement="top">
                <el-icon style="cursor: pointer;"><QuestionFilled /></el-icon>
              </el-tooltip>
            </span>
          </template>
          <template #default="{ row }">
            {{ formatLinkSpeed(row.speed) }}
          </template>
        </el-table-column>
        <el-table-column label="累计接收" min-width="100">
          <template #default="{ row }">{{ bytes(row.bytesReceived) }}</template>
        </el-table-column>
        <el-table-column label="累计发送" min-width="100">
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
.header-refresh-bar {
  display: flex;
  align-items: center;
  gap: 8px;
}
.refresh-tip {
  font-size: 12px;
  color: var(--muted);
  white-space: nowrap;
}
.interval-select {
  width: 120px;
}
.heading-title-group {
  display: flex;
  align-items: baseline;
  gap: 12px;
  flex-wrap: wrap;
}
.heading-title-group h2 {
  margin: 0;
}
.heading-extra {
  margin-left: auto;
}

/* 实时网速卡片 */
.network-cards-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 20px;
  margin-bottom: 8px;
}
.network-card {
  background: #fff;
  border: 1px solid var(--border);
  border-radius: 6px;
  padding: 18px 20px;
  display: flex;
  flex-direction: column;
  box-shadow: 0 1px 3px rgba(0, 0, 0, 0.02);
}
.net-card-header {
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
  gap: 12px;
}
.net-card-title {
  display: flex;
  align-items: center;
  gap: 10px;
}
.net-icon-wrap {
  width: 34px;
  height: 34px;
  border-radius: 6px;
  display: grid;
  place-items: center;
  font-size: 18px;
}
.net-icon-wrap.rx {
  background: #e8f5f3;
  color: #16857a;
}
.net-icon-wrap.tx {
  background: #eaf1fa;
  color: #246ec4;
}
.net-card-title strong {
  display: block;
  font-size: 14px;
  color: #252b33;
}
.net-card-title small {
  display: block;
  font-size: 11px;
  color: var(--muted);
}
.net-total-badge {
  text-align: right;
  font-size: 11px;
  color: var(--muted);
  line-height: 1.4;
}
.net-total-badge strong {
  display: block;
  font-size: 13px;
  color: #4c5663;
  font-weight: 600;
}
.net-speed-value {
  margin: 14px 0 10px;
}
.speed-num {
  font-size: 30px;
  font-weight: 700;
  color: #1e252d;
  font-family: Consolas, -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif;
  letter-spacing: -0.5px;
}
.net-chart-wrap {
  margin-top: auto;
  padding-top: 6px;
}

/* 详细性能卡片 */
.performance-grid {
  display: grid;
  grid-template-columns: repeat(3, minmax(0, 1fr));
  gap: 20px;
}
.perf-card {
  background: #fff;
  border: 1px solid var(--border);
  border-radius: 6px;
  padding: 18px 20px;
  display: flex;
  flex-direction: column;
}
.perf-card-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 8px;
}
.perf-card-title {
  display: flex;
  align-items: center;
  gap: 8px;
  font-size: 13px;
  color: #64717d;
  font-weight: 500;
}
.perf-card-title .el-icon {
  font-size: 16px;
}
.perf-main-val {
  font-size: 20px;
  font-weight: 600;
  color: #252b33;
}
.perf-main-val small {
  font-size: 12px;
  color: var(--muted);
  font-weight: 400;
  margin-left: 4px;
}
.perf-chart {
  margin: 4px 0 10px;
}
.perf-details {
  margin-top: 14px;
  padding-top: 12px;
  border-top: 1px solid #f0f3f6;
  display: flex;
  flex-direction: column;
  gap: 7px;
}
.perf-details > div {
  display: flex;
  justify-content: space-between;
  font-size: 12px;
}
.perf-details span {
  color: #77818c;
}
.perf-details strong {
  color: #3b444e;
  font-weight: 500;
}
.perf-details strong small {
  font-size: 11px;
  color: var(--muted);
}

/* 磁盘列表卡片 */
.disk-card {
  min-height: 220px;
}
.muted-tag {
  font-size: 11px;
  color: var(--muted);
  background: #f0f3f6;
  padding: 2px 6px;
  border-radius: 3px;
}
.disk-list {
  display: flex;
  flex-direction: column;
  gap: 14px;
  margin-top: 6px;
  max-height: 190px;
  overflow-y: auto;
  padding-right: 4px;
}
.disk-row {
  display: flex;
  flex-direction: column;
  gap: 4px;
}
.disk-row-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  font-size: 12px;
}
.disk-title {
  display: flex;
  align-items: center;
  gap: 6px;
}
.disk-title strong {
  color: #2e3640;
}
.disk-label {
  color: var(--muted);
  font-size: 11px;
}
.data-tag {
  font-size: 10px;
  background: #e8f5f3;
  color: #16857a;
  padding: 1px 5px;
  border-radius: 2px;
  font-weight: 500;
}
.disk-stat {
  color: #65717d;
  font-size: 11px;
}
.disk-free-hint {
  font-size: 11px;
  color: var(--muted);
  text-align: right;
  margin-top: 1px;
}
.single-disk-fallback {
  margin-top: 16px;
}
.single-disk-fallback strong {
  display: block;
  font-size: 20px;
  margin-bottom: 12px;
}

@media (max-width: 1024px) {
  .network-cards-grid {
    grid-template-columns: 1fr;
    gap: 14px;
  }
  .performance-grid {
    grid-template-columns: 1fr;
    gap: 14px;
  }
}
</style>
