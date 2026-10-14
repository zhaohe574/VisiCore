<script setup lang="ts">
import TerminalBoxWorkbenchDrawer from './TerminalBoxWorkbenchDrawer.vue'

const props = defineProps<{
  modelValue: boolean
  boxId: number | null
  onuId?: number | null
}>()

const emit = defineEmits<{
  (e: 'update:modelValue', value: boolean): void
  (e: 'inspectChannel', channelId: number): void
  (e: 'refresh'): void
}>()
</script>

<template>
  <TerminalBoxWorkbenchDrawer
    :model-value="modelValue"
    :box-id="boxId"
    :onu-id="onuId"
    @update:model-value="emit('update:modelValue', $event)"
    @inspect-channel="emit('inspectChannel', $event)"
    @refresh="emit('refresh')"
  />
</template>

