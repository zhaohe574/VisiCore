<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { assetApi, allPages, computerApi, ipPoolApi, managementApi, ponApi, type Asset, type ComputerInput, type Organization, type OnuDeviceDto, type TerminalBoxDto, type Channel } from '../api'
import { buildUnitCascaderOptions } from '../lib/organization'
import { useAuth } from '../stores/auth'
import { useAction } from '../composables/useAction'
import { errorMessage } from '../lib/format'
import ComputerDialog from './computers/ComputerDialog.vue'
import AddCameraDialog from './organization/AddCameraDialog.vue'
const props = defineProps<{ modelValue: boolean; source?: Asset; unitId?: number }>()
const emit = defineEmits<{ 'update:modelValue': [boolean]; saved: [] }>()
const auth = useAuth(), { run, busy } = useAction()
const step = ref(0), kind = ref('computer'), error = ref('')
const organization = ref<Organization>({ workshops: [], areas: [], units: [] })
const boxes = ref<TerminalBoxDto[]>([]), onus = ref<OnuDeviceDto[]>([]), channels = ref<Channel[]>([])
const unit = ref<number>(), box = ref<number>(), onu = ref<number>(), port = ref(1), isSwitch = ref(false), existingChannel = ref<number>()
const initialIp = ref(''), computerOpen = ref(false), cameraOpen = ref(false)
const options = computed(() => buildUnitCascaderOptions(organization.value))
const filteredOnus = computed(() => onus.value.filter(o => !box.value || o.terminalBoxId === box.value))
const context = computed<Partial<ComputerInput>>(() => ({
  unitId: unit.value || null, areaId: props.source?.areaId || null, workshopId: props.source?.workshopId || null,
  terminalBoxId: box.value || null, onuDeviceId: onu.value || null, portNumber: port.value, isSwitchPort: isSwitch.value
}))
watch(() => props.modelValue, async open => {
  if (!open) return
  step.value = 0; error.value = ''; existingChannel.value = undefined
  kind.value = auth.can('computer.manage') ? 'computer' : auth.can('device.manage') || auth.can('channel.assign') ? 'channel' : 'existing'
  unit.value = props.unitId || props.source?.unitId || undefined
  box.value = props.source?.terminalBoxId || (props.source?.ref.type === 'terminal-box' ? props.source.ref.id : undefined)
  onu.value = props.source?.onuDeviceId || undefined
  port.value = 1; isSwitch.value = false
  try {
    const [org, tb, od, unassigned] = await Promise.all([
      managementApi.organization(), auth.can('pon.read') ? ponApi.terminalBoxes() : [],
      auth.can('pon.read') ? allPages(ponApi.onus) : [], auth.can('channel.assign') ? managementApi.unassignedChannels() : []
    ])
    organization.value = org; boxes.value = tb; onus.value = od; channels.value = unassigned
  } catch (e) { error.value = errorMessage(e) }
})
async function next() {
  if (step.value === 0) { if (!unit.value && kind.value !== 'computer') { error.value = '请选择摄像头的组织归属。'; return }; step.value = 1; error.value = ''; return }
  if (kind.value === 'existing') { step.value = 2; return }
  if (kind.value === 'computer') {
    initialIp.value = ''
    if (auth.can('ip.read')) {
      try { initialIp.value = (await ipPoolApi.recommendIp(context.value.workshopId, props.source?.ref.type === 'ip-pool' ? props.source.ref.id : null))?.ip || '' }
      catch (e) { error.value = errorMessage(e); return }
    }
    computerOpen.value = true
  } else cameraOpen.value = true
  emit('update:modelValue', false)
}
async function claim() {
  const success = await run(async () => {
    if (!existingChannel.value || !unit.value) throw new Error('请选择已接入通道及组织归属。')
    await managementApi.batchClaimChannels({ channelIds: [existingChannel.value], unitId: unit.value,
      terminalBoxId: box.value || null, onuDeviceId: onu.value || null, startPortNumber: port.value, isSwitchPort: isSwitch.value })
  }, '摄像头已建档并关联。')
  if (success) { emit('update:modelValue', false); emit('saved') }
}
</script>
<template>
  <el-dialog :model-value="modelValue" title="资产建档与关联" width="680px" @update:model-value="emit('update:modelValue', $event)">
    <el-steps :active="step" simple style="margin-bottom: 20px;"><el-step title="选择归属" /><el-step title="资产与接入" /><el-step title="校验提交" /></el-steps>
    <el-alert v-if="error" :title="error" type="error" :closable="false" />
    <el-form label-position="top">
      <template v-if="step === 0">
        <el-form-item label="资产类型"><el-radio-group v-model="kind"><el-radio-button v-if="auth.can('computer.manage')" value="computer">登记电脑</el-radio-button><el-radio-button v-if="auth.can('device.manage') || auth.can('channel.assign')" value="channel">接入新摄像头</el-radio-button><el-radio-button v-if="auth.can('channel.assign')" value="existing">认领已有通道</el-radio-button></el-radio-group></el-form-item>
        <el-form-item label="组织归属"><el-cascader v-model="unit" :options="options" :props="{ emitPath: false }" clearable filterable style="width: 100%;" /></el-form-item>
        <p v-if="source" class="muted">来源：{{ source.name }}。归属与网络接入已自动带入，可继续调整。</p>
      </template>
      <template v-else>
        <el-form-item v-if="kind === 'existing'" label="已接入但未建档的通道"><el-select v-model="existingChannel" filterable style="width: 100%;"><el-option v-for="c in channels" :key="c.id" :label="c.deviceName + ' ／ ' + (c.alias || c.name)" :value="c.id" /></el-select></el-form-item>
        <el-form-item label="物理终端箱"><el-select v-model="box" clearable filterable @change="onu = undefined"><el-option v-for="b in boxes" :key="b.id" :label="b.name" :value="b.id" /></el-select></el-form-item>
        <el-form-item label="ONU"><el-select v-model="onu" clearable filterable><el-option v-for="o in filteredOnus" :key="o.id" :label="o.alias || o.onuName" :value="o.id" /></el-select></el-form-item>
        <el-form-item label="接入端口"><el-input-number v-model="port" :min="1" :max="isSwitch ? 128 : 4" /><el-checkbox v-model="isSwitch" style="margin-left: 16px;">下挂交换机端口</el-checkbox></el-form-item>
        <el-alert v-if="step === 2" title="提交时将重新校验组织、IP 与端口占用，冲突时保留已填写内容。" type="info" :closable="false" />
        <p v-else class="muted">继续填写资产信息后进行校验与提交。</p>
      </template>
    </el-form>
    <template #footer><el-button v-if="step" @click="step--">上一步</el-button><el-button @click="emit('update:modelValue', false)">取消</el-button><el-button v-if="step === 2" type="primary" :loading="busy" @click="claim">校验并提交</el-button><el-button v-else type="primary" @click="next">继续</el-button></template>
  </el-dialog>
  <ComputerDialog v-model="computerOpen" :organization="organization" :terminal-boxes="boxes" :onus="onus" :initial-context="context" :initial-ip="initialIp" @saved="emit('saved')" />
  <AddCameraDialog v-model="cameraOpen" :organization="organization" :initial-unit-id="unit" :initial-context="context" @saved="emit('saved')" />
</template>
