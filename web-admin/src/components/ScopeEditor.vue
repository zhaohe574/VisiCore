<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { allPages, managementApi, type AccessScope, type Channel, type Organization, type ScopeType } from '../api'
import { useAction } from '../composables/useAction'
import { errorMessage } from '../lib/format'
const props = defineProps<{ target: 'user' | 'role'; targetId: number; name: string }>()
const emit = defineEmits<{ close: []; saved: [] }>()
const { busy, run } = useAction(), loading = ref(false), error = ref('')
const scope = ref<AccessScope>({ allChannels: false, scopes: [] }), organization = ref<Organization>({ workshops: [], areas: [], units: [] }), channels = ref<Channel[]>([])
const selections = ref<Record<ScopeType, number[]>>({ workshop: [], area: [], unit: [], channel: [] })
const groups = computed(() => [ { type: 'workshop' as const, label: '车间', items: organization.value.workshops }, { type: 'area' as const, label: '区域', items: organization.value.areas }, { type: 'unit' as const, label: '单元', items: organization.value.units }, { type: 'channel' as const, label: '通道', items: channels.value.map(channel => ({ id: channel.id, name: `${channel.deviceName} / ${channel.alias || channel.name}（${channel.deviceChannel}）` })) } ])
async function load() {
  loading.value = true; error.value = ''
  try { const result = await Promise.all([managementApi.scope(props.target, props.targetId), managementApi.organization(), allPages(managementApi.channels)]); scope.value = result[0]; organization.value = result[1]; channels.value = result[2]; for (const type of ['workshop', 'area', 'unit', 'channel'] as const) selections.value[type] = scope.value.scopes.filter(item => item.type === type).map(item => item.id) }
  catch (e) { error.value = errorMessage(e) } finally { loading.value = false }
}
async function save() { if (await run(async () => { await managementApi.saveScope(props.target, props.targetId, { allChannels: scope.value.allChannels, scopes: scope.value.allChannels ? [] : Object.entries(selections.value).flatMap(([type, ids]) => ids.map(id => ({ type: type as ScopeType, id }))) }) }, '数据范围已保存')) { emit('saved'); emit('close') } }
watch(() => [props.target, props.targetId], load, { immediate: true })
</script>
<template><el-dialog :model-value="true" :title="`数据范围 · ${name}`" width="600px" @close="emit('close')"><el-alert v-if="error" :title="error" type="error" :closable="false" /><el-form v-loading="loading" label-position="top"><el-form-item label="全部通道授权"><el-switch v-model="scope.allChannels" :disabled="loading || !!error" /></el-form-item><template v-if="!scope.allChannels"><el-form-item v-for="group in groups" :key="group.type" :label="group.label"><el-select v-model="selections[group.type]" multiple filterable collapse-tags collapse-tags-tooltip :disabled="loading || !!error" :placeholder="`选择${group.label}`"><el-option v-for="item in group.items" :key="item.id" :value="item.id" :label="item.name" /></el-select></el-form-item><p class="muted">未选择范围时，此授权不授予通道访问权。</p></template></el-form><template #footer><el-button @click="emit('close')">取消</el-button><el-button type="primary" :loading="busy" :disabled="loading || !!error" @click="save">保存范围</el-button></template></el-dialog></template>
