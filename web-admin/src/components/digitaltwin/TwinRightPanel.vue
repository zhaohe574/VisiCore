<script setup lang="ts">
import { computed } from 'vue'
import { useRouter } from 'vue-router'
import {
  Close,
  VideoPlay,
  Monitor,
  Box,
  Connection,
  Camera,
  Top,
  Bottom,
  Back,
  Right,
  Refresh,
  Location,
  User
} from '@element-plus/icons-vue'
import type { DigitalTwinNodeDto } from '../../types'

const props = defineProps<{
  node: DigitalTwinNodeDto | null
}>()

const emit = defineEmits<{
  (e: 'close'): void
  (e: 'inspectChannel', channelId: number): void
}>()

const router = useRouter()

const isBox = computed(() => props.node?.targetType === 'terminal_box')
const isCamera = computed(() => props.node?.targetType === 'channel')
const isComputer = computed(() => props.node?.targetType === 'computer')
const isDevice = computed(() => props.node?.targetType === 'network_device')

function goToBoxKanban() {
  if (!props.node) return
  router.push({
    path: '/app/terminal-boxes',
    query: { search: props.node.code || props.node.name }
  })
}

function goToComputerPage() {
  if (!props.node) return
  router.push({
    path: '/app/computers',
    query: { search: props.node.ip || props.node.name }
  })
}

function goToNetworkDevicePage() {
  if (!props.node) return
  router.push({
    path: '/app/network-devices',
    query: { search: props.node.name }
  })
}
</script>

<template>
  <aside v-if="node" class="twin-right-panel">
    <!-- Header -->
    <div class="panel-header">
      <div class="header-left">
        <el-tag
          size="small"
          :type="node.status === 'online' ? 'success' : (node.status === 'abnormal' ? 'warning' : 'danger')"
          effect="dark"
        >
          {{ node.status === 'online' ? '正常在线' : (node.status === 'abnormal' ? '光衰预警' : '离线故障') }}
        </el-tag>
        <span class="node-title">{{ node.name }}</span>
      </div>
      <el-button circle size="small" :icon="Close" @click="emit('close')" />
    </div>

    <!-- Subtitle / Meta -->
    <div class="meta-strip">
      <div v-if="node.code" class="meta-item">
        <span class="meta-label">编码:</span>
        <span class="meta-val font-mono">{{ node.code }}</span>
      </div>
      <div v-if="node.ip" class="meta-item">
        <span class="meta-label">IP:</span>
        <span class="meta-val font-mono">{{ node.ip }}</span>
      </div>
      <div v-if="node.location" class="meta-item">
        <el-icon><Location /></el-icon>
        <span class="meta-val">{{ node.location }}</span>
      </div>
    </div>

    <!-- 1. 终端箱专属透视面板 (Terminal Box Inspector) -->
    <div v-if="isBox" class="panel-body">
      <!-- 光衰指标卡片 -->
      <div class="stat-card">
        <div class="stat-card-title">光网链路质量 (Rx Power)</div>
        <div class="stat-card-value" :class="{ 'text-warn': node.rxPower && node.rxPower < -27 }">
          {{ node.rxPower ? `${node.rxPower.toFixed(2)} dBm` : '未采集' }}
        </div>
        <div class="stat-card-tip">
          {{ (node.rxPower && node.rxPower < -27) ? '光衰超出建议阈值 (-27dBm)，建议清洁光纤端面' : '接收光功率在优良区间 (-8 ~ -26 dBm)' }}
        </div>
      </div>

      <!-- 下挂设备统计药丸 -->
      <div class="box-stats-grid">
        <div class="box-stat-item">
          <span class="stat-num text-cyan">{{ node.onuOnlineCount || 0 }}/{{ node.onuCount || 0 }}</span>
          <span class="stat-lbl">全光 ONU</span>
        </div>
        <div class="box-stat-item">
          <span class="stat-num text-purple">{{ node.computerCount || 0 }}</span>
          <span class="stat-lbl">工位电脑</span>
        </div>
        <div class="box-stat-item">
          <span class="stat-num text-emerald">{{ node.cameraCount || 0 }}</span>
          <span class="stat-lbl">监控摄像头</span>
        </div>
      </div>

      <div class="action-buttons">
        <el-button type="primary" size="small" :icon="Box" @click="goToBoxKanban">
          打开终端箱监控看板
        </el-button>
      </div>
    </div>

    <!-- 2. 摄像头专属视讯面板 (Camera Live Inspector) -->
    <div v-else-if="isCamera" class="panel-body">
      <div class="video-preview-wrapper">
        <img
          v-if="node.channelId && node.status === 'online'"
          :src="`/api/v2/channels/${node.channelId}/preview?t=${Date.now()}`"
          class="live-preview-img"
          alt="实时视讯"
        />
        <div v-else class="preview-empty">
          <el-icon :size="28"><Camera /></el-icon>
          <span>{{ node.status === 'online' ? '视频流准备中...' : '设备当前离线' }}</span>
        </div>
      </div>

      <div class="fov-meta-card">
        <div class="meta-row">
          <span>空间高度: {{ node.altitudeM || 4.5 }} 米 (高空立柱)</span>
          <span>监控视场角: {{ node.fovAngle || 60 }}°</span>
        </div>
        <div class="meta-row">
          <span>水平朝向: {{ node.fovYaw || 0 }}°</span>
          <span>覆盖半径: {{ node.fovRadiusM || 25 }} 米</span>
        </div>
      </div>

      <div class="action-buttons">
        <el-button
          v-if="node.channelId"
          type="primary"
          size="small"
          :icon="VideoPlay"
          @click="emit('inspectChannel', node.channelId)"
        >
          展开通道全屏点检与控制
        </el-button>
      </div>
    </div>

    <!-- 3. 工位电脑专属面板 (Computer Station Inspector) -->
    <div v-else-if="isComputer" class="panel-body">
      <div class="info-list">
        <div class="info-row">
          <span class="info-k">主机名称</span>
          <span class="info-v font-bold">{{ node.name }}</span>
        </div>
        <div class="info-row">
          <span class="info-k">IP 地址</span>
          <span class="info-v font-mono text-cyan">{{ node.ip || 'DHCP' }}</span>
        </div>
        <div class="info-row">
          <span class="info-k">责任人</span>
          <span class="info-v">{{ node.responsiblePerson || '未登记' }}</span>
        </div>
        <div class="info-row">
          <span class="info-k">所属部门</span>
          <span class="info-v">{{ node.department || '未登记' }}</span>
        </div>
        <div class="info-row">
          <span class="info-k">Ping 响应延迟</span>
          <span class="info-v font-mono" :class="node.responseTimeMs ? 'text-emerald' : 'text-slate'">
            {{ node.responseTimeMs ? `${node.responseTimeMs} ms` : '未响应' }}
          </span>
        </div>
      </div>

      <div class="action-buttons">
        <el-button type="primary" size="small" :icon="Monitor" @click="goToComputerPage">
          查看电脑台账详情
        </el-button>
      </div>
    </div>

    <!-- 4. 核心网络设备专属面板 (Core Network Device Inspector) -->
    <div v-else-if="isDevice" class="panel-body">
      <div class="info-list">
        <div class="info-row">
          <span class="info-k">设备名称</span>
          <span class="info-v font-bold">{{ node.name }}</span>
        </div>
        <div class="info-row">
          <span class="info-k">设备类型</span>
          <span class="info-v">{{ node.code }}</span>
        </div>
        <div class="info-row">
          <span class="info-k">管理 IP</span>
          <span class="info-v font-mono text-cyan">{{ node.ip }}</span>
        </div>
        <div class="info-row">
          <span class="info-k">型号规格</span>
          <span class="info-v">{{ node.subSwitchModel || '华为核心网元' }}</span>
        </div>
      </div>

      <div class="action-buttons">
        <el-button type="primary" size="small" :icon="Connection" @click="goToNetworkDevicePage">
          查看网络设备拓扑与端口
        </el-button>
      </div>
    </div>
  </aside>
</template>

<style scoped>
.twin-right-panel {
  width: 320px;
  height: 100%;
  background: rgba(7, 12, 22, 0.94);
  backdrop-filter: blur(16px);
  border-left: 1px solid rgba(56, 189, 248, 0.2);
  display: flex;
  flex-direction: column;
  padding: 16px;
  box-shadow: -4px 0 24px rgba(0, 0, 0, 0.6);
  z-index: 70;
  overflow-y: auto;
}

.panel-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding-bottom: 10px;
  border-bottom: 1px solid rgba(255, 255, 255, 0.08);
}

.header-left {
  display: flex;
  align-items: center;
  gap: 8px;
  overflow: hidden;
}

.node-title {
  font-size: 14px;
  font-weight: 700;
  color: #f8fafc;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.meta-strip {
  display: flex;
  flex-direction: column;
  gap: 4px;
  padding: 10px 0;
  font-size: 11px;
  color: #94a3b8;
  border-bottom: 1px solid rgba(255, 255, 255, 0.05);
}

.meta-item {
  display: flex;
  align-items: center;
  gap: 6px;
}

.meta-val {
  color: #e2e8f0;
}

.font-mono {
  font-family: monospace;
}

.panel-body {
  display: flex;
  flex-direction: column;
  gap: 14px;
  padding-top: 14px;
}

/* Stat card */
.stat-card {
  background: rgba(15, 23, 42, 0.8);
  border: 1px solid rgba(56, 189, 248, 0.25);
  border-radius: 6px;
  padding: 10px 12px;
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.stat-card-title {
  font-size: 11px;
  color: #94a3b8;
}

.stat-card-value {
  font-size: 20px;
  font-weight: 800;
  font-family: monospace;
  color: #38bdf8;
}

.stat-card-value.text-warn {
  color: #f59e0b;
}

.stat-card-tip {
  font-size: 10px;
  color: #64748b;
  line-height: 1.4;
}

/* Box stats grid */
.box-stats-grid {
  display: grid;
  grid-template-columns: repeat(3, 1fr);
  gap: 6px;
}

.box-stat-item {
  background: rgba(15, 23, 42, 0.6);
  border: 1px solid rgba(255, 255, 255, 0.06);
  border-radius: 5px;
  padding: 8px 4px;
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 2px;
}

.stat-num {
  font-size: 14px;
  font-weight: 800;
  font-family: monospace;
}

.stat-lbl {
  font-size: 10px;
  color: #94a3b8;
}

.text-cyan { color: #38bdf8; }
.text-purple { color: #c084fc; }
.text-emerald { color: #34d399; }
.text-slate { color: #64748b; }

/* Video Preview */
.video-preview-wrapper {
  width: 100%;
  height: 160px;
  background: #020617;
  border: 1px solid #0284c7;
  border-radius: 6px;
  overflow: hidden;
  display: flex;
  align-items: center;
  justify-content: center;
}

.live-preview-img {
  width: 100%;
  height: 100%;
  object-fit: cover;
}

.preview-empty {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 6px;
  color: #64748b;
  font-size: 12px;
}

.fov-meta-card {
  background: rgba(15, 23, 42, 0.6);
  border-radius: 5px;
  padding: 8px 10px;
  display: flex;
  flex-direction: column;
  gap: 4px;
  font-size: 11px;
  color: #94a3b8;
}

.meta-row {
  display: flex;
  justify-content: space-between;
}

/* Info list */
.info-list {
  display: flex;
  flex-direction: column;
  gap: 8px;
  background: rgba(15, 23, 42, 0.6);
  border-radius: 6px;
  padding: 10px 12px;
}

.info-row {
  display: flex;
  justify-content: space-between;
  align-items: center;
  font-size: 12px;
}

.info-k {
  color: #94a3b8;
}

.info-v {
  color: #f1f5f9;
}

.font-bold {
  font-weight: 700;
}

.action-buttons {
  margin-top: 6px;
  display: flex;
  flex-direction: column;
  gap: 8px;
}
</style>
