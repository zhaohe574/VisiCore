<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { Box, Connection, Cpu, FolderOpened, Refresh, Search, VideoCamera } from '@element-plus/icons-vue'
import { ElMessage } from 'element-plus'
import {
  allPages,
  managementApi,
  ponApi,
  type Channel,
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
}>()

const emit = defineEmits<{
  (e: 'update:modelValue', value: boolean): void
  (e: 'saved'): void
}>()

const { busy, run } = useAction()

const loading = ref(false)
const unassignedChannels = ref<Channel[]>([])
const selectedChannels = ref<Channel[]>([])
const channelSearch = ref('')

const terminalBoxes = ref<TerminalBoxDto[]>([])
const allOnus = ref<OnuDeviceDto[]>([])

// 表单字段
const targetUnitCascade = ref<number | null>(null)
const bindNetwork = ref(false)
const targetBoxId = ref<number | null>(null)
const targetOnuId = ref<number | null>(null)
const startPort = ref<number>(1)
const isSwitchPort = ref(false)

const cascaderOptions = computed(() => buildUnitCascaderOptions(props.organization))

// 过滤当前终端箱下的 ONU
const filteredOnus = computed(() => {
  if (!targetBoxId.value) return allOnus.value
  return allOnus.value.filter(o => o.terminalBoxId === targetBoxId.value)
})

// 过滤通道列表
const filteredChannels = computed(() => {
  const kw = channelSearch.value.trim().toLowerCase()
  if (!kw) return unassignedChannels.value
  return unassignedChannels.value.filter(c =>
    (c.name && c.name.toLowerCase().includes(kw)) ||
    (c.alias && c.alias.toLowerCase().includes(kw)) ||
    (c.ip && c.ip.toLowerCase().includes(kw)) ||
    (c.deviceHost && c.deviceHost.toLowerCase().includes(kw))
  )
})

watch(
  () => props.modelValue,
  async (visible) => {
    if (visible) {
      targetUnitCascade.value = props.initialUnitId ?? null
      bindNetwork.value = false
      targetBoxId.value = null
      targetOnuId.value = null
      startPort.value = 1
      isSwitchPort.value = false
      selectedChannels.value = []
      channelSearch.value = ''
      await loadData()
    }
  }
)

async function loadData() {
  loading.value = true
  try {
    const [channelsRes, boxesRes, onusRes] = await Promise.all([
      managementApi.unassignedChannels(),
      ponApi.terminalBoxes().catch(() => []),
      allPages<OnuDeviceDto>(q => ponApi.onus(q)).catch(() => [])
    ])
    unassignedChannels.value = channelsRes || []
    terminalBoxes.value = boxesRes || []
    allOnus.value = onusRes || []
  } catch (err: any) {
    ElMessage.error(err.message || '加载未划拨通道数据失败')
  } finally {
    loading.value = false
  }
}

function handleSelectionChange(rows: Channel[]) {
  selectedChannels.value = rows
}

async function submit() {
  if (selectedChannels.value.length === 0) {
    ElMessage.warning('请至少选择一个待认领的通道')
    return
  }
  if (!targetUnitCascade.value) {
    ElMessage.warning('请选择目标划拨业务单元')
    return
  }

  await run(async () => {
    await managementApi.batchClaimChannels({
      channelIds: selectedChannels.value.map(c => c.id),
      unitId: targetUnitCascade.value!,
      terminalBoxId: bindNetwork.value ? targetBoxId.value : null,
      onuDeviceId: bindNetwork.value ? targetOnuId.value : null,
      startPortNumber: bindNetwork.value ? startPort.value : null,
      isSwitchPort: bindNetwork.value ? isSwitchPort.value : null
    })
    ElMessage.success(`成功认领 ${selectedChannels.value.length} 个通道！`)
    emit('saved')
    emit('update:modelValue', false)
  }, '从录像机批量认领通道')
}
</script>

<template>
  <el-dialog
    :model-value="modelValue"
    title="从录像机批量认领通道"
    width="820px"
    destroy-on-close
    @update:model-value="emit('update:modelValue', $event)"
  >
    <div v-loading="loading">
      <el-alert
        title="快速从 physical NVR 同步的通道中批量认领摄像头至指定业务单元，并可一键批量分配物理网络终端箱承载端口。"
        type="info"
        :closable="false"
        show-icon
        style="margin-bottom: 16px;"
      />

      <!-- 目标单元选择 -->
      <el-form label-position="top">
        <el-row :gutter="16">
          <el-col :span="12">
            <el-form-item label="目标业务单元" required>
              <el-cascader
                v-model="targetUnitCascade"
                :options="cascaderOptions"
                :props="{ checkStrictly: false, emitPath: false }"
                placeholder="选择划拨归属的车间 / 区域 / 单元"
                clearable
                style="width: 100%;"
              />
            </el-form-item>
          </el-col>
          <el-col :span="12">
            <el-form-item label="网络承载设置">
              <el-switch
                v-model="bindNetwork"
                active-text="同时配置网络终端箱承载"
                inactive-text="仅划拨单元"
              />
            </el-form-item>
          </el-col>
        </el-row>

        <!-- 级联物理网络承载设置 -->
        <div v-if="bindNetwork" class="network-binding-box">
          <div class="network-title">
            <el-icon><Connection /></el-icon> 物理网络接入承载（端口自动递增分配）
          </div>
          <el-row :gutter="12" style="margin-top: 10px;">
            <el-col :span="8">
              <el-form-item label="承载网络终端箱" style="margin-bottom: 0;">
                <el-select
                  v-model="targetBoxId"
                  placeholder="选择网络终端箱"
                  clearable
                  filterable
                  style="width: 100%;"
                  @change="targetOnuId = null"
                >
                  <el-option
                    v-for="b in terminalBoxes"
                    :key="b.id"
                    :label="`${b.name} (${b.code})`"
                    :value="b.id"
                  />
                </el-select>
              </el-form-item>
            </el-col>
            <el-col :span="8">
              <el-form-item label="箱内 ONU 设备" style="margin-bottom: 0;">
                <el-select
                  v-model="targetOnuId"
                  placeholder="选择箱内 ONU"
                  clearable
                  filterable
                  style="width: 100%;"
                >
                  <el-option
                    v-for="o in filteredOnus"
                    :key="o.id"
                    :label="`${o.alias || o.onuName} (${o.terminalType})`"
                    :value="o.id"
                  />
                </el-select>
              </el-form-item>
            </el-col>
            <el-col :span="4">
              <el-form-item label="起始端口号" style="margin-bottom: 0;">
                <el-input-number
                  v-model="startPort"
                  :min="1"
                  :max="48"
                  controls-position="right"
                  style="width: 100%;"
                />
              </el-form-item>
            </el-col>
            <el-col :span="4">
              <el-form-item label="端口类别" style="margin-bottom: 0;">
                <el-select v-model="isSwitchPort" style="width: 100%;">
                  <el-option label="LAN直连" :value="false" />
                  <el-option label="SW交换机" :value="true" />
                </el-select>
              </el-form-item>
            </el-col>
          </el-row>
          <div class="binding-tip">
            系统将为选中的通道自动递增设置端口，如从 {{ isSwitchPort ? 'SW-P' : 'LAN' }}{{ startPort }} 开始往后顺延。
          </div>
        </div>
      </el-form>

      <!-- 待认领通道列表 -->
      <div class="channel-selector-header">
        <div class="left-info">
          <span>待认领录像机通道 (共 {{ unassignedChannels.length }} 路未划拨，已勾选 <strong>{{ selectedChannels.length }}</strong> 路)</span>
        </div>
        <div class="right-search">
          <el-input
            v-model="channelSearch"
            placeholder="搜索通道名称、IP、NVR..."
            :prefix-icon="Search"
            size="small"
            clearable
            style="width: 220px;"
          />
          <el-button link type="primary" :icon="Refresh" @click="loadData">刷新</el-button>
        </div>
      </div>

      <el-table
        :data="filteredChannels"
        size="small"
        max-height="320"
        border
        stripe
        @selection-change="handleSelectionChange"
      >
        <el-table-column type="selection" width="48" align="center" />
        <el-table-column label="通道号" prop="channelNumber" width="75" align="center" />
        <el-table-column label="通道别名 / 名称" min-width="160" show-overflow-tooltip>
          <template #default="{ row }">
            <span class="font-medium">{{ row.alias || row.name }}</span>
          </template>
        </el-table-column>
        <el-table-column label="摄像机 IP" width="130">
          <template #default="{ row }">
            <span class="monospace text-xs">{{ row.ip || '-' }}</span>
          </template>
        </el-table-column>
        <el-table-column label="所属 NVR" min-width="130" show-overflow-tooltip>
          <template #default="{ row }">
            <span class="text-xs text-slate-500">{{ row.deviceHost || row.deviceSerial || '-' }}</span>
          </template>
        </el-table-column>
        <el-table-column label="在线状态" width="85" align="center">
          <template #default="{ row }">
            <el-tag size="small" :type="row.status === 'online' ? 'success' : 'danger'">
              {{ row.status === 'online' ? '在线' : '离线' }}
            </el-tag>
          </template>
        </el-table-column>
        <template #empty>
          <el-empty description="暂无待划拨的录像机通道" :image-size="60" />
        </template>
      </el-table>
    </div>

    <template #footer>
      <el-button @click="emit('update:modelValue', false)">取消</el-button>
      <el-button
        type="primary"
        :loading="busy"
        :disabled="selectedChannels.length === 0 || !targetUnitCascade"
        @click="submit"
      >
        确认认领 ({{ selectedChannels.length }})
      </el-button>
    </template>
  </el-dialog>
</template>

<style scoped>
.network-binding-box {
  background: #f8fafc;
  border: 1px solid #e2e8f0;
  border-radius: 6px;
  padding: 12px;
  margin-bottom: 16px;
}
.network-title {
  font-size: 13px;
  font-weight: 600;
  color: #334155;
  display: flex;
  align-items: center;
  gap: 6px;
}
.binding-tip {
  margin-top: 8px;
  font-size: 12px;
  color: #64748b;
}
.channel-selector-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-bottom: 8px;
  font-size: 13px;
  color: #334155;
}
.monospace {
  font-family: monospace;
}
</style>
