<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { Check, Refresh } from '@element-plus/icons-vue'
import { managementApi, type Settings } from '../api'
import { useAction } from '../composables/useAction'
import { errorMessage } from '../lib/format'
import PageHeader from '../components/PageHeader.vue'
const { busy, run } = useAction(), settings = ref<Settings>(), loading = ref(false), error = ref('')
const limits: Record<keyof Settings, [number, number]> = { livePerUser: [1, 64], playbackPerUser: [1, 16], playbackPerDevice: [1, 128], playbackGlobal: [1, 256], transcodeGlobal: [0, 32], exportGlobal: [1, 16], exportPerDevice: [1, 4], exportRetentionDays: [1, 365], exportQuotaGb: [1, 10000], alarmRetentionDays: [1, 3650], auditRetentionDays: [1, 3650] }
const groups: { title: string; fields: { key: keyof Settings; label: string; unit: string }[] }[] = [
  { title: '视频与转码', fields: [{ key: 'livePerUser', label: '每用户实时预览', unit: '路' }, { key: 'playbackPerUser', label: '每用户录像回放', unit: '路' }, { key: 'playbackPerDevice', label: '每设备录像回放', unit: '路' }, { key: 'playbackGlobal', label: '全局录像回放', unit: '路' }, { key: 'transcodeGlobal', label: '全局兼容转码', unit: '路' }] },
  { title: '录像导出', fields: [{ key: 'exportGlobal', label: '全局并发任务', unit: '个' }, { key: 'exportPerDevice', label: '每设备并发任务', unit: '个' }, { key: 'exportRetentionDays', label: '导出成品保留', unit: '天' }, { key: 'exportQuotaGb', label: '导出空间配额', unit: 'GB' }] },
  { title: '数据保留', fields: [{ key: 'alarmRetentionDays', label: '报警记录保留', unit: '天' }, { key: 'auditRetentionDays', label: '审计记录保留', unit: '天' }] },
]
async function load() { loading.value = true; error.value = ''; try { settings.value = await managementApi.settings() } catch (e) { error.value = errorMessage(e) } finally { loading.value = false } }
async function save() { if (!settings.value) return; await run(async () => { for (const [key, value] of Object.entries(settings.value!) as [keyof Settings, number][]) { if (!Number.isInteger(value) || value < limits[key][0] || value > limits[key][1]) throw new Error('配额或保留天数超出允许范围') }; await managementApi.saveSettings(settings.value!); await load() }, '系统设置已保存') }
onMounted(load)
</script>
<template><div><PageHeader title="系统设置"><el-button :icon="Refresh" :loading="loading" @click="load">重新加载</el-button><el-button type="primary" :icon="Check" :loading="busy" :disabled="!settings || loading" @click="save">保存设置</el-button></PageHeader><el-alert v-if="error" :title="error" type="error" :closable="false" /><div v-loading="loading"><el-form v-if="settings" label-position="left" label-width="170px" class="settings-form"><section v-for="group in groups" :key="group.title" class="page-section"><h2>{{ group.title }}</h2><div class="settings-fields"><el-form-item v-for="field in group.fields" :key="field.key" :label="field.label"><el-input-number v-model="settings[field.key]" :min="limits[field.key][0]" :max="limits[field.key][1]" :precision="0" /><span class="field-unit">{{ field.unit }}</span></el-form-item></div></section></el-form></div></div></template>
