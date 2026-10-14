<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { Aim, Plus, Minus, Refresh, View, Connection } from '@element-plus/icons-vue'
import type { DigitalTwinCableDto, DigitalTwinNodeDto, DigitalTwinSceneDto } from '../../types'
import BoxTwinMarker from './markers/BoxTwinMarker.vue'
import CameraTwinMarker from './markers/CameraTwinMarker.vue'
import ComputerTwinMarker from './markers/ComputerTwinMarker.vue'
import DeviceTwinMarker from './markers/DeviceTwinMarker.vue'

const props = defineProps<{
  scene: DigitalTwinSceneDto
  nodes: DigitalTwinNodeDto[]
  cables: DigitalTwinCableDto[]
  selectedNode: DigitalTwinNodeDto | null
  isWorkbenchMode?: boolean
  showCables?: boolean
  showFov?: boolean
}>()

const emit = defineEmits<{
  (e: 'selectNode', node: DigitalTwinNodeDto | null): void
  (e: 'updateCoordinate', node: DigitalTwinNodeDto, xPct: number, yPct: number): void
  (e: 'canvasClick', xPct: number, yPct: number): void
}>()

const containerRef = ref<HTMLDivElement>()
const stageRef = ref<HTMLDivElement>()

// 视口变换状态 (平移与缩放)
const zoom = ref(1.0)
const panX = ref(0)
const panY = ref(0)
const isPanning = ref(false)
const startPanX = ref(0)
const startPanY = ref(0)

// 节点拖拽标定状态
const draggingNode = ref<DigitalTwinNodeDto | null>(null)
const dragStartClientX = ref(0)
const dragStartClientY = ref(0)
const dragOriginalXPct = ref(0)
const dragOriginalYPct = ref(0)

// 基础缩放与居中初始化
function resetView() {
  zoom.value = Number(props.scene.defaultZoom) || 1.0
  panX.value = 0
  panY.value = 0
}

watch(() => props.scene.id, () => {
  resetView()
}, { immediate: true })

// 鼠标滚轮平滑缩放
function onWheel(e: WheelEvent) {
  e.preventDefault()
  const zoomFactor = e.deltaY < 0 ? 1.12 : 0.89
  const newZoom = Math.min(Math.max(zoom.value * zoomFactor, 0.35), 4.5)
  zoom.value = Number(newZoom.toFixed(3))
}

// 鼠标拖拽平移视口
function onMouseDown(e: MouseEvent) {
  if (e.button !== 0) return // 只响应左键
  if (draggingNode.value) return

  isPanning.value = true
  startPanX.value = e.clientX - panX.value
  startPanY.value = e.clientY - panY.value
}

function onMouseMove(e: MouseEvent) {
  if (draggingNode.value && stageRef.value) {
    const rect = stageRef.value.getBoundingClientRect()
    const currentX = (e.clientX - rect.left) / rect.width
    const currentY = (e.clientY - rect.top) / rect.height
    const clampedX = Math.min(Math.max(Number(currentX.toFixed(4)), 0.02), 0.98)
    const clampedY = Math.min(Math.max(Number(currentY.toFixed(4)), 0.02), 0.98)

    emit('updateCoordinate', draggingNode.value, clampedX, clampedY)
    return
  }

  if (isPanning.value) {
    panX.value = e.clientX - startPanX.value
    panY.value = e.clientY - startPanY.value
  }
}

function onMouseUp(e: MouseEvent) {
  if (draggingNode.value) {
    draggingNode.value = null
    return
  }
  isPanning.value = false
}

// 节点拖拽启动（仅标定模式生效）
function startDragNode(node: DigitalTwinNodeDto, e: MouseEvent) {
  if (!props.isWorkbenchMode) return
  e.stopPropagation()
  draggingNode.value = node
  dragStartClientX.value = e.clientX
  dragStartClientY.value = e.clientY
  dragOriginalXPct.value = node.xPct
  dragOriginalYPct.value = node.yPct
}

// 点击空白画布
function onStageClick(e: MouseEvent) {
  if (draggingNode.value || Math.abs(panX.value - (e.clientX - startPanX.value)) > 5) return
  if (stageRef.value) {
    const rect = stageRef.value.getBoundingClientRect()
    const xPct = Math.min(Math.max(Number(((e.clientX - rect.left) / rect.width).toFixed(4)), 0), 1)
    const yPct = Math.min(Math.max(Number(((e.clientY - rect.top) / rect.height).toFixed(4)), 0), 1)
    emit('canvasClick', xPct, yPct)
  }
  emit('selectNode', null)
}

function zoomIn() {
  zoom.value = Math.min(zoom.value * 1.2, 4.5)
}

function zoomOut() {
  zoom.value = Math.max(zoom.value * 0.8, 0.35)
}

// 计算视场角光锥 (FOV Cone) 多边形点
function getFovPolygon(node: DigitalTwinNodeDto): string {
  const cx = node.xPct * 100
  const cy = node.yPct * 100
  const radius = (node.fovRadiusM || 25) * 0.22 // 比例缩放映射到百分比
  const yaw = (node.fovYaw || 0) * (Math.PI / 180)
  const halfAngle = ((node.fovAngle || 60) / 2) * (Math.PI / 180)

  // 摄像头指向中心角
  const a1 = yaw - halfAngle - Math.PI / 2
  const a2 = yaw + halfAngle - Math.PI / 2

  const x1 = cx + radius * Math.cos(a1)
  const y1 = cy + radius * Math.sin(a1)
  const x2 = cx + radius * Math.cos(a2)
  const y2 = cy + radius * Math.sin(a2)

  return `${cx}% ${cy}%, ${x1}% ${y1}%, ${x2}% ${y2}%`
}

// 解析光缆路径折线点
function parseCablePoints(cable: DigitalTwinCableDto): string {
  try {
    const pts = JSON.parse(cable.pointsJson) as Array<{ x: number; y: number } | [number, number]>
    if (!Array.isArray(pts)) return ''
    return pts.map(p => {
      const x = Array.isArray(p) ? p[0] : p.x
      const y = Array.isArray(p) ? p[1] : p.y
      return `${(x * 100).toFixed(2)}%,${(y * 100).toFixed(2)}%`
    }).join(' ')
  } catch {
    return ''
  }
}

onMounted(() => {
  window.addEventListener('mouseup', onMouseUp)
  window.addEventListener('mousemove', onMouseMove)
})

onBeforeUnmount(() => {
  window.removeEventListener('mouseup', onMouseUp)
  window.removeEventListener('mousemove', onMouseMove)
})
</script>

<template>
  <div
    ref="containerRef"
    class="twin-viewport"
    :class="{ 'is-panning': isPanning, 'is-workbench': isWorkbenchMode }"
    @wheel="onWheel"
    @mousedown="onMouseDown"
  >
    <!-- 可变换舞台容器 (Stage) -->
    <div
      ref="stageRef"
      class="twin-stage"
      :style="{
        transform: `translate(${panX}px, ${panY}px) scale(${zoom})`,
        aspectRatio: `${scene.naturalWidth} / ${scene.naturalHeight}`
      }"
      @click="onStageClick"
    >
      <!-- Layer 1: 2.5D 微立体矢量底图 -->
      <img
        :src="scene.bgImageUrl"
        class="base-map-img"
        draggable="false"
        alt="数字孪生底图"
      />

      <!-- Layer 2: 动态流光光纤管缆 (SVG Overlay) -->
      <svg
        v-if="showCables !== false"
        class="cable-svg-layer"
        xmlns="http://www.w3.org/2000/svg"
        viewBox="0 0 100 100"
        preserveAspectRatio="none"
      >
        <defs>
          <filter id="cableGlow" x="-20%" y="-20%" width="140%" height="140%">
            <feGaussianBlur stdDeviation="0.4" result="blur" />
            <feMerge>
              <feMergeNode in="blur" />
              <feMergeNode in="SourceGraphic" />
            </feMerge>
          </filter>
        </defs>

        <g v-for="cable in cables" :key="cable.id" class="cable-group">
          <!-- 底层微弱光晕轨道 -->
          <polyline
            :points="parseCablePoints(cable)"
            fill="none"
            :stroke="cable.color || '#0284c7'"
            stroke-width="0.8"
            stroke-opacity="0.35"
            stroke-linecap="round"
            stroke-linejoin="round"
          />
          <!-- 核心脉冲流光飞线 (Dash-Array 动画) -->
          <polyline
            :points="parseCablePoints(cable)"
            fill="none"
            :stroke="cable.status === 'down' ? '#ef4444' : (cable.color || '#38bdf8')"
            stroke-width="0.4"
            stroke-linecap="round"
            stroke-linejoin="round"
            stroke-dasharray="1.2 2.4"
            class="pulse-flow-line"
            filter="url(#cableGlow)"
          />
        </g>
      </svg>

      <!-- Layer 3: 监控视场角 (FOV Radar Cones) -->
      <div v-if="showFov !== false" class="fov-layer">
        <template v-for="node in nodes" :key="'fov-' + node.coordinateId">
          <div
            v-if="node.targetType === 'channel' && node.status === 'online'"
            class="fov-cone"
            :style="{ clipPath: `polygon(${getFovPolygon(node)})` }"
          />
        </template>
      </div>

      <!-- Layer 4: 空间资产立体浮标 (Markers) -->
      <div class="markers-layer">
        <template v-for="node in nodes" :key="node.coordinateId">
          <!-- 终端箱发光微机柜 -->
          <BoxTwinMarker
            v-if="node.targetType === 'terminal_box'"
            :node="node"
            :selected="selectedNode?.coordinateId === node.coordinateId"
            :is-dragging="draggingNode?.coordinateId === node.coordinateId"
            :style="{ left: `${node.xPct * 100}%`, top: `${node.yPct * 100}%` }"
            @select="emit('selectNode', node)"
            @mousedown="startDragNode(node, $event)"
          />

          <!-- 监控摄像头 -->
          <CameraTwinMarker
            v-else-if="node.targetType === 'channel'"
            :node="node"
            :selected="selectedNode?.coordinateId === node.coordinateId"
            :is-dragging="draggingNode?.coordinateId === node.coordinateId"
            :style="{ left: `${node.xPct * 100}%`, top: `${node.yPct * 100}%` }"
            @select="emit('selectNode', node)"
            @mousedown="startDragNode(node, $event)"
          />

          <!-- 工位电脑 -->
          <ComputerTwinMarker
            v-else-if="node.targetType === 'computer'"
            :node="node"
            :selected="selectedNode?.coordinateId === node.coordinateId"
            :is-dragging="draggingNode?.coordinateId === node.coordinateId"
            :style="{ left: `${node.xPct * 100}%`, top: `${node.yPct * 100}%` }"
            @select="emit('selectNode', node)"
            @mousedown="startDragNode(node, $event)"
          />

          <!-- 核心网络设备 -->
          <DeviceTwinMarker
            v-else-if="node.targetType === 'network_device'"
            :node="node"
            :selected="selectedNode?.coordinateId === node.coordinateId"
            :is-dragging="draggingNode?.coordinateId === node.coordinateId"
            :style="{ left: `${node.xPct * 100}%`, top: `${node.yPct * 100}%` }"
            @select="emit('selectNode', node)"
            @mousedown="startDragNode(node, $event)"
          />
        </template>
      </div>
    </div>

    <!-- 浮动视口操作控件 (HUD Controls) -->
    <div class="viewport-hud-controls">
      <el-button-group>
        <el-button circle :icon="Plus" title="放大" @click.stop="zoomIn" />
        <el-button circle :icon="Minus" title="缩小" @click.stop="zoomOut" />
        <el-button circle :icon="Refresh" title="重置视角" @click.stop="resetView" />
      </el-button-group>

      <div class="zoom-pill">
        {{ Math.round(zoom * 100) }}%
      </div>
    </div>
  </div>
</template>

<style scoped>
.twin-viewport {
  position: relative;
  width: 100%;
  height: 100%;
  background: #050811;
  overflow: hidden;
  user-select: none;
  cursor: grab;
  display: flex;
  align-items: center;
  justify-content: center;
}

.twin-viewport.is-panning {
  cursor: grabbing;
}

.twin-viewport.is-workbench {
  background: radial-gradient(circle at center, #0a1324 0%, #04070e 100%);
}

.twin-stage {
  position: relative;
  width: 100%;
  max-width: 100%;
  max-height: 100%;
  transform-origin: center center;
  transition: transform 0.05s linear;
  box-shadow: 0 0 50px rgba(0, 0, 0, 0.8);
}

.base-map-img {
  width: 100%;
  height: 100%;
  display: block;
  pointer-events: none;
}

/* Layer 2: Cables */
.cable-svg-layer {
  position: absolute;
  inset: 0;
  width: 100%;
  height: 100%;
  pointer-events: none;
  z-index: 5;
}

.pulse-flow-line {
  animation: fiber-flow 2.2s linear infinite;
}

@keyframes fiber-flow {
  from { stroke-dashoffset: 3.6; }
  to { stroke-dashoffset: 0; }
}

/* Layer 3: FOV Radar */
.fov-layer {
  position: absolute;
  inset: 0;
  pointer-events: none;
  z-index: 8;
}

.fov-cone {
  position: absolute;
  inset: 0;
  background: radial-gradient(circle at center, rgba(56, 189, 248, 0.35) 0%, rgba(14, 165, 233, 0.05) 75%, transparent 100%);
  opacity: 0.75;
  filter: drop-shadow(0 0 6px rgba(56, 189, 248, 0.4));
  animation: fov-pulse 4s ease-in-out infinite;
}

@keyframes fov-pulse {
  0%, 100% { opacity: 0.65; }
  50% { opacity: 0.9; }
}

/* Layer 4: Markers */
.markers-layer {
  position: absolute;
  inset: 0;
  z-index: 10;
  pointer-events: auto;
}

/* Floating HUD Controls */
.viewport-hud-controls {
  position: absolute;
  bottom: 24px;
  right: 24px;
  z-index: 80;
  display: flex;
  align-items: center;
  gap: 8px;
  background: rgba(8, 15, 29, 0.85);
  backdrop-filter: blur(10px);
  padding: 6px 10px;
  border-radius: 20px;
  border: 1px solid rgba(56, 189, 248, 0.3);
  box-shadow: 0 4px 18px rgba(0, 0, 0, 0.6);
}

.zoom-pill {
  font-family: monospace;
  font-size: 11px;
  font-weight: bold;
  color: #38bdf8;
  padding: 2px 6px;
  background: rgba(56, 189, 248, 0.1);
  border-radius: 4px;
}
</style>
