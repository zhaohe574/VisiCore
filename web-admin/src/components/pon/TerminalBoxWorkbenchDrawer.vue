<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import {
  Box,
  CircleCheck,
  CloseBold,
  Connection,
  Cpu,
  Delete,
  InfoFilled,
  Location,
  Monitor,
  Plus,
  Refresh,
  VideoCamera,
  VideoPlay,
  Warning
} from '@element-plus/icons-vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import {
  computerApi,
  managementApi,
  ponApi,
  type NetworkTopologyOnuDto,
  type NetworkTopologyPortDto,
  type TerminalBoxTopologyDto
} from '../../api'

const props = defineProps<{
  modelValue: boolean
  boxId: number | null
  onuId?: number | null
}>()

const emit = defineEmits<{
  (e: 'update:modelValue', value: boolean): void
  (e: 'inspectChannel', channelId: number): void
  (e: 'addCamera', boxId: number): void
  (e: 'addComputer', boxId: number): void
  (e: 'refresh'): void
}>()

const router = useRouter()
const loading = ref(false)
const currentBox = ref<TerminalBoxTopologyDto | null>(null)
const activeTab = ref<'cameras' | 'computers' | 'topology'>('cameras')
const probingComputerId = ref<number | null>(null)

watch(
  () => [props.modelValue, props.boxId, props.onuId],
  async ([visible]) => {
    if (visible) {
      await loadBoxDetail()
    } else {
      currentBox.value = null
    }
  },
  { immediate: true }
)

async function loadBoxDetail() {
  let targetBoxId = props.boxId
  loading.value = true
  try {
    if (!targetBoxId && props.onuId) {
      const onu = await ponApi.onu(props.onuId)
      if (onu?.terminalBoxId) {
        targetBoxId = onu.terminalBoxId
      }
    }

    if (targetBoxId) {
      currentBox.value = await ponApi.terminalBoxTopology(targetBoxId)
    } else {
      currentBox.value = null
    }
  } catch (err: any) {
    ElMessage.error(err.message || '加载终端箱信息失败')
  } finally {
    loading.value = false
  }
}

// 扁平化统计下挂摄像头列表
interface ConnectedCameraItem {
  onuId: number
  onuName: string
  portNumber: number
  isSwitchPort: boolean
  portLabel: string
  channelId: number
  channelName: string
  channelIp?: string | null
  status?: string | null
}

const connectedCameras = computed<ConnectedCameraItem[]>(() => {
  if (!currentBox.value?.onus) return []
  const list: ConnectedCameraItem[] = []
  for (const onu of currentBox.value.onus) {
    for (const p of onu.ports || []) {
      if (p.boundChannelId) {
        list.push({
          onuId: onu.id,
          onuName: onu.alias || onu.onuName,
          portNumber: p.portNumber,
          isSwitchPort: p.isSwitchPort,
          portLabel: p.portLabel,
          channelId: p.boundChannelId,
          channelName: p.boundChannelName || `通道 #${p.boundChannelId}`,
          channelIp: p.boundChannelIp,
          status: p.boundChannelStatus
        })
      }
    }
  }
  return list
})

// 扁平化统计下挂电脑列表
interface ConnectedComputerItem {
  onuId: number
  onuName: string
  portNumber: number
  isSwitchPort: boolean
  portLabel: string
  computerId: number
  computerName: string
  computerIp: string
  status?: string | null
  owner?: string | null
  department?: string | null
}

const connectedComputers = computed<ConnectedComputerItem[]>(() => {
  if (!currentBox.value?.onus) return []
  const list: ConnectedComputerItem[] = []
  for (const onu of currentBox.value.onus) {
    for (const p of onu.ports || []) {
      if (p.boundComputerId) {
        list.push({
          onuId: onu.id,
          onuName: onu.alias || onu.onuName,
          portNumber: p.portNumber,
          isSwitchPort: p.isSwitchPort,
          portLabel: p.portLabel,
          computerId: p.boundComputerId,
          computerName: p.boundComputerName || `电脑 #${p.boundComputerId}`,
          computerIp: p.boundComputerIp || '',
          status: p.boundComputerStatus,
          owner: p.boundComputerOwner,
          department: p.boundComputerDepartment
        })
      }
    }
  }
  return list
})

function getRxHealthBadge(rx?: number | null) {
  if (rx === null || rx === undefined) {
    return { type: 'info' as const, label: '光功率未采集', text: '-- dBm' }
  }
  if (rx >= -24 && rx <= -8) {
    return { type: 'success' as const, label: '优良区间', text: `${rx.toFixed(1)} dBm` }
  }
  if (rx < -24 && rx >= -27) {
    return { type: 'warning' as const, label: '光衰偏大', text: `${rx.toFixed(1)} dBm` }
  }
  return { type: 'danger' as const, label: '光衰超标', text: `${rx.toFixed(1)} dBm` }
}

// 快速解除摄像头网络绑定
async function unbindCamera(item: any) {
  try {
    await ElMessageBox.confirm(
      `确定将摄像头【${item.channelName}】从端口【${item.portLabel}】解除承载关系？`,
      '确认解绑',
      { confirmButtonText: '解除绑定', cancelButtonText: '取消', type: 'warning' }
    )
    await managementApi.batchSetNetworkBindings({
      channelIds: [item.channelId],
      terminalBoxId: null,
      onuDeviceId: null
    })
    ElMessage.success('已解除摄像头承载绑定')
    emit('refresh')
    await loadBoxDetail()
  } catch (e: any) {
    if (e !== 'cancel') {
      ElMessage.error(e.message || '解除绑定失败')
    }
  }
}

// 快速解除工位电脑网络绑定
async function unbindComputer(item: any) {
  try {
    await ElMessageBox.confirm(
      `确定将电脑【${item.computerName} (${item.computerIp})】从端口【${item.portLabel}】解除承载关系？`,
      '确认解绑',
      { confirmButtonText: '解除绑定', cancelButtonText: '取消', type: 'warning' }
    )
    const comp = await computerApi.get(item.computerId)
    await computerApi.save(item.computerId, {
      name: comp.name,
      ip: comp.ip,
      mac: comp.mac,
      owner: comp.owner,
      department: comp.department,
      os: comp.os,
      location: comp.location,
      workshopId: comp.workshopId,
      areaId: comp.areaId,
      unitId: comp.unitId,
      terminalBoxId: null,
      onuDeviceId: null,
      portNumber: undefined,
      isSwitchPort: false,
      portLabel: null,
      remarks: comp.remarks
    })
    ElMessage.success('已解除电脑承载绑定')
    emit('refresh')
    await loadBoxDetail()
  } catch (e: any) {
    if (e !== 'cancel') {
      ElMessage.error(e.message || '解除绑定失败')
    }
  }
}

// 探测电脑 Ping
async function probeComputer(item: any) {
  probingComputerId.value = item.computerId
  try {
    const res = await computerApi.probe(item.computerId)
    if (res.status === 'online') {
      ElMessage.success(`探测成功：${item.computerName} 网络在线 (响应: ${res.responseTimeMs ?? '<1'}ms)`)
    } else {
      ElMessage.warning(`探测反馈：${item.computerName} 目前离线或无响应`)
    }
    await loadBoxDetail()
  } catch (e: any) {
    ElMessage.error(e.message || '探测执行失败')
  } finally {
    probingComputerId.value = null
  }
}

function handleInspect(channelId: number) {
  emit('inspectChannel', channelId)
  emit('update:modelValue', false)
  router.push({ path: '/app/organization', query: { inspectId: channelId } })
}

function handleAddCamera() {
  if (currentBox.value) {
    emit('addCamera', currentBox.value.id)
    emit('update:modelValue', false)
    router.push({ path: '/app/organization', query: { boxId: currentBox.value.id } })
  }
}

function handleAddComputer() {
  if (currentBox.value) {
    emit('addComputer', currentBox.value.id)
    emit('update:modelValue', false)
    router.push({ path: '/app/computers', query: { boxId: currentBox.value.id } })
  }
}
</script>

<template>
  <el-drawer
    :model-value="modelValue"
    title="终端箱综合运维工作台"
    size="720px"
    destroy-on-close
    @update:model-value="emit('update:modelValue', $event)"
  >
    <div v-loading="loading" class="workbench-drawer-body">
      <div v-if="currentBox">
        <!-- 终端箱头部卡片 -->
        <div class="box-overview-card">
          <div class="box-title-row">
            <div class="box-title-left">
              <div class="box-icon-badge"><el-icon :size="20"><Box /></el-icon></div>
              <div>
                <div class="box-name-line">
                  <span class="box-name">{{ currentBox.name }}</span>
                  <el-tag size="small" type="primary" effect="plain">{{ currentBox.code }}</el-tag>
                </div>
                <div class="box-meta-line">
                  <span class="meta-item">
                    <el-icon><Location /></el-icon> {{ currentBox.location || '未标注具体物理位置' }}
                  </span>
                  <span v-if="currentBox.areaName || currentBox.unitName" class="meta-item">
                    归属: {{ [currentBox.areaName, currentBox.unitName].filter(Boolean).join(' / ') }}
                  </span>
                </div>
              </div>
            </div>
            <el-button link type="primary" :icon="Refresh" @click="loadBoxDetail">刷新链路</el-button>
          </div>

          <!-- ONU 设备与光衰状况矩阵 -->
          <div class="onus-summary-section">
            <div class="section-sub-title">
              <el-icon><Cpu /></el-icon> 箱内 ONU 状态及光功率 ({{ currentBox.onus?.length || 0 }} 台)
            </div>
            <div v-if="!currentBox.onus || currentBox.onus.length === 0" class="muted-empty">
              当前箱体尚未绑定任何 ONU 设备，可在 ONU 台账中关联
            </div>
            <div v-else class="onu-chips-grid">
              <div
                v-for="onu in currentBox.onus"
                :key="onu.id"
                class="onu-chip"
                :class="{ 'is-offline': onu.status !== 'online' }"
              >
                <div class="onu-chip-header">
                  <div class="onu-chip-name">
                    <span class="onu-tag">ONU</span>
                    <strong>{{ onu.alias || onu.onuName }}</strong>
                    <span class="muted" style="font-size: 11.5px;">({{ onu.sn }})</span>
                  </div>
                  <el-tag size="small" :type="onu.status === 'online' ? 'success' : 'danger'">
                    {{ onu.status === 'online' ? '光网在线' : '离线' }}
                  </el-tag>
                </div>
                <div class="onu-chip-power">
                  <span class="power-label">接收光功率 (Rx):</span>
                  <el-tag
                    size="small"
                    :type="getRxHealthBadge(onu.rxPower).type"
                    effect="light"
                  >
                    {{ getRxHealthBadge(onu.rxPower).text }} · {{ getRxHealthBadge(onu.rxPower).label }}
                  </el-tag>
                </div>
                <div v-if="onu.hasSubSwitch" class="onu-chip-sub">
                  下挂扩展交换机: {{ onu.subSwitchModel || '千兆交换机' }} ({{ onu.subSwitchPorts }}口)
                </div>
              </div>
            </div>
          </div>
        </div>

        <!-- Tab 切换工作区 -->
        <el-tabs v-model="activeTab" class="workbench-tabs">
          <!-- 下挂摄像头台账 -->
          <el-tab-pane name="cameras">
            <template #label>
              <span class="tab-label">
                <el-icon><VideoCamera /></el-icon>
                <span>下挂摄像头</span>
                <el-badge :value="connectedCameras.length" :type="connectedCameras.length ? 'primary' : 'info'" class="tab-badge" />
              </span>
            </template>

            <div class="tab-toolbar">
              <span class="tab-tip">
                本终端箱共接入 <strong>{{ connectedCameras.length }}</strong> 路视频监控通道
              </span>
              <el-button
                size="small"
                type="primary"
                :icon="Plus"
                @click="handleAddCamera"
              >
                快速接入摄像头
              </el-button>
            </div>

            <el-table :data="connectedCameras" size="small" border stripe class="workbench-table">
              <el-table-column label="承载端口" width="110">
                <template #default="{ row }">
                  <span class="port-chip">{{ row.portLabel }}</span>
                </template>
              </el-table-column>
              <el-table-column label="摄像头名称" min-width="150" show-overflow-tooltip>
                <template #default="{ row }">
                  <span class="font-medium">{{ row.channelName }}</span>
                </template>
              </el-table-column>
              <el-table-column label="IP 地址" width="130">
                <template #default="{ row }">
                  <span class="monospace text-xs">{{ row.channelIp || '-' }}</span>
                </template>
              </el-table-column>
              <el-table-column label="状态" width="80" align="center">
                <template #default="{ row }">
                  <el-tag size="small" :type="row.status === 'online' ? 'success' : 'danger'">
                    {{ row.status === 'online' ? '推流正常' : '离线' }}
                  </el-tag>
                </template>
              </el-table-column>
              <el-table-column label="接入 ONU" min-width="110" show-overflow-tooltip>
                <template #default="{ row }">
                  <span class="text-xs text-slate-500">{{ row.onuName }}</span>
                </template>
              </el-table-column>
              <el-table-column label="操作" width="140" align="right" fixed="right">
                <template #default="{ row }">
                  <el-button
                    link
                    type="primary"
                    size="small"
                    :icon="VideoPlay"
                    @click="handleInspect(row.channelId)"
                  >
                    点检
                  </el-button>
                  <el-button
                    link
                    type="danger"
                    size="small"
                    :icon="CloseBold"
                    @click="unbindCamera(row)"
                  >
                    解除
                  </el-button>
                </template>
              </el-table-column>
              <template #empty>
                <el-empty description="当前终端箱暂无接入的摄像头" :image-size="60" />
              </template>
            </el-table>
          </el-tab-pane>

          <!-- 下挂工位电脑台账 -->
          <el-tab-pane name="computers">
            <template #label>
              <span class="tab-label">
                <el-icon><Monitor /></el-icon>
                <span>下挂工位电脑</span>
                <el-badge :value="connectedComputers.length" :type="connectedComputers.length ? 'success' : 'info'" class="tab-badge" />
              </span>
            </template>

            <div class="tab-toolbar">
              <span class="tab-tip">
                本终端箱共接入 <strong>{{ connectedComputers.length }}</strong> 台生产/办公工位电脑
              </span>
              <el-button
                size="small"
                type="primary"
                :icon="Plus"
                @click="handleAddComputer"
              >
                登记工位电脑
              </el-button>
            </div>

            <el-table :data="connectedComputers" size="small" border stripe class="workbench-table">
              <el-table-column label="承载端口" width="110">
                <template #default="{ row }">
                  <span class="port-chip">{{ row.portLabel }}</span>
                </template>
              </el-table-column>
              <el-table-column label="电脑名称" min-width="140" show-overflow-tooltip>
                <template #default="{ row }">
                  <span class="font-medium">{{ row.computerName }}</span>
                </template>
              </el-table-column>
              <el-table-column label="IP 地址" width="130">
                <template #default="{ row }">
                  <span class="monospace text-xs">{{ row.computerIp || '-' }}</span>
                </template>
              </el-table-column>
              <el-table-column label="使用人 / 部门" min-width="130" show-overflow-tooltip>
                <template #default="{ row }">
                  <span class="text-xs">{{ [row.owner, row.department].filter(Boolean).join(' · ') || '-' }}</span>
                </template>
              </el-table-column>
              <el-table-column label="网络状态" width="80" align="center">
                <template #default="{ row }">
                  <el-tag size="small" :type="row.status === 'online' ? 'success' : 'warning'">
                    {{ row.status === 'online' ? '在线' : '离线' }}
                  </el-tag>
                </template>
              </el-table-column>
              <el-table-column label="操作" width="140" align="right" fixed="right">
                <template #default="{ row }">
                  <el-button
                    link
                    type="primary"
                    size="small"
                    :loading="probingComputerId === row.computerId"
                    :icon="VideoPlay"
                    @click="probeComputer(row)"
                  >
                    Ping
                  </el-button>
                  <el-button
                    link
                    type="danger"
                    size="small"
                    :icon="CloseBold"
                    @click="unbindComputer(row)"
                  >
                    解除
                  </el-button>
                </template>
              </el-table-column>
              <template #empty>
                <el-empty description="当前终端箱暂无接入的工位电脑" :image-size="60" />
              </template>
            </el-table>
          </el-tab-pane>

          <!-- 链路拓扑透视图 -->
          <el-tab-pane name="topology">
            <template #label>
              <span class="tab-label">
                <el-icon><Connection /></el-icon>
                <span>链路走线图</span>
              </span>
            </template>

            <div v-if="!currentBox.onus || currentBox.onus.length === 0" style="padding: 30px; text-align: center;">
              <el-empty description="该终端箱内暂未分配任何 ONU 设备" />
            </div>
            <div v-else style="display: flex; flex-direction: column; gap: 14px; margin-top: 10px;">
              <div
                v-for="onu in currentBox.onus"
                :key="onu.id"
                class="onu-topo-card"
              >
                <div class="onu-topo-header">
                  <div style="display: flex; align-items: center; gap: 8px;">
                    <span class="onu-tag">ONU</span>
                    <strong>{{ onu.alias || onu.onuName }}</strong>
                    <span class="muted" style="font-size: 12px;">({{ onu.terminalType }})</span>
                  </div>
                  <el-tag size="small" :type="onu.status === 'online' ? 'success' : 'danger'">
                    {{ onu.status === 'online' ? '在线' : '离线' }}
                  </el-tag>
                </div>

                <div v-if="onu.hasSubSwitch" class="switch-banner">
                  <el-icon><Connection /></el-icon>
                  <span>下挂交换机: <strong>{{ onu.subSwitchModel || '标准千兆交换机' }}</strong> ({{ onu.subSwitchPorts }}口)</span>
                </div>

                <div class="ports-grid">
                  <div
                    v-for="p in onu.ports"
                    :key="p.portNumber"
                    class="port-card"
                    :class="{ 'has-camera': !!p.boundChannelId, 'has-comp': !!p.boundComputerId }"
                  >
                    <div class="port-card-title">
                      <span class="port-chip">{{ p.portLabel }}</span>
                      <span v-if="p.boundChannelId" class="port-status green">摄像头</span>
                      <span v-else-if="p.boundComputerId" class="port-status blue">电脑</span>
                      <span v-else class="port-status gray">空闲</span>
                    </div>

                    <div class="port-card-content">
                      <template v-if="p.boundChannelId">
                        <div class="target-name text-ellipsis">{{ p.boundChannelName }}</div>
                        <div class="target-ip monospace">{{ p.boundChannelIp || '-' }}</div>
                      </template>
                      <template v-else-if="p.boundComputerId">
                        <div class="target-name text-ellipsis">{{ p.boundComputerName }}</div>
                        <div class="target-ip monospace">{{ p.boundComputerIp || '-' }}</div>
                      </template>
                      <template v-else>
                        <div class="muted" style="font-size: 12px;">空闲备用</div>
                      </template>
                    </div>
                  </div>
                </div>
              </div>
            </div>
          </el-tab-pane>
        </el-tabs>
      </div>

      <div v-else style="padding: 60px 20px; text-align: center;">
        <el-empty description="未找到或未指定网络终端箱" />
      </div>
    </div>
  </el-drawer>
</template>

<style scoped>
.workbench-drawer-body {
  padding: 0 4px;
}

.box-overview-card {
  background: #f8fafc;
  border: 1px solid #e2e8f0;
  border-radius: 8px;
  padding: 16px;
  margin-bottom: 16px;
}

.box-title-row {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
}

.box-title-left {
  display: flex;
  align-items: center;
  gap: 12px;
}

.box-icon-badge {
  width: 40px;
  height: 40px;
  border-radius: 8px;
  background: var(--el-color-primary-light-9, #ecf5ff);
  color: var(--primary, #0284c7);
  display: flex;
  align-items: center;
  justify-content: center;
}

.box-name-line {
  display: flex;
  align-items: center;
  gap: 8px;
}

.box-name {
  font-size: 16px;
  font-weight: 600;
  color: #1e293b;
}

.box-meta-line {
  margin-top: 4px;
  font-size: 12.5px;
  color: #64748b;
  display: flex;
  align-items: center;
  gap: 12px;
}

.meta-item {
  display: flex;
  align-items: center;
  gap: 4px;
}

.onus-summary-section {
  margin-top: 14px;
  padding-top: 12px;
  border-top: 1px dashed #cbd5e1;
}

.section-sub-title {
  font-size: 13px;
  font-weight: 600;
  color: #334155;
  display: flex;
  align-items: center;
  gap: 6px;
  margin-bottom: 8px;
}

.muted-empty {
  font-size: 12px;
  color: #94a3b8;
  padding: 8px 0;
}

.onu-chips-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(280px, 1fr));
  gap: 10px;
}

.onu-chip {
  background: #ffffff;
  border: 1px solid #e2e8f0;
  border-radius: 6px;
  padding: 10px 12px;
  font-size: 12.5px;
}

.onu-chip.is-offline {
  border-color: #fca5a5;
  background: #fef2f2;
}

.onu-chip-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
}

.onu-chip-name {
  display: flex;
  align-items: center;
  gap: 6px;
}

.onu-tag {
  background: #0284c7;
  color: #fff;
  font-size: 10px;
  font-weight: 700;
  padding: 1px 5px;
  border-radius: 3px;
}

.onu-chip-power {
  margin-top: 6px;
  display: flex;
  align-items: center;
  gap: 8px;
}

.power-label {
  font-size: 12px;
  color: #64748b;
}

.onu-chip-sub {
  margin-top: 6px;
  font-size: 11.5px;
  color: #64748b;
}

.workbench-tabs {
  margin-top: 10px;
}

.tab-label {
  display: inline-flex;
  align-items: center;
  gap: 6px;
}

.tab-badge {
  margin-left: 2px;
}

.tab-toolbar {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-bottom: 12px;
}

.tab-tip {
  font-size: 13px;
  color: #475569;
}

.port-chip {
  background: #f1f5f9;
  border: 1px solid #cbd5e1;
  padding: 2px 6px;
  border-radius: 4px;
  font-size: 11.5px;
  font-family: monospace;
  font-weight: 600;
  color: #334155;
}

.monospace {
  font-family: monospace;
}

.text-ellipsis {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.onu-topo-card {
  background: #ffffff;
  border: 1px solid #e2e8f0;
  border-radius: 8px;
  overflow: hidden;
}

.onu-topo-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 8px 12px;
  background: #f1f5f9;
  border-bottom: 1px solid #e2e8f0;
}

.switch-banner {
  background: #eff6ff;
  color: #1e40af;
  padding: 6px 12px;
  font-size: 12px;
  display: flex;
  align-items: center;
  gap: 6px;
  border-bottom: 1px solid #dbeafe;
}

.ports-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(140px, 1fr));
  gap: 10px;
  padding: 12px;
}

.port-card {
  border: 1px solid #e2e8f0;
  border-radius: 6px;
  padding: 8px;
  background: #f8fafc;
}

.port-card.has-camera {
  border-color: #86efac;
  background: #f0fdf4;
}

.port-card.has-comp {
  border-color: #bae6fd;
  background: #f0f9ff;
}

.port-card-title {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-bottom: 6px;
}

.port-status {
  font-size: 10.5px;
  font-weight: 600;
  padding: 1px 4px;
  border-radius: 3px;
}

.port-status.green { background: #dcfce7; color: #15803d; }
.port-status.blue { background: #e0f2fe; color: #0369a1; }
.port-status.gray { background: #f1f5f9; color: #64748b; }

.target-name {
  font-size: 12px;
  font-weight: 500;
  color: #1e293b;
}

.target-ip {
  font-size: 11px;
  color: #64748b;
}
</style>
