<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import {
  CircleCheck,
  Close,
  Connection,
  Edit,
  Folder,
  FolderOpened,
  InfoFilled,
  Plus,
  Refresh,
  Search,
  VideoCamera,
  VideoPlay,
  Warning
} from '@element-plus/icons-vue'
import { ElMessage } from 'element-plus'
import {
  allPages,
  managementApi,
  type Channel,
  type Device,
  type Organization,
  type OrganizationNode
} from '../api'
import {
  buildFullOrgCascaderOptions,
  calculateOrgChannelCounts,
  getUnitArea,
  getUnitName,
  getUnitWorkshop
} from '../lib/organization'
import { usePaged } from '../composables/usePaged'
import { useAction } from '../composables/useAction'
import { useAuth } from '../stores/auth'
import { useActivePage } from '../composables/useActivePage'
import PageHeader from '../components/PageHeader.vue'
import StatusBadge from '../components/StatusBadge.vue'
import ChannelDetailDrawer from '../components/organization/ChannelDetailDrawer.vue'
import ChannelInspectDrawer from '../components/ChannelInspectDrawer.vue'
import ChannelEditDialog from '../components/organization/ChannelEditDialog.vue'
import ChannelAssignDialog from '../components/organization/ChannelAssignDialog.vue'
import AddCameraDialog from '../components/organization/AddCameraDialog.vue'
import BatchClaimChannelsDialog from '../components/organization/BatchClaimChannelsDialog.vue'
import BatchNetworkBindingDialog from '../components/organization/BatchNetworkBindingDialog.vue'
import TerminalBoxWorkbenchDrawer from '../components/pon/TerminalBoxWorkbenchDrawer.vue'

const route = useRoute()
const router = useRouter()
const auth = useAuth()
const { confirm } = useAction()

function goToPon(onuId?: number | null, searchTerm?: string | null) {
  const term = onuId ? String(onuId) : (searchTerm || '')
  router.push({ path: '/app/onus', query: { search: term } })
}

function goToOrganization(unitIdTarget?: number | null) {
  if (unitIdTarget) {
    router.push({ path: '/app/organization', query: { unitId: String(unitIdTarget) } })
  } else {
    router.push({ path: '/app/organization' })
  }
}

const organization = ref<Organization>({ workshops: [], areas: [], units: [] })
const devices = ref<Device[]>([])
const allChannels = ref<Channel[]>([])

// 弹窗与抽屉控制
const addCameraOpen = ref(false)
const addCameraInitialUnitId = ref<number | null>(null)
const batchClaimOpen = ref(false)
const batchBindingOpen = ref(false)
const channelsForBinding = ref<Channel[]>([])

const workbenchDrawerOpen = ref(false)
const viewingBoxId = ref<number | null>(null)
const viewingOnuId = ref<number | null>(null)

function openWorkbench(boxId?: number | null, onuId?: number | null) {
  viewingBoxId.value = boxId ?? null
  viewingOnuId.value = onuId ?? null
  workbenchDrawerOpen.value = true
}

function openBatchNetworkBinding(channels: Channel[]) {
  channelsForBinding.value = channels
  batchBindingOpen.value = true
}

// 筛选字段
const deviceId = ref<number | undefined>(Number(route.query.deviceId) || undefined)
const workshopId = ref<number | undefined>(Number(route.query.workshopId) || undefined)
const areaId = ref<number | undefined>(Number(route.query.areaId) || undefined)
const unitId = ref<number | undefined>(Number(route.query.unitId) || undefined)
const online = ref<string>('')
const assignedFilter = ref<string>('')

// 组织级联绑定值
const orgCascaderOptions = computed(() => buildFullOrgCascaderOptions(organization.value))
const orgScopeModel = computed({
  get: () => {
    if (unitId.value) return `unit:${unitId.value}`
    if (areaId.value) return `area:${areaId.value}`
    if (workshopId.value) return `workshop:${workshopId.value}`
    return undefined
  },
  set: (val: string | undefined) => {
    if (!val) {
      workshopId.value = undefined
      areaId.value = undefined
      unitId.value = undefined
    } else {
      const [type, idStr] = val.split(':')
      const id = parseInt(idStr, 10)
      if (type === 'workshop') {
        workshopId.value = id
        areaId.value = undefined
        unitId.value = undefined
      } else if (type === 'area') {
        workshopId.value = undefined
        areaId.value = id
        unitId.value = undefined
      } else if (type === 'unit') {
        workshopId.value = undefined
        areaId.value = undefined
        unitId.value = id
      }
    }
    void load(true)
  }
})

// 分页数据加载器
const { items, total, page, pageSize, search, loading, error, load } = usePaged(
  managementApi.channels,
  () => ({
    deviceId: deviceId.value,
    unitId: unitId.value ?? undefined,
    areaId: areaId.value ?? undefined,
    workshopId: workshopId.value ?? undefined,
    assigned: assignedFilter.value === 'true' ? true : (assignedFilter.value === 'false' ? false : undefined),
    online: online.value || undefined
  }),
  ['device.changed', 'access.changed']
)

// 全局统计
const channelStats = computed(() => calculateOrgChannelCounts(allChannels.value))

// 对话框与操作
const selected = ref<Channel[]>([])
const assignmentOpen = ref(false)
const selectedForAssign = ref<Channel[]>([])

const detailDrawer = ref(false)
const currentChannel = ref<Channel | null>(null)

const inspectDrawer = ref(false)
const inspectChannel = ref<Channel | null>(null)

const editDialog = ref(false)
const editTarget = ref<Channel | null>(null)

async function loadOrganization() {
  const ticket = lifecycle.ticket('organization')
  if (ticket === undefined) return
  const value = await managementApi.organization()
  if (lifecycle.current(ticket)) organization.value = value
}

async function loadAllChannels() {
  const ticket = lifecycle.ticket('channels')
  if (ticket === undefined) return
  try {
    const value = await allPages(managementApi.channels)
    if (lifecycle.current(ticket)) allChannels.value = value
  } catch {}
}

async function loadOptions() {
  await Promise.all([
    loadOrganization(),
    loadAllChannels(),
    auth.can('device.read') ? allPages(managementApi.devices).then(res => { devices.value = res }) : Promise.resolve()
  ])
}
const lifecycle = useActivePage(loadOptions)

function resetFilters() {
  search.value = ''
  deviceId.value = undefined
  workshopId.value = undefined
  areaId.value = undefined
  unitId.value = undefined
  online.value = ''
  assignedFilter.value = ''
  void load(true)
}

function openAddCamera() {
  addCameraInitialUnitId.value = unitId.value ?? null
  addCameraOpen.value = true
}

function openAssignBatch(channels: Channel[]) {
  selectedForAssign.value = channels
  assignmentOpen.value = true
}

function showDetail(channel: Channel) {
  currentChannel.value = channel
  detailDrawer.value = true
}

function openInspect(channel: Channel) {
  inspectChannel.value = channel
  inspectDrawer.value = true
}

function openEdit(channel: Channel) {
  editTarget.value = channel
  editDialog.value = true
}

function onChannelSaved(updated: Channel) {
  if (currentChannel.value && currentChannel.value.id === updated.id) {
    Object.assign(currentChannel.value, updated)
  }
  void loadAllChannels()
  void load()
}

function onCameraAdded() {
  void loadAllChannels()
  void loadOrganization()
  void load(true)
}

function onAssigned() {
  selected.value = []
  selectedForAssign.value = []
  void loadAllChannels()
  void load()
}

function onBatchActionSaved() {
  void loadAllChannels()
  void load()
}

function getUnitAreaNode(uId?: number | null): OrganizationNode | undefined {
  return getUnitArea(organization.value, uId)
}

function getUnitWorkshopNode(uId?: number | null): OrganizationNode | undefined {
  return getUnitWorkshop(organization.value, uId)
}

function getUnitLabel(uId?: number | null): string {
  return getUnitName(organization.value, uId)
}

function getChannelIp(channel?: Channel | Record<string, any> | null): string {
  if (!channel || !channel.ip) return '—'
  return String(channel.ip)
}

async function checkInspectParam() {
  const inspectId = Number(route.query.inspectId)
  if (!inspectId) return
  let ch = allChannels.value.find(c => c.id === inspectId) || items.value.find(c => c.id === inspectId)
  if (!ch) {
    try {
      const res = await managementApi.channels({ page: 1, pageSize: 1, search: String(inspectId) })
      ch = res.items.find(c => c.id === inspectId)
    } catch {}
  }
  if (ch) {
    openInspect(ch)
  }
}

watch(() => route.query.inspectId, () => {
  void checkInspectParam()
})

watch(() => route.query.unitId, (newUnitId) => {
  if (newUnitId) {
    unitId.value = Number(newUnitId)
    workshopId.value = undefined
    areaId.value = undefined
    void load(true)
  }
})

onMounted(async () => {
  await loadOptions()
  void load()
  void checkInspectParam()
})
</script>

<template>
  <div class="channels-page">
    <PageHeader
      title="监控通道台账"
      subtitle="集中查看全网监控摄像机通道在线状态、空间归属划拨、物理与承载光网络拓扑链路"
    >
      <template #extra>
        <el-button
          v-if="auth.can('device.manage') || auth.can('channel.assign')"
          type="primary"
          :icon="Plus"
          @click="openAddCamera"
        >
          新增摄像头
        </el-button>
        <el-button
          v-if="auth.can('channel.assign')"
          :icon="FolderOpened"
          @click="batchClaimOpen = true"
        >
          批量认领通道
        </el-button>
        <el-button :icon="Refresh" :loading="loading" @click="loadOptions(); load()">
          刷新数据
        </el-button>
      </template>
    </PageHeader>

    <!-- 全局 KPI 指标卡片 -->
    <div class="kpi-grid">
      <div class="kpi-card" style="cursor: pointer;" @click="resetFilters">
        <div class="kpi-icon-wrap blue"><el-icon><VideoCamera /></el-icon></div>
        <div class="kpi-content">
          <div class="kpi-label">匹配通道总数</div>
          <div class="kpi-value">{{ total }}</div>
          <div class="kpi-sub">当前空间筛选范围内的通道总数</div>
        </div>
      </div>
      <div class="kpi-card" style="cursor: pointer;" @click="online = 'online'; void load(true)">
        <div class="kpi-icon-wrap teal"><el-icon><CircleCheck /></el-icon></div>
        <div class="kpi-content">
          <div class="kpi-label">在线通道</div>
          <div class="kpi-value" style="color: #10b981;">{{ channelStats.onlineCount }}</div>
          <div class="kpi-sub">全网当前可正常预览的通道</div>
        </div>
      </div>
      <div class="kpi-card" style="cursor: pointer;" @click="online = 'offline'; void load(true)">
        <div class="kpi-icon-wrap amber"><el-icon><Warning /></el-icon></div>
        <div class="kpi-content">
          <div class="kpi-label">离线通道</div>
          <div class="kpi-value" style="color: #f59e0b;">{{ channelStats.offlineCount }}</div>
          <div class="kpi-sub">全网离线或不可达监控通道</div>
        </div>
      </div>
      <div class="kpi-card" style="cursor: pointer;" @click="assignedFilter = 'false'; void load(true)">
        <div class="kpi-icon-wrap purple"><el-icon><Folder /></el-icon></div>
        <div class="kpi-content">
          <div class="kpi-label">未划拨通道</div>
          <div class="kpi-value" style="color: #8b5cf6;">{{ channelStats.unassignedCount }}</div>
          <div class="kpi-sub">未绑定至任何业务单元的通道</div>
        </div>
      </div>
    </div>

    <!-- 筛选工具栏与全宽表格 -->
    <el-card shadow="never" class="table-card">
      <div class="table-toolbar">
        <div class="toolbar-filters">
          <el-input
            v-model="search"
            placeholder="搜索别名、原名或 IP..."
            :prefix-icon="Search"
            clearable
            style="width: 220px;"
            @clear="load(true)"
            @keyup.enter="load(true)"
          />

          <el-cascader
            v-model="orgScopeModel"
            :options="orgCascaderOptions"
            :props="{ checkStrictly: true, emitPath: false }"
            placeholder="筛选空间 (车间/区域/单元)"
            clearable
            filterable
            style="width: 240px;"
          />

          <el-select
            v-model="deviceId"
            placeholder="归属录像机 (NVR)"
            clearable
            filterable
            style="width: 180px;"
            @change="load(true)"
          >
            <el-option v-for="d in devices" :key="d.id" :value="d.id" :label="`${d.name} (${d.host})`" />
          </el-select>

          <el-select
            v-model="online"
            placeholder="在线状态"
            clearable
            style="width: 120px;"
            @change="load(true)"
          >
            <el-option label="全部状态" value="" />
            <el-option label="在线通道" value="online" />
            <el-option label="离线通道" value="offline" />
          </el-select>

          <el-select
            v-model="assignedFilter"
            placeholder="划拨状态"
            clearable
            style="width: 130px;"
            @change="load(true)"
          >
            <el-option label="全部划拨" value="" />
            <el-option label="已划拨单元" value="true" />
            <el-option label="未划拨通道" value="false" />
          </el-select>

          <el-button type="primary" :icon="Search" @click="load(true)">查询</el-button>
          <el-button @click="resetFilters">重置</el-button>
        </div>
      </div>

      <!-- 批量操作工具条 -->
      <div v-if="selected.length" class="batch-action-bar">
        <div class="batch-info">
          <el-icon><CircleCheck /></el-icon>
          <span>已勾选 <strong>{{ selected.length }}</strong> 路监控通道</span>
        </div>
        <div class="batch-buttons">
          <el-button type="success" size="small" @click="openAssignBatch(selected)">
            批量划拨至工位单元...
          </el-button>
          <el-button
            type="warning"
            size="small"
            :icon="Connection"
            @click="openBatchNetworkBinding(selected)"
          >
            批量设置网络承载...
          </el-button>
          <el-button size="small" @click="selected = []">取消勾选</el-button>
        </div>
      </div>

      <div v-if="error" class="error-banner">
        <el-icon><Warning /></el-icon>
        <span>{{ error }}</span>
      </div>

      <!-- 全宽监控通道数据表 -->
      <el-table
        v-loading="loading"
        :data="items"
        row-key="id"
        stripe
        empty-text="当前筛选条件下未找到匹配的通道记录"
        @selection-change="selected = $event as Channel[]"
      >
        <el-table-column v-if="auth.can('channel.assign')" type="selection" width="44" />

        <el-table-column prop="alias" label="通道别名" min-width="140" show-overflow-tooltip>
          <template #default="{ row }">
            <span v-if="row.alias" style="font-weight: 600; color: var(--el-color-primary);">
              {{ row.alias }}
            </span>
            <span v-else class="text-muted">未设置别名</span>
          </template>
        </el-table-column>

        <el-table-column prop="name" label="设备原始名称" min-width="140" show-overflow-tooltip />

        <el-table-column label="IP 地址" min-width="130" show-overflow-tooltip>
          <template #default="{ row }">
            <span class="font-mono" style="font-size: 13px; font-weight: 500;">
              {{ getChannelIp(row) }}
            </span>
          </template>
        </el-table-column>

        <el-table-column prop="deviceName" label="归属设备" min-width="130" show-overflow-tooltip />

        <el-table-column prop="deviceChannel" label="通道号" width="75" align="center" />

        <el-table-column label="承载网络与端口" min-width="210">
          <template #default="{ row }">
            <div v-if="row.onuDeviceId || row.terminalBoxId || row.portLabel || row.networkPort" style="cursor: pointer; display: flex; flex-direction: column; gap: 3px;" @click.stop="goToPon(row.onuDeviceId)">
              <div style="display: flex; align-items: center; gap: 4px; flex-wrap: wrap;">
                <el-tag v-if="row.terminalBoxCode || row.terminalBoxName" size="small" type="primary" effect="plain" style="font-weight: 600;">
                  {{ row.terminalBoxCode || row.terminalBoxName }}
                </el-tag>
                <el-tag v-if="row.portLabel || row.networkPort" size="small" :type="row.isSwitchPort ? 'warning' : 'success'" effect="dark" style="font-weight: 600;">
                  {{ row.portLabel || (row.isSwitchPort ? `SW-P${row.networkPort}` : `LAN${row.networkPort}`) }}
                </el-tag>
              </div>
              <div style="font-size: 11px; color: #64748b; display: flex; align-items: center; gap: 4px;">
                <el-icon style="color: #0284c7; font-size: 12px;"><Connection /></el-icon>
                <span class="text-truncate" style="max-width: 140px;" :title="row.onuAlias || row.onuName">
                  {{ row.onuAlias || row.onuName || 'ONU' }}
                </span>
                <span v-if="row.onuRxPower !== null && row.onuRxPower !== undefined" :style="{ color: row.onuRxPower < -24 ? '#d97706' : '#10b981', marginLeft: 'auto', fontWeight: 500 }">
                  {{ row.onuRxPower }} dBm
                </span>
              </div>
            </div>
            <span v-else class="text-muted">—</span>
          </template>
        </el-table-column>

        <el-table-column label="在线状态" min-width="130" align="center">
          <template #default="{ row }">
            <div style="display: flex; flex-direction: column; align-items: center; gap: 3px;">
              <template v-if="row.status !== 'online'">
                <template v-if="row.onuDeviceId && (row.onuHealthStatus || row.opticalLink?.healthStatus)">
                  <el-tooltip v-if="(row.onuHealthStatus || row.opticalLink?.healthStatus) === 'power_off'" content="网管检测到 Dying Gasp 掉电遗言，点击跳转排障" placement="top">
                    <el-tag type="danger" effect="dark" size="small" style="cursor: pointer; font-weight: 600;" @click.stop="goToPon(row.onuDeviceId)">
                      【终端箱掉电】
                    </el-tag>
                  </el-tooltip>
                  <el-tooltip v-else-if="(row.onuHealthStatus || row.opticalLink?.healthStatus) === 'trunk_broken'" content="同 PON 口全量设备全灭，主干光缆中断，点击排障" placement="top">
                    <el-tag type="danger" effect="dark" size="small" style="cursor: pointer; font-weight: 600; background-color: #8b0000; border-color: #8b0000;" @click.stop="goToPon(row.onuDeviceId)">
                      【主干光缆中断】
                    </el-tag>
                  </el-tooltip>
                  <el-tooltip v-else-if="(row.onuHealthStatus || row.opticalLink?.healthStatus) === 'splitter_broken'" content="分光器或支干光缆并发中断，点击排障" placement="top">
                    <el-tag type="warning" effect="dark" size="small" style="cursor: pointer; font-weight: 600; background-color: #9333ea; border-color: #9333ea; color: #fff;" @click.stop="goToPon(row.onuDeviceId)">
                      【支干/分光中断】
                    </el-tag>
                  </el-tooltip>
                  <el-tooltip v-else-if="(row.onuHealthStatus || row.opticalLink?.healthStatus) === 'fiber_cut'" content="分支光纤断纤或弱光中断，点击排障" placement="top">
                    <el-tag type="danger" effect="plain" size="small" style="cursor: pointer; font-weight: 600;" @click.stop="goToPon(row.onuDeviceId)">
                      【光网断纤】
                    </el-tag>
                  </el-tooltip>
                  <el-tooltip v-else-if="(row.onuHealthStatus || row.opticalLink?.healthStatus) === 'sub_health_weak_light'" content="光功率劣化亚健康预警，点击排障" placement="top">
                    <el-tag type="warning" effect="dark" size="small" style="cursor: pointer; font-weight: 600;" @click.stop="goToPon(row.onuDeviceId)">
                      【弱光亚健康 🟡】
                    </el-tag>
                  </el-tooltip>
                  <el-tooltip v-else-if="(row.onuHealthStatus || row.opticalLink?.healthStatus) === 'jitter_buffering'" content="设备状态跳变处于 30 秒防抖缓冲期" placement="top">
                    <el-tag type="info" effect="dark" size="small" style="cursor: pointer; font-weight: 600;" @click.stop="goToPon(row.onuDeviceId)">
                      【防抖缓冲】
                    </el-tag>
                  </el-tooltip>
                  <template v-else>
                    <StatusBadge :value="row.status" />
                    <span class="text-muted" style="font-size: 10px;">光网通畅·末端故障</span>
                  </template>
                </template>
                <template v-else>
                  <StatusBadge :value="row.status" />
                </template>
              </template>
              <template v-else>
                <StatusBadge :value="row.status" />
                <el-tooltip v-if="(row.onuHealthStatus || row.opticalLink?.healthStatus) === 'sub_health_weak_light'" content="接收光功率劣化预警" placement="top">
                  <el-tag type="warning" size="small" effect="plain" style="cursor: pointer; font-size: 10.5px;" @click.stop="goToPon(row.onuDeviceId)">
                    弱光亚健康
                  </el-tag>
                </el-tooltip>
                <el-tooltip v-else-if="(row.onuHealthStatus || row.opticalLink?.healthStatus) === 'jitter_buffering'" content="设备处于防抖缓冲期" placement="top">
                  <el-tag type="info" size="small" effect="plain" style="cursor: pointer; font-size: 10.5px;" @click.stop="goToPon(row.onuDeviceId)">
                    防抖缓冲
                  </el-tag>
                </el-tooltip>
              </template>
            </div>
          </template>
        </el-table-column>

        <el-table-column label="归属空间区域" min-width="130" show-overflow-tooltip>
          <template #default="{ row }">
            <div v-if="getUnitAreaNode(row.unitId)">
              <span style="font-weight: 600; color: var(--el-text-color-primary);">
                {{ getUnitAreaNode(row.unitId)?.name }}
              </span>
              <span v-if="getUnitWorkshopNode(row.unitId)" class="text-muted" style="font-size: 11px; margin-left: 4px;">
                ({{ getUnitWorkshopNode(row.unitId)?.name }})
              </span>
            </div>
            <span v-else class="text-muted">—</span>
          </template>
        </el-table-column>

        <el-table-column label="划拨工位单元" min-width="130" show-overflow-tooltip>
          <template #default="{ row }">
            <el-link
              v-if="row.unitId"
              type="primary"
              :underline="false"
              style="font-weight: 600;"
              @click.stop="goToOrganization(row.unitId)"
            >
              {{ getUnitLabel(row.unitId) }}
            </el-link>
            <el-tag v-else size="small" type="info">未划拨</el-tag>
          </template>
        </el-table-column>


        <el-table-column prop="codec" label="编码" width="70" align="center" />

        <el-table-column label="操作" width="190" fixed="right" align="right">
          <template #default="{ row }">
            <el-button link type="primary" :icon="VideoPlay" @click="openInspect(row as Channel)">
              点检
            </el-button>
            <el-button link type="primary" :icon="InfoFilled" @click="showDetail(row as Channel)">
              详情
            </el-button>
            <el-button v-if="auth.can('channel.assign')" link type="primary" :icon="Edit" @click="openEdit(row as Channel)">
              编辑
            </el-button>
          </template>
        </el-table-column>
      </el-table>

      <!-- 分页工具栏 -->
      <div class="pagination-bar">
        <el-pagination
          v-model:current-page="page"
          v-model:page-size="pageSize"
          :total="total"
          :page-sizes="[15, 30, 50, 100]"
          layout="total, sizes, prev, pager, next, jumper"
          background
          size="small"
          @current-change="load()"
          @size-change="load(true)"
        />
      </div>
    </el-card>

    <!-- 弹窗与抽屉集合 -->
    <AddCameraDialog
      v-model="addCameraOpen"
      :organization="organization"
      :devices="devices"
      :initial-unit-id="addCameraInitialUnitId"
      @saved="onCameraAdded"
    />

    <BatchClaimChannelsDialog
      v-model="batchClaimOpen"
      :organization="organization"
      :initial-unit-id="unitId"
      @saved="onBatchActionSaved"
    />

    <BatchNetworkBindingDialog
      v-model="batchBindingOpen"
      :channels="channelsForBinding"
      @saved="onBatchActionSaved"
      @open-terminal-box="openWorkbench"
    />

    <ChannelAssignDialog
      v-model="assignmentOpen"
      :channels="selectedForAssign"
      :organization="organization"
      @assigned="onAssigned"
    />

    <ChannelDetailDrawer
      v-model="detailDrawer"
      :channel="currentChannel"
      :organization="organization"
      :devices="devices"
      :can-assign="auth.can('channel.assign')"
      @edit="openEdit"
    />

    <ChannelInspectDrawer
      v-model="inspectDrawer"
      :channel="inspectChannel"
      :all-channels="allChannels"
      :organization="organization"
      @select="inspectChannel = $event"
    />

    <ChannelEditDialog
      v-model="editDialog"
      :channel="editTarget"
      :organization="organization"
      @saved="onChannelSaved"
    />

    <TerminalBoxWorkbenchDrawer
      v-model="workbenchDrawerOpen"
      :box-id="viewingBoxId"
      :onu-id="viewingOnuId"
      @refresh="load()"
    />
  </div>
</template>

<style scoped>
.channels-page {
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.kpi-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(220px, 1fr));
  gap: 16px;
}

.kpi-card {
  background: var(--el-bg-color);
  border: 1px solid var(--el-border-color-lighter);
  border-radius: 8px;
  padding: 14px 18px;
  display: flex;
  align-items: center;
  gap: 14px;
  transition: all 0.2s ease;
  user-select: none;
}
.kpi-card:hover {
  transform: translateY(-2px);
  box-shadow: 0 4px 12px rgba(0, 0, 0, 0.05);
  border-color: var(--el-color-primary-light-5);
}

.kpi-icon-wrap {
  width: 44px;
  height: 44px;
  border-radius: 10px;
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 20px;
  flex-shrink: 0;
}
.kpi-icon-wrap.blue { background: rgba(59, 130, 246, 0.12); color: #2563eb; }
.kpi-icon-wrap.teal { background: rgba(16, 185, 129, 0.12); color: #059669; }
.kpi-icon-wrap.amber { background: rgba(245, 158, 11, 0.12); color: #d97706; }
.kpi-icon-wrap.purple { background: rgba(139, 92, 246, 0.12); color: #7c3aed; }

.kpi-content {
  flex: 1;
  min-width: 0;
}
.kpi-label {
  font-size: 12px;
  color: var(--el-text-color-secondary);
}
.kpi-value {
  font-size: 22px;
  font-weight: 700;
  line-height: 1.2;
  margin: 2px 0;
  color: var(--el-text-color-primary);
}
.kpi-sub {
  font-size: 11px;
  color: var(--el-text-color-placeholder);
}

.table-card {
  border-radius: 8px;
}

.table-toolbar {
  margin-bottom: 16px;
}

.toolbar-filters {
  display: flex;
  flex-wrap: wrap;
  gap: 10px;
  align-items: center;
}

.batch-action-bar {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 10px 16px;
  background: var(--el-color-primary-light-9);
  border: 1px solid var(--el-color-primary-light-7);
  border-radius: 6px;
  margin-bottom: 14px;
}
.batch-info {
  display: flex;
  align-items: center;
  gap: 8px;
  font-size: 13px;
  color: var(--el-color-primary-dark-2);
}
.batch-buttons {
  display: flex;
  align-items: center;
  gap: 8px;
}

.error-banner {
  padding: 8px 12px;
  background: var(--el-color-danger-light-9);
  border: 1px solid var(--el-color-danger-light-7);
  color: var(--el-color-danger);
  border-radius: 6px;
  display: flex;
  align-items: center;
  gap: 8px;
  margin-bottom: 12px;
  font-size: 13px;
}

.pagination-bar {
  display: flex;
  justify-content: flex-end;
  margin-top: 16px;
  padding-top: 12px;
  border-top: 1px solid var(--el-border-color-lighter);
}

.text-muted {
  color: var(--el-text-color-placeholder);
}
</style>
