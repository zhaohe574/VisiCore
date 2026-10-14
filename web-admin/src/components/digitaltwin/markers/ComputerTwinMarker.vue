<script setup lang="ts">
import { computed } from 'vue'
import { Monitor } from '@element-plus/icons-vue'
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
    class="twin-marker comp-marker"
    :class="{ 'is-online': isOnline, 'is-offline': !isOnline, selected, dragging: isDragging }"
    @click.stop="emit('select', node)"
  >
    <div class="comp-icon-box">
      <el-icon :size="13"><Monitor /></el-icon>
      <span v-if="isOnline" class="latency-dot" />
    </div>

    <div class="marker-pill">
      <div class="pill-title">{{ node.name }}</div>
      <div class="pill-meta">
        <span class="pill-ip">{{ node.ip || 'DHCP' }}</span>
        <span v-if="node.responsiblePerson" class="pill-user">· {{ node.responsiblePerson }}</span>
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
  z-index: 15;
  transition: transform 0.15s ease;
}

.twin-marker:hover,
.twin-marker.selected {
  transform: translate(-50%, -50%) scale(1.15);
  z-index: 55;
}

.twin-marker.dragging {
  cursor: grabbing;
  z-index: 99;
}

.comp-icon-box {
  position: relative;
  width: 22px;
  height: 22px;
  border-radius: 4px;
  background: #091322;
  border: 1px solid #6366f1;
  color: #818cf8;
  display: flex;
  align-items: center;
  justify-content: center;
  box-shadow: 0 0 8px rgba(99, 102, 241, 0.4);
}

.is-online .comp-icon-box {
  border-color: #a855f7;
  color: #c084fc;
  box-shadow: 0 0 10px rgba(168, 85, 247, 0.5);
}

.is-offline .comp-icon-box {
  border-color: #64748b;
  color: #64748b;
  box-shadow: none;
}

.latency-dot {
  position: absolute;
  top: -2px;
  right: -2px;
  width: 5px;
  height: 5px;
  border-radius: 50%;
  background: #10b981;
  box-shadow: 0 0 6px #10b981;
}

.marker-pill {
  margin-top: 2px;
  background: rgba(11, 20, 36, 0.9);
  backdrop-filter: blur(8px);
  border: 1px solid rgba(168, 85, 247, 0.35);
  border-radius: 4px;
  padding: 1px 4px;
  display: flex;
  flex-direction: column;
  align-items: center;
  white-space: nowrap;
}

.selected .marker-pill {
  border-color: #c084fc;
  box-shadow: 0 0 12px rgba(192, 132, 252, 0.6);
}

.pill-title {
  font-size: 10px;
  font-weight: 600;
  color: #f1f5f9;
}

.pill-meta {
  display: flex;
  align-items: center;
  gap: 2px;
  font-size: 8px;
  color: #94a3b8;
}

.pill-ip {
  font-family: monospace;
  color: #38bdf8;
}

.pill-user {
  color: #cbd5e1;
}
</style>
