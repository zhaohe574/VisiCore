<script setup lang="ts">
import { computed, reactive, ref, watch } from 'vue'
import {
  managementApi,
  ponApi,
  type Channel,
  type OnuDeviceDto,
  type Organization,
  type TerminalBoxDto
} from '../../api'
import { buildUnitCascaderOptions, getUnitArea, getUnitName } from '../../lib/organization'
import { useAction } from '../../composables/useAction'
import { Connection, InfoFilled } from '@element-plus/icons-vue'

const props = defineProps<{
  modelValue: boolean
  channel: Channel | null
  organization: Organization
}>()

const emit = defineEmits<{
  (e: 'update:modelValue', value: boolean): void
  (e: 'saved', updated: Channel): void
}>()

const { busy, run } = useAction()

const terminalBoxes = ref<TerminalBoxDto[]>([])
const boxOnus = ref<OnuDeviceDto[]>([])
const loadingBoxOnus = ref(false)

const editForm = reactive({
  alias: '',
  unitId: null as number | null,
  ip: '',
  username: '',
  password: '',
  remark: '',
  terminalBoxId: null as number | null,
  onuDeviceId: null as number | null,
  networkPort: null as number | null,
  isSwitchPort: false,
  portLabel: ''
})

const unitCascaderOptions = computed(() => buildUnitCascaderOptions(props.organization))

const selectedOnu = computed(() => boxOnus.value.find(o => o.id === editForm.onuDeviceId))
const selectedBox = computed(() => terminalBoxes.value.find(b => b.id === editForm.terminalBoxId))
const selectedBoxUnitName = computed(() => selectedBox.value?.unitName || '')

// 端口选择列表
const portOptions = computed(() => {
  if (!selectedOnu.value) return []
  if (selectedOnu.value.hasSubSwitch && selectedOnu.value.subSwitchPorts > 0) {
    return Array.from({ length: selectedOnu.value.subSwitchPorts }, (_, i) => ({
      label: `交换机 #${i + 1} 口`,
      value: i + 1,
      isSwitch: true
    }))
  }
  return [1, 2, 3, 4].map(p => ({
    label: `LAN${p}`,
    value: p,
    isSwitch: false
  }))
})

async function loadBoxOnus(boxId: number | null, keepSelectedOnu = true) {
  if (!boxId) {
    boxOnus.value = []
    editForm.onuDeviceId = null
    editForm.networkPort = null
    editForm.portLabel = ''
    editForm.isSwitchPort = false
    return
  }
  loadingBoxOnus.value = true
  try {
    const res = await ponApi.onus({ terminalBoxId: boxId, pageSize: 100 })
    boxOnus.value = res.items
    
    // 如果已有选中的 ONU 且属于该箱，保留并刷新其详情；否则自动选中第 1 个 ONU
    if (keepSelectedOnu && editForm.onuDeviceId && res.items.some(o => o.id === editForm.onuDeviceId)) {
      onOnuChange(editForm.onuDeviceId)
    } else if (res.items.length === 1) {
      editForm.onuDeviceId = res.items[0].id
      onOnuChange(res.items[0].id)
    } else if (!res.items.some(o => o.id === editForm.onuDeviceId)) {
      if (res.items.length > 0) {
        editForm.onuDeviceId = res.items[0].id
        onOnuChange(res.items[0].id)
      } else {
        editForm.onuDeviceId = null
        editForm.networkPort = null
        editForm.portLabel = ''
        editForm.isSwitchPort = false
      }
    }
  } catch {
    boxOnus.value = []
  } finally {
    loadingBoxOnus.value = false
  }
}

watch(
  () => props.modelValue,
  (visible) => {
    if (visible && props.channel) {
      editForm.alias = props.channel.alias || ''
      editForm.unitId = props.channel.unitId ?? null
      editForm.ip = props.channel.ip || ''
      editForm.username = props.channel.username || ''
      editForm.password = ''
      editForm.remark = props.channel.remark || ''
      editForm.terminalBoxId = props.channel.terminalBoxId ?? null
      editForm.onuDeviceId = props.channel.onuDeviceId ?? null
      editForm.networkPort = props.channel.networkPort ?? null
      editForm.isSwitchPort = props.channel.isSwitchPort || false
      editForm.portLabel = props.channel.portLabel || ''

      void ponApi.terminalBoxes().then(b => { terminalBoxes.value = b }).catch(() => {})
      if (editForm.terminalBoxId) {
        void loadBoxOnus(editForm.terminalBoxId, true)
      } else if (editForm.onuDeviceId) {
        void ponApi.onu(editForm.onuDeviceId).then(onu => {
          if (onu?.terminalBoxId) {
            editForm.terminalBoxId = onu.terminalBoxId
            void loadBoxOnus(onu.terminalBoxId, true)
          }
        }).catch(() => {})
      } else {
        boxOnus.value = []
      }
    }
  }
)

function onBoxChange(boxId?: number | null) {
  editForm.terminalBoxId = boxId ?? null
  if (boxId) {
    const box = terminalBoxes.value.find(b => b.id === boxId)
    if (box?.unitId) {
      editForm.unitId = box.unitId
    }
  }
  void loadBoxOnus(boxId ?? null, false)
}

function onOnuChange(onuId?: number | null) {
  editForm.onuDeviceId = onuId ?? null
  const onu = boxOnus.value.find(o => o.id === onuId)
  if (onu) {
    if (onu.terminalBoxId && !editForm.terminalBoxId) {
      editForm.terminalBoxId = onu.terminalBoxId
      const box = terminalBoxes.value.find(b => b.id === onu.terminalBoxId)
      if (box?.unitId) {
        editForm.unitId = box.unitId
      }
    }
    editForm.isSwitchPort = onu.hasSubSwitch
    if (!editForm.networkPort) {
      editForm.networkPort = 1
      editForm.portLabel = onu.hasSubSwitch ? '交换机 #1 口' : 'LAN1'
    }
  }
}

function onPortSelect(portVal: number) {
  editForm.networkPort = portVal
  const opt = portOptions.value.find(o => o.value === portVal)
  if (opt) {
    editForm.isSwitchPort = opt.isSwitch
    editForm.portLabel = opt.label
  }
}

async function save() {
  if (!props.channel) return
  const success = await run(async () => {
    const updated = await managementApi.updateChannel(props.channel!.id, {
      alias: editForm.alias.trim(),
      ip: editForm.ip.trim() || null,
      username: editForm.username.trim() || null,
      password: editForm.password ? editForm.password.trim() : null,
      remark: editForm.remark.trim(),
      terminalBoxId: editForm.terminalBoxId,
      onuDeviceId: editForm.onuDeviceId,
      networkPort: editForm.networkPort,
      isSwitchPort: editForm.isSwitchPort,
      portLabel: editForm.portLabel.trim() || null,
      clearNetworkBinding: !editForm.terminalBoxId && !editForm.onuDeviceId && Boolean(props.channel?.terminalBoxId || props.channel?.onuDeviceId)
    })
    if (editForm.unitId !== (props.channel!.unitId ?? null)) {
      await managementApi.assign([props.channel!.id], editForm.unitId ?? null)
      updated.unitId = editForm.unitId ?? null
    }
    emit('saved', updated)
  }, '通道配置已成功保存')

  if (success) {
    emit('update:modelValue', false)
  }
}
</script>

<template>
  <el-dialog
    :model-value="modelValue"
    title="编辑通道信息与网络绑定"
    width="560px"
    destroy-on-close
    @update:model-value="emit('update:modelValue', $event)"
  >
    <el-form label-position="top">
      <div style="display: grid; grid-template-columns: 1fr 1fr; gap: 12px;">
        <el-form-item label="通道原名">
          <el-input :model-value="channel?.name" disabled />
        </el-form-item>
        <el-form-item label="归属设备">
          <el-input :model-value="channel?.deviceName" disabled />
        </el-form-item>
      </div>

      <el-form-item label="通道业务别名">
        <el-input
          v-model="editForm.alias"
          maxlength="100"
          clearable
          placeholder="如：东门入口高清（留空恢复原名）"
        />
      </el-form-item>

      <el-form-item label="划拨业务单元">
        <el-cascader
          v-model="editForm.unitId"
          :options="unitCascaderOptions"
          :props="{ emitPath: false, checkStrictly: false }"
          clearable
          filterable
          placeholder="未划拨（支持车间下钻，留空解除划拨）"
          style="width: 100%;"
        />
        <div v-if="editForm.unitId" style="margin-top: 6px; font-size: 12px; display: flex; gap: 16px;" class="muted">
          <span>归属区域: <strong style="color: var(--text-primary);">{{ getUnitArea(organization, editForm.unitId)?.name || '—' }}</strong></span>
          <span>划拨单元: <strong style="color: var(--text-primary);">{{ getUnitName(organization, editForm.unitId) }}</strong></span>
        </div>
      </el-form-item>

      <!-- 光网承载链路与终端箱绑定 -->
      <div style="background: #f8fafc; border: 1px solid var(--border); border-radius: var(--radius-md); padding: 12px 14px; margin-bottom: 16px;">
        <div style="font-weight: 600; font-size: 13px; color: var(--text-primary); margin-bottom: 10px; display: flex; align-items: center; gap: 6px;">
          <el-icon><Connection /></el-icon>
          <span>光网物理承载链路（终端箱 / ONU / 端口）</span>
        </div>

        <div style="display: grid; grid-template-columns: 1fr 1fr; gap: 10px;">
          <el-form-item label="承载网络终端箱" style="margin-bottom: 10px;">
            <el-select
              v-model="editForm.terminalBoxId"
              placeholder="请先选择终端箱"
              clearable
              filterable
              style="width: 100%;"
              @change="onBoxChange"
            >
              <el-option
                v-for="b in terminalBoxes"
                :key="b.id"
                :value="b.id"
                :label="b.unitName ? `${b.name} (${b.code}) · 归属: ${b.unitName}` : `${b.name} (${b.code})`"
              >
                <div style="display: flex; justify-content: space-between; align-items: center; width: 100%;">
                  <span>
                    <strong>{{ b.name }}</strong>
                    <span class="muted" style="margin-left: 6px; font-family: 'JetBrains Mono', monospace; font-size: 11.5px;">({{ b.code }})</span>
                  </span>
                  <el-tag v-if="b.unitName" size="small" type="info" effect="plain" style="margin-left: 8px;">
                    {{ b.unitName }}
                  </el-tag>
                </div>
              </el-option>
            </el-select>
            <div v-if="selectedBoxUnitName && editForm.unitId === selectedBox?.unitId" class="box-unit-link-tip">
              <el-icon><Connection /></el-icon>
              <span>已联动划拨至终端箱归属业务单元：<strong>{{ selectedBoxUnitName }}</strong></span>
            </div>
          </el-form-item>

          <el-form-item label="承载 ONU 设备" style="margin-bottom: 10px;">
            <el-select
              v-model="editForm.onuDeviceId"
              :placeholder="!editForm.terminalBoxId ? '请先选择左侧终端箱' : (boxOnus.length ? '选择箱内承载 ONU' : '该终端箱暂无关联 ONU')"
              :disabled="!editForm.terminalBoxId || boxOnus.length === 0"
              :loading="loadingBoxOnus"
              clearable
              filterable
              no-data-text="该终端箱内暂未配置/关联 ONU 设备"
              style="width: 100%;"
              @change="onOnuChange"
            >
              <el-option
                v-for="o in boxOnus"
                :key="o.id"
                :value="o.id"
                :label="`${o.alias || o.onuName} · ${o.terminalType} (${o.status === 'online' || o.status === '1' ? '在线' : '离线'})`"
              >
                <div style="display: flex; justify-content: space-between; align-items: center; width: 100%;">
                  <div>
                    <span style="font-weight: 500;">{{ o.alias ? `${o.alias} (${o.onuName})` : o.onuName }}</span>
                    <small class="muted" style="margin-left: 6px;">({{ o.terminalType }})</small>
                    <div v-if="o.description" style="font-size: 11px; color: #94a3b8; line-height: 1.2;">{{ o.description }}</div>
                  </div>
                  <el-tag size="small" :type="o.status === 'online' || o.status === '1' ? 'success' : 'info'">
                    {{ o.status === 'online' || o.status === '1' ? '在线' : '离线' }}
                  </el-tag>
                </div>
              </el-option>
            </el-select>
          </el-form-item>
        </div>

        <!-- 当所选终端箱内无关联 ONU 时的提示 -->
        <div v-if="editForm.terminalBoxId && boxOnus.length === 0 && !loadingBoxOnus" style="margin-top: -4px; margin-bottom: 10px; padding: 6px 10px; background: #fffbeb; border: 1px solid #fef3c7; border-radius: var(--radius-sm); font-size: 12px; color: #b45309; display: flex; align-items: center; gap: 6px;">
          <el-icon><InfoFilled /></el-icon>
          <span>当前终端箱内未关联 ONU，请先在【光网与终端箱】页面划分归属 ONU 资产。</span>
        </div>

        <div v-if="selectedOnu" style="display: grid; grid-template-columns: 1fr 1fr; gap: 10px;">
          <el-form-item label="接入端口序号" style="margin-bottom: 0;">
            <el-select
              :model-value="editForm.networkPort"
              placeholder="选择端口"
              style="width: 100%;"
              @change="onPortSelect"
            >
              <el-option
                v-for="p in portOptions"
                :key="p.value"
                :value="p.value"
                :label="p.label"
              />
            </el-select>
          </el-form-item>

          <el-form-item label="端口标签备注" style="margin-bottom: 0;">
            <el-input
              v-model="editForm.portLabel"
              placeholder="如：交换机 #1 口"
              maxlength="50"
            />
          </el-form-item>
        </div>
      </div>

      <div style="display: grid; grid-template-columns: 1fr 1fr; gap: 12px;">
        <el-form-item label="通道独立 IP">
          <el-input
            v-model="editForm.ip"
            maxlength="253"
            clearable
            placeholder="未单独配置则保持滞空"
          />
        </el-form-item>

        <el-form-item label="访问账号">
          <el-input
            v-model="editForm.username"
            maxlength="128"
            clearable
            placeholder="未单独配置则保持滞空"
          />
        </el-form-item>
      </div>

      <el-form-item label="访问密码">
        <el-input
          v-model="editForm.password"
          type="password"
          show-password
          maxlength="256"
          clearable
          placeholder="未单独配置则保持滞空"
        />
      </el-form-item>

      <el-form-item label="备注说明">
        <el-input
          v-model="editForm.remark"
          type="textarea"
          :rows="2"
          maxlength="500"
          show-word-limit
          placeholder="可填写点位物理位置、用途说明、维护信息等..."
        />
      </el-form-item>
    </el-form>
    <template #footer>
      <el-button @click="emit('update:modelValue', false)">取消</el-button>
      <el-button type="primary" :loading="busy" @click="save">保存通道信息</el-button>
    </template>
  </el-dialog>
</template>

<style scoped>
.muted {
  color: var(--text-muted, #64748b);
}
.box-unit-link-tip {
  font-size: 11.5px;
  color: var(--primary, #1976d2);
  margin-top: 5px;
  display: flex;
  align-items: center;
  gap: 4px;
  line-height: 1.35;
  background: #eff6ff;
  padding: 3px 8px;
  border-radius: 4px;
  border: 1px solid #dbeafe;
}
</style>
