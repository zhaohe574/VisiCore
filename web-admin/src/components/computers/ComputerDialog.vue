<script setup lang="ts">
import { computed, reactive, ref, watch, onBeforeUnmount } from 'vue'
import { Connection, Cpu, Monitor, Search } from '@element-plus/icons-vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import {
  computerApi,
  ipPoolApi,
  type Computer,
  type ComputerInput,
  type IpConflictCheckResultDto,
  type OnuDeviceDto,
  type Organization,
  type TerminalBoxDto
} from '../../api'
import { buildUnitCascaderOptions } from '../../lib/organization'
import { useAction } from '../../composables/useAction'

const props = defineProps<{
  modelValue: boolean
  computer?: Computer | null
  organization: Organization
  terminalBoxes: TerminalBoxDto[]
  onus: OnuDeviceDto[]
  initialIp?: string | null
  initialContext?: Partial<ComputerInput>
}>()

const emit = defineEmits<{
  (e: 'update:modelValue', val: boolean): void
  (e: 'saved', comp?: Computer): void
}>()

const { busy, run } = useAction()

const form = reactive<ComputerInput & { unitCascade?: number | string | null }>({
  name: '',
  ip: '',
  mac: '',
  owner: '',
  department: '',
  os: 'Windows 10',
  location: '',
  workshopId: null,
  areaId: null,
  unitId: null,
  terminalBoxId: null,
  onuDeviceId: null,
  portNumber: 1,
  isSwitchPort: false,
  portLabel: '',
  remarks: '',
  unitCascade: null
})

const pingTesting = ref(false)
const pingResult = ref<{ success: boolean; rtt?: number | null } | null>(null)

const ipConflict = ref<IpConflictCheckResultDto | null>(null)
const checkingIp = ref(false)
const recommendingIp = ref(false)
let checkIpTimer: any = null

watch(
  () => form.ip,
  (val) => {
    if (checkIpTimer) clearTimeout(checkIpTimer)
    const ip = val?.trim()
    if (!ip || !/^(\d{1,3}\.){3}\d{1,3}$/.test(ip)) {
      ipConflict.value = null
      return
    }
    checkIpTimer = setTimeout(async () => {
      checkingIp.value = true
      try {
        const res = await ipPoolApi.checkIp(ip, 'computer', props.computer?.id)
        ipConflict.value = res.occupied ? res : null
      } catch {
        ipConflict.value = null
      } finally {
        checkingIp.value = false
      }
    }, 300)
  }
)

async function recommendFreeIp() {
  recommendingIp.value = true
  try {
    const res = await ipPoolApi.recommendIp(form.workshopId, null)
    if (res?.ip) {
      form.ip = res.ip
      ElMessage.success(`已推荐可用空闲 IP: ${res.ip} (${res.poolName})`)
    } else {
      ElMessage.warning('IP 池中暂无可用空闲 IP')
    }
  } catch (err: any) {
    ElMessage.error(err.message || '获取推荐 IP 失败')
  } finally {
    recommendingIp.value = false
  }
}

const cascaderOptions = computed(() => buildUnitCascaderOptions(props.organization))

// 过滤当前终端箱下的 ONU
const filteredOnus = computed(() => {
  if (!form.terminalBoxId) return props.onus
  return props.onus.filter(o => o.terminalBoxId === form.terminalBoxId)
})

watch(
  () => props.modelValue,
  (visible) => {
    if (visible) {
      pingResult.value = null
      if (props.computer) {
        form.name = props.computer.name || ''
        form.ip = props.computer.ip || ''
        form.mac = props.computer.mac || ''
        form.owner = props.computer.owner || ''
        form.department = props.computer.department || ''
        form.os = props.computer.os || 'Windows 10'
        form.location = props.computer.location || ''
        form.workshopId = props.computer.workshopId ?? null
        form.areaId = props.computer.areaId ?? null
        form.unitId = props.computer.unitId ?? null
        form.unitCascade = props.computer.unitId ?? null
        form.terminalBoxId = props.computer.terminalBoxId ?? null
        form.onuDeviceId = props.computer.onuDeviceId ?? null
        form.portNumber = props.computer.portNumber || 1
        form.isSwitchPort = props.computer.isSwitchPort || false
        form.portLabel = props.computer.portLabel || ''
        form.remarks = props.computer.remarks || ''
      } else {
        form.name = ''
        form.ip = props.initialIp || ''
        form.mac = ''
        form.owner = ''
        form.department = ''
        form.os = 'Windows 10'
        form.location = ''
        form.workshopId = props.initialContext?.workshopId ?? null
        form.areaId = props.initialContext?.areaId ?? null
        form.unitId = props.initialContext?.unitId ?? null
        form.unitCascade = form.unitId
        form.terminalBoxId = props.initialContext?.terminalBoxId ?? null
        form.onuDeviceId = props.initialContext?.onuDeviceId ?? null
        form.portNumber = props.initialContext?.portNumber ?? 1
        form.isSwitchPort = props.initialContext?.isSwitchPort ?? false
        form.portLabel = ''
        form.remarks = ''
      }
    }
  }
)

function handleUnitChange(val: any) {
  if (typeof val === 'number') {
    form.unitId = val
    // 自动寻找父级车间与区域
    const unit = props.organization.units?.find(u => u.id === val)
    if (unit) {
      form.areaId = unit.parentId
      const area = props.organization.areas?.find(a => a.id === unit.parentId)
      if (area) form.workshopId = area.parentId
    }
  } else {
    form.unitId = null
  }
}

function handleBoxChange(boxId: number | null) {
  if (!boxId) return
  const box = props.terminalBoxes.find(b => b.id === boxId)
  if (box) {
    if (!form.location && box.location) {
      form.location = box.location
    }
    if (box.areaId && !form.areaId) {
      form.areaId = box.areaId
    }
  }
}

async function testPing() {
  if (!form.ip?.trim()) {
    ElMessage.warning('请先输入 IP 地址')
    return
  }
  pingTesting.value = true
  pingResult.value = null
  try {
    const res = await computerApi.probe(props.computer?.id || 0)
    pingResult.value = { success: res.status === 'online', rtt: res.responseTimeMs }
  } catch {
    // 若未保存则可能无法探测，给出提示
    pingResult.value = null
  } finally {
    pingTesting.value = false
  }
}

async function submit() {
  if (ipConflict.value?.occupied && form.ip !== props.computer?.ip) { ElMessage.error('此 IP 已被登记，请选择空闲地址。'); return }

  const success = await run(async () => {
    if (!form.name.trim()) throw new Error('请输入计算机或设备名称')
    if (!form.ip?.trim()) throw new Error('请输入 IP 地址')

    const payload: ComputerInput = {
      name: form.name.trim(),
      ip: form.ip.trim(),
      mac: form.mac?.trim() || null,
      owner: form.owner?.trim() || null,
      department: form.department?.trim() || null,
      os: form.os?.trim() || null,
      location: form.location?.trim() || null,
      workshopId: form.workshopId,
      areaId: form.areaId,
      unitId: form.unitId,
      terminalBoxId: form.terminalBoxId,
      onuDeviceId: form.onuDeviceId,
      portNumber: form.portNumber || 1,
      isSwitchPort: form.isSwitchPort,
      portLabel: form.portLabel?.trim() || (form.isSwitchPort ? `SW-P${form.portNumber}` : `LAN${form.portNumber}`),
      remarks: form.remarks?.trim() || null
    }

    const saved = await computerApi.save(props.computer?.id || null, payload)
    emit('saved', saved)
  }, props.computer ? '电脑信息已更新' : '电脑登记已添加')

  if (success) {
    emit('update:modelValue', false)
  }
}
onBeforeUnmount(() => clearTimeout(checkIpTimer))
</script>

<template>
  <el-dialog
    :model-value="modelValue"
    :title="computer ? '编辑电脑登记' : '新增电脑登记'"
    width="680px"
    destroy-on-close
    @update:model-value="emit('update:modelValue', $event)"
  >
    <el-form label-position="top">
      <el-row :gutter="16">
        <el-col :span="12">
          <el-form-item label="计算机 / 设备名称" required>
            <el-input
              v-model="form.name"
              placeholder="例如：主控操作机-01、质检机等"
              clearable
            />
          </el-form-item>
        </el-col>
        <el-col :span="12">
          <el-form-item required>
            <template #label>
              <div style="display: flex; justify-content: space-between; align-items: center; width: 100%;">
                <span>IP 地址</span>
                <el-button
                  link
                  type="primary"
                  size="small"
                  :loading="recommendingIp"
                  @click="recommendFreeIp"
                >
                  推荐空闲 IP
                </el-button>
              </div>
            </template>
            <el-input
              v-model="form.ip"
              placeholder="例如：10.37.20.105"
              clearable
            />
          </el-form-item>
        </el-col>
      </el-row>

      <!-- IP 冲突警告横幅 -->
      <el-alert
        v-if="ipConflict"
        type="error"
        :closable="false"
        show-icon
        style="margin-bottom: 16px;"
      >
        <template #title>
          <span>IP 冲突预警：该 IP ({{ form.ip }}) 已被其他设备占用！</span>
        </template>
        <template #default>
          <div style="font-size: 12.5px; margin-top: 4px;">
            占用设备：<strong>{{ ipConflict.boundName }}</strong>
            <span v-if="ipConflict.boundType"> [{{ ipConflict.boundType }}]</span>
            <span v-if="ipConflict.boundDetail" class="muted" style="margin-left: 6px;">({{ ipConflict.boundDetail }})</span>
          </div>
        </template>
      </el-alert>

      <el-row :gutter="16">
        <el-col :span="12">
          <el-form-item label="MAC 物理地址">
            <el-input
              v-model="form.mac"
              placeholder="例如：00:1A:2B:3C:4D:5E"
              clearable
            />
          </el-form-item>
        </el-col>
        <el-col :span="12">
          <el-form-item label="责任人 / 使用人">
            <el-input
              v-model="form.owner"
              placeholder="例如：张工、值班员"
              clearable
            />
          </el-form-item>
        </el-col>
      </el-row>

      <el-row :gutter="16">
        <el-col :span="12">
          <el-form-item label="所属部门 / 工段">
            <el-input
              v-model="form.department"
              placeholder="例如：炼钢部设备组"
              clearable
            />
          </el-form-item>
        </el-col>
        <el-col :span="12">
          <el-form-item label="操作系统">
            <el-select
              v-model="form.os"
              filterable
              allow-create
              default-first-option
              placeholder="选择或输入操作系统"
              style="width: 100%;"
            >
              <el-option label="Windows 10" value="Windows 10" />
              <el-option label="Windows 11" value="Windows 11" />
              <el-option label="Windows 7" value="Windows 7" />
              <el-option label="Windows Server" value="Windows Server" />
              <el-option label="Ubuntu Linux" value="Ubuntu Linux" />
              <el-option label="CentOS / Linux" value="Linux" />
            </el-select>
          </el-form-item>
        </el-col>
      </el-row>

      <el-divider content-position="left">空间与组织归属</el-divider>

      <el-row :gutter="16">
        <el-col :span="12">
          <el-form-item label="组织架构 (车间/区域/单元)">
            <el-cascader
              v-model="form.unitCascade"
              :options="cascaderOptions"
              :props="{ checkStrictly: false, emitPath: false }"
              placeholder="请选择归属工位单元"
              clearable
              filterable
              style="width: 100%;"
              @change="handleUnitChange"
            />
          </el-form-item>
        </el-col>
        <el-col :span="12">
          <el-form-item label="物理安装具体位置">
            <el-input
              v-model="form.location"
              placeholder="例如：热轧主控室3号机柜下方操作台"
              clearable
            />
          </el-form-item>
        </el-col>
      </el-row>

      <el-divider content-position="left">POL 全光网络拓扑挂载</el-divider>

      <el-row :gutter="16">
        <el-col :span="12">
          <el-form-item label="接入网络终端箱">
            <el-select
              v-model="form.terminalBoxId"
              filterable
              clearable
              placeholder="选择网络终端箱"
              style="width: 100%;"
              @change="handleBoxChange"
            >
              <el-option
                v-for="b in terminalBoxes"
                :key="b.id"
                :value="b.id"
                :label="`${b.name} (${b.code})${b.location ? ' - ' + b.location : ''}`"
              />
            </el-select>
          </el-form-item>
        </el-col>
        <el-col :span="12">
          <el-form-item label="接入 ONU 终端">
            <el-select
              v-model="form.onuDeviceId"
              filterable
              clearable
              placeholder="选择关联 ONU 设备"
              style="width: 100%;"
            >
              <el-option
                v-for="o in filteredOnus"
                :key="o.id"
                :value="o.id"
                :label="`${o.alias || o.onuName} (${o.sn})`"
              />
            </el-select>
          </el-form-item>
        </el-col>
      </el-row>

      <el-row :gutter="16">
        <el-col :span="8">
          <el-form-item label="物理端口号">
            <el-input-number
              v-model="form.portNumber"
              :min="1"
              :max="48"
              style="width: 100%;"
            />
          </el-form-item>
        </el-col>
        <el-col :span="8">
          <el-form-item label="端口类型">
            <el-switch
              v-model="form.isSwitchPort"
              active-text="下挂交换机端口"
              inactive-text="ONU 直连端口"
            />
          </el-form-item>
        </el-col>
        <el-col :span="8">
          <el-form-item label="端口标识 / 备注">
            <el-input
              v-model="form.portLabel"
              :placeholder="form.isSwitchPort ? `SW-P${form.portNumber}` : `LAN${form.portNumber}`"
            />
          </el-form-item>
        </el-col>
      </el-row>

      <el-form-item label="备注说明">
        <el-input
          v-model="form.remarks"
          type="textarea"
          :rows="2"
          placeholder="填写其他硬件配置、工控用途或资产编号等信息..."
        />
      </el-form-item>
    </el-form>

    <template #footer>
      <div style="display: flex; justify-content: space-between; align-items: center; width: 100%;">
        <div>
          <el-tag v-if="pingResult" :type="pingResult.success ? 'success' : 'danger'">
            {{ pingResult.success ? `在线响应 (${pingResult.rtt} ms)` : '探测离线' }}
          </el-tag>
        </div>
        <div>
          <el-button @click="emit('update:modelValue', false)">取消</el-button>
          <el-button type="primary" :loading="busy" @click="submit">确认保存</el-button>
        </div>
      </div>
    </template>
  </el-dialog>
</template>
