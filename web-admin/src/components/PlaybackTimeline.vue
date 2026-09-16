<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { Aim, Crop, DArrowRight, RefreshLeft, RefreshRight, ZoomIn, ZoomOut } from '@element-plus/icons-vue'

export interface TimelineTrack {
  channelId?: number
  channelName: string
  segments: Array<{ start: string; end: string }>
  isActive?: boolean
}

const props = withDefaults(defineProps<{
  tracks?: TimelineTrack[]
  currentTime?: string | null
  clipRange?: [string, string] | null
  baseDate?: Date | null
}>(), {
  tracks: () => [],
  currentTime: null,
  clipRange: null,
  baseDate: null
})

const emit = defineEmits<{
  (e: 'seek', isoString: string): void
  (e: 'clipChange', range: [string, string] | null): void
  (e: 'stepFrame'): void
  (e: 'jump', seconds: number): void
}>()

const containerRef = ref<HTMLDivElement | null>(null)
const canvasRef = ref<HTMLCanvasElement | null>(null)

// 视窗时间范围（毫秒时间戳），严格限制在基准日期的 24 小时之内
const MIN_SPAN = 60 * 1000 // 最小 1 分钟
const MAX_SPAN = 86400000 // 最大 24 小时整

const dayStart = computed(() => {
  const d = new Date(props.baseDate || Date.now())
  d.setHours(0, 0, 0, 0)
  return d.getTime()
})
const dayEnd = computed(() => dayStart.value + MAX_SPAN)

const viewStart = ref<number>(dayStart.value)
const viewEnd = ref<number>(dayEnd.value)

function clampViewRange(start: number, end: number): [number, number] {
  let span = end - start
  if (span > MAX_SPAN) {
    span = MAX_SPAN
  } else if (span < MIN_SPAN) {
    span = MIN_SPAN
  }

  let newStart = start
  let newEnd = start + span

  if (newStart < dayStart.value) {
    newStart = dayStart.value
    newEnd = newStart + span
  }
  if (newEnd > dayEnd.value) {
    newEnd = dayEnd.value
    newStart = Math.max(dayStart.value, newEnd - span)
  }
  return [newStart, newEnd]
}

watch(dayStart, (newStart) => {
  viewStart.value = newStart
  viewEnd.value = newStart + MAX_SPAN
  render()
})

// 交互状态
const isDraggingHead = ref(false)
const isPanning = ref(false)
const isBoxSelecting = ref(false)
const panStartX = ref(0)
const panStartViewStart = ref(0)
const panStartViewEnd = ref(0)
const boxSelectStartMs = ref(0)
const mousePos = ref<{ x: number; y: number } | null>(null)

const RULER_HEIGHT = 26
const TRACK_HEIGHT = 26
const TRACK_GAP = 4
const PADDING_TOP = 4

const totalHeight = computed(() => {
  const trackCount = Math.max(1, props.tracks.length)
  return RULER_HEIGHT + PADDING_TOP + trackCount * (TRACK_HEIGHT + TRACK_GAP) + 6
})

// 时间与像素坐标转换
function timeToX(ms: number, width: number): number {
  const span = viewEnd.value - viewStart.value
  if (span <= 0) return 0
  return ((ms - viewStart.value) / span) * width
}

function xToTime(x: number, width: number): number {
  const span = viewEnd.value - viewStart.value
  return viewStart.value + (x / width) * span
}

// 格式化刻度
function formatTick(date: Date, stepMs: number): string {
  const h = String(date.getHours()).padStart(2, '0')
  const m = String(date.getMinutes()).padStart(2, '0')
  const s = String(date.getSeconds()).padStart(2, '0')
  if (stepMs < 60000) return `${h}:${m}:${s}`
  return `${h}:${m}`
}

function formatFullTime(ms: number): string {
  const d = new Date(ms)
  const y = d.getFullYear()
  const mon = String(d.getMonth() + 1).padStart(2, '0')
  const date = String(d.getDate()).padStart(2, '0')
  const h = String(d.getHours()).padStart(2, '0')
  const m = String(d.getMinutes()).padStart(2, '0')
  const s = String(d.getSeconds()).padStart(2, '0')
  return `${y}-${mon}-${date} ${h}:${m}:${s}`
}

// 磁吸附吸附点（像素阈值内对齐最近录像段起止点）
function snapToSegments(targetMs: number, width: number, snapPixel = 10): number {
  const snapMsThreshold = ((viewEnd.value - viewStart.value) / width) * snapPixel
  let closestMs = targetMs
  let minDiff = snapMsThreshold

  for (const track of props.tracks) {
    for (const seg of track.segments) {
      const sMs = Date.parse(seg.start)
      const eMs = Date.parse(seg.end)
      const dStart = Math.abs(targetMs - sMs)
      if (dStart < minDiff) {
        minDiff = dStart
        closestMs = sMs
      }
      const dEnd = Math.abs(targetMs - eMs)
      if (dEnd < minDiff) {
        minDiff = dEnd
        closestMs = eMs
      }
    }
  }
  return closestMs
}

// 绘制主循环
function render() {
  const canvas = canvasRef.value
  if (!canvas) return
  const ctx = canvas.getContext('2d')
  if (!ctx) return

  const dpr = window.devicePixelRatio || 1
  const width = canvas.clientWidth
  const height = totalHeight.value

  if (canvas.width !== Math.round(width * dpr) || canvas.height !== Math.round(height * dpr)) {
    canvas.width = Math.round(width * dpr)
    canvas.height = Math.round(height * dpr)
  }

  ctx.save()
  ctx.scale(dpr, dpr)

  // 1. 背景绘制
  ctx.fillStyle = '#141517'
  ctx.fillRect(0, 0, width, height)

  // 2. 刻度尺背景与刻度网格
  ctx.fillStyle = '#1a1d21'
  ctx.fillRect(0, 0, width, RULER_HEIGHT)

  const spanMs = viewEnd.value - viewStart.value
  let majorStepMs = 3600000 // 1 hour
  let minorStepMs = 900000 // 15 min

  if (spanMs <= 120000) { // <= 2 min
    majorStepMs = 10000; minorStepMs = 2000
  } else if (spanMs <= 600000) { // <= 10 min
    majorStepMs = 60000; minorStepMs = 10000
  } else if (spanMs <= 1800000) { // <= 30 min
    majorStepMs = 300000; minorStepMs = 60000
  } else if (spanMs <= 7200000) { // <= 2 hours
    majorStepMs = 900000; minorStepMs = 300000
  } else if (spanMs <= 21600000) { // <= 6 hours
    majorStepMs = 1800000; minorStepMs = 600000
  } else if (spanMs <= 86400000) { // <= 24 hours
    majorStepMs = 3600000; minorStepMs = 900000
  } else {
    majorStepMs = 14400000; minorStepMs = 3600000
  }

  // 次级刻度
  const firstMinor = Math.floor(viewStart.value / minorStepMs) * minorStepMs
  ctx.strokeStyle = '#2d3139'
  ctx.lineWidth = 1
  ctx.beginPath()
  for (let t = firstMinor; t <= viewEnd.value; t += minorStepMs) {
    const x = timeToX(t, width)
    if (x >= 0 && x <= width) {
      ctx.moveTo(x, RULER_HEIGHT - 4)
      ctx.lineTo(x, RULER_HEIGHT)
      ctx.moveTo(x, RULER_HEIGHT)
      ctx.lineTo(x, height)
    }
  }
  ctx.stroke()

  // 主级刻度与时间标签
  const firstMajor = Math.floor(viewStart.value / majorStepMs) * majorStepMs
  ctx.strokeStyle = '#4b5563'
  ctx.fillStyle = '#9ca3af'
  ctx.font = '11px -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif'
  ctx.textAlign = 'center'
  ctx.textBaseline = 'middle'

  ctx.beginPath()
  for (let t = firstMajor; t <= viewEnd.value; t += majorStepMs) {
    const x = timeToX(t, width)
    if (x >= 0 && x <= width) {
      ctx.moveTo(x, RULER_HEIGHT - 8)
      ctx.lineTo(x, RULER_HEIGHT)
      ctx.fillText(formatTick(new Date(t), spanMs), x, RULER_HEIGHT / 2)
    }
  }
  ctx.stroke()

  // 刻度尺底部分割线
  ctx.strokeStyle = '#374151'
  ctx.beginPath()
  ctx.moveTo(0, RULER_HEIGHT)
  ctx.lineTo(width, RULER_HEIGHT)
  ctx.stroke()

  // 3. 通道录像分轨绘制
  const tracksToDraw = props.tracks.length > 0 ? props.tracks : [{ channelName: '录像通道', segments: [], isActive: true }]
  let currentY = RULER_HEIGHT + PADDING_TOP

  tracksToDraw.forEach((track, index) => {
    // 轨道底槽
    ctx.fillStyle = track.isActive ? '#1f242d' : '#181b20'
    ctx.fillRect(0, currentY, width, TRACK_HEIGHT)

    // 录像片段
    for (const seg of track.segments) {
      const sMs = Date.parse(seg.start)
      const eMs = Date.parse(seg.end)
      const x1 = Math.max(0, timeToX(sMs, width))
      const x2 = Math.min(width, timeToX(eMs, width))
      const segWidth = Math.max(2, x2 - x1)

      if (x2 >= 0 && x1 <= width) {
        ctx.fillStyle = track.isActive ? '#10b981' : '#059669'
        ctx.fillRect(x1, currentY + 2, segWidth, TRACK_HEIGHT - 4)

        // 顶边微光高亮
        ctx.fillStyle = 'rgba(255, 255, 255, 0.25)'
        ctx.fillRect(x1, currentY + 2, segWidth, 1)
      }
    }

    // 轨道标签悬浮胶囊
    const label = track.channelName
    ctx.font = '11px sans-serif'
    const textWidth = ctx.measureText(label).width
    const badgeWidth = textWidth + 12
    const badgeX = 8
    const badgeY = currentY + 4

    ctx.fillStyle = track.isActive ? 'rgba(37, 99, 235, 0.85)' : 'rgba(31, 41, 55, 0.85)'
    ctx.beginPath()
    ctx.roundRect(badgeX, badgeY, badgeWidth, 18, 4)
    ctx.fill()

    ctx.fillStyle = '#ffffff'
    ctx.textAlign = 'left'
    ctx.textBaseline = 'middle'
    ctx.fillText(label, badgeX + 6, badgeY + 9)

    currentY += TRACK_HEIGHT + TRACK_GAP
  })

  // 4. Shift 框选片段高亮导出区域 (Clip Range)
  if (props.clipRange && props.clipRange[0] && props.clipRange[1]) {
    const c1Ms = Date.parse(props.clipRange[0])
    const c2Ms = Date.parse(props.clipRange[1])
    const cx1 = timeToX(Math.min(c1Ms, c2Ms), width)
    const cx2 = timeToX(Math.max(c1Ms, c2Ms), width)
    const cWidth = Math.max(2, cx2 - cx1)

    ctx.fillStyle = 'rgba(245, 158, 11, 0.2)'
    ctx.fillRect(cx1, RULER_HEIGHT, cWidth, height - RULER_HEIGHT)

    ctx.strokeStyle = '#f59e0b'
    ctx.lineWidth = 1.5
    ctx.setLineDash([4, 3])
    ctx.strokeRect(cx1, RULER_HEIGHT, cWidth, height - RULER_HEIGHT)
    ctx.setLineDash([])

    // 标注剪辑时段时长
    const durSec = Math.round(Math.abs(c2Ms - c1Ms) / 1000)
    const m = Math.floor(durSec / 60)
    const s = durSec % 60
    const durText = m > 0 ? `${m}分${s}秒` : `${s}秒`

    ctx.fillStyle = '#f59e0b'
    ctx.font = 'bold 11px sans-serif'
    ctx.textAlign = 'center'
    ctx.fillText(`[导出片段: ${durText}]`, cx1 + cWidth / 2, RULER_HEIGHT + 14)
  }

  // 5. 鼠标悬停标线与时间
  if (mousePos.value && mousePos.value.x >= 0 && mousePos.value.x <= width) {
    const hx = mousePos.value.x
    const hoverMs = xToTime(hx, width)

    ctx.strokeStyle = 'rgba(255, 255, 255, 0.4)'
    ctx.lineWidth = 1
    ctx.setLineDash([3, 3])
    ctx.beginPath()
    ctx.moveTo(hx, 0)
    ctx.lineTo(hx, height)
    ctx.stroke()
    ctx.setLineDash([])

    // 悬停时间浮条
    const tipText = formatFullTime(hoverMs)
    ctx.font = '11px monospace'
    const tw = ctx.measureText(tipText).width + 12
    const tipX = Math.max(tw / 2 + 2, Math.min(width - tw / 2 - 2, hx))

    ctx.fillStyle = 'rgba(0, 0, 0, 0.75)'
    ctx.beginPath()
    ctx.roundRect(tipX - tw / 2, 2, tw, 18, 3)
    ctx.fill()

    ctx.fillStyle = '#e2e8f0'
    ctx.textAlign = 'center'
    ctx.fillText(tipText, tipX, 11)
  }

  // 6. 当前播放头标线 (Playhead)
  if (props.currentTime) {
    const playMs = Date.parse(props.currentTime)
    if (!isNaN(playMs)) {
      const px = timeToX(playMs, width)

      // 贯通竖线
      ctx.strokeStyle = '#ef4444'
      ctx.lineWidth = 2
      ctx.beginPath()
      ctx.moveTo(px, 0)
      ctx.lineTo(px, height)
      ctx.stroke()

      // 顶部倒三角游标
      ctx.fillStyle = '#ef4444'
      ctx.beginPath()
      ctx.moveTo(px - 6, 0)
      ctx.lineTo(px + 6, 0)
      ctx.lineTo(px, 8)
      ctx.closePath()
      ctx.fill()

      // 播放头时间气泡
      const playText = formatTick(new Date(playMs), 1000)
      ctx.font = 'bold 11px monospace'
      const ptw = ctx.measureText(playText).width + 10
      const ptx = Math.max(ptw / 2 + 2, Math.min(width - ptw / 2 - 2, px))

      ctx.fillStyle = '#ef4444'
      ctx.beginPath()
      ctx.roundRect(ptx - ptw / 2, RULER_HEIGHT - 16, ptw, 15, 3)
      ctx.fill()

      ctx.fillStyle = '#ffffff'
      ctx.textAlign = 'center'
      ctx.fillText(playText, ptx, RULER_HEIGHT - 9)
    }
  }

  ctx.restore()
}

// 鼠标交互
function onMouseDown(e: MouseEvent) {
  const canvas = canvasRef.value
  if (!canvas) return
  const rect = canvas.getBoundingClientRect()
  const x = e.clientX - rect.left
  const width = rect.width

  // 右键或中键：平移画布
  if (e.button === 2 || e.button === 1 || e.altKey) {
    isPanning.value = true
    panStartX.value = e.clientX
    panStartViewStart.value = viewStart.value
    panStartViewEnd.value = viewEnd.value
    return
  }

  // 左键
  if (e.button === 0) {
    if (e.shiftKey) {
      // Shift 组合键：框选导出区间
      isBoxSelecting.value = true
      boxSelectStartMs.value = xToTime(x, width)
      emit('clipChange', [new Date(boxSelectStartMs.value).toISOString(), new Date(boxSelectStartMs.value).toISOString()])
    } else {
      // 播放头擦洗与定位
      isDraggingHead.value = true
      const rawMs = xToTime(x, width)
      const snappedMs = snapToSegments(rawMs, width)
      emit('seek', new Date(snappedMs).toISOString())
    }
  }
}

function onMouseMove(e: MouseEvent) {
  const canvas = canvasRef.value
  if (!canvas) return
  const rect = canvas.getBoundingClientRect()
  const x = e.clientX - rect.left
  const width = rect.width

  mousePos.value = { x, y: e.clientY - rect.top }

  if (isPanning.value) {
    const dx = e.clientX - panStartX.value
    const span = panStartViewEnd.value - panStartViewStart.value
    const timeDelta = (dx / width) * span
    let rawStart = panStartViewStart.value - timeDelta
    let rawEnd = panStartViewEnd.value - timeDelta

    if (rawStart < dayStart.value) {
      rawStart = dayStart.value
      rawEnd = rawStart + span
    }
    if (rawEnd > dayEnd.value) {
      rawEnd = dayEnd.value
      rawStart = Math.max(dayStart.value, rawEnd - span)
    }

    viewStart.value = rawStart
    viewEnd.value = rawEnd
    render()
    return
  }

  if (isBoxSelecting.value) {
    const currentMs = xToTime(x, width)
    const start = Math.min(boxSelectStartMs.value, currentMs)
    const end = Math.max(boxSelectStartMs.value, currentMs)
    emit('clipChange', [new Date(start).toISOString(), new Date(end).toISOString()])
    render()
    return
  }

  if (isDraggingHead.value) {
    const rawMs = xToTime(x, width)
    const snappedMs = snapToSegments(rawMs, width)
    emit('seek', new Date(snappedMs).toISOString())
    render()
    return
  }

  render()
}

function onMouseUp() {
  isDraggingHead.value = false
  isPanning.value = false
  isBoxSelecting.value = false
}

function onMouseLeave() {
  mousePos.value = null
  isDraggingHead.value = false
  isPanning.value = false
  isBoxSelecting.value = false
  render()
}

// 滚轮以鼠标指向为中心缩放（最大 24 小时，且严格锁定在当天）
function onWheel(e: WheelEvent) {
  e.preventDefault()
  const canvas = canvasRef.value
  if (!canvas) return
  const rect = canvas.getBoundingClientRect()
  const mouseX = Math.max(0, Math.min(rect.width, e.clientX - rect.left))
  const ratio = mouseX / rect.width

  const zoomFactor = e.deltaY < 0 ? 0.8 : 1.25
  const currentSpan = viewEnd.value - viewStart.value
  let newSpan = currentSpan * zoomFactor

  newSpan = Math.max(MIN_SPAN, Math.min(MAX_SPAN, newSpan))

  const mouseTime = viewStart.value + ratio * currentSpan
  const rawStart = mouseTime - ratio * newSpan
  const rawEnd = mouseTime + (1 - ratio) * newSpan

  const [clampedStart, clampedEnd] = clampViewRange(rawStart, rawEnd)
  viewStart.value = clampedStart
  viewEnd.value = clampedEnd
  render()
}

// 双击重置视角为当天全览
function onDoubleClick() {
  zoomFit()
}

// 快捷操作：适应当天全览（24小时）、缩放与步进
function zoomFit() {
  viewStart.value = dayStart.value
  viewEnd.value = dayEnd.value
  render()
}

function zoomIn() {
  const center = (viewStart.value + viewEnd.value) / 2
  const halfSpan = ((viewEnd.value - viewStart.value) * 0.75) / 2
  const [s, e] = clampViewRange(center - halfSpan, center + halfSpan)
  viewStart.value = s
  viewEnd.value = e
  render()
}

function zoomOut() {
  const center = (viewStart.value + viewEnd.value) / 2
  const halfSpan = ((viewEnd.value - viewStart.value) * 1.35) / 2
  const [s, e] = clampViewRange(center - halfSpan, center + halfSpan)
  viewStart.value = s
  viewEnd.value = e
  render()
}

// 监听外界数据变化刷新重绘
watch(() => [props.tracks, props.currentTime, props.clipRange, totalHeight.value], () => {
  nextTick(() => render())
}, { deep: true })

let resizeObserver: ResizeObserver | null = null

onMounted(() => {
  if (containerRef.value) {
    resizeObserver = new ResizeObserver(() => render())
    resizeObserver.observe(containerRef.value)
  }
  window.addEventListener('mouseup', onMouseUp)
  nextTick(() => {
    zoomFit()
  })
})

onBeforeUnmount(() => {
  if (resizeObserver) resizeObserver.disconnect()
  window.removeEventListener('mouseup', onMouseUp)
})
</script>

<template>
  <div ref="containerRef" class="playback-timeline-container" @contextmenu.prevent>
    <!-- 时间轴顶栏微型工具条 -->
    <div class="timeline-subbar">
      <div class="subbar-left">
        <span class="subbar-title">回放时间轴</span>
        <span class="subbar-hint">滚轮缩放 · 右键平移 · Shift框选片段 · 双击全览</span>
      </div>
      <div class="subbar-right">
        <el-tooltip content="后退 10 秒">
          <el-button size="small" :icon="RefreshLeft" text @click="emit('jump', -10)">-10s</el-button>
        </el-tooltip>
        <el-tooltip content="前进 10 秒">
          <el-button size="small" :icon="RefreshRight" text @click="emit('jump', 10)">+10s</el-button>
        </el-tooltip>
        <el-tooltip content="单帧步进">
          <el-button size="small" :icon="DArrowRight" text @click="emit('stepFrame')">逐帧</el-button>
        </el-tooltip>
        <el-divider direction="vertical" />
        <el-tooltip content="放大时间刻度">
          <el-button size="small" :icon="ZoomIn" text @click="zoomIn" />
        </el-tooltip>
        <el-tooltip content="缩小时间刻度">
          <el-button size="small" :icon="ZoomOut" text @click="zoomOut" />
        </el-tooltip>
        <el-tooltip content="全览适应">
          <el-button size="small" :icon="Aim" text @click="zoomFit" />
        </el-tooltip>
        <el-tooltip v-if="clipRange" content="清除框选区间">
          <el-button size="small" :icon="Crop" text type="warning" @click="emit('clipChange', null)">清除框选</el-button>
        </el-tooltip>
      </div>
    </div>

    <!-- 交互 Canvas 画布 -->
    <div class="timeline-canvas-wrapper" :style="{ height: `${totalHeight}px` }">
      <canvas
        ref="canvasRef"
        class="timeline-canvas"
        @mousedown="onMouseDown"
        @mousemove="onMouseMove"
        @mouseleave="onMouseLeave"
        @wheel="onWheel"
        @dblclick="onDoubleClick"
      />
    </div>
  </div>
</template>

<style scoped>
.playback-timeline-container {
  display: flex;
  flex-direction: column;
  background: #141517;
  border-top: 1px solid #2d3139;
  user-select: none;
}

.timeline-subbar {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 4px 12px;
  background: #1a1d21;
  border-bottom: 1px solid #262a30;
  font-size: 12px;
}

.subbar-left {
  display: flex;
  align-items: center;
  gap: 12px;
}

.subbar-title {
  font-weight: 600;
  color: #e5e7eb;
}

.subbar-hint {
  color: #6b7280;
  font-size: 11px;
}

.subbar-right {
  display: flex;
  align-items: center;
  gap: 4px;
}

.timeline-canvas-wrapper {
  position: relative;
  width: 100%;
  overflow: hidden;
  cursor: crosshair;
}

.timeline-canvas {
  width: 100%;
  height: 100%;
  display: block;
}
</style>
