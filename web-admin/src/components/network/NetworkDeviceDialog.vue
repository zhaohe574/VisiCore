<script setup lang="ts">
import { computed, reactive, ref, watch } from 'vue'
import { Delete, Plus, VideoPlay } from '@element-plus/icons-vue'
import { ElMessage } from 'element-plus'
import {
  networkDeviceApi,
  type NetworkDevice,
  type NetworkDeviceInput,
  type NetworkDeviceIpInput,
  type Organization
} from '../../api'
import { useAction } from '../../composables/useAction'
import { buildAreaCascaderOptions } from '../../lib/organization'
import DevicePortManager from './DevicePortManager.vue'
import VirtualMachineManager from './VirtualMachineManager.vue'

const props = defineProps<{
  modelValue: boolean
  device?: NetworkDevice | null
  organization?: Organization | null
  initialIp?: string | null
  initialVlanId?: number | null
}>()

const emit = defineEmits<{
  (e: 'update:modelValue', val: boolean): void
  (e: 'saved', dev?: NetworkDevice): void
}>()

const { busy, run } = useAction()
const activeTab = ref('basic')

const form = reactive<NetworkDeviceInput>({
  name: '',
  deviceType: 'core_switch',
  vendor: '',
  model: '',
  managementIp: '',
  mac: '',
  location: '',
  workshopId: null,
  areaId: null,
  owner: '',
  status: 'online',
  remarks: '',
  assignedIps: []
})

function extractAreaId(val: any): number | null {
  if (val == null) return null
  if (Array.isArray(val)) {
    const last = val[val.length - 1]
    return typeof last === 'number' ? last : (typeof last === 'string' && !isNaN(Number(last)) ? Number(last) : null)
  }
  return typeof val === 'number' ? val : (typeof val === 'string' && !isNaN(Number(val)) ? Number(val) : null)
}

const cascaderValue = ref<any>(null)
const areaCascaderOptions = computed(() => {
  if (!props.organization) return []
  return buildAreaCascaderOptions(props.organization)
})

watch(cascaderValue, (val) => {
  const areaId = extractAreaId(val)
  form.areaId = areaId
  if (areaId && props.organization) {
    const area = props.organization.areas.find(a => a.id === areaId)
    form.workshopId = area ? area.parentId : null
  } else {
    form.workshopId = null
  }
})

const deviceTypeOptions = [
  { label: '核心交换机 (Core Switch)', value: 'core_switch' },
  { label: '汇聚交换机 (Aggregation Switch)', value: 'agg_switch' },
  { label: '接入交换机 (Access Switch)', value: 'access_switch' },
  { label: '路由器 (Router)', value: 'router' },
  { label: '网络防火墙 (Firewall)', value: 'firewall' },
  { label: '无线控制器 (AC / AP 网关)', value: 'ac' },
  { label: '服务器网卡 / 存储网元', value: 'server' },
  { label: '其它网络设施', value: 'other' }
]

const roleOptions = [
  { label: '网关地址 (Gateway)', value: 'gateway' },
  { label: '带外管理口 (Management)', value: 'management' },
  { label: '核心互联口 (Interconnect)', value: 'interconnect' },
  { label: '业务直连接口 (Service)', value: 'service' }
]

watch(
  () => props.modelValue,
  (visible) => {
    if (visible) {
      if (props.device) {
        form.name = props.device.name
        form.deviceType = props.device.deviceType || 'core_switch'
        form.vendor = props.device.vendor || ''
        form.model = props.device.model || ''
        form.managementIp = props.device.managementIp || ''
        form.mac = props.device.mac || ''
        form.location = props.device.location || ''
        form.workshopId = props.device.workshopId ?? null
        form.areaId = props.device.areaId ?? null
        form.owner = props.device.owner || ''
        form.status = props.device.status || 'online'
        form.remarks = props.device.remarks || ''
        cascaderValue.value = props.device.areaId ?? null
        form.assignedIps = props.device.assignedIps.map(i => ({
          ip: i.ip,
          vlanId: i.vlanId ?? null,
          interfaceName: i.interfaceName || 'Vlanif',
          role: i.role || 'gateway',
          description: i.description || ''
        }))
      } else {
        form.name = ''
        form.deviceType = 'core_switch'
        form.vendor = 'Huawei'
        form.model = ''
        form.managementIp = ''
        form.mac = ''
        form.location = ''
        form.workshopId = null
        form.areaId = null
        form.owner = ''
        form.status = 'online'
        form.remarks = ''
        cascaderValue.value = null
        form.assignedIps = []

        if (props.initialIp) {
          form.assignedIps.push({
            ip: props.initialIp,
            vlanId: props.initialVlanId ?? null,
            interfaceName: props.initialVlanId ? `Vlanif${props.initialVlanId}` : 'Vlanif',
            role: 'gateway',
            description: '网关接口'
          })
        }
      }
    }
  }
)

function addIpRow() {
  if (!form.assignedIps) form.assignedIps = []
  form.assignedIps.push({
    ip: '',
    vlanId: null,
    interfaceName: 'Vlanif',
    role: 'gateway',
    description: ''
  })
}

function removeIpRow(index: number) {
  form.assignedIps?.splice(index, 1)
}

async function handleSave() {
  const success = await run(async () => {
    if (!form.name?.trim()) throw new Error('请输入设备名称')
    if (!form.deviceType?.trim()) throw new Error('请选择设备类型')

    // 校验接口 IP 格式
    const validIps: NetworkDeviceIpInput[] = []
    if (form.assignedIps && form.assignedIps.length > 0) {
      for (const item of form.assignedIps) {
        if (!item.ip?.trim()) continue
        validIps.push({
          ip: item.ip.trim(),
          vlanId: item.vlanId,
          interfaceName: item.interfaceName?.trim() || 'Vlanif',
          role: item.role?.trim() || 'gateway',
          description: item.description?.trim() || null
        })
      }
    }

    const payload: NetworkDeviceInput = {
      name: form.name.trim(),
      deviceType: form.deviceType.trim(),
      vendor: form.vendor?.trim() || null,
      model: form.model?.trim() || null,
      managementIp: form.managementIp?.trim() || null,
      mac: form.mac?.trim() || null,
      location: form.location?.trim() || null,
      workshopId: form.workshopId != null ? Number(form.workshopId) : null,
      areaId: extractAreaId(form.areaId ?? cascaderValue.value),
      owner: form.owner?.trim() || null,
      status: form.status?.trim() || 'online',
      remarks: form.remarks?.trim() || null,
      assignedIps: validIps
    }

    const saved = await networkDeviceApi.save(props.device?.id || null, payload)
    emit('saved', saved)
  }, props.device ? '网络设备已更新' : '网络设备已成功登记')

  if (success) {
    emit('update:modelValue', false)
  }
}
</script>

<template>
  <el-dialog
    :model-value="modelValue"
    :title="device ? `编辑设备：${device.name}` : '登记网络/服务器设备'"
    width="880px"
    destroy-on-close
    @update:model-value="emit('update:modelValue', $event)"
  >
    <el-tabs v-model="activeTab" class="device-dialog-tabs">
      <!-- Tab 1: 基础属性 -->
      <el-tab-pane label="基本台账属性" name="basic">
        <el-form label-position="top">
          <el-row :gutter="16">
            <el-col :span="12">
              <el-form-item label="设备名称" required>
                <el-input
                  v-model="form.name"
                  placeholder="例如：总控核心交换机 S6730、业务应用服务器"
                  clearable
                />
              </el-form-item>
            </el-col>
            <el-col :span="12">
              <el-form-item label="设备类型" required>
                <el-select v-model="form.deviceType" placeholder="选择类型" style="width: 100%;">
                  <el-option
                    v-for="op in deviceTypeOptions"
                    :key="op.value"
                    :label="op.label"
                    :value="op.value"
                  />
                </el-select>
              </el-form-item>
            </el-col>
          </el-row>

          <el-row :gutter="16">
            <el-col :span="8">
              <el-form-item label="设备品牌 / 厂商">
                <el-input v-model="form.vendor" placeholder="例如：Huawei, H3C, Inspur, Dell" clearable />
              </el-form-item>
            </el-col>
            <el-col :span="8">
              <el-form-item label="硬件型号">
                <el-input v-model="form.model" placeholder="例如：S6730-H48X6C, NF5280M6" clearable />
              </el-form-item>
            </el-col>
            <el-col :span="8">
              <el-form-item label="带外管理 IP (选填)">
                <el-input v-model="form.managementIp" placeholder="例如：10.37.200.250" clearable />
              </el-form-item>
            </el-col>
          </el-row>

          <el-row :gutter="16">
            <el-col :span="12">
              <el-form-item label="物理安装位置 / 机柜号">
                <el-input v-model="form.location" placeholder="例如：总控大楼二层核心机房 A01 机柜 12U" clearable />
              </el-form-item>
            </el-col>
            <el-col :span="6">
              <el-form-item label="所属区域">
                <el-cascader
                  v-model="cascaderValue"
                  :options="areaCascaderOptions"
                  :props="{ checkStrictly: false, emitPath: false }"
                  placeholder="选择车间 / 区域"
                  clearable
                  style="width: 100%;"
                />
              </el-form-item>
            </el-col>
            <el-col :span="6">
              <el-form-item label="网络管理员 / 责任人">
                <el-input v-model="form.owner" placeholder="例如：网络中心 张工" clearable />
              </el-form-item>
            </el-col>
          </el-row>

          <el-row :gutter="16">
            <el-col :span="12">
              <el-form-item label="设备 MAC 地址 (选填)">
                <el-input v-model="form.mac" placeholder="例如：48-57-02-1A-BC-01" clearable />
              </el-form-item>
            </el-col>
            <el-col :span="12">
              <el-form-item label="运行状态">
                <el-select v-model="form.status" style="width: 100%;">
                  <el-option label="在线 (Online)" value="online" />
                  <el-option label="离线 (Offline)" value="offline" />
                  <el-option label="未知 (Unknown)" value="unknown" />
                </el-select>
              </el-form-item>
            </el-col>
          </el-row>

          <el-form-item label="补充备注说明">
            <el-input
              v-model="form.remarks"
              type="textarea"
              :rows="2"
              placeholder="例如：主备 VRRP 虚地址规划、上行业务汇聚说明、宿主机规划等"
            />
          </el-form-item>
        </el-form>
      </el-tab-pane>

      <!-- Tab 2: 物理端口与跳线链路 -->
      <el-tab-pane label="物理端口与跳线 (Ports)" name="ports">
        <div v-if="device?.id">
          <DevicePortManager
            :device-id="device.id"
            :device-type="'network_device'"
            :device-name="form.name || device.name"
            @changed="emit('saved')"
          />
        </div>
        <div v-else class="tab-empty-tip">
          <el-empty description="请先保存基本属性，保存后即可自由输入数量生成物理端口并插拔跳线" />
        </div>
      </el-tab-pane>

      <!-- Tab 3: 运行虚拟机 (针对服务器类型) -->
      <el-tab-pane
        v-if="form.deviceType === 'server' || (device?.virtualMachines && device.virtualMachines.length > 0)"
        label="运行虚拟机分配 (VMs)"
        name="vms"
      >
        <div v-if="device?.id">
          <VirtualMachineManager
            :host-device-id="device.id"
            :host-device-name="form.name || device.name"
            @changed="emit('saved')"
          />
        </div>
        <div v-else class="tab-empty-tip">
          <el-empty description="请先保存服务器基本属性，保存后即可挂载分配虚拟机" />
        </div>
      </el-tab-pane>

      <!-- Tab 4: 接口 IP 与 VLAN 网关配置 -->
      <el-tab-pane label="接口 IP 与 VLAN 网关" name="ips">
        <div class="assigned-ips-section">
          <div class="section-header">
            <div class="title-wrap">
              <span class="section-title">分配接口 IP 与 VLAN 网关配置</span>
              <span class="section-sub">支持单台核心交换机配置多个网关 IP，在 IP 池中将识别为专属网络设施并免除冲突</span>
            </div>
            <el-button type="primary" link size="small" :icon="Plus" @click="addIpRow">
              添加接口 IP
            </el-button>
          </div>

          <el-table
            :data="form.assignedIps"
            size="small"
            border
            empty-text="暂未分配接口 IP（点击右上角“添加接口 IP”录入各 VLAN 网关）"
            style="width: 100%; margin-top: 8px;"
          >
            <el-table-column label="接口 IP 地址" width="160">
              <template #default="{ row }">
                <el-input v-model="row.ip" placeholder="如 10.37.20.1" size="small" />
              </template>
            </el-table-column>
            <el-table-column label="VLAN ID" width="100">
              <template #default="{ row }">
                <el-input-number
                  v-model="row.vlanId"
                  :min="1"
                  :max="4094"
                  size="small"
                  controls-position="right"
                  placeholder="20"
                  style="width: 100%;"
                />
              </template>
            </el-table-column>
            <el-table-column label="接口名称" width="130">
              <template #default="{ row }">
                <el-input v-model="row.interfaceName" placeholder="如 Vlanif20" size="small" />
              </template>
            </el-table-column>
            <el-table-column label="接口角色" width="140">
              <template #default="{ row }">
                <el-select v-model="row.role" size="small" style="width: 100%;">
                  <el-option
                    v-for="ro in roleOptions"
                    :key="ro.value"
                    :label="ro.label"
                    :value="ro.value"
                  />
                </el-select>
              </template>
            </el-table-column>
            <el-table-column label="说明备注" min-width="120">
              <template #default="{ row }">
                <el-input v-model="row.description" placeholder="如 办公楼网关" size="small" />
              </template>
            </el-table-column>
            <el-table-column label="操作" width="60" align="center">
              <template #default="{ $index }">
                <el-button
                  type="danger"
                  link
                  size="small"
                  :icon="Delete"
                  @click="removeIpRow($index)"
                />
              </template>
            </el-table-column>
          </el-table>
        </div>
      </el-tab-pane>
    </el-tabs>

    <template #footer>
      <el-button @click="emit('update:modelValue', false)">取消</el-button>
      <el-button type="primary" :loading="busy" @click="handleSave">
        保存设备台账
      </el-button>
    </template>
  </el-dialog>
</template>

<style scoped>
.assigned-ips-section {
  margin-top: 10px;
  padding: 12px 14px;
  background: var(--el-fill-color-light);
  border-radius: 6px;
  border: 1px solid var(--el-border-color-lighter);
}
.section-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
}
.title-wrap {
  display: flex;
  flex-direction: column;
}
.section-title {
  font-size: 13.5px;
  font-weight: 600;
  color: var(--el-text-color-primary);
}
.section-sub {
  font-size: 11.5px;
  color: var(--el-text-color-secondary);
  margin-top: 2px;
}
</style>
