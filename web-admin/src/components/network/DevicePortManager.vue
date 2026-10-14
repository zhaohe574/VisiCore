<script setup lang="ts">
import { computed, onMounted, reactive, ref, watch } from 'vue'
import {
  Check,
  Close,
  Connection,
  Cpu,
  Delete,
  Edit,
  Lightning,
  Plus,
  Refresh,
  Search
} from '@element-plus/icons-vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import {
  devicePortApi,
  type ConnectLinkInput,
  type DevicePort,
  type DevicePortInput,
  type PortCandidate
} from '../../api'
import { useAction } from '../../composables/useAction'

const props = withDefaults(
  defineProps<{
    deviceId: number
    deviceType?: string
    deviceName?: string
    readonly?: boolean
  }>(),
  {
    deviceType: 'network_device',
    deviceName: '当前设备',
    readonly: false
  }
)

const emit = defineEmits<{
  (e: 'changed'): void
}>()

const { busy, run } = useAction()
const loading = ref(false)
const ports = ref<DevicePort[]>([])

// 批量生成助手表单
const generator = reactive({
  copperCount: 24,
  copperPrefix: 'GE 1/0/',
  copperSpeed: '1Gbps',
  fiberCount: 4,
  fiberPrefix: 'XGE 1/0/',
  fiberSpeed: '10Gbps',
  potsCount: 0,
  potsPrefix: 'POTS ',
  replaceExisting: true
})

// 单端口表单 / 弹窗
const addSingleDialog = ref(false)
const singleForm = reactive<DevicePortInput>({
  portName: '',
  portType: 'copper',
  speed: '1Gbps',
  status: 'unused',
  slotNumber: 0,
  portIndex: 1,
  remarks: ''
})

// 跳线连接弹窗
const connectDialog = ref(false)
const currentConnectingPort = ref<DevicePort | null>(null)
const candidateSearch = ref('')
const candidateLoading = ref(false)
const candidates = ref<PortCandidate[]>([])
const connectForm = reactive<ConnectLinkInput>({
  localPortId: 0,
  peerPortId: 0,
  cableType: 'copper',
  cableLabel: '',
  remarks: ''
})

const portTypeOptions = [
  { label: '电口 (RJ45)', value: 'copper', tagType: 'primary' },
  { label: '光口 (SFP/SFP+)', value: 'fiber', tagType: 'warning' },
  { label: 'PON 进纤口', value: 'pon', tagType: 'danger' },
  { label: 'Wi-Fi 无线', value: 'wifi', tagType: 'success' },
  { label: '电话口 (RJ11/POTS)', value: 'pots', tagType: 'info' },
  { label: '带外管理口 (MGMT)', value: 'mgmt', tagType: 'info' }
]

const speedOptions = [
  { label: '100 Mbps (百兆)', value: '100M' },
  { label: '1 Gbps (千兆)', value: '1Gbps' },
  { label: '2.5 Gbps', value: '2.5Gbps' },
  { label: '10 Gbps (万兆)', value: '10Gbps' },
  { label: '40 Gbps', value: '40Gbps' },
  { label: '100 Gbps', value: '100Gbps' },
  { label: '其它速率', value: 'other' }
]

const cableTypeOptions = [
  { label: '双绞网线 (超六类/六类线)', value: 'copper' },
  { label: '单模光纤 (Yellow Patch Cord)', value: 'single_fiber' },
  { label: '多模光纤 (Aqua/Orange Patch Cord)', value: 'multi_fiber' },
  { label: 'DAC 高速直连铜缆', value: 'dac' },
  { label: '电话语音跳线', value: 'telephone_line' }
]

const stats = computed(() => {
  const total = ports.value.length
  const copper = ports.value.filter(p => p.portType === 'copper').length
  const fiber = ports.value.filter(p => p.portType === 'fiber').length
  const pots = ports.value.filter(p => p.portType === 'pots').length
  const linked = ports.value.filter(p => !!p.link).length
  return { total, copper, fiber, pots, linked }
})

async function loadPorts() {
  if (!props.deviceId) return
  loading.value = true
  try {
    ports.value = await devicePortApi.list(props.deviceType, props.deviceId)
  } catch (err: any) {
    ElMessage.error(err.message || '加载端口列表失败')
  } finally {
    loading.value = false
  }
}

watch(() => props.deviceId, () => {
  loadPorts()
}, { immediate: true })

// 批量生成端口
async function handleBatchGenerate() {
  if (generator.copperCount < 0 || generator.fiberCount < 0 || generator.potsCount < 0) {
    ElMessage.warning('端口数量不能为负数')
    return
  }

  const totalToGen = generator.copperCount + generator.fiberCount + generator.potsCount
  if (totalToGen === 0) {
    ElMessage.warning('请至少指定 1 个端口数量')
    return
  }

  let replace = generator.replaceExisting
  if (ports.value.length > 0) {
    try {
      await ElMessageBox.confirm(
        `当前设备已有 ${ports.value.length} 个端口。是否覆盖（清除现有端口及跳线重新生成）？点击【确定】覆盖，点击【取消】追加。`,
        '批量生成端口确认',
        {
          confirmButtonText: '覆盖现有端口',
          cancelButtonText: '保留并追加',
          type: 'warning',
          distinguishCancelAndClose: true
        }
      )
      replace = true
    } catch (action) {
      if (action === 'cancel') {
        replace = false
      } else {
        return
      }
    }
  }

  await run(async () => {
    ports.value = await devicePortApi.batchGenerate(props.deviceType, props.deviceId, {
      copperCount: Number(generator.copperCount),
      fiberCount: Number(generator.fiberCount),
      potsCount: Number(generator.potsCount),
      copperPrefix: generator.copperPrefix,
      fiberPrefix: generator.fiberPrefix,
      potsPrefix: generator.potsPrefix,
      copperSpeed: generator.copperSpeed,
      fiberSpeed: generator.fiberSpeed,
      replaceExisting: replace
    })
    emit('changed')
  }, '已批量生成端口')
}

// 打开单端口创建
function openAddSingle() {
  singleForm.portName = `GE 1/0/${ports.value.length + 1}`
  singleForm.portType = 'copper'
  singleForm.speed = '1Gbps'
  singleForm.status = 'unused'
  singleForm.slotNumber = 0
  singleForm.portIndex = ports.value.length + 1
  singleForm.remarks = ''
  addSingleDialog.value = true
}

async function handleSaveSingle() {
  if (!singleForm.portName.trim()) {
    ElMessage.warning('请输入端口名称')
    return
  }
  await run(async () => {
    await devicePortApi.create(props.deviceType, props.deviceId, {
      ...singleForm,
      portName: singleForm.portName.trim()
    })
    addSingleDialog.value = false
    await loadPorts()
    emit('changed')
  }, '端口添加成功')
}

// 保存单行端口编辑
async function handleUpdatePort(row: DevicePort) {
  if (!row.portName.trim()) {
    ElMessage.warning('端口名称不能为空')
    return
  }
  await run(async () => {
    await devicePortApi.update(row.id, {
      portName: row.portName.trim(),
      portType: row.portType,
      speed: row.speed,
      status: row.status,
      slotNumber: row.slotNumber,
      portIndex: row.portIndex,
      remarks: row.remarks
    })
    emit('changed')
  }, `端口 ${row.portName} 已保存`)
}

// 删除端口
async function handleDeletePort(row: DevicePort) {
  try {
    await ElMessageBox.confirm(`确认删除端口【${row.portName}】吗？若已接跳线将自动切断。`, '删除确认', {
      type: 'warning',
      confirmButtonText: '删除',
      cancelButtonText: '取消'
    })
  } catch {
    return
  }

  await run(async () => {
    await devicePortApi.delete(row.id)
    await loadPorts()
    emit('changed')
  }, `端口 ${row.portName} 已删除`)
}

// 打开跳线连接
async function openConnectDialog(port: DevicePort) {
  currentConnectingPort.value = port
  candidateSearch.value = ''
  connectForm.localPortId = port.id
  connectForm.peerPortId = 0
  connectForm.replaceExisting = false
  connectForm.cableType = port.portType === 'fiber' ? 'single_fiber' : (port.portType === 'pots' ? 'telephone_line' : 'copper')
  connectForm.cableLabel = ''
  connectForm.remarks = ''
  connectDialog.value = true
  await searchCandidates()
}

// 搜索候选连接对端端口
async function searchCandidates() {
  candidateLoading.value = true
  try {
    candidates.value = await devicePortApi.candidates({
      search: candidateSearch.value.trim() || undefined
    })
    // 排除自己当前端口
    candidates.value = candidates.value.filter(c => c.portId !== currentConnectingPort.value?.id)
  } catch (err: any) {
    ElMessage.error(err.message || '搜索对端端口失败')
  } finally {
    candidateLoading.value = false
  }
}

async function handleConnectLink() {
  if (!connectForm.peerPortId) {
    ElMessage.warning('请选择要连接的对端设备端口')
    return
  }

  await run(async () => {
    await devicePortApi.connect({
      localPortId: connectForm.localPortId,
      peerPortId: connectForm.peerPortId,
      cableType: connectForm.cableType,
      cableLabel: connectForm.cableLabel?.trim() || null,
      remarks: connectForm.remarks?.trim() || null,
      replaceExisting: connectForm.replaceExisting || false
    })
    connectDialog.value = false
    await loadPorts()
    emit('changed')
  }, '跳线连接已成功建立')
}

// 断开跳线
async function handleDisconnectLink(row: DevicePort) {
  if (!row.link) return
  try {
    await ElMessageBox.confirm(
      `确认断开端口【${row.portName}】与【${row.link.peerDeviceName} - ${row.link.peerPortName}】之间的跳线连接？`,
      '断开跳线确认',
      { type: 'warning' }
    )
  } catch {
    return
  }

  await run(async () => {
    await devicePortApi.disconnect(row.link!.id)
    await loadPorts()
    emit('changed')
  }, '跳线连接已断开')
}

function getPortTypeTag(type: string): 'primary' | 'success' | 'warning' | 'info' | 'danger' | undefined {
  const item = portTypeOptions.find(o => o.value === type)
  return (item?.tagType as any) || 'info'
}

function getPortTypeLabel(type: string) {
  const item = portTypeOptions.find(o => o.value === type)
  return item?.label || type
}

function getSpeedLabel(speed: string) {
  const item = speedOptions.find(o => o.value === speed)
  return item?.label || speed
}

function getCableTypeLabel(type: string) {
  const item = cableTypeOptions.find(o => o.value === type)
  return item?.label.split(' ')[0] || type
}
</script>

<template>
  <div class="device-port-manager">
    <!-- 顶部：快速生成器与统计栏 -->
    <div v-if="!readonly" class="generator-panel">
      <div class="gen-title">
        <el-icon><Lightning /></el-icon>
        <strong>快速动态端口生成器（拒绝死板模板，自由输入端口数量）</strong>
      </div>
      <div class="gen-form">
        <div class="gen-item">
          <span class="label">电口数量:</span>
          <el-input-number v-model="generator.copperCount" :min="0" :max="96" size="small" controls-position="right" style="width: 80px;" />
          <el-input v-model="generator.copperPrefix" placeholder="前缀" size="small" style="width: 90px; margin-left: 4px;" />
        </div>
        <div class="gen-item">
          <span class="label">光口数量:</span>
          <el-input-number v-model="generator.fiberCount" :min="0" :max="48" size="small" controls-position="right" style="width: 80px;" />
          <el-input v-model="generator.fiberPrefix" placeholder="前缀" size="small" style="width: 100px; margin-left: 4px;" />
        </div>
        <div class="gen-item">
          <span class="label">电话/语音口:</span>
          <el-input-number v-model="generator.potsCount" :min="0" :max="32" size="small" controls-position="right" style="width: 80px;" />
          <el-input v-model="generator.potsPrefix" placeholder="前缀" size="small" style="width: 80px; margin-left: 4px;" />
        </div>
        <div class="gen-actions">
          <el-button type="primary" size="small" :icon="Lightning" :loading="busy" @click="handleBatchGenerate">
            一键动态生成
          </el-button>
          <el-button size="small" :icon="Plus" @click="openAddSingle">
            + 单独添加端口
          </el-button>
        </div>
      </div>
    </div>

    <!-- 端口统计摘要 -->
    <div class="ports-summary-bar">
      <div class="summary-left">
        <span class="sum-tag">共 <strong>{{ stats.total }}</strong> 个端口</span>
        <span v-if="stats.copper" class="sum-tag copper">⚡ 电口 {{ stats.copper }}</span>
        <span v-if="stats.fiber" class="sum-tag fiber">✨ 光口 {{ stats.fiber }}</span>
        <span v-if="stats.pots" class="sum-tag pots">📞 电话口 {{ stats.pots }}</span>
        <span class="sum-tag linked">🔗 已接跳线 {{ stats.linked }} / {{ stats.total }}</span>
      </div>
      <el-button size="small" link :icon="Refresh" :loading="loading" @click="loadPorts">
        刷新端口
      </el-button>
    </div>

    <!-- 端口自由明细表格 -->
    <el-table
      v-loading="loading"
      :data="ports"
      size="small"
      border
      max-height="420px"
      empty-text="暂无物理端口，请使用上方“快速动态端口生成器”输入数量一键生成，或点击“+ 单独添加端口”"
      style="width: 100%; margin-top: 8px;"
    >
      <el-table-column label="#" width="45" align="center">
        <template #default="{ row }">
          <span style="color: #94a3b8; font-size: 11px;">{{ row.portIndex }}</span>
        </template>
      </el-table-column>

      <el-table-column label="端口名称 (可自由改名)" min-width="130">
        <template #default="{ row }">
          <el-input v-if="!readonly" v-model="row.portName" size="small" placeholder="如 GE 1/0/1" />
          <strong v-else style="font-family: monospace;">{{ row.portName }}</strong>
        </template>
      </el-table-column>

      <el-table-column label="物理类型" width="145">
        <template #default="{ row }">
          <el-select v-if="!readonly" v-model="row.portType" size="small" style="width: 100%;">
            <el-option
              v-for="op in portTypeOptions"
              :key="op.value"
              :label="op.label"
              :value="op.value"
            />
          </el-select>
          <el-tag v-else :type="getPortTypeTag(row.portType)" size="small">
            {{ getPortTypeLabel(row.portType) }}
          </el-tag>
        </template>
      </el-table-column>

      <el-table-column label="速率" width="145">
        <template #default="{ row }">
          <el-select v-if="!readonly" v-model="row.speed" size="small" style="width: 100%;">
            <el-option
              v-for="sp in speedOptions"
              :key="sp.value"
              :label="sp.label"
              :value="sp.value"
            />
          </el-select>
          <span v-else style="font-size: 12px;">{{ getSpeedLabel(row.speed) }}</span>
        </template>
      </el-table-column>

      <el-table-column label="状态" width="90" align="center">
        <template #default="{ row }">
          <el-select v-if="!readonly" v-model="row.status" size="small" style="width: 100%;">
            <el-option label="UP" value="up" />
            <el-option label="DOWN" value="down" />
            <el-option label="空闲" value="unused" />
            <el-option label="禁用" value="disabled" />
          </el-select>
          <el-tag
            v-else
            size="small"
            :type="row.status === 'up' ? 'success' : (row.status === 'down' ? 'danger' : 'info')"
          >
            {{ row.status.toUpperCase() }}
          </el-tag>
        </template>
      </el-table-column>

      <el-table-column label="跳线链路对端 (端到端双向连接)" min-width="260">
        <template #default="{ row }">
          <div v-if="row.link" class="link-item">
            <el-tag type="success" size="small" effect="plain" class="link-tag">
              <el-icon><Connection /></el-icon>
              {{ row.link.peerDeviceName }} · {{ row.link.peerPortName }}
            </el-tag>
            <span v-if="row.link.cableLabel" class="cable-label" title="现场跳线线标">
              🏷️ {{ row.link.cableLabel }}
            </span>
            <el-button
              v-if="!readonly"
              type="danger"
              link
              size="small"
              :icon="Close"
              title="断开跳线"
              @click="handleDisconnectLink(row as DevicePort)"
            >
              断开
            </el-button>
          </div>
          <div v-else class="unlinked-item">
            <span style="color: #94a3b8; font-size: 12px; margin-right: 8px;">未接跳线</span>
            <el-button
              v-if="!readonly"
              type="primary"
              link
              size="small"
              :icon="Connection"
              @click="openConnectDialog(row as DevicePort)"
            >
              插线连接
            </el-button>
          </div>
        </template>
      </el-table-column>

      <el-table-column v-if="!readonly" label="操作" width="105" align="center">
        <template #default="{ row }">
          <el-button
            type="primary"
            link
            size="small"
            :icon="Check"
            title="保存当前行修改"
            @click="handleUpdatePort(row as DevicePort)"
          />
          <el-button
            type="danger"
            link
            size="small"
            :icon="Delete"
            title="删除端口"
            @click="handleDeletePort(row as DevicePort)"
          />
        </template>
      </el-table-column>
    </el-table>

    <!-- 单独添加端口弹窗 -->
    <el-dialog v-model="addSingleDialog" title="添加单个物理端口" width="460px" append-to-body>
      <el-form label-position="top">
        <el-form-item label="端口名称" required>
          <el-input v-model="singleForm.portName" placeholder="例如：GE 1/0/25、WAN1、LAN4" />
        </el-form-item>
        <el-row :gutter="12">
          <el-col :span="12">
            <el-form-item label="端口类型">
              <el-select v-model="singleForm.portType" style="width: 100%;">
                <el-option
                  v-for="op in portTypeOptions"
                  :key="op.value"
                  :label="op.label"
                  :value="op.value"
                />
              </el-select>
            </el-form-item>
          </el-col>
          <el-col :span="12">
            <el-form-item label="端口速率">
              <el-select v-model="singleForm.speed" style="width: 100%;">
                <el-option
                  v-for="sp in speedOptions"
                  :key="sp.value"
                  :label="sp.label"
                  :value="sp.value"
                />
              </el-select>
            </el-form-item>
          </el-col>
        </el-row>
        <el-form-item label="备注说明">
          <el-input v-model="singleForm.remarks" placeholder="可选备注说明" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="addSingleDialog = false">取消</el-button>
        <el-button type="primary" :loading="busy" @click="handleSaveSingle">确定添加</el-button>
      </template>
    </el-dialog>

    <!-- 跳线连接配置弹窗 -->
    <el-dialog
      v-model="connectDialog"
      :title="`建立跳线互联：[${deviceName}] - [${currentConnectingPort?.portName}]`"
      width="600px"
      append-to-body
    >
      <el-form label-position="top">
        <el-form-item label="选择对端设备及目标端口" required>
          <div style="display: flex; gap: 8px; width: 100%; margin-bottom: 8px;">
            <el-input
              v-model="candidateSearch"
              placeholder="输入设备名称或端口名称过滤..."
              size="small"
              clearable
              @input="searchCandidates"
            />
            <el-button size="small" :icon="Search" :loading="candidateLoading" @click="searchCandidates">
              搜索
            </el-button>
          </div>
          <el-select
            v-model="connectForm.peerPortId"
            filterable
            placeholder="从全厂设备可用端口中选择对端"
            style="width: 100%;"
          >
            <el-option
              v-for="cand in candidates"
              :key="cand.portId"
              :label="`${cand.deviceName} · ${cand.portName} (${cand.portType === 'copper' ? '电口' : (cand.portType === 'fiber' ? '光口' : cand.portType)}) ${cand.isLinked ? '⚠️ [' + cand.linkedPeerDesc + ']' : '🟢 空闲可用'}`"
              :value="cand.portId"
            />
          </el-select>
        </el-form-item>
        <el-alert v-if="currentConnectingPort?.link || candidates.find(c => c.portId === connectForm.peerPortId)?.isLinked" type="warning" :closable="false" style="margin-bottom: 12px;">
          <template #title>端口已有连接，确认替换会断开以下连接。</template>
          <p v-if="currentConnectingPort?.link">本端：{{ currentConnectingPort.portName }} → {{ currentConnectingPort.link.peerDeviceName }} ／ {{ currentConnectingPort.link.peerPortName }}</p>
          <p v-if="candidates.find(c => c.portId === connectForm.peerPortId)?.isLinked">对端：{{ candidates.find(c => c.portId === connectForm.peerPortId)?.linkedPeerDesc }}</p>
          <el-checkbox v-model="connectForm.replaceExisting">已核对原连接，显式替换并记录审计</el-checkbox>
        </el-alert>

        <el-row :gutter="12">
          <el-col :span="12">
            <el-form-item label="物理跳线介质类型">
              <el-select v-model="connectForm.cableType" style="width: 100%;">
                <el-option
                  v-for="ct in cableTypeOptions"
                  :key="ct.value"
                  :label="ct.label"
                  :value="ct.value"
                />
              </el-select>
            </el-form-item>
          </el-col>
          <el-col :span="12">
            <el-form-item label="现场跳线线标号 (选填)">
              <el-input v-model="connectForm.cableLabel" placeholder="例如：LK-CORE-01-SW02-P25" />
            </el-form-item>
          </el-col>
        </el-row>

        <el-form-item label="链路说明 (选填)">
          <el-input v-model="connectForm.remarks" placeholder="例如：机房核心至制管车间1号箱主干光纤跳线" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="connectDialog = false">取消</el-button>
        <el-button type="primary" :loading="busy" @click="handleConnectLink">确认连接建立跳线</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<style scoped>
.device-port-manager {
  display: flex;
  flex-direction: column;
}

.generator-panel {
  background: rgba(30, 41, 59, 0.04);
  border: 1px dashed #cbd5e1;
  border-radius: 8px;
  padding: 12px;
  margin-bottom: 12px;
}

.gen-title {
  display: flex;
  align-items: center;
  gap: 6px;
  font-size: 13px;
  color: #1e293b;
  margin-bottom: 8px;
}

.gen-title .el-icon {
  color: #3b82f6;
  font-size: 16px;
}

.gen-form {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 16px;
}

.gen-item {
  display: flex;
  align-items: center;
}

.gen-item .label {
  font-size: 12px;
  color: #475569;
  margin-right: 6px;
}

.gen-actions {
  display: flex;
  gap: 8px;
  margin-left: auto;
}

.ports-summary-bar {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 4px 8px;
  background: rgba(241, 245, 249, 0.8);
  border-radius: 6px;
  font-size: 12px;
}

.summary-left {
  display: flex;
  gap: 12px;
  align-items: center;
}

.sum-tag {
  color: #475569;
}

.sum-tag.copper {
  color: #2563eb;
  font-weight: 500;
}

.sum-tag.fiber {
  color: #d97706;
  font-weight: 500;
}

.sum-tag.pots {
  color: #0d9488;
  font-weight: 500;
}

.sum-tag.linked {
  color: #16a34a;
  font-weight: 600;
}

.link-item {
  display: flex;
  align-items: center;
  gap: 8px;
}

.link-tag {
  font-weight: 500;
  display: inline-flex;
  align-items: center;
  gap: 4px;
}

.cable-label {
  font-size: 11px;
  background: #f1f5f9;
  border: 1px solid #e2e8f0;
  padding: 1px 6px;
  border-radius: 4px;
  color: #475569;
}

.unlinked-item {
  display: flex;
  align-items: center;
}
</style>
