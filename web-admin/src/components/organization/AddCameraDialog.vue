<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { CircleCheck, Connection, Cpu, InfoFilled, VideoCamera } from '@element-plus/icons-vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import {
  managementApi,
  ponApi,
  type AddCameraRequest,
  type Channel,
  type NvrAvailableChannelDto,
  type OnuDeviceDto,
  type Organization,
  type TerminalBoxDto
} from '../../api'
import { buildUnitCascaderOptions } from '../../lib/organization'
import { useAction } from '../../composables/useAction'

const props = defineProps<{
  modelValue: boolean
  organization: Organization
  initialUnitId?: number | null
  initialContext?: { terminalBoxId?: number | null; onuDeviceId?: number | null; portNumber?: number | null; isSwitchPort?: boolean }
}>()

const emit = defineEmits<{
  (e: 'update:modelValue', value: boolean): void
  (e: 'saved', channel: Channel): void
}>()

const { busy, run } = useAction()

const loadingNvr = ref(false)
const nvrList = ref<NvrAvailableChannelDto[]>([])
const terminalBoxes = ref<TerminalBoxDto[]>([])
const boxOnus = ref<OnuDeviceDto[]>([])
const loadingBoxOnus = ref(false)

// 表单状态
const form = ref<AddCameraRequest>({
  nvrDeviceId: 0,
  channelNumber: null,
  alias: '',
  name: '',
  ipcHost: '',
  ipcPort: 8000,
  ipcUsername: 'admin',
  ipcPassword: '',
  protocolType: 'HIKVISION',
  unitId: null,
  terminalBoxId: null,
  onuDeviceId: null,
  portNumber: null,
  isSwitchPort: false,
  portLabel: ''
})

const unitCascaderOptions = computed(() => buildUnitCascaderOptions(props.organization))

// 当前选中的 NVR
const selectedNvr = computed(() => nvrList.value.find(n => n.deviceId === form.value.nvrDeviceId))

// 是否为中维录像机
const isJovisionNvr = computed(() => {
  const nvr = selectedNvr.value
  if (!nvr) return false
  return nvr.pluginId === 'jovision'
    || nvr.port === 18320
    || (nvr.model && nvr.model.toUpperCase().includes('JVS'))
    || (nvr.deviceName && (nvr.deviceName.includes('中维') || nvr.deviceName.includes('操作台')))
})

// 动态协议选项
const protocolOptions = computed(() => {
  if (isJovisionNvr.value) {
    return [
      { label: '中维私有协议 (JOVISION)', value: 'JOVISION', defaultPort: 9101 },
      { label: '通用标准 (ONVIF)', value: 'ONVIF', defaultPort: 80 }
    ]
  }
  return [
    { label: '海康私有协议 (HIKVISION)', value: 'HIKVISION', defaultPort: 8000 },
    { label: '通用标准 (ONVIF)', value: 'ONVIF', defaultPort: 80 }
  ]
})

function onProtocolChange(proto: string) {
  if (proto === 'JOVISION') {
    form.value.ipcPort = 9101
  } else if (proto === 'HIKVISION') {
    form.value.ipcPort = 8000
  } else if (proto === 'ONVIF') {
    form.value.ipcPort = 80
  }
}

// 当前选中的 ONU
const selectedOnu = computed(() => boxOnus.value.find(o => o.id === form.value.onuDeviceId))

// 当前选中的终端箱及其业务归属
const selectedBox = computed(() => terminalBoxes.value.find(b => b.id === form.value.terminalBoxId))
const selectedBoxUnitName = computed(() => selectedBox.value?.unitName || '')

// 端口选项
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

watch(
  () => props.modelValue,
  async (visible) => {
    if (visible) {
      resetForm()
      await loadInitialData()
      if (props.initialContext?.terminalBoxId) {
        form.value.terminalBoxId = props.initialContext.terminalBoxId
        await loadBoxOnus(form.value.terminalBoxId)
        if (props.initialContext.onuDeviceId) onOnuChange(props.initialContext.onuDeviceId)
        form.value.portNumber = props.initialContext.portNumber ?? 1
        form.value.isSwitchPort = props.initialContext.isSwitchPort ?? false
      }
    }
  }
)

function resetForm() {
  boxOnus.value = []
  const firstNvr = nvrList.value.find(n => n.deviceId === form.value.nvrDeviceId) || nvrList.value[0]
  const isJov = firstNvr ? (firstNvr.pluginId === 'jovision' || firstNvr.port === 18320 || (firstNvr.model && firstNvr.model.toUpperCase().includes('JVS')) || (firstNvr.deviceName && (firstNvr.deviceName.includes('中维') || firstNvr.deviceName.includes('操作台')))) : false
  form.value = {
    nvrDeviceId: 0,
    channelNumber: null,
    alias: '',
    name: '',
    ipcHost: '',
    ipcPort: isJov ? 9101 : 8000,
    ipcUsername: 'admin',
    ipcPassword: '',
    protocolType: isJov ? 'JOVISION' : 'HIKVISION',
    unitId: props.initialUnitId ?? null,
    terminalBoxId: null,
    onuDeviceId: null,
    portNumber: null,
    isSwitchPort: false,
    portLabel: ''
  }
}

async function loadInitialData() {
  loadingNvr.value = true
  try {
    const [nvrs, boxes] = await Promise.all([
      managementApi.availableNvrChannels(),
      ponApi.terminalBoxes().catch(() => [] as TerminalBoxDto[])
    ])
    nvrList.value = nvrs
    terminalBoxes.value = boxes
    boxOnus.value = []

    // 默认选择第一个有空闲通道的 NVR
    const candidateNvr = nvrs.find(n => n.freeChannels > 0) || nvrs[0]
    if (candidateNvr) {
      form.value.nvrDeviceId = candidateNvr.deviceId
      onNvrChange(candidateNvr.deviceId)
    }
  } catch (err: any) {
    ElMessage.error(err.message || '加载录像机及网络资产信息失败')
  } finally {
    loadingNvr.value = false
  }
}

function onNvrChange(deviceId: number) {
  const nvr = nvrList.value.find(n => n.deviceId === deviceId)
  if (nvr && nvr.availableChannelNumbers.length > 0) {
    form.value.channelNumber = nvr.availableChannelNumbers[0]
  } else {
    form.value.channelNumber = null
  }
  const isJov = nvr ? (nvr.pluginId === 'jovision' || nvr.port === 18320 || (nvr.model && nvr.model.toUpperCase().includes('JVS')) || (nvr.deviceName && (nvr.deviceName.includes('中维') || nvr.deviceName.includes('操作台')))) : false
  if (isJov) {
    if (form.value.protocolType === 'HIKVISION') {
      form.value.protocolType = 'JOVISION'
      form.value.ipcPort = 9101
    }
  } else {
    if (form.value.protocolType === 'JOVISION') {
      form.value.protocolType = 'HIKVISION'
      form.value.ipcPort = 8000
    }
  }
}

async function loadBoxOnus(boxId: number | null) {
  if (!boxId) {
    boxOnus.value = []
    form.value.onuDeviceId = null
    form.value.portNumber = null
    form.value.portLabel = ''
    form.value.isSwitchPort = false
    return
  }
  loadingBoxOnus.value = true
  try {
    const res = await ponApi.onus({ terminalBoxId: boxId, pageSize: 100 })
    boxOnus.value = res.items
    if (res.items.length === 1) {
      form.value.onuDeviceId = res.items[0].id
      onOnuChange(res.items[0].id)
    } else if (form.value.onuDeviceId && res.items.some(o => o.id === form.value.onuDeviceId)) {
      onOnuChange(form.value.onuDeviceId)
    } else if (res.items.length > 0) {
      form.value.onuDeviceId = res.items[0].id
      onOnuChange(res.items[0].id)
    } else {
      form.value.onuDeviceId = null
      form.value.portNumber = null
      form.value.portLabel = ''
      form.value.isSwitchPort = false
    }
  } catch {
    boxOnus.value = []
  } finally {
    loadingBoxOnus.value = false
  }
}

function onBoxChange(boxId?: number | null) {
  form.value.terminalBoxId = boxId ?? null
  if (boxId) {
    const box = terminalBoxes.value.find(b => b.id === boxId)
    if (box?.unitId) {
      form.value.unitId = box.unitId
    }
  }
  void loadBoxOnus(boxId ?? null)
}

function onOnuChange(onuId?: number | null) {
  form.value.onuDeviceId = onuId ?? null
  const onu = boxOnus.value.find(o => o.id === onuId)
  if (onu) {
    if (onu.terminalBoxId && (!form.value.terminalBoxId || form.value.terminalBoxId !== onu.terminalBoxId)) {
      form.value.terminalBoxId = onu.terminalBoxId
      const box = terminalBoxes.value.find(b => b.id === onu.terminalBoxId)
      if (box?.unitId) {
        form.value.unitId = box.unitId
      }
    }
    form.value.isSwitchPort = onu.hasSubSwitch
    form.value.portNumber = 1
    form.value.portLabel = onu.hasSubSwitch ? '交换机 #1 口' : 'LAN1'
  }
}

function onPortSelect(portVal: number) {
  form.value.portNumber = portVal
  const opt = portOptions.value.find(o => o.value === portVal)
  if (opt) {
    form.value.isSwitchPort = opt.isSwitch
    form.value.portLabel = opt.label
  }
}

async function submit() {
  if (!form.value.nvrDeviceId) {
    ElMessage.warning('请选择目标录像机')
    return
  }

  if (isJovisionNvr.value) {
    ElMessageBox.alert(
      '中维世纪录像机（NVR）不支持通过平台远程自动向录像机添加数字通道。\n\n请按以下指引操作：\n1. 请先在中维录像机本地界面或客户端中完成摄像头【' + (form.value.ipcHost || '目标IP') + '】的搜索绑定；\n2. 绑定完成后，回到平台【设备管理】页面，点击该录像机的【同步】按钮；\n3. 平台将自动探测该摄像头上线、同步点位画面并呈现在监控树中。',
      '中维录像机接入指引',
      { type: 'warning', confirmButtonText: '知道了' }
    )
    return
  }

  const pointAlias = (form.value.alias || form.value.name || '').trim()
  if (!pointAlias) {
    ElMessage.warning('请输入通道别名（点位名称）')
    return
  }
  if (!form.value.ipcHost.trim()) {
    ElMessage.warning('请输入摄像机 IP 地址')
    return
  }
  if (!form.value.ipcPassword) {
    ElMessage.warning('请输入摄像机访问密码')
    return
  }

  await run(async () => {
    const created = await managementApi.addCamera({
      ...form.value,
      alias: pointAlias,
      name: '', // 设备原始名称留空，由系统及录像机自动探测
      ipcHost: form.value.ipcHost.trim(),
      ipcUsername: (form.value.ipcUsername || 'admin').trim(),
      portLabel: form.value.portLabel?.trim() || null
    })
    ElMessage.success(`摄像头【${created.alias || created.name}】已成功添加至录像机并挂载！`)
    emit('saved', created)
    emit('update:modelValue', false)
  }, '摄像机添加完成')
}
</script>

<template>
  <el-dialog
    :model-value="modelValue"
    title="新增摄像头（直接对接设备与网络）"
    width="680px"
    destroy-on-close
    :close-on-click-modal="false"
    @update:model-value="emit('update:modelValue', $event)"
  >
    <el-alert
      v-if="isJovisionNvr"
      type="warning"
      show-icon
      :closable="false"
      style="margin-bottom: 16px;"
      title="中维录像机通道接入提示"
      description="中维世纪录像机暂不支持通过平台远程下发添加通道指令。请先在中维录像机本地界面或客户端完成摄像头的搜索绑定；绑定完成后，在【设备管理】点击对应录像机的【同步】，视枢将自动探测识别并挂载上线。"
    />
    <el-alert
      v-else
      type="info"
      show-icon
      :closable="false"
      style="margin-bottom: 16px;"
      title="视枢将直接向目标海康录像机（NVR）下发添加数字通道指令，并自动在系统建立监控通道、划拨业务组织及绑定光网承载链路，无需登入 NVR Web 端。"
    />

    <el-form label-position="top" :disabled="busy">
      <!-- 1. 录像机与通道槽位选择 -->
      <div class="form-section-title">
        <el-icon><VideoCamera /></el-icon>
        <span>1. 目标录像机与空闲槽位分配</span>
      </div>

      <div style="display: grid; grid-template-columns: 1.5fr 1fr; gap: 12px;">
        <el-form-item label="目标录像机 (NVR)" required>
          <el-select
            v-model="form.nvrDeviceId"
            placeholder="请选择录像机"
            style="width: 100%;"
            :loading="loadingNvr"
            @change="onNvrChange"
          >
            <el-option
              v-for="nvr in nvrList"
              :key="nvr.deviceId"
              :value="nvr.deviceId"
              :label="`${nvr.deviceName} (${nvr.host}) · 剩余 ${nvr.freeChannels} 空闲通道`"
            >
              <div style="display: flex; justify-content: space-between; align-items: center;">
                <span>{{ nvr.deviceName }} <small class="muted">({{ nvr.host }})</small></span>
                <el-tag size="small" :type="nvr.freeChannels > 0 ? 'success' : 'danger'">
                  {{ nvr.freeChannels > 0 ? `空闲 ${nvr.freeChannels} 槽` : '槽位已满' }}
                </el-tag>
              </div>
            </el-option>
          </el-select>
        </el-form-item>

        <el-form-item label="分配数字通道号" required>
          <el-select
            v-model="form.channelNumber"
            placeholder="选择通道号"
            filterable
            style="width: 100%;"
            :disabled="!selectedNvr || selectedNvr.availableChannelNumbers.length === 0"
          >
            <el-option
              v-for="chNum in (selectedNvr?.availableChannelNumbers || [])"
              :key="chNum"
              :value="chNum"
              :label="`D${chNum} (空闲可用)`"
            />
          </el-select>
        </el-form-item>
      </div>

      <!-- 2. IPC 摄像机网络与认证参数 -->
      <div class="form-section-title" style="margin-top: 14px;">
        <el-icon><Cpu /></el-icon>
        <span>2. 摄像机 (IPC) 网络与登录凭据</span>
      </div>

      <div style="display: grid; grid-template-columns: 1.5fr 1fr; gap: 12px;">
        <el-form-item label="通道别名（点位名称）" required>
          <el-input
            v-model="form.alias"
            placeholder="如：铝管段114号 / 1号车间东门高空枪机"
            maxlength="100"
            clearable
          />
          <div class="form-item-tip">
            用于业务呈现与检索的点位别名；设备原始名称将由录像机自动探测填入。
          </div>
        </el-form-item>

        <el-form-item label="接入协议" required>
          <el-select v-model="form.protocolType" style="width: 100%;" @change="onProtocolChange">
            <el-option
              v-for="opt in protocolOptions"
              :key="opt.value"
              :label="opt.label"
              :value="opt.value"
            />
          </el-select>
          <div class="form-item-tip">
            <template v-if="isJovisionNvr">
              接入中维品牌摄像头请选【中维私有协议 (默认 9101)】；接入海康/大华等其他品牌摄像头请选【通用标准 ONVIF (默认 80)】。
            </template>
            <template v-else>
              接入海康品牌摄像头请选【海康私有协议 (默认 8000)】；接入第三方品牌摄像头请选【通用标准 ONVIF (默认 80)】。
            </template>
          </div>
        </el-form-item>
      </div>

      <div style="display: grid; grid-template-columns: 2fr 1fr; gap: 12px;">
        <el-form-item label="摄像机 IP 地址" required>
          <el-input
            v-model="form.ipcHost"
            placeholder="如：10.37.10.88"
            maxlength="253"
            clearable
          />
        </el-form-item>

        <el-form-item label="服务端口" required>
          <el-input-number
            v-model="form.ipcPort"
            :min="1"
            :max="65535"
            controls-position="right"
            style="width: 100%;"
          />
        </el-form-item>
      </div>

      <div style="display: grid; grid-template-columns: 1fr 1fr; gap: 12px;">
        <el-form-item label="访问用户名" required>
          <el-input
            v-model="form.ipcUsername"
            placeholder="默认 admin"
            maxlength="64"
          />
        </el-form-item>

        <el-form-item label="访问密码" required>
          <el-input
            v-model="form.ipcPassword"
            type="password"
            show-password
            placeholder="摄像机激活密码"
            maxlength="128"
          />
        </el-form-item>
      </div>

      <!-- 3. 组织划拨与光网物理链路绑定 -->
      <div class="form-section-title" style="margin-top: 14px;">
        <el-icon><Connection /></el-icon>
        <span>3. 业务空间归属与光网链路承载（可选）</span>
      </div>

      <el-form-item label="划拨业务组织单元">
        <el-cascader
          v-model="form.unitId"
          :options="unitCascaderOptions"
          :props="{ emitPath: false, checkStrictly: false }"
          clearable
          filterable
          placeholder="选择挂载的车间/区域/单元（选填，选择终端箱可自动联动）"
          style="width: 100%;"
        />
        <div class="form-item-tip">
          选择下方的“承载网络终端箱”将自动联动带入其所在业务单元，也可在此手动指定或按需调整。
        </div>
      </el-form-item>

      <div style="display: grid; grid-template-columns: 1fr 1fr; gap: 12px;">
        <el-form-item label="承载网络终端箱">
          <el-select
            v-model="form.terminalBoxId"
            placeholder="请先选择物理网络终端箱"
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
          <div v-if="selectedBoxUnitName && form.unitId === selectedBox?.unitId" class="box-unit-link-tip">
            <el-icon><Connection /></el-icon>
            <span>已联动划拨至终端箱归属业务单元：<strong>{{ selectedBoxUnitName }}</strong></span>
          </div>
        </el-form-item>

        <el-form-item label="承载 ONU 设备">
          <el-select
            v-model="form.onuDeviceId"
            :placeholder="!form.terminalBoxId ? '请先选择左侧终端箱' : (boxOnus.length ? '选择箱内 ONU' : '该终端箱内暂无关联 ONU')"
            :disabled="!form.terminalBoxId || boxOnus.length === 0"
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

      <!-- 当所选终端箱内无 ONU 时的温馨提示 -->
      <div v-if="form.terminalBoxId && boxOnus.length === 0 && !loadingBoxOnus" style="margin-top: -6px; margin-bottom: 12px; padding: 8px 12px; background: #fffbeb; border: 1px solid #fef3c7; border-radius: var(--radius-sm); font-size: 12px; color: #b45309; display: flex; align-items: center; gap: 6px;">
        <el-icon><InfoFilled /></el-icon>
        <span>当前所选终端箱内尚未关联任何 ONU。请前往【光网与终端箱】页面为终端箱划定归属的 ONU 资产。</span>
      </div>

      <div v-if="selectedOnu" style="display: grid; grid-template-columns: 1fr 1fr; gap: 12px; background: #f8fafc; padding: 10px 12px; border-radius: var(--radius-sm); border: 1px dashed var(--border);">
        <el-form-item label="接入端口序号" style="margin-bottom: 0;">
          <el-select
            :model-value="form.portNumber"
            placeholder="选择接入端口"
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
            v-model="form.portLabel"
            placeholder="如：交换机 #1 口 或 LAN1"
            maxlength="50"
          />
        </el-form-item>
      </div>
    </el-form>

    <template #footer>
      <el-button :disabled="busy" @click="emit('update:modelValue', false)">取消</el-button>
      <el-button type="primary" :loading="busy" @click="submit">
        <el-icon style="margin-right: 4px;"><CircleCheck /></el-icon>
        直接添加到录像机并建档
      </el-button>
    </template>
  </el-dialog>
</template>

<style scoped>
.form-section-title {
  display: flex;
  align-items: center;
  gap: 6px;
  font-size: 13.5px;
  font-weight: 600;
  color: var(--text-primary);
  padding-bottom: 6px;
  border-bottom: 1px solid var(--border);
  margin-bottom: 12px;
}
.muted {
  color: var(--text-muted, #64748b);
}
.form-item-tip {
  font-size: 11.5px;
  color: var(--el-text-color-secondary, #909399);
  line-height: 1.35;
  margin-top: 4px;
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
