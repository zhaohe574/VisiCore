<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import {
  Aim,
  ArrowRight,
  Box,
  Close,
  Connection,
  CopyDocument,
  Cpu,
  Edit,
  FolderOpened,
  Hide,
  InfoFilled,
  Monitor,
  Refresh,
  Switch,
  VideoCamera,
  VideoPlay,
  View
} from '@element-plus/icons-vue'
import { ElMessage } from 'element-plus'
import type { Channel, Device, Organization, OrganizationNode } from '../api'
import { useAuth } from '../stores/auth'
import { getUnitArea, getUnitName, getUnitWorkshop } from '../lib/organization'
import VideoTile from './VideoTile.vue'
import PtzPanel from './PtzPanel.vue'
import StatusBadge from './StatusBadge.vue'

const props = defineProps<{
  modelValue: boolean
  channel: Channel | null
  organization?: Organization
  devices?: Device[]
  canEdit?: boolean
}>()

const emit = defineEmits<{
  (e: 'update:modelValue', value: boolean): void
  (e: 'edit', channel: Channel): void
}>()

const router = useRouter()
const auth = useAuth()

// 码流类型：1 = 高清主流，2 = 标清子码流
const streamType = ref<1 | 2>(1)
const showPassword = ref(false)
const showPtzPanel = ref(false)

watch(() => props.modelValue, val => {
  if (val) {
    streamType.value = 1
    showPassword.value = false
    showPtzPanel.value = false
  }
})

function getUnitAreaNode(unitId?: number | null): OrganizationNode | undefined {
  if (!props.organization) return undefined
  return getUnitArea(props.organization, unitId)
}

function getUnitWorkshopNode(unitId?: number | null): OrganizationNode | undefined {
  if (!props.organization) return undefined
  return getUnitWorkshop(props.organization, unitId)
}

function getUnitLabel(unitId?: number | null): string {
  if (!props.organization) return ''
  return getUnitName(props.organization, unitId)
}

function getDevice(channel?: Channel | null): Device | undefined {
  if (!channel || !props.devices) return undefined
  return props.devices.find(d => d.id === channel.deviceId)
}

function goToPon(onuId?: number | null, search?: string | null) {
  emit('update:modelValue', false)
  const term = onuId ? String(onuId) : (search || '')
  router.push({ path: '/app/onus', query: { search: term } })
}

async function copyText(text?: string | null, label = '内容') {
  if (!text) {
    ElMessage.warning(`暂无${label}`)
    return
  }
  try {
    await navigator.clipboard.writeText(text)
    ElMessage.success(`${label}已复制到剪贴板`)
  } catch {
    ElMessage.error('复制失败，请手动复制')
  }
}
</script>

<template>
  <el-drawer
    :model-value="modelValue"
    title="通道即席画面点检与诊断"
    size="640px"
    destroy-on-close
    class="inspect-drawer"
    @update:model-value="emit('update:modelValue', $event)"
  >
    <div v-if="channel" class="inspect-container">
      <!-- 头部：通道标识与状态 -->
      <header class="inspect-header">
        <div class="channel-main-info">
          <div class="channel-title-row">
            <h3 class="channel-title">{{ channel.alias || channel.name }}</h3>
            <StatusBadge :value="channel.status" />
          </div>
          <div class="channel-sub-info muted">
            <span>原始名称: {{ channel.name }}</span>
            <span class="dot">·</span>
            <span>通道编号: #{{ channel.deviceChannel }}</span>
            <span v-if="channel.ip" class="dot">·</span>
            <span v-if="channel.ip" class="monospace">{{ channel.ip }}</span>
          </div>
        </div>
      </header>

      <!-- 实时视频点检窗格（即开即播，关闭抽屉自动销毁释放租约） -->
      <section class="inspect-player-section">
        <div class="player-toolbar">
          <div class="toolbar-left">
            <el-tag size="small" type="success" effect="plain" class="live-tag">
              <span class="live-dot" /> 实时出流中
            </el-tag>
            <el-radio-group v-model="streamType" size="small">
              <el-radio-button :value="1">高清主流</el-radio-button>
              <el-radio-button :value="2">流畅子码流</el-radio-button>
            </el-radio-group>
          </div>
          <div class="toolbar-right">
            <el-button
              v-if="channel.ptzCapable"
              size="small"
              :type="showPtzPanel ? 'primary' : 'default'"
              :icon="Aim"
              @click="showPtzPanel = !showPtzPanel"
            >
              云台微调
            </el-button>
          </div>
        </div>

        <div class="video-container-wrapper">
          <VideoTile
            :index="0"
            :channel="channel"
            mode="live"
            :stream-type="streamType"
            :selected="true"
          />
        </div>

        <!-- 可折叠云台控制面板 -->
        <el-collapse-transition>
          <div v-if="showPtzPanel && channel.ptzCapable" class="ptz-wrapper">
            <PtzPanel :channel="channel" :allowed="auth.can('live.ptz')" />
          </div>
        </el-collapse-transition>
      </section>

      <!-- 光网承载链路与网络诊断（运维重点） -->
      <section class="inspect-card">
        <div class="card-header">
          <el-icon><Connection /></el-icon>
          <strong>光网承载链路与网络诊断</strong>
        </div>
        <div class="card-body">
          <div class="info-grid">
            <div class="info-item">
              <span class="label">承载终端箱</span>
              <span class="value">
                <template v-if="channel.terminalBoxName || channel.terminalBoxCode">
                  <el-tag size="small" type="primary" effect="plain">
                    {{ channel.terminalBoxName }} ({{ channel.terminalBoxCode }})
                  </el-tag>
                </template>
                <span v-else class="muted">未绑定终端箱</span>
              </span>
            </div>

            <div class="info-item">
              <span class="label">承载 ONU 设备</span>
              <span class="value">
                <template v-if="channel.onuName || channel.onuAlias">
                  <span style="font-weight: 500;">{{ channel.onuAlias || channel.onuName }}</span>
                  <el-tag
                    v-if="channel.onuStatus"
                    size="small"
                    :type="channel.onuStatus === 'online' || channel.onuStatus === '1' ? 'success' : 'danger'"
                    style="margin-left: 6px;"
                  >
                    {{ (channel.onuStatus === 'online' || channel.onuStatus === '1') ? '在线' : '离线' }}
                  </el-tag>
                </template>
                <span v-else class="muted">未绑定 ONU</span>
              </span>
            </div>

            <div class="info-item">
              <span class="label">走线与端口</span>
              <span class="value">
                <span v-if="channel.portLabel" style="font-weight: 500;">{{ channel.portLabel }}</span>
                <span v-else-if="channel.networkPort">
                  {{ channel.isSwitchPort ? `下挂交换机 #${channel.networkPort} 口` : `ONU LAN${channel.networkPort} 口` }}
                </span>
                <span v-else class="muted">—</span>
              </span>
            </div>

            <div v-if="channel.onuDeviceId" class="info-item full-width">
              <span class="label">光链路健康定界</span>
              <div class="value link-health-box">
                <el-tag v-if="channel.onuHealthStatus === 'healthy'" size="small" type="success">🟢 光网运行良好</el-tag>
                <el-tag v-else-if="channel.onuHealthStatus === 'sub_health_weak_light'" size="small" type="warning" effect="dark">🟡 弱光亚健康</el-tag>
                <el-tag v-else-if="channel.onuHealthStatus === 'power_off'" size="small" type="danger" effect="dark">⚡ 供电中断 / 掉电</el-tag>
                <el-tag v-else-if="channel.onuHealthStatus === 'trunk_broken'" size="small" type="danger" effect="dark" style="background: #8b0000; border-color: #8b0000;">🚨 主干光缆中断</el-tag>
                <el-tag v-else-if="channel.onuHealthStatus === 'splitter_broken'" size="small" type="warning" effect="dark" style="background: #9333ea; border-color: #9333ea; color: #fff;">💥 分光/支干中断</el-tag>
                <el-tag v-else-if="channel.onuHealthStatus === 'fiber_cut'" size="small" type="danger" effect="plain">🔴 分支断纤/中断</el-tag>
                <el-tag v-else-if="channel.onuHealthStatus === 'jitter_buffering'" size="small" type="info" effect="dark">🛡️ 闪断防抖缓冲中</el-tag>
                <span v-if="channel.onuRootCause" class="muted reason-text">{{ channel.onuRootCause }}</span>
                <span v-if="channel.onuRxPower !== null && channel.onuRxPower !== undefined" class="rx-power" :style="{ color: channel.onuRxPower < -24 ? '#d97706' : '#10b981' }">
                  接收光功率: {{ channel.onuRxPower }} dBm
                </span>
              </div>
            </div>
          </div>
        </div>
      </section>

      <!-- 组织与设备资产归属 -->
      <section class="inspect-card">
        <div class="card-header">
          <el-icon><FolderOpened /></el-icon>
          <strong>组织架构与接入设备</strong>
        </div>
        <div class="card-body">
          <div class="info-grid">
            <div class="info-item">
              <span class="label">组织划分</span>
              <span class="value">
                <template v-if="channel.unitId && organization">
                  {{ getUnitWorkshopNode(channel.unitId)?.name }}
                  <el-icon style="font-size: 11px; margin: 0 2px;"><ArrowRight /></el-icon>
                  {{ getUnitAreaNode(channel.unitId)?.name }}
                  <el-icon style="font-size: 11px; margin: 0 2px;"><ArrowRight /></el-icon>
                  <strong>{{ getUnitLabel(channel.unitId) }}</strong>
                </template>
                <span v-else class="muted">未划分组织区域</span>
              </span>
            </div>

            <div class="info-item">
              <span class="label">归属录像机</span>
              <span class="value">
                <template v-if="getDevice(channel)">
                  <strong style="color: var(--text-primary);">{{ getDevice(channel)?.name }}</strong>
                  <span class="muted monospace" style="font-size: 12px; margin-left: 6px;">({{ getDevice(channel)?.host }})</span>
                </template>
                <span v-else class="muted">设备未加载或未知</span>
              </span>
            </div>
          </div>
        </div>
      </section>

      <!-- 网络访问独立凭据（快速调试） -->
      <section class="inspect-card">
        <div class="card-header">
          <el-icon><InfoFilled /></el-icon>
          <strong>网络访问凭据与备注</strong>
        </div>
        <div class="card-body">
          <div class="info-grid">
            <div class="info-item">
              <span class="label">独立访问 IP</span>
              <span class="value flex-row">
                <template v-if="channel.ip">
                  <span class="monospace" style="font-weight: 600;">{{ channel.ip }}</span>
                  <el-button link type="primary" :icon="CopyDocument" size="small" @click="copyText(channel.ip, 'IP地址')">复制</el-button>
                </template>
                <span v-else class="muted">—</span>
              </span>
            </div>

            <div class="info-item">
              <span class="label">访问账号</span>
              <span class="value flex-row">
                <template v-if="channel.username">
                  <span class="monospace">{{ channel.username }}</span>
                  <el-button link type="primary" :icon="CopyDocument" size="small" @click="copyText(channel.username, '账号')">复制</el-button>
                </template>
                <span v-else class="muted">—</span>
              </span>
            </div>

            <div class="info-item">
              <span class="label">访问密码</span>
              <span class="value flex-row">
                <template v-if="channel.hasPassword">
                  <span>已配置（不会回显）</span>
                </template>
                <span v-else class="muted">—</span>
              </span>
            </div>

            <div class="info-item full-width">
              <span class="label">现场备注</span>
              <span class="value" style="white-space: pre-wrap; word-break: break-all;">
                {{ channel.remark || '无备注记录' }}
              </span>
            </div>
          </div>
        </div>
      </section>
    </div>

    <!-- 底部功能按钮 -->
    <template #footer>
      <div class="drawer-footer">
        <div class="footer-left">
          <el-button
            v-if="channel?.onuDeviceId || channel?.terminalBoxId"
            type="warning"
            plain
            :icon="Connection"
            @click="goToPon(channel?.onuDeviceId, channel?.terminalBoxName)"
          >
            前往光网排障
          </el-button>
        </div>
        <div class="footer-right">
          <el-button v-if="canEdit && channel" type="primary" :icon="Edit" @click="emit('edit', channel)">
            修改通道备注/凭据
          </el-button>
          <el-button @click="emit('update:modelValue', false)">关闭</el-button>
        </div>
      </div>
    </template>
  </el-drawer>
</template>

<style scoped>
.inspect-container {
  display: flex;
  flex-direction: column;
  gap: 16px;
  padding-bottom: 24px;
}

.inspect-header {
  padding-bottom: 14px;
  border-bottom: 1px solid var(--border);
}

.channel-title-row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
}

.channel-title {
  margin: 0;
  font-size: 17px;
  font-weight: 600;
  color: var(--text-primary);
}

.channel-sub-info {
  margin-top: 6px;
  font-size: 12px;
  display: flex;
  align-items: center;
  gap: 6px;
  flex-wrap: wrap;
}

.dot {
  color: var(--border-strong);
}

.inspect-player-section {
  border-radius: var(--radius-md);
  border: 1px solid var(--border);
  background: var(--surface);
  overflow: hidden;
}

.player-toolbar {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 8px 12px;
  background: var(--surface-alt);
  border-bottom: 1px solid var(--border);
}

.toolbar-left {
  display: flex;
  align-items: center;
  gap: 10px;
}

.live-tag {
  display: flex;
  align-items: center;
  gap: 4px;
  font-weight: 500;
}

.live-dot {
  width: 6px;
  height: 6px;
  border-radius: 50%;
  background-color: var(--primary);
  animation: pulse 1.8s infinite;
}

@keyframes pulse {
  0% { transform: scale(0.95); opacity: 0.8; }
  50% { transform: scale(1.3); opacity: 1; }
  100% { transform: scale(0.95); opacity: 0.8; }
}

.video-container-wrapper {
  position: relative;
  width: 100%;
  aspect-ratio: 16 / 9;
  background: #000;
  overflow: hidden;
}

.ptz-wrapper {
  padding: 12px;
  border-top: 1px solid var(--border);
  background: var(--surface-alt);
}

.inspect-card {
  border-radius: var(--radius-md);
  border: 1px solid var(--border);
  background: var(--surface);
  overflow: hidden;
}

.card-header {
  display: flex;
  align-items: center;
  gap: 6px;
  padding: 10px 14px;
  background: var(--surface-alt);
  border-bottom: 1px solid var(--border);
  font-size: 13.5px;
  color: var(--text-primary);
}

.card-body {
  padding: 12px 14px;
}

.info-grid {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 12px 16px;
}

.info-item {
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.info-item.full-width {
  grid-column: 1 / -1;
}

.info-item .label {
  font-size: 12px;
  color: var(--text-secondary);
}

.info-item .value {
  font-size: 13px;
  color: var(--text-primary);
}

.flex-row {
  display: flex;
  align-items: center;
  gap: 8px;
}

.link-health-box {
  display: flex;
  align-items: center;
  gap: 8px;
  flex-wrap: wrap;
  margin-top: 2px;
}

.reason-text {
  font-size: 12px;
}

.rx-power {
  font-size: 12px;
  font-weight: 500;
}

.drawer-footer {
  display: flex;
  align-items: center;
  justify-content: space-between;
  width: 100%;
}

.footer-left,
.footer-right {
  display: flex;
  align-items: center;
  gap: 8px;
}
</style>
