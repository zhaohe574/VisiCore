<script setup lang="ts">
import type { OltDeviceDto } from '../../../api'

const props = defineProps<{
  olts: OltDeviceDto[]
  loading: boolean
}>()

const emit = defineEmits<{
  (e: 'selectPort', portId: number): void
}>()
</script>

<template>
  <div v-loading="loading" class="topology-panel">
    <!-- 核心 OLT 资产概览卡片 -->
    <div v-for="olt in olts" :key="olt.id" class="olt-chassis-card">
      <div class="olt-header">
        <div class="olt-title-group">
          <el-tag effect="dark" type="primary" size="large" style="font-weight: 700;">{{ olt.vendor }} {{ olt.model }}</el-tag>
          <span class="olt-name">{{ olt.name }} (IP: {{ olt.ip }})</span>
          <el-tag size="small" :type="olt.status === 'online' ? 'success' : 'danger'">{{ olt.status === 'online' ? '网元在线' : '网元脱网' }}</el-tag>
        </div>
        <div class="olt-meta muted">
          <span>网元标识: {{ olt.neDn }}</span>
          <span v-if="olt.osVersion" style="margin-left: 12px;">固件版本: {{ olt.osVersion }} ({{ olt.patchVersion }})</span>
        </div>
      </div>

      <!-- 8 个 GPON 端口监控网格卡片 -->
      <div class="pon-ports-grid" style="margin-top: 16px;">
        <div
          v-for="p in olt.ports"
          :key="p.portId"
          class="pon-port-card"
          :class="p.status"
          @click="emit('selectPort', p.portId)"
        >
          <div class="port-header">
            <span class="port-name">{{ p.ifDesc }}</span>
            <el-tag
              size="small"
              :type="p.status === 'healthy' ? 'success' : (p.status === 'warning' ? 'warning' : 'danger')"
              effect="dark"
            >
              {{ p.status === 'healthy' ? '正常' : (p.status === 'warning' ? '预警' : '主干中断') }}
            </el-tag>
          </div>
          <div class="port-body">
            <div class="port-stat">
              <span class="port-stat-label">承载 ONU:</span>
              <strong>{{ p.totalOnus }} 台</strong>
            </div>
            <div class="port-stat">
              <span class="port-stat-label">运行分布:</span>
              <span style="color: #10b981; font-weight: 600;">{{ p.onlineOnus }} 在线</span>
              <span style="color: #cbd5e1; margin: 0 4px;">/</span>
              <span :style="{ color: p.offlineOnus > 0 ? '#ef4444' : '#64748b', fontWeight: p.offlineOnus > 0 ? '700' : 'normal' }">{{ p.offlineOnus }} 离线</span>
            </div>
            <div v-if="p.subHealthOnus > 0" class="port-subhealth-notice">
              ⚠️ 包含 {{ p.subHealthOnus }} 台弱光亚健康设备
            </div>
          </div>
          <div class="port-footer">
            <span class="port-action-hint">点击聚焦此端口设备 &rarr;</span>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.topology-panel {
  display: flex;
  flex-direction: column;
  gap: 16px;
}
.olt-chassis-card {
  background: #ffffff;
  border: 1px solid #e2e8f0;
  border-radius: 10px;
  padding: 20px;
  box-shadow: 0 1px 4px rgba(0, 0, 0, 0.04);
}
.olt-header {
  border-bottom: 1px solid #f1f5f9;
  padding-bottom: 12px;
}
.olt-title-group {
  display: flex;
  align-items: center;
  gap: 10px;
}
.olt-name {
  font-size: 16px;
  font-weight: 700;
  color: #1e293b;
}
.olt-meta {
  font-size: 12px;
  margin-top: 6px;
}

.pon-ports-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(240px, 1fr));
  gap: 14px;
}
.pon-port-card {
  background: #f8fafc;
  border: 1px solid #e2e8f0;
  border-radius: 8px;
  padding: 14px;
  cursor: pointer;
  transition: all 0.2s ease;
}
.pon-port-card:hover {
  transform: translateY(-2px);
  box-shadow: 0 4px 10px rgba(0, 0, 0, 0.06);
}
.pon-port-card.healthy { border-top: 4px solid #10b981; }
.pon-port-card.warning { border-top: 4px solid #f59e0b; background: #fffdf5; }
.pon-port-card.trunk_broken { border-top: 4px solid #8b0000; background: #fff1f2; }

.port-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
}
.port-name {
  font-weight: 700;
  font-size: 14px;
  color: #0f172a;
}
.port-body {
  margin: 10px 0;
  display: flex;
  flex-direction: column;
  gap: 4px;
  font-size: 12.5px;
}
.port-stat {
  display: flex;
  justify-content: space-between;
}
.port-stat-label {
  color: #64748b;
}
.port-subhealth-notice {
  font-size: 11px;
  color: #b45309;
  font-weight: 600;
  margin-top: 4px;
}
.port-footer {
  font-size: 11px;
  color: var(--primary, #3b82f6);
  text-align: right;
  font-weight: 600;
}
.muted {
  color: var(--text-muted, #64748b);
}
</style>
