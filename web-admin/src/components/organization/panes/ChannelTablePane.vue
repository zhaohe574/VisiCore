<script setup lang="ts">
import { computed, ref } from 'vue'
import {
  ArrowRight,
  CircleCheck,
  Close,
  Connection,
  Edit,
  FolderOpened,
  InfoFilled,
  Plus,
  Search,
  Sort,
  VideoPlay,
  Warning
} from '@element-plus/icons-vue'
import type { Channel, Device, Organization, OrganizationNode } from '../../../api'
import {
  buildFullOrgCascaderOptions,
  getUnitArea,
  getUnitName,
  getUnitWorkshop,
  type BreadcrumbItem,
  type OrgNodeKind
} from '../../../lib/organization'
import StatusBadge from '../../StatusBadge.vue'

const props = defineProps<{
  items: Channel[]
  total: number
  loading: boolean
  error: string
  devices: Device[]
  organization: Organization
  selectedScope: { kind: OrgNodeKind | 'all' | 'unassigned'; id?: number; name: string }
  breadcrumbs: BreadcrumbItem[]
  unitId?: number
  currentUnitName: string
  canAssign: boolean
  canManageDevice: boolean
  search: string
  deviceId?: number
  online: string
  assignedFilter: string
  page: number
  pageSize: number
}>()

const emit = defineEmits<{
  (e: 'update:search', val: string): void
  (e: 'update:deviceId', val: number | undefined): void
  (e: 'update:online', val: string): void
  (e: 'update:assignedFilter', val: string): void
  (e: 'update:page', val: number): void
  (e: 'update:pageSize', val: number): void
  (e: 'breadcrumbClick', item: BreadcrumbItem): void
  (e: 'clearScope'): void
  (e: 'selectScope', kind: OrgNodeKind | 'all' | 'unassigned', id?: number, name?: string): void
  (e: 'query', resetPage?: boolean): void
  (e: 'reset'): void
  (e: 'addCamera'): void
  (e: 'batchClaim'): void
  (e: 'batchNetworkBinding', channels: Channel[]): void
  (e: 'openTerminalBox', boxId?: number | null, onuId?: number | null): void
  (e: 'sortActiveUnit'): void
  (e: 'inspect', channel: Channel): void
  (e: 'detail', channel: Channel): void
  (e: 'edit', channel: Channel): void
  (e: 'goToPon', onuId?: number | null, search?: string | null): void
  (e: 'assignBatch', channels: Channel[]): void
  (e: 'assignToCurrentUnit', channels: Channel[]): void
}>()

const selected = ref<Channel[]>([])

const orgCascaderOptions = computed(() => buildFullOrgCascaderOptions(props.organization))
const orgScopeModel = computed({
  get: () => {
    if (props.selectedScope.kind === 'workshops' && props.selectedScope.id) return `workshop:${props.selectedScope.id}`
    if (props.selectedScope.kind === 'areas' && props.selectedScope.id) return `area:${props.selectedScope.id}`
    if (props.selectedScope.kind === 'units' && props.selectedScope.id) return `unit:${props.selectedScope.id}`
    return undefined
  },
  set: (val: string | undefined) => {
    if (!val) {
      emit('selectScope', 'all')
    } else {
      const [type, idStr] = val.split(':')
      const id = parseInt(idStr, 10)
      if (type === 'workshop') emit('selectScope', 'workshops', id)
      else if (type === 'area') emit('selectScope', 'areas', id)
      else if (type === 'unit') emit('selectScope', 'units', id)
    }
  }
})

const searchModel = computed({
  get: () => props.search,
  set: (val: string) => emit('update:search', val)
})

const deviceModel = computed({
  get: () => props.deviceId,
  set: (val: number | undefined) => emit('update:deviceId', val)
})

const onlineModel = computed({
  get: () => props.online,
  set: (val: string) => emit('update:online', val)
})

const assignedModel = computed({
  get: () => props.assignedFilter,
  set: (val: string) => emit('update:assignedFilter', val)
})

const pageModel = computed({
  get: () => props.page,
  set: (val: number) => emit('update:page', val)
})

const pageSizeModel = computed({
  get: () => props.pageSize,
  set: (val: number) => emit('update:pageSize', val)
})

function getUnitAreaNode(uId?: number | null): OrganizationNode | undefined {
  return getUnitArea(props.organization, uId)
}

function getUnitWorkshopNode(uId?: number | null): OrganizationNode | undefined {
  return getUnitWorkshop(props.organization, uId)
}

function getUnitLabel(uId?: number | null): string {
  return getUnitName(props.organization, uId)
}

function getChannelIp(channel?: Channel | Record<string, any> | null): string {
  if (!channel || !channel.ip) return '—'
  return String(channel.ip)
}

function clearSelection() {
  selected.value = []
}

defineExpose({
  clearSelection
})
</script>

<template>
  <section class="card channel-main-panel" style="padding: 20px; min-width: 0;">
    <!-- 层级面包屑指示栏 (Breadcrumb Bar) -->
    <div class="scope-breadcrumb-bar">
      <div class="breadcrumb-content">
        <span class="breadcrumb-label">当前空间范围:</span>
        <el-breadcrumb separator-icon="ArrowRight">
          <el-breadcrumb-item
            v-for="(item, idx) in breadcrumbs"
            :key="idx"
          >
            <a
              v-if="idx < breadcrumbs.length - 1"
              class="breadcrumb-link"
              @click.prevent="emit('breadcrumbClick', item)"
            >
              {{ item.name }}
            </a>
            <span v-else class="breadcrumb-current">{{ item.name }}</span>
          </el-breadcrumb-item>
        </el-breadcrumb>
        <span class="breadcrumb-count-badge">
          (匹配 {{ total }} 路通道)
        </span>
      </div>

      <div v-if="selectedScope.kind !== 'all'" class="breadcrumb-extra">
        <el-button link type="primary" size="small" :icon="Close" @click="emit('clearScope')">
          清除范围筛选 (查看全部)
        </el-button>
      </div>
    </div>

    <!-- 筛选与操作面板 -->
    <div class="filter-panel" style="margin-bottom: 16px;">
      <div class="filter-row" style="display: flex; flex-wrap: wrap; gap: 10px; align-items: center;">
        <el-input
          v-model="searchModel"
          placeholder="搜索别名、原名或 IP..."
          :prefix-icon="Search"
          clearable
          style="width: 200px;"
          @clear="emit('query', true)"
          @keyup.enter="emit('query', true)"
        />

        <el-cascader
          v-model="orgScopeModel"
          :options="orgCascaderOptions"
          :props="{ checkStrictly: true, emitPath: false }"
          placeholder="归属组织 (车间/区域/单元)"
          clearable
          filterable
          style="width: 220px;"
        />

        <el-select
          v-model="deviceModel"
          placeholder="归属录像机 (NVR)"
          clearable
          filterable
          style="width: 170px;"
          @change="emit('query', true)"
        >
          <el-option v-for="d in devices" :key="d.id" :value="d.id" :label="`${d.name} (${d.host})`" />
        </el-select>

        <el-select
          v-model="onlineModel"
          placeholder="在线状态"
          clearable
          style="width: 115px;"
          @change="emit('query', true)"
        >
          <el-option label="全部状态" value="" />
          <el-option label="在线通道" value="online" />
          <el-option label="离线通道" value="offline" />
        </el-select>

        <el-select
          v-if="selectedScope.kind === 'all'"
          v-model="assignedModel"
          placeholder="划拨状态"
          clearable
          style="width: 125px;"
          @change="emit('query', true)"
        >
          <el-option label="全部划拨" value="" />
          <el-option label="已划拨单元" value="true" />
          <el-option label="未划拨通道" value="false" />
        </el-select>

        <el-button-group>
          <el-button type="primary" :icon="Search" @click="emit('query', true)">查询</el-button>
          <el-button @click="emit('reset')">重置</el-button>
        </el-button-group>

        <!-- 聚焦单元上下文操作：调整当前单元排序 -->
        <el-button
          v-if="unitId"
          type="warning"
          plain
          :icon="Sort"
          @click="emit('sortActiveUnit')"
        >
          调整当前单元排序
        </el-button>
      </div>

      <!-- 批量操作工具栏 -->
      <div v-if="selected.length" class="batch-action-bar">
        <div class="batch-info">
          <el-icon><CircleCheck /></el-icon>
          <span>已勾选 <strong>{{ selected.length }}</strong> 路监控通道</span>
        </div>
        <div class="batch-buttons">
          <el-button
            v-if="unitId"
            type="primary"
            size="small"
            @click="emit('assignToCurrentUnit', selected)"
          >
            直接划拨至当前单元 ({{ currentUnitName }})
          </el-button>
          <el-button type="success" size="small" @click="emit('assignBatch', selected)">
            批量划拨至单元...
          </el-button>
          <el-button
            type="warning"
            size="small"
            :icon="Connection"
            @click="emit('batchNetworkBinding', selected)"
          >
            批量设置网络承载...
          </el-button>
          <el-button size="small" @click="selected = []">取消勾选</el-button>
        </div>
      </div>
    </div>

    <div v-if="error" class="error-banner" style="margin-bottom: 16px;">
      <el-icon><Warning /></el-icon>
      <span>{{ error }}</span>
    </div>

    <div class="table-card">
      <el-table
        v-loading="loading"
        :data="items"
        row-key="id"
        empty-text="当前空间或筛选条件下未找到匹配的通道记录"
        @selection-change="selected = $event as Channel[]"
      >
        <el-table-column v-if="canAssign" type="selection" width="44" />

        <el-table-column prop="alias" label="通道别名" min-width="120" show-overflow-tooltip>
          <template #default="{ row }">
            <span v-if="row.alias" style="font-weight: 600; color: var(--primary);">
              {{ row.alias }}
            </span>
            <span v-else class="muted">未设置别名</span>
          </template>
        </el-table-column>

        <el-table-column prop="name" label="设备原始名称" min-width="130" show-overflow-tooltip />

        <el-table-column label="IP 地址" min-width="125" show-overflow-tooltip>
          <template #default="{ row }">
            <span class="monospace" style="font-size: 12.5px; font-weight: 500;">
              {{ getChannelIp(row) }}
            </span>
          </template>
        </el-table-column>

        <el-table-column prop="deviceName" label="归属设备" min-width="120" show-overflow-tooltip />

        <el-table-column prop="deviceChannel" label="通道号" width="70" align="center" />

        <el-table-column label="承载网络与端口" min-width="190">
          <template #default="{ row }">
            <div v-if="row.terminalBoxName || row.onuName || row.portLabel || row.networkPort" style="font-size: 12px; line-height: 1.4;">
              <div style="font-weight: 500; display: flex; align-items: center; gap: 4px; flex-wrap: wrap;">
                <el-tag
                  v-if="row.terminalBoxName"
                  size="small"
                  type="info"
                  style="cursor: pointer;"
                  title="点击打开网络终端箱运维工作台"
                  @click.stop="emit('openTerminalBox', row.terminalBoxId, row.onuDeviceId)"
                >
                  {{ row.terminalBoxName }}
                </el-tag>
                <span
                  v-if="row.onuAlias || row.onuName"
                  style="color: var(--primary); cursor: pointer; text-decoration: underline;"
                  title="点击打开网络终端箱/ONU运维工作台"
                  @click.stop="emit('openTerminalBox', row.terminalBoxId, row.onuDeviceId)"
                >
                  {{ row.onuAlias || row.onuName }}
                </span>
                <el-tag
                  v-if="row.onuIfDesc"
                  size="small"
                  effect="plain"
                  type="info"
                  style="font-size: 10px; transform: scale(0.9);"
                >
                  {{ row.onuIfDesc }}
                </el-tag>
              </div>
              <div class="muted" style="font-size: 11px; margin-top: 2px; display: flex; align-items: center; gap: 6px;">
                <span v-if="row.portLabel">{{ row.portLabel }}</span>
                <span v-else-if="row.networkPort">
                  {{ row.isSwitchPort ? `交换机 #${row.networkPort} 口` : `ONU LAN${row.networkPort}` }}
                </span>
                <span v-if="row.onuRxPower !== null && row.onuRxPower !== undefined" class="monospace" style="font-size: 10.5px;">
                  {{ row.onuRxPower }}dBm
                </span>
              </div>
            </div>
            <span v-else class="muted">—</span>
          </template>
        </el-table-column>

        <el-table-column v-if="unitId" label="排序" width="65" align="center">
          <template #default="{ row }">
            <el-tag v-if="row.sortOrder" size="small" effect="plain" type="warning" style="font-weight: 600;">
              {{ row.sortOrder }}
            </el-tag>
            <span v-else class="muted">—</span>
          </template>
        </el-table-column>

        <el-table-column label="在线状态" min-width="125" align="center">
          <template #default="{ row }">
            <div style="display: flex; flex-direction: column; align-items: center; gap: 3px;">
              <template v-if="row.status !== 'online'">
                <template v-if="row.onuDeviceId && (row.onuHealthStatus || row.opticalLink?.healthStatus)">
                  <el-tooltip v-if="(row.onuHealthStatus || row.opticalLink?.healthStatus) === 'power_off'" content="网管检测到 Dying Gasp 掉电遗言，点击跳转排障" placement="top">
                    <el-tag type="danger" effect="dark" size="small" style="cursor: pointer; font-weight: 600;" @click.stop="emit('goToPon', row.onuDeviceId)">
                      【终端箱掉电】
                    </el-tag>
                  </el-tooltip>
                  <el-tooltip v-else-if="(row.onuHealthStatus || row.opticalLink?.healthStatus) === 'trunk_broken'" content="同 PON 口全量设备全灭，主干光缆中断，点击排障" placement="top">
                    <el-tag type="danger" effect="dark" size="small" style="cursor: pointer; font-weight: 600; background-color: #8b0000; border-color: #8b0000;" @click.stop="emit('goToPon', row.onuDeviceId)">
                      【主干光缆中断】
                    </el-tag>
                  </el-tooltip>
                  <el-tooltip v-else-if="(row.onuHealthStatus || row.opticalLink?.healthStatus) === 'splitter_broken'" content="分光器或支干光缆并发中断，点击排障" placement="top">
                    <el-tag type="warning" effect="dark" size="small" style="cursor: pointer; font-weight: 600; background-color: #9333ea; border-color: #9333ea; color: #fff;" @click.stop="emit('goToPon', row.onuDeviceId)">
                      【支干/分光中断】
                    </el-tag>
                  </el-tooltip>
                  <el-tooltip v-else-if="(row.onuHealthStatus || row.opticalLink?.healthStatus) === 'fiber_cut'" content="分支光纤断纤或弱光中断，点击排障" placement="top">
                    <el-tag type="danger" effect="plain" size="small" style="cursor: pointer; font-weight: 600;" @click.stop="emit('goToPon', row.onuDeviceId)">
                      【光网断纤】
                    </el-tag>
                  </el-tooltip>
                  <el-tooltip v-else-if="(row.onuHealthStatus || row.opticalLink?.healthStatus) === 'sub_health_weak_light'" content="光功率劣化亚健康预警，点击排障" placement="top">
                    <el-tag type="warning" effect="dark" size="small" style="cursor: pointer; font-weight: 600;" @click.stop="emit('goToPon', row.onuDeviceId)">
                      【弱光亚健康 🟡】
                    </el-tag>
                  </el-tooltip>
                  <el-tooltip v-else-if="(row.onuHealthStatus || row.opticalLink?.healthStatus) === 'jitter_buffering'" content="设备状态跳变处于 30 秒防抖缓冲期，正在过滤工频抖动" placement="top">
                    <el-tag type="info" effect="dark" size="small" style="cursor: pointer; font-weight: 600;" @click.stop="emit('goToPon', row.onuDeviceId)">
                      【防抖缓冲】
                    </el-tag>
                  </el-tooltip>
                  <template v-else>
                    <StatusBadge :value="row.status" />
                    <span class="muted" style="font-size: 10px;">光网通畅·末端故障</span>
                  </template>
                </template>
                <template v-else>
                  <StatusBadge :value="row.status" />
                </template>
              </template>
              <template v-else>
                <StatusBadge :value="row.status" />
                <el-tooltip v-if="(row.onuHealthStatus || row.opticalLink?.healthStatus) === 'sub_health_weak_light'" content="接收光功率 -25~-27dBm 亚健康预警，随时面临断流" placement="top">
                  <el-tag type="warning" size="small" effect="plain" style="cursor: pointer; font-size: 10.5px;" @click.stop="emit('goToPon', row.onuDeviceId)">
                    弱光亚健康
                  </el-tag>
                </el-tooltip>
                <el-tooltip v-else-if="(row.onuHealthStatus || row.opticalLink?.healthStatus) === 'jitter_buffering'" content="设备处于 30 秒防抖缓冲期" placement="top">
                  <el-tag type="info" size="small" effect="plain" style="cursor: pointer; font-size: 10.5px;" @click.stop="emit('goToPon', row.onuDeviceId)">
                    防抖缓冲
                  </el-tag>
                </el-tooltip>
              </template>
            </div>
          </template>
        </el-table-column>

        <el-table-column label="归属区域" min-width="120" show-overflow-tooltip>
          <template #default="{ row }">
            <div v-if="getUnitAreaNode(row.unitId)" class="unit-cell">
              <span style="font-weight: 600; color: var(--text-primary);">
                {{ getUnitAreaNode(row.unitId)?.name }}
              </span>
              <span v-if="getUnitWorkshopNode(row.unitId)" class="unit-parent-label">
                {{ getUnitWorkshopNode(row.unitId)?.name }}
              </span>
            </div>
            <span v-else class="muted">—</span>
          </template>
        </el-table-column>

        <el-table-column label="划拨单元" min-width="110" show-overflow-tooltip>
          <template #default="{ row }">
            <el-tag v-if="row.unitId" size="small" type="success" effect="plain" style="font-weight: 500;">
              {{ getUnitLabel(row.unitId) }}
            </el-tag>
            <el-tag v-else size="small" type="info">未分配</el-tag>
          </template>
        </el-table-column>

        <el-table-column label="承载光链路" min-width="180" show-overflow-tooltip>
          <template #default="{ row }">
            <div v-if="row.onuDeviceId || row.terminalBoxId" style="cursor: pointer; display: flex; flex-direction: column; gap: 2px;" @click.stop="emit('goToPon', row.onuDeviceId)">
              <div style="display: flex; align-items: center; gap: 4px;">
                <el-icon style="color: #0284c7; font-size: 13px;"><Connection /></el-icon>
                <span style="font-weight: 500; color: var(--text-primary); font-size: 12px;">
                  {{ row.terminalBoxName || row.terminalBoxCode || '终端箱' }}
                </span>
                <span class="muted" style="font-size: 11px;">/ {{ row.onuAlias || row.onuName || 'ONU' }}</span>
              </div>
              <div style="font-size: 11px; color: #64748b;">
                <span v-if="row.portLabel">{{ row.portLabel }}</span>
                <span v-else-if="row.networkPort">{{ row.isSwitchPort ? `SW-P${row.networkPort}` : `LAN${row.networkPort}` }}</span>
                <span v-if="row.onuRxPower !== null && row.onuRxPower !== undefined" :style="{ color: row.onuRxPower < -24 ? '#d97706' : '#10b981', marginLeft: '6px' }">
                  {{ row.onuRxPower }} dBm
                </span>
              </div>
            </div>
            <span v-else class="muted">未关联光网</span>
          </template>
        </el-table-column>

        <el-table-column prop="codec" label="编码" width="70" align="center" />

        <el-table-column label="操作" width="200" fixed="right">
          <template #default="{ row }">
            <div class="table-tools">
              <el-button link type="primary" :icon="VideoPlay" @click="emit('inspect', row as Channel)">
                点检
              </el-button>
              <el-button link type="primary" :icon="InfoFilled" @click="emit('detail', row as Channel)">
                详情
              </el-button>
              <el-button v-if="canAssign" link type="primary" :icon="Edit" @click="emit('edit', row as Channel)">
                编辑
              </el-button>
            </div>
          </template>
        </el-table-column>
      </el-table>

      <div class="pagination-bar">
        <el-pagination
          v-model:current-page="pageModel"
          v-model:page-size="pageSizeModel"
          :total="total"
          :page-sizes="[15, 30, 50, 100]"
          layout="total, sizes, prev, pager, next, jumper"
          @current-change="emit('query')"
          @size-change="emit('query', true)"
        />
      </div>
    </div>
  </section>
</template>

<style scoped>
.channel-main-panel {
  background: #ffffff;
}

.scope-breadcrumb-bar {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 10px 14px;
  background: #f8fafc;
  border: 1px solid var(--border);
  border-radius: var(--radius-md);
  margin-bottom: 16px;
}

.breadcrumb-content {
  display: flex;
  align-items: center;
  gap: 8px;
  flex-wrap: wrap;
}

.breadcrumb-label {
  font-size: 12.5px;
  font-weight: 600;
  color: var(--text-secondary);
}

.breadcrumb-link {
  color: var(--primary);
  cursor: pointer;
  font-weight: 500;
  font-size: 13px;
}

.breadcrumb-link:hover {
  text-decoration: underline;
}

.breadcrumb-current {
  font-size: 13px;
  font-weight: 600;
  color: var(--text-primary);
}

.breadcrumb-count-badge {
  font-size: 12px;
  color: var(--text-muted);
  margin-left: 4px;
}

.filter-panel {
  margin-bottom: 16px;
}

.batch-action-bar {
  margin-top: 12px;
  padding: 8px 14px;
  background: #f0fdf4;
  border: 1px solid #bbf7d0;
  border-radius: var(--radius-md);
  display: flex;
  align-items: center;
  justify-content: space-between;
}

.batch-info {
  font-size: 13px;
  color: #166534;
  display: flex;
  align-items: center;
  gap: 8px;
}

.batch-buttons {
  display: flex;
  gap: 8px;
  align-items: center;
}

.table-card {
  background: #ffffff;
}

.table-tools {
  display: flex;
  align-items: center;
  gap: 4px;
}

.pagination-bar {
  display: flex;
  justify-content: flex-end;
  padding: 12px 16px;
}

.unit-cell {
  display: inline-flex;
  flex-direction: column;
  align-items: flex-start;
  gap: 2px;
  line-height: 1.2;
}

.unit-parent-label {
  font-size: 11.5px;
  color: var(--text-secondary, #6b7280);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
  max-width: 170px;
}

.error-banner {
  background: #fef2f2;
  border: 1px solid #fecaca;
  color: #ef4444;
  padding: 8px 12px;
  border-radius: 6px;
  display: flex;
  align-items: center;
  gap: 8px;
  font-size: 12.5px;
}

.monospace {
  font-family: 'JetBrains Mono', monospace;
}
.muted {
  color: var(--text-muted, #64748b);
}
</style>
