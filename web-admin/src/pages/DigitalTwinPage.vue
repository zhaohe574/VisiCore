<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import {
  Aim,
  FullScreen,
  MagicStick,
  Platform,
  Refresh,
  Tools,
  VideoCamera,
  Connection,
  Monitor
} from '@element-plus/icons-vue'
import { ElMessage } from 'element-plus'
import { digitalTwinApi } from '../api'
import type {
  CandidateNodeDto,
  DigitalTwinCableDto,
  DigitalTwinNodeDto,
  DigitalTwinSceneDetailDto,
  DigitalTwinSceneDto,
  DigitalTwinStatsDto,
  SaveCoordinateRequest
} from '../types'
import DigitalTwinCanvas from '../components/digitaltwin/DigitalTwinCanvas.vue'
import TwinLeftPanel from '../components/digitaltwin/TwinLeftPanel.vue'
import TwinRightPanel from '../components/digitaltwin/TwinRightPanel.vue'
import TwinWorkbenchModal from '../components/digitaltwin/TwinWorkbenchModal.vue'

const router = useRouter()
const loading = ref(false)
const scenes = ref<DigitalTwinSceneDto[]>([])
const currentScene = ref<DigitalTwinSceneDto | null>(null)
const nodes = ref<DigitalTwinNodeDto[]>([])
const cables = ref<DigitalTwinCableDto[]>([])
const stats = ref<DigitalTwinStatsDto>({
  totalNodes: 0,
  onlineNodes: 0,
  abnormalNodes: 0,
  offlineNodes: 0,
  terminalBoxCount: 0,
  cameraCount: 0,
  computerCount: 0,
  networkDeviceCount: 0,
  opticalHealthPercent: 100
})

// 交互状态
const selectedNode = ref<DigitalTwinNodeDto | null>(null)
const isWorkbenchMode = ref(false)
const showWorkbenchModal = ref(false)
const pendingPlaceCandidate = ref<CandidateNodeDto | null>(null)

// 视觉图层开关
const showCables = ref(true)
const showFov = ref(true)

// 全屏状态
const isFullscreen = ref(false)
const pageContainerRef = ref<HTMLDivElement>()

async function loadScenes() {
  loading.value = true
  try {
    const list = await digitalTwinApi.scenes()
    scenes.value = list
    if (list.length > 0) {
      // 默认优先进入制管车间 (先行试点 L2)，若无则进入第一项
      const pipeScene = list.find(s => s.sceneCode === 'pipe_making')
      currentScene.value = pipeScene || list[0]
      await loadSceneDetail(currentScene.value.id)
    }
  } catch (err: any) {
    ElMessage.error(err.message || '加载空间数字孪生场景失败')
  } finally {
    loading.value = false
  }
}

async function loadSceneDetail(sceneId: number) {
  try {
    const detail = await digitalTwinApi.sceneDetail(sceneId)
    currentScene.value = detail.scene
    nodes.value = detail.nodes
    cables.value = detail.cables
    stats.value = detail.stats

    // 如果选中的节点不在当前场景，清理选中
    if (selectedNode.value && !nodes.value.some(n => n.coordinateId === selectedNode.value?.coordinateId)) {
      selectedNode.value = null
    }
  } catch (err: any) {
    ElMessage.error(err.message || '加载场景空间拓扑数据失败')
  }
}

function handleSwitchScene(scene: DigitalTwinSceneDto) {
  currentScene.value = scene
  selectedNode.value = null
  loadSceneDetail(scene.id)
}

function handleSelectNode(node: DigitalTwinNodeDto | null) {
  selectedNode.value = node
}

// 拖拽完成或微调后保存坐标
async function handleUpdateCoordinate(node: DigitalTwinNodeDto, xPct: number, yPct: number) {
  // 本地先响应式更新
  node.xPct = xPct
  node.yPct = yPct

  // 异步持久化
  if (currentScene.value) {
    try {
      await digitalTwinApi.saveCoordinate(currentScene.value.id, {
        targetType: node.targetType,
        targetId: node.targetId,
        xPct,
        yPct,
        altitudeM: node.altitudeM ?? 1.5,
        fovYaw: node.fovYaw ?? 0,
        fovAngle: node.fovAngle ?? 60,
        fovRadiusM: node.fovRadiusM ?? 25,
        iconStyle: node.iconStyle,
        remarks: node.remarks
      })
    } catch (err: any) {
      ElMessage.warning(`保存坐标失败: ${err.message}`)
    }
  }
}

// 点击画布放置待标定设备
async function handleCanvasClick(xPct: number, yPct: number) {
  if (pendingPlaceCandidate.value && currentScene.value) {
    const cand = pendingPlaceCandidate.value
    try {
      await digitalTwinApi.saveCoordinate(currentScene.value.id, {
        targetType: cand.targetType,
        targetId: cand.targetId,
        xPct,
        yPct,
        altitudeM: cand.targetType === 'channel' ? 4.5 : (cand.targetType === 'computer' ? 0.8 : 1.5),
        fovYaw: 0,
        fovAngle: 60,
        fovRadiusM: 25,
        remarks: `手动标定放置`
      })
      ElMessage.success(`已成功标定放置: ${cand.name}`)
      pendingPlaceCandidate.value = null
      await loadSceneDetail(currentScene.value.id)
    } catch (err: any) {
      ElMessage.error(`标定失败: ${err.message}`)
    }
  }
}

function handlePlacePending(candidate: CandidateNodeDto) {
  pendingPlaceCandidate.value = candidate
  showWorkbenchModal.value = false
  ElMessage.info(`请在地图上单击目标位置以放置: ${candidate.name}`)
}

function handleInspectChannel(channelId: number) {
  router.push({ path: '/app/organization', query: { inspectId: channelId } })
}

function toggleFullscreen() {
  if (!document.fullscreenElement) {
    pageContainerRef.value?.requestFullscreen?.()
    isFullscreen.value = true
  } else {
    document.exitFullscreen?.()
    isFullscreen.value = false
  }
}

onMounted(() => {
  loadScenes()
})
</script>

<template>
  <div ref="pageContainerRef" class="digital-twin-page" :class="{ 'is-fullscreen-page': isFullscreen }">
    <!-- 顶部科技感知控制栏 (Top Bar) -->
    <header class="twin-top-bar">
      <div class="top-left">
        <el-icon :size="20" class="brand-glow-icon"><Platform /></el-icon>
        <span class="system-title">全厂网络与空间数字孪生中心</span>
        <div v-if="currentScene" class="scene-active-pill">
          <span class="pulse-indicator" />
          <span>{{ currentScene.name }}</span>
          <span class="scene-badge">{{ currentScene.levelType === 'l1_overview' ? 'L1 宏观总览' : 'L2 工业作业面' }}</span>
        </div>
      </div>

      <div class="top-center">
        <!-- 待放置提醒 -->
        <div v-if="pendingPlaceCandidate" class="pending-place-alert">
          <el-icon><Aim /></el-icon>
          <span>正在标定设备: <strong>{{ pendingPlaceCandidate.name }}</strong> (点击画布目标立柱完成放置)</span>
          <el-button link type="danger" size="small" @click="pendingPlaceCandidate = null">取消</el-button>
        </div>
      </div>

      <div class="top-right">
        <!-- 标定工作台开关 -->
        <el-button
          :type="isWorkbenchMode ? 'warning' : 'default'"
          :icon="Tools"
          size="small"
          @click="isWorkbenchMode = !isWorkbenchMode"
        >
          {{ isWorkbenchMode ? '退出拖拽调整' : '拖拽微调点位' }}
        </el-button>

        <el-button
          type="primary"
          :icon="Aim"
          size="small"
          @click="showWorkbenchModal = true"
        >
          标定工作台
        </el-button>

        <el-button
          circle
          size="small"
          :icon="Refresh"
          title="刷新数据"
          @click="currentScene && loadSceneDetail(currentScene.id)"
        />

        <el-button
          circle
          size="small"
          :icon="FullScreen"
          :title="isFullscreen ? '退出全屏' : '全屏展示'"
          @click="toggleFullscreen"
        />
      </div>
    </header>

    <!-- 主视窗调度工作区 (Canvas & Panels) -->
    <main class="twin-workspace-body">
      <!-- 左侧指标与场景列表面板 -->
      <TwinLeftPanel
        v-if="currentScene"
        :scenes="scenes"
        :current-scene="currentScene"
        :stats="stats"
        :nodes="nodes"
        v-model:show-cables="showCables"
        v-model:show-fov="showFov"
        @switch-scene="handleSwitchScene"
        @select-node="handleSelectNode"
      />

      <!-- 核心 2.5D 微立体数字孪生视口画布 -->
      <section class="twin-canvas-container">
        <DigitalTwinCanvas
          v-if="currentScene"
          :scene="currentScene"
          :nodes="nodes"
          :cables="cables"
          :selected-node="selectedNode"
          :is-workbench-mode="isWorkbenchMode"
          :show-cables="showCables"
          :show-fov="showFov"
          @select-node="handleSelectNode"
          @update-coordinate="handleUpdateCoordinate"
          @canvas-click="handleCanvasClick"
        />
      </section>

      <!-- 右侧节点详细透视面板 -->
      <TwinRightPanel
        v-if="selectedNode"
        :node="selectedNode"
        @close="selectedNode = null"
        @inspect-channel="handleInspectChannel"
      />
    </main>

    <!-- 可视化空间标定工作台弹窗 -->
    <TwinWorkbenchModal
      v-if="currentScene"
      v-model:visible="showWorkbenchModal"
      :scene="currentScene"
      :current-nodes="nodes"
      @refresh="loadSceneDetail(currentScene.id)"
      @place-pending-node="handlePlacePending"
    />
  </div>
</template>

<style scoped>
.digital-twin-page {
  position: relative;
  width: 100%;
  height: calc(100vh - 110px);
  min-height: 600px;
  background: #050811;
  display: flex;
  flex-direction: column;
  overflow: hidden;
  color: #f1f5f9;
}

.is-fullscreen-page {
  height: 100vh;
  z-index: 9999;
}

/* Top bar */
.twin-top-bar {
  height: 52px;
  background: rgba(7, 12, 22, 0.95);
  backdrop-filter: blur(16px);
  border-bottom: 1px solid rgba(56, 189, 248, 0.25);
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 0 16px;
  z-index: 100;
  box-shadow: 0 4px 20px rgba(0, 0, 0, 0.6);
}

.top-left {
  display: flex;
  align-items: center;
  gap: 12px;
}

.brand-glow-icon {
  color: #38bdf8;
  filter: drop-shadow(0 0 8px #38bdf8);
}

.system-title {
  font-size: 16px;
  font-weight: 800;
  letter-spacing: 0.8px;
  background: linear-gradient(90deg, #f8fafc 0%, #38bdf8 100%);
  -webkit-background-clip: text;
  -webkit-text-fill-color: transparent;
}

.scene-active-pill {
  display: flex;
  align-items: center;
  gap: 6px;
  background: rgba(2, 132, 199, 0.2);
  border: 1px solid rgba(56, 189, 248, 0.4);
  padding: 3px 10px;
  border-radius: 14px;
  font-size: 12px;
  font-weight: 600;
  color: #7dd3fc;
}

.pulse-indicator {
  width: 6px;
  height: 6px;
  border-radius: 50%;
  background: #38bdf8;
  box-shadow: 0 0 8px #38bdf8;
  animation: pulse-ring 2s infinite;
}

@keyframes pulse-ring {
  0% { transform: scale(0.9); opacity: 0.8; }
  50% { transform: scale(1.4); opacity: 1; }
  100% { transform: scale(0.9); opacity: 0.8; }
}

.scene-badge {
  font-size: 10px;
  padding: 1px 5px;
  border-radius: 3px;
  background: rgba(56, 189, 248, 0.3);
  color: #e0f2fe;
}

.top-center {
  flex: 1;
  display: flex;
  justify-content: center;
}

.pending-place-alert {
  display: flex;
  align-items: center;
  gap: 8px;
  background: rgba(245, 158, 11, 0.2);
  border: 1px solid #f59e0b;
  padding: 4px 12px;
  border-radius: 6px;
  font-size: 12px;
  color: #fef3c7;
  animation: bounce 1.5s infinite;
}

.top-right {
  display: flex;
  align-items: center;
  gap: 10px;
}

/* Workspace body */
.twin-workspace-body {
  position: relative;
  flex: 1;
  display: flex;
  overflow: hidden;
}

.twin-canvas-container {
  flex: 1;
  height: 100%;
  position: relative;
  overflow: hidden;
}
</style>
