<script setup lang="ts">
import { computed, ref } from 'vue'
import { VideoCamera } from '@element-plus/icons-vue'
import type { DigitalTwinNodeDto } from '../../../types'

const props = defineProps<{
  node: DigitalTwinNodeDto
  selected?: boolean
  isDragging?: boolean
}>()

const emit = defineEmits<{
  (e: 'select', node: DigitalTwinNodeDto): void
}>()

const isHovered = ref(false)

const isOnline = computed(() => props.node.status === 'online')

const yawRotation = computed(() => {
  return `rotate(${props.node.fovYaw || 0}deg)`
})
</script>

<template>
  <div
    class="twin-marker cam-marker"
    :class="{ 'is-online': isOnline, 'is-offline': !isOnline, selected, dragging: isDragging }"
    @mouseenter="isHovered = true"
    @mouseleave="isHovered = false"
    @click.stop="emit('select', node)"
  >
    <!-- 摄像头方向性图标 -->
    <div class="cam-lens-ring" :style="{ transform: yawRotation }">
      <div class="cam-body">
        <el-icon :size="14" class="cam-icon"><VideoCamera /></el-icon>
      </div>
      <div class="lens-indicator" />
    </div>

    <!-- 简易标签 -->
    <div class="marker-pill">
      <span class="status-dot" />
      <span class="cam-name">{{ node.name }}</span>
    </div>

    <!-- 鼠标悬浮画中画微流预览卡片 (PiP Popover) -->
    <Transition name="fade">
      <div v-if="isHovered && !isDragging" class="pip-popover">
        <div class="pip-header">
          <span class="pip-title">{{ node.name }}</span>
          <span class="pip-ip">{{ node.ip || '无IP' }}</span>
        </div>
        <div class="pip-stream-box">
          <img
            v-if="isOnline && node.channelId"
            :src="`/api/v2/channels/${node.channelId}/preview?t=${Date.now()}`"
            class="pip-img"
            alt="实时快照"
            @error="($event.target as HTMLElement).style.display = 'none'"
          />
          <div v-else class="pip-placeholder">
            <span>{{ isOnline ? '载入视讯中...' : '设备离线' }}</span>
          </div>
        </div>
        <div class="pip-footer">
          <span>单击展开详细监控与云台</span>
        </div>
      </div>
    </Transition>
  </div>
</template>

<style scoped>
.twin-marker {
  position: absolute;
  transform: translate(-50%, -50%);
  display: flex;
  flex-direction: column;
  align-items: center;
  cursor: pointer;
  user-select: none;
  z-index: 25;
}

.twin-marker:hover,
.twin-marker.selected {
  z-index: 60;
}

.twin-marker.dragging {
  cursor: grabbing;
  z-index: 99;
}

.cam-lens-ring {
  position: relative;
  width: 26px;
  height: 26px;
  display: flex;
  align-items: center;
  justify-content: center;
  transition: transform 0.25s ease;
}

.cam-body {
  width: 24px;
  height: 24px;
  border-radius: 50%;
  background: #0f172a;
  border: 1.5px solid #0284c7;
  display: flex;
  align-items: center;
  justify-content: center;
  box-shadow: 0 0 10px rgba(2, 132, 199, 0.4);
}

.is-online .cam-body {
  border-color: #38bdf8;
  color: #38bdf8;
  box-shadow: 0 0 12px rgba(56, 189, 248, 0.6);
}

.is-offline .cam-body {
  border-color: #ef4444;
  color: #ef4444;
  box-shadow: 0 0 8px rgba(239, 68, 68, 0.4);
}

.selected .cam-body {
  border-color: #a855f7;
  color: #c084fc;
  box-shadow: 0 0 16px rgba(168, 85, 247, 0.8);
}

.lens-indicator {
  position: absolute;
  top: -4px;
  width: 4px;
  height: 4px;
  background: #38bdf8;
  border-radius: 50%;
  box-shadow: 0 0 6px #38bdf8;
}

.is-offline .lens-indicator {
  background: #ef4444;
  box-shadow: 0 0 6px #ef4444;
}

.marker-pill {
  margin-top: 2px;
  background: rgba(11, 20, 36, 0.88);
  backdrop-filter: blur(8px);
  border: 1px solid rgba(56, 189, 248, 0.35);
  border-radius: 4px;
  padding: 1px 5px;
  display: flex;
  align-items: center;
  gap: 4px;
  white-space: nowrap;
}

.status-dot {
  width: 5px;
  height: 5px;
  border-radius: 50%;
}

.is-online .status-dot { background: #10b981; }
.is-offline .status-dot { background: #ef4444; }

.cam-name {
  font-size: 10px;
  font-weight: 600;
  color: #e2e8f0;
}

/* Hover PiP Popover */
.pip-popover {
  position: absolute;
  bottom: 110%;
  left: 50%;
  transform: translateX(-50%);
  width: 220px;
  background: #090e17;
  border: 1px solid #0284c7;
  border-radius: 8px;
  box-shadow: 0 10px 25px rgba(0, 0, 0, 0.8), 0 0 15px rgba(56, 189, 248, 0.4);
  padding: 6px;
  display: flex;
  flex-direction: column;
  gap: 6px;
  pointer-events: none;
}

.pip-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  font-size: 11px;
}

.pip-title {
  font-weight: bold;
  color: #f8fafc;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.pip-ip {
  font-family: monospace;
  color: #38bdf8;
  font-size: 10px;
}

.pip-stream-box {
  width: 100%;
  height: 124px;
  background: #020617;
  border-radius: 4px;
  overflow: hidden;
  display: flex;
  align-items: center;
  justify-content: center;
}

.pip-img {
  width: 100%;
  height: 100%;
  object-fit: cover;
}

.pip-placeholder {
  color: #64748b;
  font-size: 11px;
}

.pip-footer {
  font-size: 9px;
  color: #94a3b8;
  text-align: center;
}

.fade-enter-active,
.fade-leave-active {
  transition: opacity 0.15s ease, transform 0.15s ease;
}

.fade-enter-from,
.fade-leave-to {
  opacity: 0;
  transform: translateX(-50%) translateY(4px);
}
</style>
