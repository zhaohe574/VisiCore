<script setup lang="ts">
import { computed, ref } from 'vue'
import {
  ArrowDown,
  ArrowRight,
  Box,
  CircleCheck,
  Clock,
  Connection,
  CopyDocument,
  Edit,
  Film,
  FolderOpened,
  InfoFilled,
  Location,
  Monitor,
  Odometer,
  Refresh,
  SwitchButton,
  VideoCamera,
  VideoPlay,
  Warning
} from '@element-plus/icons-vue'
import { ElMessage } from 'element-plus'
import type { PonKanbanBoxDto, PonKanbanCameraDto, PonKanbanComputerDto, PonKanbanDeviceDto, PonKanbanOnuDto } from '../../types'

const props = defineProps<{
  box: PonKanbanBoxDto
  initiallyExpanded?: boolean
}>()

const emit = defineEmits<{
  (e: 'diagnose-onu', onu: PonKanbanOnuDto): void
  (e: 'reboot-onu', onu: PonKanbanOnuDto): void
  (e: 'view-topology', box: PonKanbanBoxDto): void
  (e: 'edit-box', box: PonKanbanBoxDto): void
  (e: 'view-history', box: PonKanbanBoxDto): void
}>()

const isExpanded = ref(props.initiallyExpanded ?? (props.box.healthState === 'danger' || props.box.healthState === 'warning'))

const totalDevices = computed(() => {
  let count = props.box.directDevices?.length ?? 0
  for (const onu of props.box.onus) {
    count += onu.devices?.length ?? 0
  }
  return count
})

function toggleExpand() {
  isExpanded.value = !isExpanded.value
}

function getRxClass(rx?: number | null): string {
  if (rx === null || rx === undefined) return 'muted'
  if (rx >= -24 && rx <= -8) return 'rx-normal'
  if (rx < -24 && rx >= -27) return 'rx-warning'
  return 'rx-danger'
}

function formatDuration(startTime?: string | null): string {
  if (!startTime) return '时间未知'
  const diff = Math.floor((Date.now() - new Date(startTime).getTime()) / 1000)
  if (diff <= 0) return '刚刚'
  const days = Math.floor(diff / 86400)
  const hours = Math.floor((diff % 86400) / 3600)
  const minutes = Math.floor((diff % 3600) / 60)
  if (days > 0) return `已离线 ${days}天${hours}小时`
  if (hours > 0) return `已离线 ${hours}小时${minutes}分`
  return `已离线 ${minutes}分钟`
}

async function copySn(sn: string) {
  try {
    await navigator.clipboard.writeText(sn)
    ElMessage.success('SN 码已复制到剪贴板')
  } catch {}
}

function formatPortLabel(raw?: string | null, isSwitch?: boolean): string {
  if (!raw) return isSwitch ? 'SW' : 'LAN'
  const trimmed = raw.trim()
  const lanMatch = trimmed.match(/LAN\s*(\d+)/i)
  if (lanMatch) {
    return `LAN${lanMatch[1]}`
  }
  const swMatch = trimmed.match(/(?:SW|交换机).*?(\d+)/i)
  if (swMatch) {
    return `SW-${swMatch[1]}`
  }
  if (trimmed.includes('直连') || trimmed.includes('直挂')) {
    return '直连'
  }
  const cleaned = trimmed.replace(/\s*(物理网口|端口)$/g, '').replace(/^ONU\s*/i, '').trim()
  return cleaned || (isSwitch ? 'SW' : 'LAN')
}
</script>

<template>
  <div class="kanban-box-card" :class="[`state-${box.healthState}`]">
    <!-- 终端箱头部条 -->
    <div class="box-card-header" @click="toggleExpand">
      <div class="header-left">
        <el-button link class="expand-btn">
          <el-icon><ArrowDown v-if="isExpanded" /><ArrowRight v-else /></el-icon>
        </el-button>
        <div class="box-code-badge">{{ box.code }}</div>
        <div class="box-meta">
          <div class="box-title-row">
            <span class="box-name">{{ box.name }}</span>
            <el-tag v-if="box.areaName || box.unitName" size="small" type="info" effect="plain" class="unit-tag">
              {{ box.areaName || box.unitName }}
            </el-tag>
          </div>
          <div class="box-sub-row">
            <el-icon><Location /></el-icon>
            <span class="location-text">{{ box.location || '现场未登记具体安装位置' }}</span>
          </div>
        </div>
      </div>

      <div class="header-right" @click.stop>
        <!-- 统计药丸 -->
        <div class="stats-pills">
          <div class="stat-pill" :class="{ alert: box.offlineOnus > 0 }">
            <span class="pill-label">ONU</span>
            <span class="pill-val">
              <strong :style="{ color: box.offlineOnus > 0 ? '#ef4444' : '#10b981' }">
                {{ box.onlineOnus }}
              </strong>
              / {{ box.totalOnus }}
            </span>
          </div>

          <div class="stat-pill" :class="{ alert: box.offlineCameras > 0 }">
            <span class="pill-label">摄像头</span>
            <span class="pill-val">
              <strong :style="{ color: box.offlineCameras > 0 ? '#f59e0b' : '#10b981' }">
                {{ box.onlineCameras }}
              </strong>
              / {{ box.totalCameras }}
            </span>
          </div>

          <div class="stat-pill" :class="{ alert: (box.offlineComputers ?? 0) > 0 }">
            <span class="pill-label">电脑</span>
            <span class="pill-val">
              <strong :style="{ color: (box.offlineComputers ?? 0) > 0 ? '#f59e0b' : '#10b981' }">
                {{ box.onlineComputers ?? 0 }}
              </strong>
              / {{ box.totalComputers ?? 0 }}
            </span>
          </div>

          <div v-if="totalDevices > 0" class="stat-pill">
            <span class="pill-label">录像机</span>
            <span class="pill-val">
              <strong style="color: #6366f1;">{{ totalDevices }}</strong>
            </span>
          </div>
        </div>

        <!-- 健康状态标签 -->
        <div class="health-badge-wrap">
          <span v-if="box.healthState === 'danger'" class="health-tag tag-danger">
            <span class="pulse-dot red"></span>
            存在掉线 ONU
          </span>
          <span v-else-if="box.healthState === 'warning'" class="health-tag tag-warning">
            <span class="pulse-dot amber"></span>
            <template v-if="(box.offlineCameras ?? 0) > 0 && (box.offlineComputers ?? 0) > 0">
              监控/电脑离线 ({{ (box.offlineCameras ?? 0) + (box.offlineComputers ?? 0) }})
            </template>
            <template v-else-if="(box.offlineCameras ?? 0) > 0">
              摄像头离线 ({{ box.offlineCameras }})
            </template>
            <template v-else>
              电脑离线 ({{ box.offlineComputers }})
            </template>
          </span>
          <span v-else-if="box.healthState === 'healthy'" class="health-tag tag-healthy">
            <span class="pulse-dot green"></span>
            全链路正常
          </span>
          <span v-else class="health-tag tag-empty">
            空终端箱
          </span>
        </div>

        <!-- 箱体动作 -->
        <div class="header-actions">
          <el-tooltip content="查看该终端箱历次掉线与恢复历史" placement="top">
            <el-button link type="primary" size="small" :icon="Clock" @click="emit('view-history', box)">
              历史
            </el-button>
          </el-tooltip>
          <el-tooltip content="查看终端箱物理走线与端口拓扑" placement="top">
            <el-button link type="primary" size="small" :icon="Connection" @click="emit('view-topology', box)">
              拓扑
            </el-button>
          </el-tooltip>
          <el-tooltip content="编辑终端箱台账信息" placement="top">
            <el-button link type="primary" size="small" :icon="Edit" @click="emit('edit-box', box)">
              编辑
            </el-button>
          </el-tooltip>
        </div>
      </div>
    </div>

    <!-- 展开详情面板 (维度自顶向下: 箱内 ONU -> 下挂摄像头) -->
    <el-collapse-transition>
      <div v-show="isExpanded" class="box-card-body">
        <!-- 若箱内无任何设备 -->
        <div v-if="!box.onus.length && !box.directCameras.length && !(box.directComputers && box.directComputers.length) && !(box.directDevices && box.directDevices.length)" class="empty-box-hint">
          <el-icon><InfoFilled /></el-icon>
          <span>箱内暂未绑定任何 ONU、摄像头、电脑或录像机设备，可在相关设备台账中将设备指派到此箱体。</span>
        </div>

        <!-- 维度 1: 箱内容纳的 ONU 设备 -->
        <div v-else class="onus-container">
          <div
            v-for="onu in box.onus"
            :key="onu.id"
            class="onu-subcard"
            :class="{ 'is-offline': onu.status !== 'online' }"
          >
            <!-- ONU 概要行 -->
            <div class="onu-main-row">
              <div class="onu-identity">
                <span class="onu-badge">ONU</span>
                <div class="onu-title-block">
                  <div class="onu-name-text">
                    <strong>{{ onu.alias || onu.onuName }}</strong>
                    <span v-if="onu.alias" class="onu-raw-name">({{ onu.onuName }})</span>
                    <el-tag size="small" type="info" class="model-tag">{{ onu.terminalType }}</el-tag>
                  </div>
                  <div class="onu-sn-line">
                    <span class="sn-text">SN: {{ onu.sn }}</span>
                    <el-button link type="primary" size="small" class="copy-btn" @click="copySn(onu.sn)">
                      <el-icon><CopyDocument /></el-icon>
                    </el-button>
                    <span v-if="onu.hasSubSwitch" class="switch-tag">
                      下挂: {{ onu.subSwitchModel || '交换机' }} ({{ onu.subSwitchPorts }}口)
                    </span>
                  </div>
                </div>
              </div>

              <div class="onu-status-block">
                <!-- 在线/掉线状态 -->
                <div v-if="onu.status === 'online'" class="online-indicator">
                  <span class="status-pill-green">● 光网在线</span>
                  <span v-if="onu.rxPower !== null && onu.rxPower !== undefined" :class="getRxClass(onu.rxPower)" class="rx-pill">
                    Rx: {{ onu.rxPower }} dBm
                  </span>
                  <span v-else class="rx-pill muted">Rx: 正常</span>
                </div>
                <div v-else class="offline-indicator">
                  <span class="status-pill-red">● 光网离线</span>
                  <span class="offline-cause-tag" :title="onu.lastDownCause || '光网掉电/断纤'">
                    {{ onu.lastDownCause || '光网掉线' }}
                  </span>
                  <span class="offline-time-tag">
                    {{ formatDuration(onu.lastDownTime) }}
                  </span>
                </div>

                <!-- ONU 操作区 -->
                <div class="onu-actions">
                  <el-button link type="primary" size="small" :icon="Odometer" @click="emit('diagnose-onu', onu)">
                    光衰排障
                  </el-button>
                  <el-button link type="danger" size="small" :icon="SwitchButton" @click="emit('reboot-onu', onu)">
                    远程重启
                  </el-button>
                </div>
              </div>
            </div>

            <!-- 维度 2: 该 ONU 下挂的终端设备 (监控摄像头、电脑终端与录像机) -->
            <div v-if="onu.cameras.length || (onu.computers && onu.computers.length) || (onu.devices && onu.devices.length)" class="cameras-subgrid">
              <!-- 摄像头卡片 -->
              <div
                v-for="cam in onu.cameras"
                :key="'cam-' + cam.channelId"
                class="camera-chip"
                :class="{ 'chip-offline': cam.status !== 'online' }"
              >
                <!-- 卡片首行：端口微标 + 图标 + 摄像头名称 + 推流/离线状态 -->
                <div class="chip-top-row">
                  <div class="chip-title-wrap">
                    <span
                      class="port-label"
                      :class="{ 'is-switch': cam.isSwitchPort }"
                      :title="cam.portLabel ? `物理网口: ${cam.portLabel}` : '网络端口'"
                    >
                      {{ formatPortLabel(cam.portLabel, cam.isSwitchPort) }}
                    </span>
                    <el-icon class="cam-icon"><VideoCamera /></el-icon>
                    <span class="cam-name" :title="cam.alias ? `${cam.alias} (${cam.name})` : cam.name">
                      {{ cam.alias || cam.name }}
                    </span>
                  </div>

                  <div class="chip-status-wrap">
                    <span v-if="cam.status === 'online'" class="cam-tag-online">
                      <span class="status-dot green"></span>推流正常
                    </span>
                    <span v-else class="cam-tag-offline">
                      <span class="status-dot red"></span>摄像头离线
                    </span>
                  </div>
                </div>

                <!-- 卡片次行：IP 独立徽章 + 通道号 + 快捷操作 -->
                <div class="chip-bottom-row">
                  <div class="chip-meta-wrap">
                    <span v-if="cam.ip" class="cam-ip-badge" :title="'摄像头 IP: ' + cam.ip">
                      <span class="ip-prefix">IP</span>
                      <span class="ip-text">{{ cam.ip }}</span>
                    </span>
                    <span v-else class="cam-ip-badge muted" title="未配置 IP 地址">
                      <span class="ip-prefix">IP</span>
                      <span class="ip-text">未配置</span>
                    </span>
                    <span v-if="cam.deviceChannel" class="cam-ch-badge" title="录像机通道号">
                      CH{{ cam.deviceChannel }}
                    </span>
                  </div>

                  <div class="cam-buttons">
                    <router-link :to="{ path: '/app/organization', query: { inspectId: cam.channelId } }">
                      <el-button link type="primary" size="small" :icon="VideoPlay" title="即席点检">点检</el-button>
                    </router-link>
                  </div>
                </div>
              </div>

              <!-- 电脑终端卡片 -->
              <div
                v-for="comp in (onu.computers || [])"
                :key="'comp-' + comp.id"
                class="camera-chip computer-chip"
                :class="{ 'chip-offline': comp.status !== 'online' }"
              >
                <!-- 卡片首行：端口微标 + 图标 + 电脑名称/用途 + 在线状态 -->
                <div class="chip-top-row">
                  <div class="chip-title-wrap">
                    <span
                      class="port-label comp-port"
                      :class="{ 'is-switch': comp.isSwitchPort }"
                      :title="comp.portLabel ? `物理网口: ${comp.portLabel}` : '网络端口'"
                    >
                      {{ formatPortLabel(comp.portLabel, comp.isSwitchPort) }}
                    </span>
                    <el-icon class="comp-icon"><Monitor /></el-icon>
                    <span class="cam-name" :title="comp.name">
                      {{ comp.name }}
                    </span>
                    <el-tag v-if="comp.owner || comp.department" size="small" type="info" class="comp-owner-tag" effect="light">
                      {{ comp.department ? `${comp.department} · ` : '' }}{{ comp.owner || '公共机' }}
                    </el-tag>
                  </div>

                  <div class="chip-status-wrap">
                    <span v-if="comp.status === 'online'" class="cam-tag-online comp-online">
                      <span class="status-dot green"></span>在线
                    </span>
                    <span v-else class="cam-tag-offline">
                      <span class="status-dot red"></span>离线
                    </span>
                  </div>
                </div>

                <!-- 卡片次行：IP 独立徽章 + 延迟 + 快捷操作 -->
                <div class="chip-bottom-row">
                  <div class="chip-meta-wrap">
                    <span v-if="comp.ip" class="cam-ip-badge comp-ip" :title="'电脑 IP: ' + comp.ip">
                      <span class="ip-prefix">IP</span>
                      <span class="ip-text">{{ comp.ip }}</span>
                    </span>
                    <span v-else class="cam-ip-badge muted" title="未配置 IP 地址">
                      <span class="ip-prefix">IP</span>
                      <span class="ip-text">未配置</span>
                    </span>
                    <span v-if="comp.responseTimeMs !== null && comp.responseTimeMs !== undefined" class="cam-ch-badge comp-rtt-badge" title="探测延迟">
                      {{ comp.responseTimeMs }}ms
                    </span>
                  </div>

                  <div class="cam-buttons">
                    <router-link :to="{ path: '/app/ipam', query: { tab: 'computers', search: comp.ip } }">
                      <el-button link type="primary" size="small" title="在电脑资产台账中查看">台账</el-button>
                    </router-link>
                  </div>
                </div>
              </div>

              <!-- 录像机/网络设备卡片 -->
              <div
                v-for="dev in (onu.devices || [])"
                :key="'dev-' + dev.deviceId"
                class="camera-chip device-chip"
                :class="{ 'chip-offline': dev.status !== 'online' }"
              >
                <!-- 卡片首行：端口微标 + 图标 + 设备名称 + 在线状态 -->
                <div class="chip-top-row">
                  <div class="chip-title-wrap">
                    <span
                      class="port-label dev-port"
                      :class="{ 'is-switch': dev.isSwitchPort }"
                      :title="dev.portLabel ? `物理网口: ${dev.portLabel}` : '网络端口'"
                    >
                      {{ formatPortLabel(dev.portLabel, dev.isSwitchPort) }}
                    </span>
                    <el-icon class="dev-icon"><Film /></el-icon>
                    <span class="cam-name" :title="dev.name">
                      {{ dev.name }}
                    </span>
                    <el-tag size="small" type="warning" class="dev-type-tag" effect="light">
                      {{ dev.model || 'NVR' }}
                    </el-tag>
                  </div>

                  <div class="chip-status-wrap">
                    <span v-if="dev.status === 'online'" class="cam-tag-online dev-online">
                      <span class="status-dot green"></span>在线
                    </span>
                    <span v-else class="cam-tag-offline">
                      <span class="status-dot red"></span>离线
                    </span>
                  </div>
                </div>

                <!-- 卡片次行：IP 独立徽章 + 通道数 + 快捷操作 -->
                <div class="chip-bottom-row">
                  <div class="chip-meta-wrap">
                    <span v-if="dev.host" class="cam-ip-badge dev-ip" :title="'录像机 IP: ' + dev.host">
                      <span class="ip-prefix">IP</span>
                      <span class="ip-text">{{ dev.host }}</span>
                    </span>
                    <span v-else class="cam-ip-badge muted" title="未配置 IP 地址">
                      <span class="ip-prefix">IP</span>
                      <span class="ip-text">未配置</span>
                    </span>
                    <span v-if="dev.channelCount" class="cam-ch-badge dev-ch-badge" title="下挂通道数">
                      {{ dev.channelCount }} 通道
                    </span>
                  </div>

                  <div class="cam-buttons">
                    <router-link :to="{ path: '/app/devices', query: { search: dev.host } }">
                      <el-button link type="primary" size="small" title="在设备管理中查看">台账</el-button>
                    </router-link>
                  </div>
                </div>
              </div>
            </div>
            <div v-else class="no-camera-line">
              <span class="muted">（该 ONU 端口暂未挂接终端设备）</span>
            </div>
          </div>

          <!-- 若有直接挂接在终端箱上、未关联具体 ONU 的摄像头 -->
          <div v-if="box.directCameras.length" class="direct-cameras-block">
            <div class="direct-cameras-title">
              <el-icon><Connection /></el-icon>
              <span>终端箱直挂摄像头（未绑定特定 ONU 端口）:</span>
            </div>
            <div class="cameras-subgrid">
              <div
                v-for="cam in box.directCameras"
                :key="'dir-cam-' + cam.channelId"
                class="camera-chip"
                :class="{ 'chip-offline': cam.status !== 'online' }"
              >
                <!-- 卡片首行 -->
                <div class="chip-top-row">
                  <div class="chip-title-wrap">
                    <span
                      class="port-label direct"
                      :title="cam.portLabel ? `物理网口: ${cam.portLabel}` : '直连终端箱'"
                    >
                      {{ formatPortLabel(cam.portLabel, false) || '直连' }}
                    </span>
                    <el-icon class="cam-icon"><VideoCamera /></el-icon>
                    <span class="cam-name" :title="cam.alias ? `${cam.alias} (${cam.name})` : cam.name">
                      {{ cam.alias || cam.name }}
                    </span>
                  </div>

                  <div class="chip-status-wrap">
                    <span v-if="cam.status === 'online'" class="cam-tag-online">
                      <span class="status-dot green"></span>推流正常
                    </span>
                    <span v-else class="cam-tag-offline">
                      <span class="status-dot red"></span>摄像头离线
                    </span>
                  </div>
                </div>

                <!-- 卡片次行 -->
                <div class="chip-bottom-row">
                  <div class="chip-meta-wrap">
                    <span v-if="cam.ip" class="cam-ip-badge" :title="'摄像头 IP: ' + cam.ip">
                      <span class="ip-prefix">IP</span>
                      <span class="ip-text">{{ cam.ip }}</span>
                    </span>
                    <span v-else class="cam-ip-badge muted" title="未配置 IP 地址">
                      <span class="ip-prefix">IP</span>
                      <span class="ip-text">未配置</span>
                    </span>
                    <span v-if="cam.deviceChannel" class="cam-ch-badge" title="录像机通道号">
                      CH{{ cam.deviceChannel }}
                    </span>
                  </div>

                  <div class="cam-buttons">
                    <router-link :to="{ path: '/app/organization', query: { inspectId: cam.channelId } }">
                      <el-button link type="primary" size="small" :icon="VideoPlay" title="即席点检">点检</el-button>
                    </router-link>
                  </div>
                </div>
              </div>
            </div>
          </div>

          <!-- 若有直接挂接在终端箱上、未关联具体 ONU 的电脑终端 -->
          <div v-if="box.directComputers && box.directComputers.length" class="direct-cameras-block">
            <div class="direct-cameras-title">
              <el-icon><Monitor /></el-icon>
              <span>终端箱直挂电脑（未绑定特定 ONU 端口）:</span>
            </div>
            <div class="cameras-subgrid">
              <div
                v-for="comp in box.directComputers"
                :key="'dir-comp-' + comp.id"
                class="camera-chip computer-chip"
                :class="{ 'chip-offline': comp.status !== 'online' }"
              >
                <!-- 卡片首行 -->
                <div class="chip-top-row">
                  <div class="chip-title-wrap">
                    <span
                      class="port-label comp-port direct"
                      :title="comp.portLabel ? `物理网口: ${comp.portLabel}` : '直连终端箱'"
                    >
                      {{ formatPortLabel(comp.portLabel, false) || '直连' }}
                    </span>
                    <el-icon class="comp-icon"><Monitor /></el-icon>
                    <span class="cam-name" :title="comp.name">
                      {{ comp.name }}
                    </span>
                    <el-tag v-if="comp.owner || comp.department" size="small" type="info" class="comp-owner-tag" effect="light">
                      {{ comp.department ? `${comp.department} · ` : '' }}{{ comp.owner || '公共机' }}
                    </el-tag>
                  </div>

                  <div class="chip-status-wrap">
                    <span v-if="comp.status === 'online'" class="cam-tag-online comp-online">
                      <span class="status-dot green"></span>在线
                    </span>
                    <span v-else class="cam-tag-offline">
                      <span class="status-dot red"></span>离线
                    </span>
                  </div>
                </div>

                <!-- 卡片次行 -->
                <div class="chip-bottom-row">
                  <div class="chip-meta-wrap">
                    <span v-if="comp.ip" class="cam-ip-badge comp-ip" :title="'电脑 IP: ' + comp.ip">
                      <span class="ip-prefix">IP</span>
                      <span class="ip-text">{{ comp.ip }}</span>
                    </span>
                    <span v-else class="cam-ip-badge muted" title="未配置 IP 地址">
                      <span class="ip-prefix">IP</span>
                      <span class="ip-text">未配置</span>
                    </span>
                    <span v-if="comp.responseTimeMs !== null && comp.responseTimeMs !== undefined" class="cam-ch-badge comp-rtt-badge" title="探测延迟">
                      {{ comp.responseTimeMs }}ms
                    </span>
                  </div>

                  <div class="cam-buttons">
                    <router-link :to="{ path: '/app/ipam', query: { tab: 'computers', search: comp.ip } }">
                      <el-button link type="primary" size="small" title="在电脑资产台账中查看">台账</el-button>
                    </router-link>
                  </div>
                </div>
              </div>
            </div>
          </div>

          <!-- 若有直接挂接在终端箱上、未关联具体 ONU 的录像机设备 -->
          <div v-if="box.directDevices && box.directDevices.length" class="direct-cameras-block">
            <div class="direct-cameras-title">
              <el-icon><Film /></el-icon>
              <span>终端箱直挂录像机/设备（未绑定特定 ONU 端口）:</span>
            </div>
            <div class="cameras-subgrid">
              <div
                v-for="dev in box.directDevices"
                :key="'dir-dev-' + dev.deviceId"
                class="camera-chip device-chip"
                :class="{ 'chip-offline': dev.status !== 'online' }"
              >
                <!-- 卡片首行 -->
                <div class="chip-top-row">
                  <div class="chip-title-wrap">
                    <span
                      class="port-label dev-port direct"
                      :title="dev.portLabel ? `物理网口: ${dev.portLabel}` : '直连终端箱'"
                    >
                      {{ formatPortLabel(dev.portLabel, false) || '直连' }}
                    </span>
                    <el-icon class="dev-icon"><Film /></el-icon>
                    <span class="cam-name" :title="dev.name">
                      {{ dev.name }}
                    </span>
                    <el-tag size="small" type="warning" class="dev-type-tag" effect="light">
                      {{ dev.model || 'NVR' }}
                    </el-tag>
                  </div>

                  <div class="chip-status-wrap">
                    <span v-if="dev.status === 'online'" class="cam-tag-online dev-online">
                      <span class="status-dot green"></span>在线
                    </span>
                    <span v-else class="cam-tag-offline">
                      <span class="status-dot red"></span>离线
                    </span>
                  </div>
                </div>

                <!-- 卡片次行 -->
                <div class="chip-bottom-row">
                  <div class="chip-meta-wrap">
                    <span v-if="dev.host" class="cam-ip-badge dev-ip" :title="'录像机 IP: ' + dev.host">
                      <span class="ip-prefix">IP</span>
                      <span class="ip-text">{{ dev.host }}</span>
                    </span>
                    <span v-else class="cam-ip-badge muted" title="未配置 IP 地址">
                      <span class="ip-prefix">IP</span>
                      <span class="ip-text">未配置</span>
                    </span>
                    <span v-if="dev.channelCount" class="cam-ch-badge dev-ch-badge" title="下挂通道数">
                      {{ dev.channelCount }} 通道
                    </span>
                  </div>

                  <div class="cam-buttons">
                    <router-link :to="{ path: '/app/devices', query: { search: dev.host } }">
                      <el-button link type="primary" size="small" title="在设备管理中查看">台账</el-button>
                    </router-link>
                  </div>
                </div>
              </div>
            </div>
          </div>
        </div>
      </div>
    </el-collapse-transition>
  </div>
</template>

<style scoped>
.kanban-box-card {
  background: #ffffff;
  border: 1px solid var(--border, #e2e8f0);
  border-radius: 8px;
  overflow: hidden;
  box-shadow: 0 1px 3px rgba(15, 23, 42, 0.05);
  transition: all 0.2s ease;
  position: relative;
}
.kanban-box-card:hover {
  box-shadow: 0 4px 12px rgba(15, 23, 42, 0.08);
  border-color: #cbd5e1;
}

/* 状态色左边框 */
.kanban-box-card.state-danger {
  border-left: 4px solid #ef4444;
}
.kanban-box-card.state-warning {
  border-left: 4px solid #f59e0b;
}
.kanban-box-card.state-healthy {
  border-left: 4px solid #10b981;
}
.kanban-box-card.state-empty {
  border-left: 4px solid #94a3b8;
}

/* 头部条 */
.box-card-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 12px 16px;
  background: #f8fafc;
  cursor: pointer;
  user-select: none;
  border-bottom: 1px solid transparent;
}
.kanban-box-card.state-danger .box-card-header {
  background: #fff5f5;
}
.kanban-box-card.state-warning .box-card-header {
  background: #fffbeb;
}
.box-card-header:hover {
  background: #f1f5f9;
}

.header-left {
  display: flex;
  align-items: center;
  gap: 10px;
}
.expand-btn {
  color: #64748b;
  padding: 0;
  margin-right: -4px;
}
.box-code-badge {
  background: #1e293b;
  color: #f8fafc;
  font-family: 'JetBrains Mono', monospace;
  font-weight: 700;
  font-size: 12px;
  padding: 3px 8px;
  border-radius: 4px;
  letter-spacing: 0.5px;
}
.box-meta {
  display: flex;
  flex-direction: column;
  gap: 2px;
}
.box-title-row {
  display: flex;
  align-items: center;
  gap: 8px;
}
.box-name {
  font-weight: 700;
  font-size: 14.5px;
  color: #1e293b;
}
.unit-tag {
  font-size: 11px;
}
.box-sub-row {
  display: flex;
  align-items: center;
  gap: 4px;
  color: #64748b;
  font-size: 12px;
}
.location-text {
  max-width: 320px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.header-right {
  display: flex;
  align-items: center;
  gap: 16px;
}
.stats-pills {
  display: flex;
  align-items: center;
  gap: 8px;
}
.stat-pill {
  display: flex;
  align-items: center;
  gap: 6px;
  background: #ffffff;
  border: 1px solid #e2e8f0;
  padding: 3px 10px;
  border-radius: 20px;
  font-size: 12px;
}
.stat-pill.alert {
  border-color: #fca5a5;
  background: #fef2f2;
}
.pill-label {
  color: #64748b;
}
.pill-val {
  font-family: 'JetBrains Mono', monospace;
  font-weight: 600;
}

.health-badge-wrap {
  display: flex;
  align-items: center;
}
.health-tag {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  font-size: 12px;
  font-weight: 600;
  padding: 4px 10px;
  border-radius: 4px;
}
.tag-danger {
  background: #fee2e2;
  color: #b91c1c;
}
.tag-warning {
  background: #fef3c7;
  color: #b45309;
}
.tag-healthy {
  background: #d1fae5;
  color: #047857;
}
.tag-empty {
  background: #f1f5f9;
  color: #64748b;
}

.pulse-dot {
  width: 7px;
  height: 7px;
  border-radius: 50%;
  display: inline-block;
}
.pulse-dot.red {
  background: #ef4444;
  box-shadow: 0 0 0 2px rgba(239, 68, 68, 0.3);
}
.pulse-dot.amber {
  background: #f59e0b;
  box-shadow: 0 0 0 2px rgba(245, 158, 11, 0.3);
}
.pulse-dot.green {
  background: #10b981;
}

.header-actions {
  display: flex;
  align-items: center;
  gap: 4px;
  border-left: 1px solid #e2e8f0;
  padding-left: 12px;
}

/* 折叠展开体 */
.box-card-body {
  padding: 14px 16px;
  background: #ffffff;
  border-top: 1px solid #f1f5f9;
}
.empty-box-hint {
  display: flex;
  align-items: center;
  gap: 8px;
  color: #94a3b8;
  font-size: 12.5px;
  padding: 12px 0;
}

/* ONU 卡片 */
.onus-container {
  display: flex;
  flex-direction: column;
  gap: 12px;
}
.onu-subcard {
  background: #fafafa;
  border: 1px solid #e2e8f0;
  border-radius: 6px;
  padding: 10px 14px;
  transition: all 0.2s ease;
}
.onu-subcard.is-offline {
  background: #fff8f8;
  border-color: #fecaca;
}
.onu-main-row {
  display: flex;
  justify-content: space-between;
  align-items: center;
}
.onu-identity {
  display: flex;
  align-items: center;
  gap: 10px;
}
.onu-badge {
  background: #0284c7;
  color: #ffffff;
  font-size: 11px;
  font-weight: 700;
  padding: 2px 6px;
  border-radius: 3px;
}
.onu-title-block {
  display: flex;
  flex-direction: column;
  gap: 2px;
}
.onu-name-text {
  display: flex;
  align-items: center;
  gap: 8px;
  font-size: 13.5px;
  color: #0f172a;
}
.onu-raw-name {
  color: #64748b;
  font-size: 11.5px;
}
.model-tag {
  font-size: 11px;
}
.onu-sn-line {
  display: flex;
  align-items: center;
  gap: 6px;
  font-size: 11.5px;
  color: #64748b;
}
.sn-text {
  font-family: 'JetBrains Mono', monospace;
}
.copy-btn {
  padding: 0;
  font-size: 12px;
}
.switch-tag {
  background: #f1f5f9;
  padding: 1px 6px;
  border-radius: 3px;
  color: #475569;
}

.onu-status-block {
  display: flex;
  align-items: center;
  gap: 14px;
}
.online-indicator {
  display: flex;
  align-items: center;
  gap: 8px;
}
.status-pill-green {
  color: #10b981;
  font-weight: 600;
  font-size: 12.5px;
}
.rx-pill {
  font-size: 12px;
  font-weight: 600;
  font-family: 'JetBrains Mono', monospace;
}
.rx-normal { color: #10b981; }
.rx-warning { color: #f59e0b; }
.rx-danger { color: #ef4444; }

.offline-indicator {
  display: flex;
  align-items: center;
  gap: 8px;
}
.status-pill-red {
  color: #ef4444;
  font-weight: 700;
  font-size: 12.5px;
}
.offline-cause-tag {
  background: #fee2e2;
  color: #b91c1c;
  font-size: 11.5px;
  font-weight: 600;
  padding: 2px 8px;
  border-radius: 3px;
  max-width: 160px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.offline-time-tag {
  background: #fef2f2;
  color: #ef4444;
  font-size: 11.5px;
  padding: 2px 6px;
  border-radius: 3px;
  border: 1px dashed #fca5a5;
  font-weight: 500;
}
.onu-actions {
  display: flex;
  align-items: center;
  gap: 4px;
  border-left: 1px solid #e2e8f0;
  padding-left: 10px;
}

/* 摄像头网格与微卡片 */
.cameras-subgrid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(310px, 1fr));
  gap: 10px;
  margin-top: 10px;
  padding-top: 10px;
  border-top: 1px dashed #e2e8f0;
}
.camera-chip {
  background: #ffffff;
  border: 1px solid #e2e8f0;
  border-radius: 6px;
  padding: 8px 10px;
  display: flex;
  flex-direction: column;
  gap: 6px;
  transition: all 0.15s ease;
  box-shadow: 0 1px 2px rgba(15, 23, 42, 0.03);
}
.camera-chip:hover {
  border-color: #cbd5e1;
  box-shadow: 0 2px 6px rgba(15, 23, 42, 0.06);
}
.camera-chip.chip-offline {
  background: #fff8f8;
  border-color: #fecaca;
}

/* 卡片首行：端口 + 图标 + 名称 + 状态 */
.chip-top-row {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 8px;
}
.chip-title-wrap {
  display: flex;
  align-items: center;
  gap: 6px;
  min-width: 0;
  flex: 1;
}
.port-label {
  background: #e0f2fe;
  color: #0369a1;
  font-family: 'JetBrains Mono', monospace;
  font-size: 11px;
  font-weight: 700;
  padding: 1px 6px;
  border-radius: 4px;
  white-space: nowrap;
  flex-shrink: 0;
  letter-spacing: 0.3px;
  border: 1px solid #bae6fd;
}
.port-label.is-switch {
  background: #fef3c7;
  color: #b45309;
  border-color: #fde68a;
}
.port-label.direct {
  background: #ede9fe;
  color: #6d28d9;
  border-color: #ddd6fe;
}
.cam-icon {
  color: #64748b;
  font-size: 14px;
  flex-shrink: 0;
}
.camera-chip.chip-offline .cam-icon {
  color: #ef4444;
}
.cam-name {
  font-weight: 600;
  font-size: 12.5px;
  color: #1e293b;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  min-width: 0;
  flex: 1;
}
.chip-status-wrap {
  flex-shrink: 0;
}
.status-dot {
  width: 6px;
  height: 6px;
  border-radius: 50%;
  display: inline-block;
  margin-right: 4px;
}
.status-dot.green { background: #10b981; }
.status-dot.red { background: #ef4444; }

.cam-tag-online {
  display: inline-flex;
  align-items: center;
  color: #059669;
  font-size: 11px;
  font-weight: 600;
  background: #ecfdf5;
  padding: 1px 6px;
  border-radius: 4px;
  border: 1px solid #a7f3d0;
}
.cam-tag-offline {
  display: inline-flex;
  align-items: center;
  color: #dc2626;
  font-size: 11px;
  font-weight: 600;
  background: #fee2e2;
  padding: 1px 6px;
  border-radius: 4px;
  border: 1px solid #fecaca;
}

/* 卡片次行：IP 微胶囊 + 通道号 + 快捷操作 */
.chip-bottom-row {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 8px;
  padding-top: 5px;
  border-top: 1px solid #f1f5f9;
}
.camera-chip.chip-offline .chip-bottom-row {
  border-top-color: #fee2e2;
}
.chip-meta-wrap {
  display: flex;
  align-items: center;
  gap: 6px;
  min-width: 0;
}
.cam-ip-badge {
  display: inline-flex;
  align-items: center;
  background: #f8fafc;
  border: 1px solid #e2e8f0;
  border-radius: 4px;
  font-family: 'JetBrains Mono', monospace;
  font-size: 11px;
  overflow: hidden;
  line-height: 1.2;
}
.cam-ip-badge.muted {
  color: #94a3b8;
}
.ip-prefix {
  background: #e2e8f0;
  color: #475569;
  padding: 1px 4px;
  font-size: 9.5px;
  font-weight: 700;
  text-transform: uppercase;
}
.ip-text {
  padding: 1px 6px;
  color: #334155;
  font-weight: 600;
}
.cam-ch-badge {
  background: #f1f5f9;
  color: #64748b;
  font-family: 'JetBrains Mono', monospace;
  font-size: 10.5px;
  font-weight: 600;
  padding: 1px 5px;
  border-radius: 3px;
}
.cam-buttons {
  display: flex;
  align-items: center;
  gap: 4px;
  flex-shrink: 0;
}
.cam-buttons :deep(.el-button) {
  padding: 0 4px;
  height: 22px;
  font-size: 11px;
}

.no-camera-line {
  margin-top: 6px;
  padding-top: 6px;
  border-top: 1px dashed #e2e8f0;
  font-size: 11.5px;
}

.direct-cameras-block {
  margin-top: 12px;
  padding-top: 10px;
  border-top: 1px solid #e2e8f0;
}
.direct-cameras-title {
  display: flex;
  align-items: center;
  gap: 6px;
  font-size: 12.5px;
  font-weight: 600;
  color: #475569;
  margin-bottom: 8px;
}

.muted {
  color: #94a3b8;
}

.computer-chip {
  border-color: #e2e8f0;
}
.computer-chip:hover {
  border-color: #7dd3fc;
}
.computer-chip .comp-icon {
  color: #0284c7;
  font-size: 14px;
  flex-shrink: 0;
}
.computer-chip.chip-offline .comp-icon {
  color: #ef4444;
}
.port-label.comp-port {
  background: #f0fdf4;
  color: #15803d;
  border-color: #bbf7d0;
}
.port-label.comp-port.direct {
  background: #eff6ff;
  color: #1d4ed8;
  border-color: #bfdbfe;
}
.comp-owner-tag {
  font-size: 10.5px;
  height: 20px;
  padding: 0 4px;
  flex-shrink: 0;
}
.cam-tag-online.comp-online {
  color: #0284c7;
  background: #f0f9ff;
  border-color: #bae6fd;
}
.cam-ip-badge.comp-ip .ip-prefix {
  background: #e0f2fe;
  color: #0369a1;
}
.comp-rtt-badge {
  color: #059669;
  background: #ecfdf5;
}

.device-chip {
  border-color: #e2e8f0;
}
.device-chip:hover {
  border-color: #a5b4fc;
}
.device-chip .dev-icon {
  color: #6366f1;
  font-size: 14px;
  flex-shrink: 0;
}
.device-chip.chip-offline .dev-icon {
  color: #ef4444;
}
.port-label.dev-port {
  background: #eef2ff;
  color: #4f46e5;
  border-color: #c7d2fe;
}
.port-label.dev-port.direct {
  background: #ede9fe;
  color: #6d28d9;
  border-color: #ddd6fe;
}
.dev-type-tag {
  font-size: 10.5px;
  height: 20px;
  padding: 0 4px;
  flex-shrink: 0;
}
.cam-tag-online.dev-online {
  color: #4f46e5;
  background: #eef2ff;
  border-color: #c7d2fe;
}
.cam-ip-badge.dev-ip .ip-prefix {
  background: #e0e7ff;
  color: #4338ca;
}
.dev-ch-badge {
  color: #4f46e5;
  background: #eef2ff;
}
</style>
