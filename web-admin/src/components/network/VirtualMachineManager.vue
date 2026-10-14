<script setup lang="ts">
import { onMounted, reactive, ref, watch } from 'vue'
import {
  Cpu,
  Delete,
  Edit,
  Monitor,
  Plus,
  Refresh
} from '@element-plus/icons-vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import {
  virtualMachineApi,
  type VirtualMachine,
  type VirtualMachineInput
} from '../../api'
import { useAction } from '../../composables/useAction'

const props = withDefaults(
  defineProps<{
    hostDeviceId: number
    hostDeviceName?: string
    readonly?: boolean
  }>(),
  {
    hostDeviceName: '宿主机',
    readonly: false
  }
)

const emit = defineEmits<{
  (e: 'changed'): void
}>()

const { busy, run } = useAction()
const loading = ref(false)
const vms = ref<VirtualMachine[]>([])

const dialogOpen = ref(false)
const editingVmId = ref<number | null>(null)
const form = reactive<VirtualMachineInput>({
  vmName: '',
  ip: '',
  vlanId: null,
  os: 'Ubuntu 22.04 LTS',
  cpuCores: 4,
  memoryGb: 8,
  status: 'running',
  businessDesc: '',
  remarks: ''
})

const osOptions = [
  'Ubuntu 22.04 LTS',
  'Ubuntu 24.04 LTS',
  'CentOS 7.9',
  'Rocky Linux 9',
  'Debian 12',
  'Windows Server 2022',
  'Windows Server 2019',
  'Windows 10 Enterprise',
  '其它操作系统'
]

async function loadVms() {
  if (!props.hostDeviceId) return
  loading.value = true
  try {
    vms.value = await virtualMachineApi.list(props.hostDeviceId)
  } catch (err: any) {
    ElMessage.error(err.message || '加载虚拟机列表失败')
  } finally {
    loading.value = false
  }
}

watch(() => props.hostDeviceId, () => {
  loadVms()
}, { immediate: true })

function openCreate() {
  editingVmId.value = null
  form.vmName = ''
  form.ip = ''
  form.vlanId = null
  form.os = 'Ubuntu 22.04 LTS'
  form.cpuCores = 4
  form.memoryGb = 8
  form.status = 'running'
  form.businessDesc = ''
  form.remarks = ''
  dialogOpen.value = true
}

function openEdit(row: VirtualMachine) {
  editingVmId.value = row.id
  form.vmName = row.vmName
  form.ip = row.ip
  form.vlanId = row.vlanId ?? null
  form.os = row.os || 'Ubuntu 22.04 LTS'
  form.cpuCores = row.cpuCores ?? 4
  form.memoryGb = row.memoryGb ?? 8
  form.status = row.status || 'running'
  form.businessDesc = row.businessDesc || ''
  form.remarks = row.remarks || ''
  dialogOpen.value = true
}

async function handleSave() {
  if (!form.vmName.trim()) {
    ElMessage.warning('请输入虚拟机名称')
    return
  }
  if (!form.ip.trim()) {
    ElMessage.warning('请输入虚拟机 IP 地址')
    return
  }

  await run(async () => {
    if (editingVmId.value) {
      await virtualMachineApi.update(editingVmId.value, {
        ...form,
        vmName: form.vmName.trim(),
        ip: form.ip.trim()
      })
    } else {
      await virtualMachineApi.create(props.hostDeviceId, {
        ...form,
        vmName: form.vmName.trim(),
        ip: form.ip.trim()
      })
    }
    dialogOpen.value = false
    await loadVms()
    emit('changed')
  }, editingVmId.value ? '虚拟机已更新' : '虚拟机已成功分配')
}

async function handleDelete(row: VirtualMachine) {
  try {
    await ElMessageBox.confirm(`确认删除虚拟机【${row.vmName}】(${row.ip})？`, '删除确认', {
      type: 'warning',
      confirmButtonText: '删除',
      cancelButtonText: '取消'
    })
  } catch {
    return
  }

  await run(async () => {
    await virtualMachineApi.delete(row.id)
    await loadVms()
    emit('changed')
  }, `虚拟机 ${row.vmName} 已删除`)
}
</script>

<template>
  <div class="virtual-machine-manager">
    <div class="vm-toolbar">
      <div class="vm-stats">
        <span>当前宿主机共承载 <strong>{{ vms.length }}</strong> 台运行中虚拟机</span>
      </div>
      <div v-if="!readonly" class="vm-actions">
        <el-button type="primary" size="small" :icon="Plus" @click="openCreate">
          + 登记分配虚拟机
        </el-button>
        <el-button size="small" link :icon="Refresh" :loading="loading" @click="loadVms">
          刷新
        </el-button>
      </div>
    </div>

    <el-table
      v-loading="loading"
      :data="vms"
      size="small"
      border
      max-height="400px"
      empty-text="暂无从属虚拟机，点击右上角“+ 登记分配虚拟机”可为该服务器挂载虚机"
      style="width: 100%; margin-top: 8px;"
    >
      <el-table-column label="虚拟机名称" min-width="140">
        <template #default="{ row }">
          <div class="vm-name-cell">
            <el-icon><Monitor /></el-icon>
            <strong>{{ row.vmName }}</strong>
          </div>
        </template>
      </el-table-column>

      <el-table-column label="IP 地址" width="130">
        <template #default="{ row }">
          <span style="font-family: monospace; font-weight: 600; color: #2563eb;">{{ row.ip }}</span>
        </template>
      </el-table-column>

      <el-table-column label="VLAN" width="80" align="center">
        <template #default="{ row }">
          <el-tag v-if="row.vlanId" size="small" type="info">VLAN {{ row.vlanId }}</el-tag>
          <span v-else style="color: #94a3b8;">-</span>
        </template>
      </el-table-column>

      <el-table-column label="操作系统" width="140">
        <template #default="{ row }">
          <span style="font-size: 12px; color: #475569;">{{ row.os || '-' }}</span>
        </template>
      </el-table-column>

      <el-table-column label="资源配置" width="110">
        <template #default="{ row }">
          <span style="font-size: 11px; color: #475569;">
            {{ row.cpuCores }}核 / {{ row.memoryGb }}GB
          </span>
        </template>
      </el-table-column>

      <el-table-column label="承载业务说明" min-width="160">
        <template #default="{ row }">
          <span style="font-size: 12px;">{{ row.businessDesc || row.remarks || '-' }}</span>
        </template>
      </el-table-column>

      <el-table-column label="状态" width="85" align="center">
        <template #default="{ row }">
          <el-tag size="small" :type="row.status === 'running' ? 'success' : 'info'">
            {{ row.status === 'running' ? '运行中' : '已关机' }}
          </el-tag>
        </template>
      </el-table-column>

      <el-table-column v-if="!readonly" label="操作" width="100" align="center">
        <template #default="{ row }">
          <el-button
            type="primary"
            link
            size="small"
            :icon="Edit"
            title="编辑虚拟机"
            @click="openEdit(row as VirtualMachine)"
          />
          <el-button
            type="danger"
            link
            size="small"
            :icon="Delete"
            title="删除虚拟机"
            @click="handleDelete(row as VirtualMachine)"
          />
        </template>
      </el-table-column>
    </el-table>

    <!-- 虚拟机登记/编辑弹窗 -->
    <el-dialog
      v-model="dialogOpen"
      :title="editingVmId ? '编辑虚拟机台账' : `为 [${hostDeviceName}] 分配挂载虚拟机`"
      width="560px"
      append-to-body
    >
      <el-form label-position="top">
        <el-row :gutter="12">
          <el-col :span="14">
            <el-form-item label="虚拟机名称" required>
              <el-input v-model="form.vmName" placeholder="例如：MES-APP-01、NeoSight全光网管" />
            </el-form-item>
          </el-col>
          <el-col :span="10">
            <el-form-item label="虚拟机 IP" required>
              <el-input v-model="form.ip" placeholder="例如：10.37.200.15" />
            </el-form-item>
          </el-col>
        </el-row>

        <el-row :gutter="12">
          <el-col :span="8">
            <el-form-item label="分配 VLAN ID">
              <el-input-number
                v-model="form.vlanId"
                :min="1"
                :max="4094"
                controls-position="right"
                placeholder="如 200"
                style="width: 100%;"
              />
            </el-form-item>
          </el-col>
          <el-col :span="8">
            <el-form-item label="CPU 核心数">
              <el-input-number
                v-model="form.cpuCores"
                :min="1"
                :max="128"
                controls-position="right"
                style="width: 100%;"
              />
            </el-form-item>
          </el-col>
          <el-col :span="8">
            <el-form-item label="内存容量 (GB)">
              <el-input-number
                v-model="form.memoryGb"
                :min="1"
                :max="1024"
                controls-position="right"
                style="width: 100%;"
              />
            </el-form-item>
          </el-col>
        </el-row>

        <el-row :gutter="12">
          <el-col :span="14">
            <el-form-item label="操作系统">
              <el-select v-model="form.os" filterable allow-create default-first-option style="width: 100%;">
                <el-option v-for="op in osOptions" :key="op" :label="op" :value="op" />
              </el-select>
            </el-form-item>
          </el-col>
          <el-col :span="10">
            <el-form-item label="运行状态">
              <el-select v-model="form.status" style="width: 100%;">
                <el-option label="运行中 (Running)" value="running" />
                <el-option label="已关机 (Stopped)" value="stopped" />
              </el-select>
            </el-form-item>
          </el-col>
        </el-row>

        <el-form-item label="业务系统说明">
          <el-input v-model="form.businessDesc" placeholder="例如：承载华为 NeoSight 全光网管系统应用服务" />
        </el-form-item>

        <el-form-item label="备注">
          <el-input v-model="form.remarks" placeholder="可选备注说明" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="dialogOpen = false">取消</el-button>
        <el-button type="primary" :loading="busy" @click="handleSave">保存</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<style scoped>
.virtual-machine-manager {
  display: flex;
  flex-direction: column;
}

.vm-toolbar {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 4px 8px;
  background: rgba(241, 245, 249, 0.8);
  border-radius: 6px;
  font-size: 12px;
}

.vm-stats {
  color: #475569;
}

.vm-actions {
  display: flex;
  gap: 8px;
  align-items: center;
}

.vm-name-cell {
  display: flex;
  align-items: center;
  gap: 6px;
}

.vm-name-cell .el-icon {
  color: #3b82f6;
}
</style>
