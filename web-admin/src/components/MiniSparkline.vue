<script setup lang="ts">
import { computed } from 'vue'

const props = withDefaults(
  defineProps<{
    data: number[]
    color?: string
    height?: number
    fill?: boolean
    min?: number
    max?: number
  }>(),
  {
    color: '#246ec4',
    height: 36,
    fill: true,
  }
)

const id = `spark-${Math.random().toString(36).slice(2, 9)}`

const points = computed(() => {
  const list = props.data && props.data.length ? props.data : [0, 0]
  const safeList = list.length === 1 ? [list[0], list[0]] : list
  const maxVal = props.max !== undefined ? props.max : Math.max(...safeList, 0.001)
  const minVal = props.min !== undefined ? props.min : Math.min(...safeList, 0)
  const range = maxVal - minVal || 1
  const width = 100
  const height = 30
  const topPad = 2
  const bottomPad = 2
  const usableHeight = height - topPad - bottomPad

  return safeList.map((val, idx) => {
    const x = (idx / (safeList.length - 1)) * width
    const normalized = Math.max(0, Math.min(1, (val - minVal) / range))
    const y = topPad + usableHeight * (1 - normalized)
    return { x: Number(x.toFixed(1)), y: Number(y.toFixed(1)) }
  })
})

const pathD = computed(() => {
  if (!points.value.length) return ''
  return points.value.reduce((acc, pt, idx) => `${acc} ${idx === 0 ? 'M' : 'L'} ${pt.x} ${pt.y}`, '')
})

const areaD = computed(() => {
  if (!points.value.length) return ''
  const first = points.value[0]
  const last = points.value[points.value.length - 1]
  return `${pathD.value} L ${last.x} 32 L ${first.x} 32 Z`
})

const lastPoint = computed(() => {
  return points.value.length ? points.value[points.value.length - 1] : { x: 100, y: 16 }
})
</script>

<template>
  <div class="mini-sparkline" :style="{ height: `${height}px` }">
    <svg viewBox="0 0 100 32" preserveAspectRatio="none" class="sparkline-svg">
      <defs>
        <linearGradient :id="id" x1="0" y1="0" x2="0" y2="1">
          <stop offset="0%" :stop-color="color" stop-opacity="0.25" />
          <stop offset="100%" :stop-color="color" stop-opacity="0.0" />
        </linearGradient>
      </defs>
      <path v-if="fill && areaD" :d="areaD" :fill="`url(#${id})`" />
      <path :d="pathD" fill="none" :stroke="color" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" />
      <circle :cx="lastPoint.x" :cy="lastPoint.y" r="2.2" :fill="color" />
    </svg>
  </div>
</template>

<style scoped>
.mini-sparkline {
  width: 100%;
  display: block;
  position: relative;
  overflow: hidden;
}
.sparkline-svg {
  width: 100%;
  height: 100%;
  display: block;
}
</style>
