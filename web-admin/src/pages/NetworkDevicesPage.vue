<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useActivePage } from '../composables/useActivePage'
import {
  CircleCheck,
  Connection,
  Cpu,
  Delete,
  Edit,
  Location,
  Platform,
  Plus,
  Refresh,
  Search,
  VideoPlay,
  Warning
} from '@element-plus/icons-vue'
import { ElMessage } from 'element-plus'
import PageHeader from '../components/PageHeader.vue'
import NetworkDeviceDialog from '../components/network/NetworkDeviceDialog.vue'
import DevicePortManager from '../components/network/DevicePortManager.vue'
import {
  managementApi,
  networkDeviceApi,
  type NetworkDevice,
  type Organization
} from '../api'
import { buildAreaCascaderOptions } from '../lib/organization'
import { useAuth } from '../stores/auth'
import { useAction } from '../composables/useAction'

const auth = useAuth()
const { confirm } = useAction()

const loading = ref(false)
const probingAll = ref(false)
const probingId = ref<number | null>(null)

// 筛选条件
const search = ref('')
const deviceTypeFilter = ref('')
const statusFilter = ref('')
const areaFilter = ref<number | null>(null)

// 分页与数据
const page = ref(1)
const pageSize = ref(20)
const total = ref(0)
const items = ref<NetworkDevice[]>([])

// 组织机构
const organization = ref<Organization>({ workshops: [], areas: [], units: [] })

// 弹窗与抽屉
const dialogOpen = ref(false)
const editingDevice = ref<NetworkDevice | null>(null)
const drawerOpen = ref(false)
const drawerDevice = ref<NetworkDevice | null>(null)

function openDrawer(row: NetworkDevice) {
  drawerDevice.value = row
  drawerOpen.value = true
}

const areaCascaderOptions = computed(() => {
  return buildAreaCascaderOptions(organization.value)
})

// 统计总览
const stats = computed(() => {
  const onlineCount = items.value.filter(c => c.status === 'online').length
  const offlineCount = items.value.filter(c => c.status === 'offline').length
  const coreAndAgg = items.value.filter(c => c.deviceType === 'core_switch' || c.deviceType === 'agg_switch').length
  const totalIps = items.value.reduce((acc, cur) => acc + (cur.assignedIps?.length || 0), 0)
  return {
    total: total.value,
    online: onlineCount,
    offline: offlineCount,
    coreAndAgg,
    totalIps
  }
})

const deviceTypeLabels: Record<string, { label: string; type: '' | 'success' | 'warning' | 'danger' | 'info' }> = {
  core_switch: { label: '核心交换机', type: 'danger' },
  agg_switch: { label: '汇聚交换机', type: 'warning' },
  access_switch: { label: '接入交换机', type: 'primary' as any },
  router: { label: '路由器', type: 'success' },
  firewall: { label: '防火墙', type: 'danger' },
  ac: { label: '无线控制器', type: 'info' },
  server: { label: '服务器/存储', type: 'info' },
  other: { label: '其它设施', type: 'info' }
}

async function loadOrganization() {
  try {
    organization.value = await managementApi.organization()
  } catch {}
}

async function load(resetPage = false) {
  const ticket = lifecycle.ticket()
  if (ticket === undefined) return
  if (resetPage) page.value = 1
  loading.value = true
  try {
    const res = await networkDeviceApi.list({
      page: page.value,
      pageSize: pageSize.value,
      search: search.value.trim() || undefined,
      deviceType: deviceTypeFilter.value || undefined,
      status: statusFilter.value || undefined
    })
    if (!lifecycle.current(ticket)) return
    items.value = res.items
    total.value = res.total
  } catch (err: any) {
    if (lifecycle.current(ticket)) ElMessage.error(err.message || '加载网络设备列表失败')
  } finally {
    if (lifecycle.current(ticket)) loading.value = false
  }
}

const lifecycle = useActivePage(() => load())

function handleAdd() {
  editingDevice.value = null
  dialogOpen.value = true
}

function handleEdit(dev: NetworkDevice) {
  editingDevice.value = dev
  dialogOpen.value = true
}

async function handleDelete(dev: NetworkDevice) {
  await confirm(
    `确定删除网络设备「${dev.name}」？该设备所配置的所有 VLAN 接口 IP 分配记录将被一并解除。`,
    async () => {
      await networkDeviceApi.delete(dev.id)
      await load()
    },
    `已删除网络设备「${dev.name}」`
  )
}

async function handleProbe(dev: NetworkDevice) {
  probingId.value = dev.id
  try {
    const res = await networkDeviceApi.probe(dev.id)
    if (res.status === 'online') {
      ElMessage.success(`设备 [${res.name}] 在线！往返耗时: ${res.responseTimeMs} ms`)
    } else {
      ElMessage.warning(`设备 [${res.name}] 离线未响应`)
    }
    await load()
  } catch (err: any) {
    ElMessage.error(err.message || '探测失败')
  } finally {
    probingId.value = null
  }
}

async function handleProbeAll() {
  probingAll.value = true
  try {
    const results = await networkDeviceApi.probeAll()
    const onlineCount = results.filter(r => r.status === 'online').length
    ElMessage.success(`全网在线探测完成：共探测 ${results.length} 台设备，${onlineCount} 台在线，${results.length - onlineCount} 台离线`)
    await load()
  } catch (err: any) {
    ElMessage.error(err.message || '批量探测失败')
  } finally {
    probingAll.value = false
  }
}

onMounted(async () => {
  await Promise.all([load(), loadOrganization()])
})
</script>

<template>
  <div class="network-devices-page">
    <PageHeader
      title="网络设备管理"
      subtitle="集中管理园区/机房核心交换机、汇聚与接入交换机、路由器及各 VLAN 接口网关分配，消除虚假冲突"
    >
      <template #extra>
        <el-button
          v-if="auth.can('network_device.manage')"
          type="primary"
          :icon="Plus"
          @click="handleAdd"
        >
          新建网络设备
        </el-button>
        <el-button
          type="success"
          :icon="VideoPlay"
          :loading="probingAll"
          @click="handleProbeAll"
        >
          ⚡ 全网在线探测
        </el-button>
        <el-button :icon="Refresh" :loading="loading" @click="load()">
          刷新
        </el-button>
      </template>
    </PageHeader>

    <!-- 统计指标卡片 -->
    <div class="stats-row">
      <div class="stat-card">
        <div class="stat-icon total"><el-icon><Platform /></el-icon></div>
        <div class="stat-info">
          <div class="stat-label">网络设备总数</div>
          <div class="stat-value">{{ stats.total }}</div>
        </div>
      </div>
      <div class="stat-card">
        <div class="stat-icon core"><el-icon><Cpu /></el-icon></div>
        <div class="stat-info">
          <div class="stat-label">{{ stats.total > items.length ? `当前页核心/汇聚 (${stats.coreAndAgg})` : '核心 / 汇聚交换机' }}</div>
          <div class="stat-value text-amber-600">{{ stats.coreAndAgg }}</div>
        </div>
      </div>
      <div class="stat-card">
        <div class="stat-icon online"><el-icon><CircleCheck /></el-icon></div>
        <div class="stat-info">
          <div class="stat-label">{{ stats.total > items.length ? `当前页在线 (${stats.online}/${items.length})` : '当前在线设备' }}</div>
          <div class="stat-value text-emerald-600">{{ stats.online }}</div>
        </div>
      </div>
      <div class="stat-card">
        <div class="stat-icon offline"><el-icon><Warning /></el-icon></div>
        <div class="stat-info">
          <div class="stat-label">{{ stats.total > items.length ? `当前页离线 (${stats.offline}/${items.length})` : '离线 / 告警设备' }}</div>
          <div class="stat-value text-rose-600">{{ stats.offline }}</div>
        </div>
      </div>
      <div class="stat-card">
        <div class="stat-icon ip-count"><el-icon><Connection /></el-icon></div>
        <div class="stat-info">
          <div class="stat-label">已分配接口 IP 总数</div>
          <div class="stat-value text-blue-600">{{ stats.totalIps }}</div>
        </div>
      </div>
    </div>

    <!-- 搜索筛选栏 -->
    <div class="filter-bar">
      <div class="filter-left">
        <el-input
          v-model="search"
          placeholder="搜索设备名称 / 管理IP / MAC / 型号 / 位置 / 责任人..."
          :prefix-icon="Search"
          clearable
          style="width: 320px;"
          @change="load(true)"
          @clear="load(true)"
        />

        <el-select
          v-model="deviceTypeFilter"
          placeholder="设备类型"
          clearable
          style="width: 170px;"
          @change="load(true)"
        >
          <el-option label="全部类型" value="" />
          <el-option label="核心交换机" value="core_switch" />
          <el-option label="汇聚交换机" value="agg_switch" />
          <el-option label="接入交换机" value="access_switch" />
          <el-option label="路由器" value="router" />
          <el-option label="防火墙" value="firewall" />
          <el-option label="无线控制器" value="ac" />
          <el-option label="其它网络设施" value="other" />
        </el-select>

        <el-select
          v-model="statusFilter"
          placeholder="在线状态"
          clearable
          style="width: 130px;"
          @change="load(true)"
        >
          <el-option label="全部状态" value="" />
          <el-option label="在线" value="online" />
          <el-option label="离线" value="offline" />
          <el-option label="未知" value="unknown" />
        </el-select>
      </div>

      <div class="filter-right">
        <el-button type="primary" :icon="Search" @click="load(true)">查询</el-button>
        <el-button
          :icon="Refresh"
          @click="() => {
            search = '';
            deviceTypeFilter = '';
            statusFilter = '';
            load(true);
          }"
        >
          重置
        </el-button>
      </div>
    </div>

    <!-- 设备数据表格 -->
    <el-card shadow="never" class="table-card">
      <el-table
        v-loading="loading"
        :data="items"
        stripe
        class="device-table"
        row-key="id"
      >
        <el-table-column label="设备名称" min-width="200">
          <template #default="{ row }">
            <div class="dev-name-cell">
              <div class="dev-title font-semibold text-slate-800">{{ row.name }}</div>
              <div class="dev-type-tag">
                <el-tag
                  :type="deviceTypeLabels[row.deviceType]?.type || 'info'"
                  size="small"
                  effect="light"
                >
                  {{ deviceTypeLabels[row.deviceType]?.label || row.deviceType }}
                </el-tag>
                <span v-if="row.model" class="dev-model text-xs text-slate-500 font-mono">{{ row.vendor }} {{ row.model }}</span>
              </div>
            </div>
          </template>
        </el-table-column>

        <el-table-column label="管理 IP / MAC" width="180">
          <template #default="{ row }">
            <div class="ip-mac-cell font-mono">
              <div v-if="row.managementIp" class="mgmt-ip font-bold text-slate-800">
                {{ row.managementIp }}
              </div>
              <div v-else class="text-slate-400 text-xs">无管理 IP</div>
              <div v-if="row.mac" class="mac-text text-xs text-slate-500">{{ row.mac }}</div>
            </div>
          </template>
        </el-table-column>

        <el-table-column label="多 VLAN 接口分配 IP" min-width="280">
          <template #default="{ row }">
            <div v-if="row.assignedIps && row.assignedIps.length" class="assigned-ips-cell">
              <div
                v-for="ipItem in row.assignedIps"
                :key="ipItem.id || ipItem.ip"
                class="assigned-ip-badge"
              >
                <span class="ip font-mono font-semibold">{{ ipItem.ip }}</span>
                <span v-if="ipItem.vlanId" class="vlan">VLAN {{ ipItem.vlanId }}</span>
                <span class="role">{{ ipItem.interfaceName || 'Vlanif' }} ({{ ipItem.role === 'gateway' ? '网关' : ipItem.role }})</span>
              </div>
            </div>
            <span v-else class="text-slate-400 text-xs">暂无分配接口 IP</span>
          </template>
        </el-table-column>

        <el-table-column label="物理端口 / 跳线 / 虚机" width="190">
          <template #default="{ row }">
            <div class="ports-summary-cell">
              <div v-if="row.portCount" class="ports-badge" style="cursor: pointer;" @click="openDrawer(row as any)">
                <el-tag size="small" type="primary" effect="plain" class="cursor-pointer">
                  <el-icon><Connection /></el-icon>
                  {{ row.portCount }} 端口 ({{ row.linkedPortCount || 0 }} 连线)
                </el-tag>
              </div>
              <div v-else class="text-slate-400 text-xs">
                <el-button link type="primary" size="small" @click="openDrawer(row as any)">
                  + 批量生成端口
                </el-button>
              </div>
              <div v-if="row.deviceType === 'server'" class="vm-badge-row" style="margin-top: 3px;">
                <el-tag size="small" type="success" effect="light">
                  <el-icon><Platform /></el-icon>
                  {{ row.vmCount || 0 }} 虚机
                </el-tag>
              </div>
            </div>
          </template>
        </el-table-column>

        <el-table-column label="物理位置 / 责任人" width="180">
          <template #default="{ row }">
            <div class="loc-owner-cell text-xs">
              <div v-if="row.location" class="location text-slate-700">
                <el-icon><Location /></el-icon> {{ row.location }}
              </div>
              <div v-if="row.owner" class="owner text-slate-500 mt-1">
                责任人: {{ row.owner }}
              </div>
            </div>
          </template>
        </el-table-column>

        <el-table-column label="在线状态" width="120" align="center">
          <template #default="{ row }">
            <div class="status-cell">
              <el-tag
                :type="row.status === 'online' ? 'success' : row.status === 'offline' ? 'danger' : 'info'"
                size="small"
              >
                {{ row.status === 'online' ? '在线' : row.status === 'offline' ? '离线' : '未探测' }}
              </el-tag>
              <div v-if="row.responseTimeMs != null" class="rtt text-xs font-mono text-emerald-600 mt-0.5">
                {{ row.responseTimeMs }} ms
              </div>
            </div>
          </template>
        </el-table-column>

        <el-table-column label="操作" width="220" fixed="right" align="right">
          <template #default="{ row }">
            <el-button
              type="primary"
              link
              size="small"
              :icon="Connection"
              @click="openDrawer(row as any)"
            >
              端口背板
            </el-button>
            <el-button
              type="success"
              link
              size="small"
              :loading="probingId === row.id"
              @click="handleProbe(row as any)"
            >
              探测
            </el-button>
            <el-button
              v-if="auth.can('network_device.manage')"
              type="primary"
              link
              size="small"
              :icon="Edit"
              @click="handleEdit(row as any)"
            >
              编辑
            </el-button>
            <el-button
              v-if="auth.can('network_device.manage')"
              type="danger"
              link
              size="small"
              :icon="Delete"
              @click="handleDelete(row as any)"
            >
              删除
            </el-button>
          </template>
        </el-table-column>
      </el-table>

      <!-- 分页栏 -->
      <div class="pagination-bar">
        <el-pagination
          v-model:current-page="page"
          v-model:page-size="pageSize"
          :page-sizes="[10, 20, 50, 100]"
          :total="total"
          layout="total, sizes, prev, pager, next, jumper"
          @size-change="load(true)"
          @current-change="load()"
        />
      </div>
    </el-card>

    <!-- 弹窗组件 -->
    <NetworkDeviceDialog
      v-model="dialogOpen"
      :device="editingDevice"
      :organization="organization"
      @saved="load()"
    />

    <!-- 快速端口与跳线管理抽屉 -->
    <el-drawer
      v-model="drawerOpen"
      :title="`设备端口背板与跳线互联：${drawerDevice?.name || ''}`"
      size="800px"
      destroy-on-close
    >
      <div v-if="drawerDevice" style="padding: 0 4px;">
        <div style="margin-bottom: 12px; padding: 8px 12px; background: #f8fafc; border-radius: 6px; font-size: 13px; color: #475569; display: flex; gap: 16px;">
          <span>型号: <strong class="font-mono text-slate-800">{{ drawerDevice.vendor }} {{ drawerDevice.model || '标准网元' }}</strong></span>
          <span>管理 IP: <strong class="font-mono text-blue-600">{{ drawerDevice.managementIp || '未配置' }}</strong></span>
          <span>位置: <strong class="text-slate-800">{{ drawerDevice.location || '未标注' }}</strong></span>
        </div>
        <DevicePortManager
          :device-id="drawerDevice.id"
          :device-type="'network_device'"
          :device-name="drawerDevice.name"
          @changed="load()"
        />
      </div>
    </el-drawer>
  </div>
</template>

<style scoped>
.network-devices-page {
  padding: 16px 20px 32px;
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.stats-row {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(200px, 1fr));
  gap: 12px;
}

.stat-card {
  background: var(--el-bg-color);
  border: 1px solid var(--el-border-color-lighter);
  border-radius: 8px;
  padding: 14px 16px;
  display: flex;
  align-items: center;
  gap: 14px;
}

.stat-icon {
  width: 44px;
  height: 44px;
  border-radius: 8px;
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 22px;
}
.stat-icon.total { background: #f1f5f9; color: #475569; }
.stat-icon.core { background: #fef3c7; color: #d97706; }
.stat-icon.online { background: #ecfdf5; color: #059669; }
.stat-icon.offline { background: #fff1f2; color: #e11d48; }
.stat-icon.ip-count { background: #eff6ff; color: #2563eb; }

.stat-label {
  font-size: 12px;
  color: var(--el-text-color-secondary);
}

.stat-value {
  font-size: 22px;
  font-weight: 700;
  line-height: 1.2;
}

.filter-bar {
  display: flex;
  justify-content: space-between;
  align-items: center;
  background: var(--el-bg-color);
  border: 1px solid var(--el-border-color-lighter);
  border-radius: 8px;
  padding: 12px 16px;
  flex-wrap: wrap;
  gap: 12px;
}

.filter-left, .filter-right {
  display: flex;
  align-items: center;
  gap: 10px;
  flex-wrap: wrap;
}

.table-card {
  border-radius: 8px;
}

.dev-name-cell {
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.dev-type-tag {
  display: flex;
  align-items: center;
  gap: 6px;
}

.assigned-ips-cell {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
}

.assigned-ip-badge {
  display: inline-flex;
  align-items: center;
  gap: 4px;
  padding: 2px 6px;
  background: #f8fafc;
  border: 1px solid #cbd5e1;
  border-radius: 4px;
  font-size: 11px;
}

.assigned-ip-badge .ip {
  color: #1e293b;
}

.assigned-ip-badge .vlan {
  color: #0284c7;
  font-weight: 600;
}

.assigned-ip-badge .role {
  color: #64748b;
}

.loc-owner-cell .location {
  display: flex;
  align-items: center;
  gap: 4px;
}

.status-cell {
  display: flex;
  flex-direction: column;
  align-items: center;
}

.pagination-bar {
  margin-top: 16px;
  display: flex;
  justify-content: flex-end;
}
</style>
