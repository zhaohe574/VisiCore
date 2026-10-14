<script setup lang="ts">
import { reactive, watch } from 'vue'
import { CircleCheck, Connection, Cpu } from '@element-plus/icons-vue'
import { ElMessage, ElNotification } from 'element-plus'
import { ponApi, type OnuDeviceDto, type TerminalBoxDto } from '../../api'
import { useAction } from '../../composables/useAction'

const props = defineProps<{
  modelValue: boolean
  onu: OnuDeviceDto | null
  terminalBoxes: TerminalBoxDto[]
}>()

const emit = defineEmits<{
  (e: 'update:modelValue', value: boolean): void
  (e: 'saved', onu: OnuDeviceDto): void
}>()

const { busy, run } = useAction()

const form = reactive({
  alias: '',
  terminalBoxId: null as number | null,
  hasSubSwitch: false,
  subSwitchModel: '',
  subSwitchPorts: 8,
  portsConfig: ''
})

watch(
  () => props.modelValue,
  (visible) => {
    if (visible && props.onu) {
      form.alias = props.onu.alias || ''
      form.terminalBoxId = props.onu.terminalBoxId ?? null
      form.hasSubSwitch = props.onu.hasSubSwitch || false
      form.subSwitchModel = props.onu.subSwitchModel || ''
      form.subSwitchPorts = props.onu.subSwitchPorts || 8
      form.portsConfig = props.onu.portsConfig === '[]' ? '' : (props.onu.portsConfig || '')
    }
  }
)

async function submit() {
  if (!props.onu) return
  await run(async () => {
    const updated = await ponApi.updateOnu(props.onu!.id, {
      alias: form.alias.trim() || null,
      terminalBoxId: form.terminalBoxId,
      hasSubSwitch: form.hasSubSwitch,
      subSwitchModel: form.hasSubSwitch ? form.subSwitchModel.trim() || null : null,
      subSwitchPorts: form.hasSubSwitch ? Number(form.subSwitchPorts) || 8 : 0,
      portsConfig: form.portsConfig.trim() || null
    })
    if (updated.syncWarning) {
      ElNotification({
        title: '本地保存成功（网管同步受限）',
        message: updated.syncWarning,
        type: 'warning',
        duration: 10000
      })
    } else if (form.alias.trim() && form.alias.trim() === updated.description) {
      ElMessage.success('ONU 配置已保存，且别名已成功同步至华为网管！')
    } else {
      ElMessage.success('ONU 配置与终端箱归属已成功保存')
    }
    emit('saved', updated)
    emit('update:modelValue', false)
  }, '保存 ONU 配置')
}
</script>

<template>
  <el-dialog
    :model-value="modelValue"
    title="编辑 ONU 资产与承载拓扑配置"
    width="540px"
    destroy-on-close
    @update:model-value="emit('update:modelValue', $event)"
  >
    <el-form label-position="top" :disabled="busy">
      <div style="display: grid; grid-template-columns: 1fr 1fr; gap: 12px;">
        <el-form-item label="ONU 原始名称">
          <el-input :model-value="onu?.onuName" disabled />
        </el-form-item>
        <el-form-item label="设备型号 / SN">
          <el-input :model-value="`${onu?.terminalType} (${onu?.sn})`" disabled />
        </el-form-item>
      </div>

      <el-form-item label="业务别名（将自动同步至华为网管平台）">
        <el-input
          v-model="form.alias"
          placeholder="如：东门终端箱-主ONU"
          maxlength="100"
          clearable
        >
          <template #append v-if="onu?.alias">
            <el-tag v-if="onu.description && onu.alias === onu.description" type="success" size="small" effect="plain">网管已同步</el-tag>
            <el-tag v-else type="warning" size="small" effect="plain">网管未同步</el-tag>
          </template>
        </el-input>
      </el-form-item>

      <el-form-item label="归属网络终端箱">
        <el-select
          v-model="form.terminalBoxId"
          placeholder="未归属终端箱（留空解除）"
          clearable
          filterable
          style="width: 100%;"
        >
          <el-option
            v-for="box in terminalBoxes"
            :key="box.id"
            :value="box.id"
            :label="`${box.name} (${box.code})${box.location ? ' - ' + box.location : ''}`"
          />
        </el-select>
      </el-form-item>

      <!-- 下挂交换机扩展 -->
      <div style="background: #f8fafc; border: 1px solid var(--border); border-radius: var(--radius-md); padding: 14px; margin-top: 10px;">
        <div style="display: flex; justify-content: space-between; align-items: center; margin-bottom: 12px;">
          <div style="display: flex; align-items: center; gap: 6px; font-weight: 600; font-size: 13.5px; color: var(--text-primary);">
            <el-icon><Connection /></el-icon>
            <span>下挂扩展交换机（扩展摄像头接入端口）</span>
          </div>
          <el-switch v-model="form.hasSubSwitch" />
        </div>

        <template v-if="form.hasSubSwitch">
          <div style="display: grid; grid-template-columns: 1.5fr 1fr; gap: 12px;">
            <el-form-item label="交换机型号" style="margin-bottom: 8px;">
              <el-input
                v-model="form.subSwitchModel"
                placeholder="如：TP-LINK TL-SG1008D"
                maxlength="50"
              />
            </el-form-item>
            <el-form-item label="交换机总口数" style="margin-bottom: 8px;">
              <el-input-number
                v-model="form.subSwitchPorts"
                :min="4"
                :max="48"
                controls-position="right"
                style="width: 100%;"
              />
            </el-form-item>
          </div>
        </template>
      </div>

      <el-form-item label="走线与端口规划说明" style="margin-top: 14px;">
        <el-input
          v-model="form.portsConfig"
          type="textarea"
          :rows="2"
          maxlength="300"
          show-word-limit
          placeholder="如：LAN1-2挂载车间东门高空枪机，LAN3接下挂交换机..."
        />
      </el-form-item>
    </el-form>

    <template #footer>
      <el-button :disabled="busy" @click="emit('update:modelValue', false)">取消</el-button>
      <el-button type="primary" :loading="busy" @click="submit">
        <el-icon style="margin-right: 4px;"><CircleCheck /></el-icon>
        保存并同步
      </el-button>
    </template>
  </el-dialog>
</template>
