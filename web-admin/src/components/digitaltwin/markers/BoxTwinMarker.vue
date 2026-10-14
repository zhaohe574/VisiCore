<script setup lang="ts">
import { computed } from 'vue'
import type { DigitalTwinNodeDto } from '../../../types'

const props = defineProps<{
  node: DigitalTwinNodeDto
  selected?: boolean
  isDragging?: boolean
}>()

const emit = defineEmits<{
  (e: 'select', node: DigitalTwinNodeDto): void
}>()

const statusClass = computed(() => {
  if (props.node.status === 'abnormal') return 'status-warning'
  if (props.node.status === 'online') return 'status-online'
  return 'status-offline'
})

const rxDisplay = computed(() => {
  if (props.node.rxPower === null || props.node.rxPower === undefined) return null
  return `${props.node.rxPower.toFixed(1)} dBm`
})
</script>

<template>
  <div
    class="twin-marker box-marker"
    :class="[statusClass, { selected, dragging: isDragging }]"
    @click.stop="emit('select', node)"
  >
    <!-- 2.5D 立体机柜徽标 -->
    <div class="cabinet-cube">
      <div class="cube-top" />
      <div class="cube-left" />
      <div class="cube-right">
        <span class="rack-strip" />
        <span class="rack-strip" />
      </div>
      <div class="pulse-beacon" />
    </div>

    <!-- 悬浮信息徽标药丸 -->
    <div class="marker-pill">
      <div class="pill-header">
        <span class="status-dot" />
        <span class="pill-code">{{ node.code || node.name }}</span>
      </div>
      <div class="pill-details">
        <span v-if="node.onuCount" class="sub-stat onu-stat" title="下挂 ONU 数量">
          PON:{{ node.onuOnlineCount || 0 }}/{{ node.onuCount }}
        </span>
        <span v-if="node.computerCount" class="sub-stat pc-stat" title="下挂电脑数量">
          💻{{ node.computerCount }}
        </span>
        <span v-if="rxDisplay" class="sub-stat rx-stat" title="光衰接收功率">
          {{ rxDisplay }}
        </span>
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
  transition: transform 0.18s cubic-bezier(0.4, 0, 0.2, 1);
  z-index: 20;
}

.twin-marker:hover,
.twin-marker.selected {
  transform: translate(-50%, -50%) scale(1.15);
  z-index: 50;
}

.twin-marker.dragging {
  cursor: grabbing;
  transform: translate(-50%, -50%) scale(1.2);
  z-index: 99;
}

/* 2.5D Isometric Micro-Chassis */
.cabinet-cube {
  position: relative;
  width: 22px;
  height: 26px;
  transform: rotate(-15deg) skewX(10deg);
  margin-bottom: 2px;
  filter: drop-shadow(0 4px 6px rgba(0, 0, 0, 0.6));
}

.cube-top {
  position: absolute;
  top: 0;
  left: 3px;
  width: 16px;
  height: 8px;
  background: #38bdf8;
  transform: skewX(-45deg);
  border: 1px solid #7dd3fc;
  border-radius: 2px 2px 0 0;
}

.cube-left {
  position: absolute;
  top: 7px;
  left: 0;
  width: 9px;
  height: 18px;
  background: #0284c7;
  border: 1px solid #38bdf8;
  border-right: none;
}

.cube-right {
  position: absolute;
  top: 7px;
  left: 9px;
  width: 13px;
  height: 18px;
  background: #0369a1;
  border: 1px solid #38bdf8;
  display: flex;
  flex-direction: column;
  justify-content: space-around;
  padding: 2px;
}

.rack-strip {
  height: 2px;
  background: rgba(56, 189, 248, 0.6);
  border-radius: 1px;
}

/* Beacon light */
.pulse-beacon {
  position: absolute;
  top: -4px;
  left: 8px;
  width: 6px;
  height: 6px;
  border-radius: 50%;
  background: #38bdf8;
  box-shadow: 0 0 10px #38bdf8;
}

/* Status variants */
.status-online .cube-top { background: #34d399; border-color: #6ee7b7; }
.status-online .cube-left { background: #059669; border-color: #34d399; }
.status-online .cube-right { background: #047857; border-color: #34d399; }
.status-online .pulse-beacon { background: #34d399; box-shadow: 0 0 10px #34d399; }

.status-warning .cube-top { background: #fbbf24; border-color: #fde68a; }
.status-warning .cube-left { background: #d97706; border-color: #fbbf24; }
.status-warning .cube-right { background: #b45309; border-color: #fbbf24; }
.status-warning .pulse-beacon { background: #f59e0b; box-shadow: 0 0 12px #f59e0b; animation: pulse-warn 1.5s infinite; }

.status-offline .cube-top { background: #ef4444; border-color: #fca5a5; }
.status-offline .cube-left { background: #b91c1c; border-color: #ef4444; }
.status-offline .cube-right { background: #991b1b; border-color: #ef4444; }
.status-offline .pulse-beacon { background: #ef4444; box-shadow: 0 0 8px #ef4444; animation: pulse-error 1s infinite; }

@keyframes pulse-warn {
  0%, 100% { opacity: 0.8; transform: scale(1); }
  50% { opacity: 1; transform: scale(1.6); }
}

@keyframes pulse-error {
  0%, 100% { opacity: 0.6; transform: scale(1); }
  50% { opacity: 1; transform: scale(1.8); }
}

/* Marker Pill Label */
.marker-pill {
  background: rgba(11, 20, 36, 0.88);
  backdrop-filter: blur(8px);
  border: 1px solid rgba(56, 189, 248, 0.4);
  border-radius: 6px;
  padding: 3px 6px;
  box-shadow: 0 4px 12px rgba(0, 0, 0, 0.5);
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 2px;
  white-space: nowrap;
}

.selected .marker-pill {
  border-color: #38bdf8;
  box-shadow: 0 0 16px rgba(56, 189, 248, 0.6);
}

.pill-header {
  display: flex;
  align-items: center;
  gap: 4px;
}

.status-dot {
  width: 6px;
  height: 6px;
  border-radius: 50%;
  background: currentColor;
}

.status-online .status-dot { background: #10b981; }
.status-warning .status-dot { background: #f59e0b; }
.status-offline .status-dot { background: #ef4444; }

.pill-code {
  font-family: 'JetBrains Mono', 'Fira Code', monospace;
  font-size: 11px;
  font-weight: 700;
  color: #f1f5f9;
  letter-spacing: 0.5px;
}

.pill-details {
  display: flex;
  align-items: center;
  gap: 4px;
  font-size: 9px;
  color: #94a3b8;
}

.sub-stat {
  padding: 1px 4px;
  border-radius: 3px;
  background: rgba(255, 255, 255, 0.05);
}

.onu-stat { color: #38bdf8; font-weight: 600; }
.pc-stat { color: #a78bfa; font-weight: 600; }
.rx-stat { color: #e2e8f0; font-family: monospace; }
</style>
