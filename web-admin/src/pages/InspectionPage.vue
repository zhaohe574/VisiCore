<script setup lang="ts">
import { onBeforeUnmount, ref, watch } from 'vue'
import { useRoute } from 'vue-router'
import { managementApi, type Channel } from '../api'
import { errorMessage } from '../lib/format'
import PageHeader from '../components/PageHeader.vue'
import ChannelPicker from '../components/ChannelPicker.vue'
import VideoTile from '../components/VideoTile.vue'
import { Box, Cpu, InfoFilled } from '@element-plus/icons-vue'

const route = useRoute()
const channelId = ref<number>()
const channel = ref<Channel>()
const error = ref('')
const stream = ref<1 | 2>(2)
let controller: AbortController | undefined

watch(() => route.query.channelId, value => { channelId.value = Number(value) || undefined }, { immediate: true })

watch(channelId, async id => {
  controller?.abort()
  channel.value = undefined
  error.value = ''
  if (!id) return
  const current = controller = new AbortController()
  try {
    const found = await managementApi.channel(id, current.signal)
    if (!current.signal.aborted) {
      channel.value = found
      if (!found) error.value = '通道不存在或不在授权范围内。'
    }
  } catch (e) {
    if (!current.signal.aborted) error.value = errorMessage(e)
  }
}, { immediate: true })

onBeforeUnmount(() => controller?.abort())
</script>

<template>
  <div class="inspection-page">
    <PageHeader
      title="设备巡查"
      description="轻量级设备实时画面与运行状态巡查预览。多路分屏值守、录像回放检索与导出请使用桌面客户端。"
    />

    <div class="filter-card filter-bar" style="margin-bottom: 16px; display: flex; align-items: center; gap: 12px; flex-wrap: wrap;">
      <ChannelPicker v-model="channelId" style="min-width: 280px;" />
      <el-select v-model="stream" style="width: 140px;">
        <el-option :value="2" label="子码流 (流畅)" />
        <el-option :value="1" label="主码流 (高清)" />
      </el-select>
      <el-button :disabled="!channelId" @click="channelId = undefined">结束巡查</el-button>

      <div style="flex: 1;" />

      <router-link
        v-if="channel"
        :to="{ path: '/app/organization', query: { channelId: channel.id } }"
      >
        <el-button type="primary" plain>在组织架构中查看</el-button>
      </router-link>
      <router-link to="/">
        <el-button text>获取桌面端</el-button>
      </router-link>
    </div>

    <el-alert v-if="error" :title="error" type="error" :closable="false" style="margin-bottom: 16px;" />

    <div class="inspection-layout">
      <!-- 视频画面预览视口 -->
      <div class="inspection-video-box">
        <VideoTile
          v-if="channel"
          :index="0"
          :channel="channel"
          mode="live"
          :stream-type="stream"
          :selected="true"
          @close="channelId = undefined"
        />
        <div v-else class="inspection-empty-placeholder">
          <el-empty description="请在上方选择通道以开始设备巡查预览" :image-size="100">
            <template #extra>
              <span class="muted" style="font-size: 13px;">可检索通道名称、IP 地址或设备编号</span>
            </template>
          </el-empty>
        </div>
      </div>

      <!-- 设备巡查与拓扑关联面板 -->
      <div v-if="channel" class="inspection-info-card filter-card">
        <div class="info-card-header">
          <div class="info-title-group">
            <h3>{{ channel.alias || channel.name }}</h3>
            <span class="muted">通道 #{{ channel.deviceChannel }} · ID: {{ channel.id }}</span>
          </div>
          <el-tag :type="channel.status === 'online' ? 'success' : 'danger'" size="small">
            {{ channel.status === 'online' ? '设备在线' : '设备离线' }}
          </el-tag>
        </div>

        <el-divider style="margin: 12px 0;" />

        <div class="info-grid">
          <div class="info-item">
            <span class="info-label">设备 IP</span>
            <strong class="info-val">{{ channel.ip || channel.deviceHost || '-' }}</strong>
          </div>
          <div class="info-item">
            <span class="info-label">服务端口</span>
            <span class="info-val">{{ channel.devicePort || 8000 }}</span>
          </div>
          <div class="info-item">
            <span class="info-label">所属设备</span>
            <span class="info-val">{{ channel.deviceName || '-' }}</span>
          </div>
          <div class="info-item">
            <span class="info-label">设备型号</span>
            <span class="info-val">{{ channel.deviceModel || '-' }}</span>
          </div>
          <div class="info-item">
            <span class="info-label">接入协议</span>
            <span class="info-val">{{ channel.pluginName || '海康私有/标准协议' }}</span>
          </div>
          <div class="info-item">
            <span class="info-label">序列号</span>
            <span class="info-val mono-font">{{ channel.deviceSerial || '-' }}</span>
          </div>
        </div>

        <el-divider style="margin: 12px 0;" />

        <div class="topology-badges">
          <div v-if="channel.terminalBoxName || channel.terminalBoxCode" class="topology-badge-item">
            <el-icon><Box /></el-icon>
            <span>终端箱：</span>
            <router-link
              v-if="channel.terminalBoxId"
              :to="{ path: '/app/terminal-boxes', query: { boxId: channel.terminalBoxId } }"
              class="link-primary"
            >
              {{ channel.terminalBoxName || channel.terminalBoxCode }}
            </router-link>
            <span v-else>{{ channel.terminalBoxName || channel.terminalBoxCode }}</span>
          </div>

          <div v-if="channel.onuName" class="topology-badge-item">
            <el-icon><Cpu /></el-icon>
            <span>ONU：{{ channel.onuName }}</span>
            <span v-if="channel.networkPort" class="muted">（端口 {{ channel.networkPort }}）</span>
          </div>
        </div>

        <div class="client-hint-banner">
          <el-icon><InfoFilled /></el-icon>
          <span>多路轮巡监控、PTZ云台控制、历史录像检索切片回放请使用桌面客户端。</span>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.inspection-page {
  display: flex;
  flex-direction: column;
}

.inspection-layout {
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.inspection-video-box {
  width: 100%;
  aspect-ratio: 16 / 9;
  max-height: 68vh;
  background: #0f172a;
  border-radius: 8px;
  overflow: hidden;
  position: relative;
  box-shadow: 0 4px 16px rgba(0, 0, 0, 0.2);
}

.inspection-empty-placeholder {
  width: 100%;
  height: 100%;
  min-height: 360px;
  display: flex;
  align-items: center;
  justify-content: center;
  background: #111827;
}

.inspection-info-card {
  padding: 18px 24px;
}

.info-card-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
}

.info-title-group {
  display: flex;
  align-items: baseline;
  gap: 10px;
}

.info-title-group h3 {
  margin: 0;
  font-size: 16px;
  font-weight: 600;
  color: var(--text-primary);
}

.info-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(200px, 1fr));
  gap: 12px 20px;
}

.info-item {
  display: flex;
  flex-direction: column;
  gap: 2px;
}

.info-label {
  font-size: 12px;
  color: var(--text-secondary);
}

.info-val {
  font-size: 13.5px;
  color: var(--text-primary);
}

.mono-font {
  font-family: ui-monospace, SFMono-Regular, Menlo, Monaco, Consolas, monospace;
}

.topology-badges {
  display: flex;
  align-items: center;
  gap: 16px;
  flex-wrap: wrap;
  margin-bottom: 12px;
}

.topology-badge-item {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  padding: 4px 10px;
  background: #f1f5f9;
  border-radius: 6px;
  font-size: 13px;
  color: var(--text-primary);
}

.link-primary {
  color: var(--primary);
  text-decoration: none;
  font-weight: 500;
}

.link-primary:hover {
  text-decoration: underline;
}

.client-hint-banner {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 10px 14px;
  background: #eff6ff;
  border: 1px solid rgba(25, 118, 210, 0.15);
  border-radius: 6px;
  font-size: 12.5px;
  color: #1e40af;
}
</style>
