<script setup lang="ts">
import { computed } from 'vue'
import {
  DataLine,
  Connection,
  VideoCamera,
  Monitor,
  Box,
  WarningFilled,
  CircleCheck,
  CircleClose,
  View
} from '@element-plus/icons-vue'
import type { DigitalTwinNodeDto, DigitalTwinSceneDto, DigitalTwinStatsDto } from '../../types'

const props = defineProps<{
  scenes: DigitalTwinSceneDto[]
  currentScene: DigitalTwinSceneDto
  stats: DigitalTwinStatsDto
  nodes: DigitalTwinNodeDto[]
  showCables: boolean
  showFov: boolean
}>()

const emit = defineEmits<{
  (e: 'switchScene', scene: DigitalTwinSceneDto): void
  (e: 'selectNode', node: DigitalTwinNodeDto): void
  (e: 'update:showCables', val: boolean): void
  (e: 'update:showFov', val: boolean): void
}>()

const abnormalNodes = computed(() => {
  return props.nodes.filter(n => n.status === 'abnormal' || n.status === 'offline')
})
</script>

<template>
  <aside class="twin-left-panel">
    <!-- 场景切换胶囊栏 -->
    <div class="panel-section scene-section">
      <div class="section-title">
        <el-icon><DataLine /></el-icon>
        <span>空间场景切换</span>
      </div>
      <div class="scene-tabs">
        <button
          v-for="s in scenes"
          :key="s.id"
          class="scene-tab-btn"
          :class="{ active: s.id === currentScene.id, 'l1-badge': s.levelType === 'l1_overview' }"
          @click="emit('switchScene', s)"
        >
          <span class="scene-dot" />
          <span class="scene-name">{{ s.name }}</span>
          <span class="scene-level-tag">{{ s.levelType === 'l1_overview' ? 'L1 宏观' : 'L2 作业面' }}</span>
        </button>
      </div>
    </div>

    <!-- 运行健康度雷达指标 -->
    <div class="panel-section health-section">
      <div class="section-title">
        <el-icon><Connection /></el-icon>
        <span>实时健康指标</span>
      </div>

      <div class="health-grid">
        <div class="health-card">
          <div class="card-val text-cyan">{{ stats.totalNodes }}</div>
          <div class="card-label">空间标定总数</div>
        </div>
        <div class="health-card">
          <div class="card-val text-emerald">{{ stats.onlineNodes }}</div>
          <div class="card-label">设备在线运行</div>
        </div>
        <div class="health-card">
          <div class="card-val text-amber">{{ stats.abnormalNodes }}</div>
          <div class="card-label">光衰/预警</div>
        </div>
        <div class="health-card">
          <div class="card-val text-rose">{{ stats.offlineNodes }}</div>
          <div class="card-label">离线故障</div>
        </div>
      </div>

      <!-- 光网健康指数仪表 -->
      <div class="optical-index-bar">
        <div class="index-meta">
          <span>全光网络健康度</span>
          <span class="index-score" :class="{ 'score-good': stats.opticalHealthPercent >= 90 }">
            {{ stats.opticalHealthPercent }}%
          </span>
        </div>
        <el-progress
          :percentage="stats.opticalHealthPercent"
          :stroke-width="6"
          :show-text="false"
          :color="stats.opticalHealthPercent >= 90 ? '#10b981' : (stats.opticalHealthPercent >= 75 ? '#f59e0b' : '#ef4444')"
        />
      </div>
    </div>

    <!-- 资产类别统计药丸 -->
    <div class="panel-section asset-stats-section">
      <div class="section-title">
        <el-icon><Box /></el-icon>
        <span>空间资产构成</span>
      </div>

      <div class="asset-pills-list">
        <div class="asset-pill">
          <div class="pill-left">
            <el-icon class="icon-box"><Box /></el-icon>
            <span>网络终端箱</span>
          </div>
          <span class="pill-count">{{ stats.terminalBoxCount }}</span>
        </div>
        <div class="asset-pill">
          <div class="pill-left">
            <el-icon class="icon-cam"><VideoCamera /></el-icon>
            <span>监控摄像头</span>
          </div>
          <span class="pill-count">{{ stats.cameraCount }}</span>
        </div>
        <div class="asset-pill">
          <div class="pill-left">
            <el-icon class="icon-pc"><Monitor /></el-icon>
            <span>工位电脑</span>
          </div>
          <span class="pill-count">{{ stats.computerCount }}</span>
        </div>
      </div>
    </div>

    <!-- 图层开关控制 -->
    <div class="panel-section layers-section">
      <div class="section-title">
        <el-icon><View /></el-icon>
        <span>动态视觉图层</span>
      </div>
      <div class="toggle-row">
        <span class="toggle-label">主干光缆脉冲飞线</span>
        <el-switch
          :model-value="showCables"
          active-color="#38bdf8"
          @update:model-value="emit('update:showCables', Boolean($event))"
        />
      </div>
      <div class="toggle-row">
        <span class="toggle-label">监控视场角雷达光锥</span>
        <el-switch
          :model-value="showFov"
          active-color="#38bdf8"
          @update:model-value="emit('update:showFov', Boolean($event))"
        />
      </div>
    </div>

    <!-- 实时告警与故障关注 -->
    <div class="panel-section alert-stream-section">
      <div class="section-title">
        <el-icon><WarningFilled /></el-icon>
        <span>异常点位关注 ({{ abnormalNodes.length }})</span>
      </div>

      <div v-if="abnormalNodes.length === 0" class="no-alert-tip">
        <el-icon class="text-emerald"><CircleCheck /></el-icon>
        <span>当前场景内设施运转正常，零告警</span>
      </div>

      <div v-else class="alert-list-scroll">
        <div
          v-for="an in abnormalNodes"
          :key="an.coordinateId"
          class="alert-node-item"
          @click="emit('selectNode', an)"
        >
          <div class="alert-item-header">
            <span class="alert-badge" :class="an.status">{{ an.status === 'abnormal' ? '光衰预警' : '设备离线' }}</span>
            <span class="alert-name">{{ an.name }}</span>
          </div>
          <div class="alert-item-meta">
            <span>{{ an.location || an.code || '未记录位置' }}</span>
            <span v-if="an.rxPower" class="alert-rx">{{ an.rxPower.toFixed(1) }} dBm</span>
          </div>
        </div>
      </div>
    </div>
  </aside>
</template>

<style scoped>
.twin-left-panel {
  width: 320px;
  height: 100%;
  background: rgba(7, 12, 22, 0.92);
  backdrop-filter: blur(16px);
  border-right: 1px solid rgba(56, 189, 248, 0.2);
  display: flex;
  flex-direction: column;
  gap: 12px;
  padding: 16px;
  overflow-y: auto;
  box-shadow: 4px 0 24px rgba(0, 0, 0, 0.6);
  z-index: 70;
}

.panel-section {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.section-title {
  display: flex;
  align-items: center;
  gap: 6px;
  font-size: 13px;
  font-weight: 700;
  color: #94a3b8;
  letter-spacing: 0.5px;
}

/* Scene Switcher */
.scene-tabs {
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.scene-tab-btn {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 8px 12px;
  background: rgba(15, 23, 42, 0.7);
  border: 1px solid rgba(56, 189, 248, 0.15);
  border-radius: 6px;
  color: #cbd5e1;
  font-size: 13px;
  font-weight: 600;
  cursor: pointer;
  transition: all 0.2s ease;
}

.scene-tab-btn:hover {
  background: rgba(56, 189, 248, 0.1);
  border-color: #38bdf8;
  color: #f8fafc;
}

.scene-tab-btn.active {
  background: linear-gradient(90deg, rgba(2, 132, 199, 0.3) 0%, rgba(14, 165, 233, 0.1) 100%);
  border-color: #38bdf8;
  color: #38bdf8;
  box-shadow: 0 0 12px rgba(56, 189, 248, 0.25);
}

.scene-dot {
  width: 6px;
  height: 6px;
  border-radius: 50%;
  background: #64748b;
  margin-right: 6px;
}

.active .scene-dot {
  background: #38bdf8;
  box-shadow: 0 0 8px #38bdf8;
}

.scene-name {
  flex: 1;
  text-align: left;
}

.scene-level-tag {
  font-size: 10px;
  padding: 2px 6px;
  border-radius: 4px;
  background: rgba(255, 255, 255, 0.06);
  color: #94a3b8;
}

.active .scene-level-tag {
  background: rgba(56, 189, 248, 0.2);
  color: #7dd3fc;
}

/* Health Grid */
.health-grid {
  display: grid;
  grid-template-columns: repeat(2, 1fr);
  gap: 8px;
}

.health-card {
  background: rgba(15, 23, 42, 0.7);
  border: 1px solid rgba(255, 255, 255, 0.08);
  border-radius: 6px;
  padding: 8px 10px;
  display: flex;
  flex-direction: column;
  gap: 2px;
}

.card-val {
  font-size: 18px;
  font-weight: 800;
  font-family: 'JetBrains Mono', monospace;
}

.card-label {
  font-size: 11px;
  color: #94a3b8;
}

.text-cyan { color: #38bdf8; }
.text-emerald { color: #10b981; }
.text-amber { color: #f59e0b; }
.text-rose { color: #ef4444; }

.optical-index-bar {
  background: rgba(15, 23, 42, 0.7);
  border: 1px solid rgba(255, 255, 255, 0.08);
  border-radius: 6px;
  padding: 8px 10px;
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.index-meta {
  display: flex;
  justify-content: space-between;
  align-items: center;
  font-size: 12px;
  color: #cbd5e1;
}

.index-score {
  font-weight: 800;
  font-family: monospace;
  color: #f59e0b;
}

.score-good {
  color: #10b981;
}

/* Asset Pills */
.asset-pills-list {
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.asset-pill {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 6px 10px;
  background: rgba(15, 23, 42, 0.5);
  border: 1px solid rgba(255, 255, 255, 0.05);
  border-radius: 5px;
  font-size: 12px;
  color: #cbd5e1;
}

.pill-left {
  display: flex;
  align-items: center;
  gap: 6px;
}

.icon-box { color: #38bdf8; }
.icon-cam { color: #06b6d4; }
.icon-pc { color: #a78bfa; }

.pill-count {
  font-family: monospace;
  font-weight: bold;
  color: #f1f5f9;
}

/* Layer Switches */
.toggle-row {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 4px 0;
  font-size: 12px;
  color: #cbd5e1;
}

/* Alert Stream */
.no-alert-tip {
  display: flex;
  align-items: center;
  gap: 6px;
  font-size: 12px;
  color: #10b981;
  padding: 8px;
  background: rgba(16, 185, 129, 0.08);
  border-radius: 4px;
}

.alert-list-scroll {
  display: flex;
  flex-direction: column;
  gap: 6px;
  max-height: 180px;
  overflow-y: auto;
}

.alert-node-item {
  padding: 6px 8px;
  background: rgba(239, 68, 68, 0.08);
  border: 1px solid rgba(239, 68, 68, 0.3);
  border-radius: 5px;
  cursor: pointer;
  display: flex;
  flex-direction: column;
  gap: 2px;
  transition: background 0.15s ease;
}

.alert-node-item:hover {
  background: rgba(239, 68, 68, 0.18);
}

.alert-item-header {
  display: flex;
  align-items: center;
  gap: 6px;
}

.alert-badge {
  font-size: 9px;
  padding: 1px 4px;
  border-radius: 3px;
  font-weight: bold;
}

.alert-badge.abnormal { background: #f59e0b; color: #fff; }
.alert-badge.offline { background: #ef4444; color: #fff; }

.alert-name {
  font-size: 11px;
  font-weight: bold;
  color: #f8fafc;
}

.alert-item-meta {
  display: flex;
  justify-content: space-between;
  font-size: 10px;
  color: #94a3b8;
}

.alert-rx {
  color: #fca5a5;
  font-family: monospace;
}
</style>
