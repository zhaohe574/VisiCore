<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { allPages, managementApi, type Channel } from '../api'
import { errorMessage } from '../lib/format'
defineProps<{ modelValue?: number | null; disabled?: boolean }>()
defineEmits<{ 'update:modelValue': [number | undefined] }>()
const items = ref<Channel[]>([]), loading = ref(false), error = ref('')
let sequence = 0
async function search(value = '') {
  const current = ++sequence; loading.value = true; error.value = ''
  try { const result = await allPages(managementApi.channels, { search: value }); if (current === sequence) items.value = result }
  catch (e) { if (current === sequence) error.value = errorMessage(e) }
  finally { if (current === sequence) loading.value = false }
}
onMounted(() => void search())
</script>
<template><div class="channel-picker"><el-select :model-value="modelValue ?? undefined" filterable remote clearable :remote-method="search" :loading="loading" :disabled="disabled" placeholder="选择通道" aria-label="选择通道" @update:model-value="$emit('update:modelValue', $event)"><el-option v-for="channel in items" :key="channel.id" :value="channel.id" :label="`${channel.deviceName} / ${channel.name}（${channel.deviceChannel}）`" /></el-select><small v-if="error" class="field-error">{{ error }}</small></div></template>
