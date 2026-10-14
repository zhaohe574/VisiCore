<script setup lang="ts">
import { ref, watch, onBeforeUnmount, onDeactivated } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { assetApi, workflowApi, type Asset, type AssetContext } from '../api'
import { assetLabels, assetRoutes } from '../lib/assets'
import { dateTime, errorMessage } from '../lib/format'
import { useAuth } from '../stores/auth'
import { useAction } from '../composables/useAction'
import { useActivePage } from '../composables/useActivePage'
import StatusBadge from './StatusBadge.vue'
const props = defineProps<{ modelValue: boolean; type: string; id: number }>()
const emit = defineEmits<{ 'update:modelValue': [value: boolean]; select: [asset: Asset]; register: [asset: Asset] }>()
const route = useRoute(), router = useRouter(), auth = useAuth()
const { run, busy } = useAction()
const context = ref<AssetContext>()
const error = ref(''), loading = ref(false), note = ref('')
const history = ref<Asset[]>([])
let controller: AbortController | undefined
const lifecycle = useActivePage(load)
async function load() {
  if (!lifecycle.isActive()) return
  controller?.abort()
  context.value = undefined
  if (!props.modelValue || !props.id) return
  const current = controller = new AbortController()
  loading.value = true; error.value = ''
  try { const value = await assetApi.context(props.type, props.id, current.signal); if (!current.signal.aborted) context.value = value }
  catch (e) { if (!current.signal.aborted) error.value = errorMessage(e) }
  finally { if (!current.signal.aborted) loading.value = false }
}
watch(() => [props.modelValue, props.type, props.id], load, { immediate: true })
function related(asset: Asset) { if (context.value) history.value.push(context.value.asset); emit('select', asset) }
function back() { const asset = history.value.pop(); if (asset) emit('select', asset) }
async function alarmAction(id: number, action: 'claim' | 'note' | 'close' | 'reopen') {
  await run(async () => {
    if (['note', 'close'].includes(action) && !note.value.trim()) throw new Error('请填写处置备注。')
    await workflowApi.alarmAction(id, action, note.value.trim()); note.value = ''; await load()
  }, '报警状态已更新。')
}
onBeforeUnmount(() => controller?.abort())
onDeactivated(() => { controller?.abort(); loading.value = false })
</script>
<template>
  <el-drawer :model-value="modelValue" :title="context?.asset.name || '资产详情工作舱'" size="min(920px, 94vw)" @update:model-value="emit('update:modelValue', $event)">
    <el-button v-if="history.length" @click="back">返回上一资产</el-button>
    <el-alert v-if="error" :title="error" type="error" :closable="false" />
    <div v-loading="loading" style="min-height: 100px;">
      <template v-if="context">
        <div class="filter-bar" style="margin-bottom: 20px;">
          <el-tag>{{ assetLabels[context.asset.ref.type] }}</el-tag><StatusBadge :value="context.asset.status" />
          <router-link :to="{ path: assetRoutes[context.asset.ref.type], query: { search: context.asset.name, assetId: context.asset.ref.id, source: route.fullPath } }"><el-button>维护资产</el-button></router-link>
          <router-link v-if="context.asset.ref.type === 'channel' && auth.can('live.view')" :to="{ path: '/app/live', query: { channelId: context.asset.ref.id } }"><el-button type="primary">单路巡查</el-button></router-link>
          <el-button v-if="['terminal-box', 'ip-pool'].includes(context.asset.ref.type)" @click="emit('register', context.asset)">从此处建档</el-button>
        </div>
        <el-descriptions :column="2" border>
          <el-descriptions-item label="资产编号">{{ context.asset.ref.id }} · {{ context.asset.code || '—' }}</el-descriptions-item>
          <el-descriptions-item label="IP">{{ context.asset.ip || '—' }}</el-descriptions-item>
          <el-descriptions-item label="组织归属" :span="2">{{ context.organization.map(n => n.name).join(' ／ ') || '尚未分配' }}</el-descriptions-item>
          <el-descriptions-item label="物理接入" :span="2">终端箱 {{ context.asset.terminalBoxId || '—' }} ／ ONU {{ context.asset.onuDeviceId || '—' }} ／ {{ context.asset.isSwitchPort ? '交换机' : '电口' }} {{ context.asset.portNumber || '—' }}</el-descriptions-item>
        </el-descriptions>
        <h3>健康证据</h3>
        <el-table :data="context.evidence"><el-table-column prop="label" label="指标" /><el-table-column prop="value" label="证据" /><el-table-column prop="source" label="来源" /><el-table-column label="采集时间"><template #default="{ row }">{{ row.observedAt ? dateTime(row.observedAt) : '尚无采集时间' }}</template></el-table-column></el-table>
        <h3>关联资产与影响范围</h3>
        <el-table :data="context.relatedAssets" empty-text="暂无授权关联资产" @row-click="related"><el-table-column label="类型" width="110"><template #default="{ row }">{{ assetLabels[row.ref.type] }}</template></el-table-column><el-table-column prop="name" label="名称" /><el-table-column prop="ip" label="IP" /><el-table-column label="状态"><template #default="{ row }"><StatusBadge :value="row.status" /></template></el-table-column></el-table>
        <h3>物理端口</h3>
        <el-table :data="context.ports" empty-text="暂无物理端口"><el-table-column prop="name" label="端口" /><el-table-column prop="status" label="状态" /><el-table-column prop="peerPortId" label="对端端口编号" /><el-table-column prop="cableLabel" label="线缆标签" /></el-table>
        <h3>掉线历史</h3>
        <el-table :data="context.offlineHistory" empty-text="暂无授权掉线记录"><el-table-column prop="deviceName" label="设备" /><el-table-column prop="reason" label="线索" /><el-table-column label="掉线时间"><template #default="{ row }">{{ dateTime(row.downTime) }}</template></el-table-column><el-table-column label="恢复时间"><template #default="{ row }">{{ row.upTime ? dateTime(row.upTime) : '未恢复' }}</template></el-table-column></el-table>
        <h3>关联报警</h3>
        <el-input v-if="auth.can('alarm.ack')" v-model="note" placeholder="填写认领、备注、关闭或重开的处置说明" style="margin-bottom: 12px;" />
        <el-table :data="context.alarms" empty-text="暂无授权报警"><el-table-column prop="eventType" label="报警" /><el-table-column label="状态"><template #default="{ row }"><StatusBadge :value="row.state" /></template></el-table-column><el-table-column label="时间"><template #default="{ row }">{{ dateTime(row.occurredAt) }}</template></el-table-column><el-table-column v-if="auth.can('alarm.ack')" label="处置" min-width="220"><template #default="{ row }"><el-button v-if="row.state === 'new'" link :disabled="busy" @click="alarmAction(row.id, 'claim')">认领</el-button><el-button link :disabled="busy" @click="alarmAction(row.id, 'note')">备注</el-button><el-button v-if="row.state !== 'closed'" link :disabled="busy" @click="alarmAction(row.id, 'close')">关闭</el-button><el-button v-else link :disabled="busy" @click="alarmAction(row.id, 'reopen')">重开</el-button></template></el-table-column></el-table>
      </template>
    </div>
  </el-drawer>
</template>
