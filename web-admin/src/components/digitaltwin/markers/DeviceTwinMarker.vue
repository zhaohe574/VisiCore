<script setup lang="ts">
import { computed } from 'vue'
import { Platform } from '@element-plus/icons-vue'
import type { DigitalTwinNodeDto } from '../../../types'

const props = defineProps<{
  node: DigitalTwinNodeDto
  selected?: boolean
  isDragging?: boolean
}>()

const emit = defineEmits<{
  (e: 'select', node: DigitalTwinNodeDto): void
}>()

const isOnline = computed(() => props.node.status === 'online')
</script>

<template>
  <div
    class="twin-marker dev-marker"
    :class="{ 'is-online': isOnline, 'is-offline': !isOnline, selected, dragging: isDragging }"
    @click.stop="emit('select', node)"
  >
    <div class="rack-beacon-box">
      <el-icon :size="16"><Platform /></el-icon>
      <div class="beacon-ring" />
    </div>

    <div class="marker-pill">
      <div class="pill-title">{{ node.name }}</div>
      <div class="pill-meta">
        <span class="pill-ip">{{ node.ip || '核心网元' }}</span>
      </div>
    </div>
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
  z-index: 30;
  transition: transform 0.15s ease;
}

.twin-marker:hover,
.twin-marker.selected {
  transform: translate(-50%, -50%) scale(1.15);
  z-index: 70;
}

.twin-marker.dragging {
  cursor: grabbing;
  z-index: 99;
}

.rack-beacon-box {
  position: relative;
  width: 28px;
  height: 28px;
  border-radius: 6px;
  background: #0284c7;
  border: 1.5px solid #38bdf8;
  color: #ffffff;
  display: flex;
  align-items: center;
  justify-content: center;
  box-shadow: 0 0 16px rgba(56, 189, 248, 0.7);
}

.beacon-ring {
  position: absolute;
  inset: -6px;
  border-radius: 50%;
  border: 1px dashed #38bdf8;
  opacity: 0.6;
  animation: spin 8s linear infinite;
}

@keyframes spin {
  from { transform: rotate(0deg); }
  to { transform: rotate(360deg); }
}

.marker-pill {
  margin-top: 4px;
  background: rgba(8, 15, 29, 0.92);
  backdrop-filter: blur(8px);
  border: 1px solid #0284c7;
  border-radius: 5px;
  padding: 2px 6px;
  display: flex;
  flex-direction: column;
  align-items: center;
  white-space: nowrap;
}

.selected .marker-pill {
  border-color: #38bdf8;
  box-shadow: 0 0 14px rgba(56, 189, 248, 0.7);
}

.pill-title {
  font-size: 11px;
  font-weight: 700;
  color: #f8fafc;
}

.pill-ip {
  font-family: monospace;
  font-size: 9px;
  color: #38bdf8;
}
</style>
