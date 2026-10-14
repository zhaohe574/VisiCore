<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { useActivePage } from '../composables/useActivePage'
import {
  ArrowDown,
  CircleCheck,
  Close,
  Connection,
  DataLine,
  Delete,
  Download,
  Edit,
  InfoFilled,
  Location,
  Monitor,
  Platform,
  Plus,
  Refresh,
  Search,
  VideoCamera,
  VideoPlay,
  Warning
} from '@element-plus/icons-vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import PageHeader from '../components/PageHeader.vue'
import IpPoolDialog from '../components/ipam/IpPoolDialog.vue'
import ComputerDialog from '../components/computers/ComputerDialog.vue'
import NetworkDeviceDialog from '../components/network/NetworkDeviceDialog.vue'
import {
  allPages,
  computerApi,
  ipPoolApi,
  managementApi,
  networkDeviceApi,
  ponApi,
  type BoundDeviceInfo,
  type IpAddressStatus,
  type IpPool,
  type IpPoolDetail,
  type NetworkDevice,
  type OnuDeviceDto,
  type Organization,
  type TerminalBoxDto
} from '../api'
import { useAuth } from '../stores/auth'
import { useAction } from '../composables/useAction'

const auth = useAuth()
const { confirm } = useAction()

const loading = ref(false)
const scanning = ref(false)
const poolList = ref<IpPool[]>([])
const selectedPoolId = ref<number | null>(null)
const poolDetail = ref<IpPoolDetail | null>(null)

// 视图与分段
const viewMode = ref<'grid' | 'table'>('grid')
const activeSubnetSlice = ref<string>('all')
const statusFilter = ref<string>('all')
const tableSearch = ref('')

// 弹窗
const poolDialogOpen = ref(false)
const editingPool = ref<IpPool | null>(null)

// 选中某个 IP 单元格进行操作抽屉
const selectedCell = ref<IpAddressStatus | null>(null)
const cellDrawerOpen = ref(false)

// 快速登记电脑弹窗联动
const computerDialogOpen = ref(false)
const prefilledComputerIp = ref<string | null>(null)
const organization = ref<Organization>({ workshops: [], areas: [], units: [] })
const terminalBoxes = ref<TerminalBoxDto[]>([])
const onus = ref<OnuDeviceDto[]>([])

// 快速登记网络设备弹窗联动
const networkDeviceDialogOpen = ref(false)
const prefilledNetworkDeviceIp = ref<string | null>(null)
const prefilledNetworkDeviceVlanId = ref<number | null>(null)
const editingNetworkDevice = ref<NetworkDevice | null>(null)

// 当前选中的号段
const currentPool = computed(() => {
  return poolList.value.find(p => p.id === selectedPoolId.value) || poolDetail.value?.pool || null
})

// 当前过滤后的地址列表
const filteredAddresses = computed<IpAddressStatus[]>(() => {
  if (!poolDetail.value) return []
  let list = poolDetail.value.addresses

  // 按切片子网筛选 (例如 20.x, 21.x)
  if (activeSubnetSlice.value !== 'all') {
    list = list.filter(a => a.subnetSlice === activeSubnetSlice.value)
  }

  // 按状态筛选
  if (statusFilter.value !== 'all') {
    list = list.filter(a => a.status === statusFilter.value)
  }

  // 按关键词搜索
  if (tableSearch.value.trim()) {
    const q = tableSearch.value.trim().toLowerCase()
    list = list.filter(a =>
      a.ip.toLowerCase().includes(q) ||
      a.boundDevices.some(d =>
        d.deviceName.toLowerCase().includes(q) ||
        (d.mac && d.mac.toLowerCase().includes(q)) ||
        (d.owner && d.owner.toLowerCase().includes(q)) ||
        (d.location && d.location.toLowerCase().includes(q))
      )
    )
  }

  return list
})

// 明细列表客户端分页控制
const tablePage = ref(1)
const tablePageSize = ref(50)

const paginatedAddresses = computed<IpAddressStatus[]>(() => {
  const start = (tablePage.value - 1) * tablePageSize.value
  return filteredAddresses.value.slice(start, start + tablePageSize.value)
})

watch([selectedPoolId, activeSubnetSlice, statusFilter, tableSearch], () => {
  tablePage.value = 1
})

function ipToNumber(ip: string): number {
  const parts = ip.split('.').map(Number)
  if (parts.length !== 4 || parts.some(isNaN)) return 0
  return ((parts[0] << 24) >>> 0) + (parts[1] << 16) + (parts[2] << 8) + parts[3]
}

function sortPoolsByCidr(list: IpPool[]): IpPool[] {
  return [...list].sort((a, b) => {
    const ipA = (a.startIp || a.cidr.split('/')[0]).trim()
    const ipB = (b.startIp || b.cidr.split('/')[0]).trim()
    const numA = ipToNumber(ipA)
    const numB = ipToNumber(ipB)
    if (numA !== numB) return numA - numB
    const maskA = Number(a.cidr.split('/')[1]) || 32
    const maskB = Number(b.cidr.split('/')[1]) || 32
    return maskA - maskB
  })
}

// 按 IP 号段网络地址自然排序
const sortedPoolList = computed(() => sortPoolsByCidr(poolList.value))

async function loadPools(keepSelected = true) {
  const ticket = lifecycle.ticket('pools')
  if (ticket === undefined) return
  loading.value = true
  try {
    const list = await ipPoolApi.list()
    if (!lifecycle.current(ticket)) return
    const sorted = sortPoolsByCidr(list)
    poolList.value = sorted
    if (sorted.length > 0) {
      if (!keepSelected || !selectedPoolId.value || !sorted.some(p => p.id === selectedPoolId.value)) {
        selectedPoolId.value = sorted[0].id
      }
      await loadPoolDetail(selectedPoolId.value!)
    } else {
      selectedPoolId.value = null
      poolDetail.value = null
    }
  } catch (err: any) {
    if (lifecycle.current(ticket)) ElMessage.error(err.message || '加载 IP 号段列表失败')
  } finally {
    if (lifecycle.current(ticket)) loading.value = false
  }
}

async function loadPoolDetail(id: number) {
  const ticket = lifecycle.ticket('detail')
  if (ticket === undefined) return
  loading.value = true
  try {
    const res = await ipPoolApi.detail(id)
    if (!lifecycle.current(ticket) || selectedPoolId.value !== id) return
    poolDetail.value = res
  } catch (err: any) {
    if (lifecycle.current(ticket)) ElMessage.error(err.message || '加载号段详情失败')
  } finally {
    if (lifecycle.current(ticket)) loading.value = false
  }
}

const lifecycle = useActivePage(() => loadPools())
watch(selectedPoolId, (id) => {
  if (id) {
    activeSubnetSlice.value = 'all'
    statusFilter.value = 'all'
    tableSearch.value = ''
    loadPoolDetail(id)
  }
})

function handleAddPool() {
  editingPool.value = null
  poolDialogOpen.value = true
}

function handleEditPool() {
  if (!currentPool.value) return
  editingPool.value = currentPool.value
  poolDialogOpen.value = true
}

async function handleDeletePool() {
  if (!currentPool.value) return
  const pool = currentPool.value
  await confirm(
    `确定删除 IP 号段「${pool.name}」(${pool.cidr})？\n历史探测缓存也将一并移除。`,
    async () => {
      await ipPoolApi.delete(pool.id)
      await loadPools(false)
    },
    `已删除号段「${pool.name}」`
  )
}

async function triggerScan(sliceOnly = false) {
  if (!selectedPoolId.value) return
  const slice = sliceOnly && activeSubnetSlice.value !== 'all' ? activeSubnetSlice.value : null
  const scanTip = slice ? `正在并发扫描分段 ${slice}...` : '正在全网段并发 Ping 扫描...'

  scanning.value = true
  try {
    const res = await ipPoolApi.scan(selectedPoolId.value, slice)
    ElMessage.success(`扫描完成：共探测 ${res.scannedCount} 个地址，活跃响应 ${res.activeCount} 个，空闲 ${res.inactiveCount} 个`)
    await loadPoolDetail(selectedPoolId.value)
  } catch (err: any) {
    ElMessage.error(err.message || '扫描失败')
  } finally {
    scanning.value = false
  }
}

function onCellClick(addr: IpAddressStatus) {
  selectedCell.value = addr
  cellDrawerOpen.value = true
}

function openRegisterComputer(ip: string) {
  prefilledComputerIp.value = ip
  cellDrawerOpen.value = false
  computerDialogOpen.value = true
}

function openRegisterNetworkDevice(ip: string, vlanId?: number | null) {
  editingNetworkDevice.value = null
  prefilledNetworkDeviceIp.value = ip
  prefilledNetworkDeviceVlanId.value = vlanId ?? currentPool.value?.vlanId ?? null
  cellDrawerOpen.value = false
  networkDeviceDialogOpen.value = true
}

async function openEditNetworkDevice(deviceId: number | string) {
  try {
    const dev = await networkDeviceApi.get(Number(deviceId))
    editingNetworkDevice.value = dev
    prefilledNetworkDeviceIp.value = null
    prefilledNetworkDeviceVlanId.value = null
    cellDrawerOpen.value = false
    networkDeviceDialogOpen.value = true
  } catch (err: any) {
    ElMessage.error(err.message || '获取网络设备详情失败')
  }
}

function exportCsv() {
  if (!poolDetail.value || !poolDetail.value.addresses.length) {
    ElMessage.warning('暂无数据可导出')
    return
  }
  const headers = ['IP地址', '占用状态', '所属子块', '响应时延(ms)', '绑定设备数', '设备详情']
  const statusNames: Record<string, string> = {
    available: '空闲可用',
    registered: '已登记使用',
    unregistered_active: '已使用未登记(活跃)',
    conflict: '重复/冲突',
    reserved: '保留/网关'
  }

  const rows = filteredAddresses.value.map(a => {
    const devDesc = a.boundDevices.map(d => `${d.deviceType}:${d.deviceName}(${d.mac || d.location || ''})`).join('; ')
    return [
      `"${a.ip}"`,
      `"${statusNames[a.status] || a.status}"`,
      `"${a.subnetSlice || ''}"`,
      `"${a.responseTimeMs ?? ''}"`,
      `"${a.boundDevices.length}"`,
      `"${devDesc.replace(/"/g, '""')}"`
    ]
  })

  const csvContent = '\uFEFF' + [headers.join(','), ...rows.map(r => r.join(','))].join('\n')
  const blob = new Blob([csvContent], { type: 'text/csv;charset=utf-8;' })
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = `IP池_${currentPool.value?.name || 'export'}_${new Date().toISOString().slice(0, 10)}.csv`
  a.click()
  URL.revokeObjectURL(url)
}

function getHostNumber(ip: string): string {
  const parts = ip.split('.')
  return parts.length === 4 ? parts[3] : ip
}

async function loadSupportData() {
  try {
    const [orgRes, boxesRes, onusRes] = await Promise.all([
      managementApi.organization().catch(() => ({ workshops: [], areas: [], units: [] })),
      ponApi.terminalBoxes().catch(() => []),
      allPages<OnuDeviceDto>(q => ponApi.onus(q)).catch(() => [])
    ])
    organization.value = orgRes
    terminalBoxes.value = boxesRes
    onus.value = onusRes
  } catch {}
}

onMounted(async () => {
  await Promise.all([loadPools(), loadSupportData()])
})
</script>

<template>
  <div class="ip-pools-page">
    <PageHeader
      title="IP池可用与占用管理"
      subtitle="录入全网号段（支持 /22 大网段与任意子网），直观透视地址占用、网络活跃黑户与多重冲突"
    >
      <template #extra>
        <el-button
          v-if="auth.can('ip.manage')"
          type="primary"
          :icon="Plus"
          @click="handleAddPool"
        >
          新建 IP 号段
        </el-button>
        <el-button
          type="success"
          :icon="VideoPlay"
          :loading="scanning"
          @click="triggerScan(false)"
        >
          全网段一键并发扫描
        </el-button>
        <el-button
          v-if="activeSubnetSlice !== 'all'"
          :loading="scanning"
          @click="triggerScan(true)"
        >
          仅扫描当前分段 ({{ activeSubnetSlice }})
        </el-button>
        <el-button :icon="Download" @click="exportCsv">
          导出 CSV
        </el-button>
        <el-button :icon="Refresh" :loading="loading" @click="loadPools()">
          刷新
        </el-button>
      </template>
    </PageHeader>

    <!-- 顶部号段切换条 -->
    <div class="pool-selector-bar">
      <div class="selector-left">
        <span class="label">当前号段：</span>
        <el-select
          v-model="selectedPoolId"
          placeholder="选择查看的 IP 号段"
          style="width: 320px;"
          filterable
        >
          <el-option
            v-for="p in sortedPoolList"
            :key="p.id"
            :value="p.id"
            :label="`${p.name} (${p.cidr})`"
          />
        </el-select>

        <div v-if="currentPool" class="pool-meta-tags">
          <span class="tag">网关: <b>{{ currentPool.gateway || '-' }}</b></span>
          <span class="tag">掩码: <b>{{ currentPool.netmask }}</b></span>
          <span class="tag">容量: <b>{{ currentPool.totalCapacity }}</b></span>
          <span v-if="currentPool.vlanId" class="tag">VLAN: <b>{{ currentPool.vlanId }}</b></span>
          <span v-if="currentPool.lastScannedAt" class="tag scan-time">
            上次扫描: {{ new Date(currentPool.lastScannedAt).toLocaleTimeString() }}
          </span>
        </div>
      </div>

      <div v-if="currentPool && auth.can('ip.manage')" class="selector-right">
        <el-button size="small" :icon="Edit" @click="handleEditPool">编辑号段</el-button>
        <el-button size="small" type="danger" :icon="Delete" @click="handleDeletePool">删除号段</el-button>
      </div>
      <div v-else-if="!currentPool && auth.can('ip.manage')" class="selector-right">
        <el-button type="primary" size="small" :icon="Plus" @click="handleAddPool">新建 IP 号段</el-button>
      </div>
    </div>

    <!-- 汇总指标 KPI 仪表盘卡片 -->
    <div v-if="currentPool" class="kpi-grid">
      <div
        class="kpi-card"
        :class="{ active: statusFilter === 'all' }"
        @click="statusFilter = 'all'"
      >
        <div class="kpi-icon-wrap total"><el-icon><DataLine /></el-icon></div>
        <div class="kpi-content">
          <div class="kpi-label">号段总容量</div>
          <div class="kpi-value">{{ currentPool.totalCapacity }}</div>
          <div class="kpi-sub">有效 IP 地址总数</div>
        </div>
      </div>

      <div
        class="kpi-card"
        :class="{ active: statusFilter === 'available' }"
        @click="statusFilter = 'available'"
      >
        <div class="kpi-icon-wrap avail"><el-icon><CircleCheck /></el-icon></div>
        <div class="kpi-content">
          <div class="kpi-label">空闲 / 可用</div>
          <div class="kpi-value text-emerald-600">
            {{ currentPool.availableCount }}
            <span class="pct">({{ Math.round(currentPool.availableCount / (currentPool.totalCapacity || 1) * 100) }}%)</span>
          </div>
          <div class="kpi-sub">未登记且网络无探测响应</div>
        </div>
      </div>

      <div
        class="kpi-card"
        :class="{ active: statusFilter === 'registered' }"
        @click="statusFilter = 'registered'"
      >
        <div class="kpi-icon-wrap reg"><el-icon><Monitor /></el-icon></div>
        <div class="kpi-content">
          <div class="kpi-label">已登记使用</div>
          <div class="kpi-value text-blue-600">
            {{ currentPool.registeredCount }}
          </div>
          <div class="kpi-sub">摄像头/电脑/ONU台账占用</div>
        </div>
      </div>

      <div
        class="kpi-card"
        :class="{ active: statusFilter === 'unregistered_active' }"
        @click="statusFilter = 'unregistered_active'"
      >
        <div class="kpi-icon-wrap unreg"><el-icon><Warning /></el-icon></div>
        <div class="kpi-content">
          <div class="kpi-label">已使用未登记</div>
          <div class="kpi-value text-amber-600">
            {{ currentPool.unregisteredActiveCount }}
          </div>
          <div class="kpi-sub">网络活跃但台账查无此设备</div>
        </div>
      </div>

      <div
        class="kpi-card"
        :class="{ active: statusFilter === 'conflict' }"
        @click="statusFilter = 'conflict'"
      >
        <div class="kpi-icon-wrap conflict"><el-icon><Warning /></el-icon></div>
        <div class="kpi-content">
          <div class="kpi-label">重复 / 冲突</div>
          <div class="kpi-value text-rose-600">
            {{ currentPool.conflictCount }}
          </div>
          <div class="kpi-sub">多台设备共用同一 IP</div>
        </div>
      </div>

      <div
        class="kpi-card"
        :class="{ active: statusFilter === 'reserved' }"
        @click="statusFilter = 'reserved'"
      >
        <div class="kpi-icon-wrap reserved"><el-icon><InfoFilled /></el-icon></div>
        <div class="kpi-content">
          <div class="kpi-label">保留 / 网关</div>
          <div class="kpi-value text-slate-500">
            {{ currentPool.reservedCount }}
          </div>
          <div class="kpi-sub">网络号、广播与网关</div>
        </div>
      </div>
    </div>

    <!-- 主展示面板 -->
    <el-card v-if="poolDetail" shadow="never" class="main-card">
      <div class="view-header">
        <div class="header-left">
          <!-- 大网段智能分段切片标签页导航 -->
          <div v-if="poolDetail.subnetSlices.length > 1" class="slice-tabs">
            <span class="slice-label">分段聚焦：</span>
            <el-radio-group v-model="activeSubnetSlice" size="small">
              <el-radio-button value="all">
                全部平铺 ({{ poolDetail.addresses.length }})
              </el-radio-button>
              <el-radio-button
                v-for="sl in poolDetail.subnetSlices"
                :key="sl"
                :value="sl"
              >
                {{ sl }} (256)
              </el-radio-button>
            </el-radio-group>
          </div>

          <!-- 图例色块说明 -->
          <div class="color-legend">
            <span class="legend-item"><span class="dot bg-avail" /> 空闲可用</span>
            <span class="legend-item"><span class="dot bg-reg" /> 已登记使用</span>
            <span class="legend-item"><span class="dot bg-unreg" /> 已使用未登记 (活跃)</span>
            <span class="legend-item"><span class="dot bg-conflict" /> 重复/冲突</span>
            <span class="legend-item"><span class="dot bg-reserved" /> 保留/网关</span>
          </div>
        </div>

        <div class="header-right">
          <el-input
            v-model="tableSearch"
            placeholder="搜索 IP、设备名、MAC、责任人..."
            :prefix-icon="Search"
            clearable
            style="width: 240px;"
          />
          <el-radio-group v-model="viewMode" size="small">
            <el-radio-button value="grid">矩阵网格</el-radio-button>
            <el-radio-button value="table">明细列表</el-radio-button>
          </el-radio-group>
        </div>
      </div>

      <!-- 视图 1：IPAM 矩阵方块热力图 -->
      <div v-if="viewMode === 'grid'" class="ipam-grid-container">
        <div class="ipam-grid">
          <el-tooltip
            v-for="addr in filteredAddresses"
            :key="addr.ip"
            placement="top"
            :show-after="100"
          >
            <template #content>
              <div class="ip-tooltip-content">
                <div class="tip-header">
                  <span class="tip-ip font-bold">{{ addr.ip }}</span>
                  <span class="tip-status font-medium" :class="addr.status">
                    {{ addr.status === 'available' ? '空闲可用' : addr.status === 'registered' ? '已登记' : addr.status === 'unregistered_active' ? '已使用未登记' : addr.status === 'conflict' ? '冲突重复' : '保留' }}
                  </span>
                </div>
                <div v-if="addr.responseTimeMs != null" class="tip-rtt">
                  往返时延: {{ addr.responseTimeMs }} ms
                </div>
                <div v-if="addr.boundDevices.length" class="tip-devices">
                  <div class="tip-sec-title">已绑定设备 ({{ addr.boundDevices.length }}):</div>
                  <div v-for="(dev, idx) in addr.boundDevices" :key="idx" class="dev-item">
                    <span class="dev-type">[{{ dev.deviceType === 'onu' ? '全光ONU' : dev.deviceType === 'computer' ? '电脑' : dev.deviceType === 'network_device' ? '网络设备' : dev.deviceType === 'channel' ? '摄像机' : dev.deviceType === 'device' ? '录像设备' : dev.deviceType }}]</span>
                    <span class="dev-name">{{ dev.deviceName }}</span>
                    <span v-if="dev.mac" class="dev-mac font-mono">({{ dev.mac }})</span>
                    <span v-if="dev.location" class="dev-loc">@ {{ dev.location }}</span>
                    <span v-if="dev.owner" class="dev-owner">责任人: {{ dev.owner }}</span>
                  </div>
                </div>
                <div v-else-if="addr.status === 'unregistered_active'" class="tip-hint text-amber-300">
                  ⚠️ 网络在线但未录入台账，点击可快速登记为电脑！
                </div>
              </div>
            </template>

            <!-- 单元格方块 -->
            <div
              class="ip-cell cursor-pointer"
              :class="[
                addr.status,
                { 'has-devices': addr.boundDevices.length > 0 }
              ]"
              @click="onCellClick(addr)"
            >
              <span class="cell-num">{{ getHostNumber(addr.ip) }}</span>
              <span v-if="addr.status === 'conflict'" class="conflict-badge">!</span>
            </div>
          </el-tooltip>
        </div>
      </div>

      <!-- 视图 2：明细数据列表 -->
      <div v-else class="ipam-table-container">
        <el-table :data="paginatedAddresses" stripe class="detail-table">
          <el-table-column label="IP 地址" prop="ip" width="160">
            <template #default="{ row }">
              <span class="ip-text font-mono font-bold">{{ row.ip }}</span>
            </template>
          </el-table-column>

          <el-table-column label="占用状态" width="150" align="center">
            <template #default="{ row }">
              <el-tag
                :type="row.status === 'available' ? 'success' : row.status === 'registered' ? 'primary' : row.status === 'unregistered_active' ? 'warning' : row.status === 'conflict' ? 'danger' : 'info'"
                size="small"
              >
                {{ row.status === 'available' ? '空闲可用' : row.status === 'registered' ? '已登记使用' : row.status === 'unregistered_active' ? '已使用未登记' : row.status === 'conflict' ? '重复冲突' : '保留预留' }}
              </el-tag>
            </template>
          </el-table-column>

          <el-table-column label="网络探测时延" width="130" align="center">
            <template #default="{ row }">
              <span v-if="row.responseTimeMs != null" class="rtt-badge">
                {{ row.responseTimeMs }} ms
              </span>
              <span v-else class="text-slate-400">-</span>
            </template>
          </el-table-column>

          <el-table-column label="已绑定系统设备" min-width="260">
            <template #default="{ row }">
              <div v-if="row.boundDevices.length" class="bound-list">
                <div v-for="(d, i) in row.boundDevices" :key="i" class="bound-item">
                  <el-tag size="small" effect="plain">{{ d.deviceType === 'onu' ? '全光ONU' : d.deviceType === 'computer' ? '电脑终端' : d.deviceType === 'network_device' ? '网络设备' : d.deviceType === 'channel' ? '摄像机' : d.deviceType === 'device' ? '录像设备' : d.deviceType }}</el-tag>
                  <span class="font-medium text-slate-800">{{ d.deviceName }}</span>
                  <span v-if="d.mac" class="font-mono text-xs text-slate-500">{{ d.mac }}</span>
                  <span v-if="d.location" class="text-xs text-slate-500">@ {{ d.location }}</span>
                </div>
              </div>
              <span v-else-if="row.status === 'unregistered_active'" class="text-amber-600 font-medium">
                ⚠️ 网络有响应，未登记
              </span>
              <span v-else class="text-slate-400">无绑定</span>
            </template>
          </el-table-column>

          <el-table-column label="所属切片" prop="subnetSlice" width="120" />

          <el-table-column label="操作" width="200" fixed="right" align="right">
            <template #default="{ row }">
              <el-dropdown v-if="row.status === 'unregistered_active' || row.status === 'available'" size="small">
                <el-button type="primary" link size="small">
                  登记纳管 <el-icon class="el-icon--right"><ArrowDown /></el-icon>
                </el-button>
                <template #dropdown>
                  <el-dropdown-menu>
                    <el-dropdown-item :icon="Monitor" @click="openRegisterComputer(row.ip)">
                      登记为电脑
                    </el-dropdown-item>
                    <el-dropdown-item :icon="Platform" @click="openRegisterNetworkDevice(row.ip, currentPool?.vlanId)">
                      登记为网络设备
                    </el-dropdown-item>
                  </el-dropdown-menu>
                </template>
              </el-dropdown>
              <el-button
                type="primary"
                link
                size="small"
                @click="onCellClick(row as any)"
              >
                详情
              </el-button>
            </template>
          </el-table-column>
        </el-table>

        <!-- 极速分页工具条 -->
        <div class="table-pagination-bar">
          <div class="pagination-info">
            显示第 <b>{{ filteredAddresses.length === 0 ? 0 : (tablePage - 1) * tablePageSize + 1 }}</b> 至 <b>{{ Math.min(tablePage * tablePageSize, filteredAddresses.length) }}</b> 条，
            共 <b>{{ filteredAddresses.length }}</b> 个地址
            <span v-if="poolDetail && filteredAddresses.length !== poolDetail.addresses.length" class="filter-tip">
              (已过滤，号段总计 {{ poolDetail.addresses.length }} 个)
            </span>
          </div>
          <el-pagination
            v-model:current-page="tablePage"
            v-model:page-size="tablePageSize"
            :page-sizes="[20, 50, 100, 200]"
            :total="filteredAddresses.length"
            layout="sizes, prev, pager, next, jumper"
            background
            size="small"
          />
        </div>
      </div>
    </el-card>

    <el-card v-else-if="!loading" shadow="never" class="main-card empty-card" style="margin-top: 16px; text-align: center; padding: 40px 0;">
      <el-empty description="当前暂未选择或尚未创建 IP 号段">
        <template #extra>
          <el-button v-if="auth.can('ip.manage')" type="primary" :icon="Plus" @click="handleAddPool">
            立即新建 IP 号段
          </el-button>
        </template>
      </el-empty>
    </el-card>

    <!-- IP 地址详情抽屉 -->
    <el-drawer
      v-model="cellDrawerOpen"
      :title="`IP 地址透视 · ${selectedCell?.ip}`"
      size="480px"
      destroy-on-close
    >
      <div v-if="selectedCell" class="cell-drawer-body">
        <div class="drawer-header-box">
          <div class="drawer-ip">{{ selectedCell.ip }}</div>
          <el-tag
            :type="selectedCell.status === 'available' ? 'success' : selectedCell.status === 'registered' ? 'primary' : selectedCell.status === 'unregistered_active' ? 'warning' : selectedCell.status === 'conflict' ? 'danger' : 'info'"
            size="large"
          >
            {{ selectedCell.status === 'available' ? '空闲可用' : selectedCell.status === 'registered' ? '正常已登记' : selectedCell.status === 'unregistered_active' ? '已使用未登记 (活跃设备)' : selectedCell.status === 'conflict' ? '严重冲突 (多个设备重叠)' : '保留/网关预留' }}
          </el-tag>
          <div v-if="selectedCell.responseTimeMs != null" class="drawer-rtt">
            Ping 往返耗时: <b>{{ selectedCell.responseTimeMs }} ms</b>
          </div>
        </div>

        <!-- 冲突排查警告 -->
        <div v-if="selectedCell.status === 'conflict'" class="alert-box error">
          <div class="alert-title">⚠️ IP 地址冲突警报！</div>
          <div class="alert-desc">系统内检测到 {{ selectedCell.boundDevices.length }} 个设备登记了相同的 IP 地址，会导致网络通信争抢或掉线，请及时修改台账或更换设备 IP。</div>
        </div>

        <!-- 未登记黑户提示 -->
        <div v-if="selectedCell.status === 'unregistered_active'" class="alert-box warning">
          <div class="alert-title">⚠️ 发现未登记活跃设备</div>
          <div class="alert-desc">该 IP 在网络探测中能够正常应答，但在系统所有台账中均未找到匹配记录。支持直接一键将其纳管登记为电脑终端或网络设备接口！</div>
          <div class="alert-actions">
            <el-button type="warning" size="small" :icon="Plus" @click="openRegisterComputer(selectedCell.ip)">
              一键登记为电脑终端
            </el-button>
            <el-button type="primary" size="small" :icon="Platform" @click="openRegisterNetworkDevice(selectedCell.ip, currentPool?.vlanId)">
              登记为网络设备 / 接口
            </el-button>
          </div>
        </div>

        <!-- 空闲可用快速登记 -->
        <div v-if="selectedCell.status === 'available'" class="alert-box success">
          <div class="alert-title">🟢 该地址当前处于空闲状态</div>
          <div class="alert-desc">可以分配给新装电脑终端、摄像头或交换机接口。</div>
          <div class="alert-actions">
            <el-button type="primary" size="small" :icon="Plus" @click="openRegisterComputer(selectedCell.ip)">
              分配并登记为电脑
            </el-button>
            <el-button size="small" :icon="Platform" @click="openRegisterNetworkDevice(selectedCell.ip, currentPool?.vlanId)">
              登记为网络设备 / 接口
            </el-button>
          </div>
        </div>

        <!-- 已绑定设备列表 -->
        <div class="bound-devices-section">
          <div class="section-title">系统关联设备信息 ({{ selectedCell.boundDevices.length }})</div>
          <div v-if="selectedCell.boundDevices.length" class="dev-cards">
            <div
              v-for="(dev, idx) in selectedCell.boundDevices"
              :key="idx"
              class="dev-card"
              :class="{ 'network-device-card': dev.deviceType === 'network_device' }"
            >
              <div class="dev-card-header">
                <span class="type-badge" :class="dev.deviceType === 'network_device' ? 'network-badge' : ''">
                  {{ dev.deviceType === 'network_device' ? '网络设备' : dev.deviceType === 'computer' ? '电脑终端' : dev.deviceType === 'channel' ? '监控通道' : dev.deviceType === 'onu' ? 'ONU终端' : dev.deviceType }}
                </span>
                <span class="name font-bold">{{ dev.deviceName }}</span>
                <el-button
                  v-if="dev.deviceType === 'network_device' && dev.deviceId && auth.can('network_device.manage')"
                  type="primary"
                  link
                  size="small"
                  style="margin-left: auto;"
                  @click="openEditNetworkDevice(dev.deviceId)"
                >
                  管理设备
                </el-button>
              </div>
              <div class="dev-card-rows">
                <div v-if="dev.interfaceName || dev.role" class="row">
                  <span class="k">接口属性:</span>
                  <span class="v font-semibold text-sky-600">
                    {{ dev.interfaceName || 'Vlanif' }} ({{ dev.role === 'gateway' ? '网关地址' : dev.role }})
                  </span>
                </div>
                <div v-if="dev.detail" class="row">
                  <span class="k">描述:</span> <span class="v">{{ dev.detail }}</span>
                </div>
                <div v-if="dev.mac" class="row">
                  <span class="k">MAC:</span> <span class="v font-mono">{{ dev.mac }}</span>
                </div>
                <div v-if="dev.location" class="row">
                  <span class="k">位置:</span> <span class="v">{{ dev.location }}</span>
                </div>
                <div v-if="dev.owner" class="row">
                  <span class="k">责任人:</span> <span class="v">{{ dev.owner }}</span>
                </div>

                <!-- 核心交换机等多 IP 拓扑透视 -->
                <div v-if="dev.relatedIps && dev.relatedIps.length" class="related-ips-box">
                  <div class="related-title">{{ dev.deviceType === 'onu' ? '该全光 ONU 关联的所有 IP (有线/Wi-Fi)' : '该核心/网管设备其它分配 IP' }} ({{ dev.relatedIps.length }} 个):</div>
                  <div class="related-tags">
                    <span
                      v-for="(relIp, rIdx) in dev.relatedIps"
                      :key="rIdx"
                      class="related-tag"
                      :class="{ current: relIp === selectedCell.ip }"
                    >
                      <b>{{ relIp }}</b>
                    </span>
                  </div>
                </div>
              </div>
            </div>
          </div>
          <div v-else class="empty-text">当前 IP 地址无系统台账记录</div>
        </div>
      </div>
    </el-drawer>

    <!-- 号段新建/编辑弹窗 -->
    <IpPoolDialog
      v-model="poolDialogOpen"
      :pool="editingPool"
      @saved="loadPools()"
    />

    <!-- 电脑登记弹窗联动 -->
    <ComputerDialog
      v-model="computerDialogOpen"
      :organization="organization"
      :terminal-boxes="terminalBoxes"
      :onus="onus"
      :initial-ip="prefilledComputerIp"
      @saved="loadPoolDetail(selectedPoolId!)"
    />

    <!-- 网络设备登记弹窗联动 -->
    <NetworkDeviceDialog
      v-model="networkDeviceDialogOpen"
      :device="editingNetworkDevice"
      :organization="organization"
      :initial-ip="prefilledNetworkDeviceIp"
      :initial-vlan-id="prefilledNetworkDeviceVlanId"
      @saved="loadPoolDetail(selectedPoolId!)"
    />
  </div>
</template>

<style scoped>
.ip-pools-page {
  padding: 16px 20px 32px;
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.pool-selector-bar {
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

.selector-left {
  display: flex;
  align-items: center;
  gap: 12px;
  flex-wrap: wrap;
}

.selector-left .label {
  font-weight: 600;
  font-size: 14px;
}

.pool-meta-tags {
  display: flex;
  gap: 8px;
  align-items: center;
  flex-wrap: wrap;
}

.pool-meta-tags .tag {
  font-size: 12px;
  padding: 3px 8px;
  background: var(--el-fill-color);
  border-radius: 4px;
  color: var(--el-text-color-regular);
}
.pool-meta-tags .tag b {
  color: var(--el-color-primary);
}
.pool-meta-tags .tag.scan-time {
  background: rgba(16, 185, 129, 0.1);
  color: #059669;
}

.kpi-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(180px, 1fr));
  gap: 14px;
}

.kpi-card {
  display: flex;
  align-items: center;
  gap: 12px;
  background: var(--el-bg-color);
  border: 1px solid var(--el-border-color-lighter);
  border-radius: 8px;
  padding: 12px 16px;
  cursor: pointer;
  transition: all 0.2s ease;
  user-select: none;
}
.kpi-card:hover {
  transform: translateY(-2px);
  box-shadow: 0 4px 12px rgba(0, 0, 0, 0.06);
}
.kpi-card.active {
  border-color: var(--el-color-primary);
  background: rgba(59, 130, 246, 0.04);
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
.kpi-icon-wrap.total { background: rgba(59, 130, 246, 0.12); color: #2563eb; }
.kpi-icon-wrap.avail { background: rgba(16, 185, 129, 0.12); color: #059669; }
.kpi-icon-wrap.reg { background: rgba(59, 130, 246, 0.12); color: #2563eb; }
.kpi-icon-wrap.unreg { background: rgba(245, 158, 11, 0.12); color: #d97706; }
.kpi-icon-wrap.conflict { background: rgba(239, 68, 68, 0.12); color: #dc2626; }
.kpi-icon-wrap.reserved { background: rgba(148, 163, 184, 0.15); color: #64748b; }

.kpi-content {
  flex: 1;
  min-width: 0;
}
.kpi-label {
  font-size: 12px;
  color: var(--el-text-color-secondary);
}
.kpi-value {
  font-size: 20px;
  font-weight: 700;
  line-height: 1.2;
  margin: 2px 0;
}
.kpi-value .pct {
  font-size: 12px;
  font-weight: 500;
  color: var(--el-text-color-secondary);
}
.kpi-sub {
  font-size: 11px;
  color: var(--el-text-color-placeholder);
}

.main-card {
  border-radius: 8px;
}

.view-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 16px;
  flex-wrap: wrap;
  gap: 12px;
}

.header-left {
  display: flex;
  align-items: center;
  gap: 16px;
  flex-wrap: wrap;
}

.slice-tabs {
  display: flex;
  align-items: center;
  gap: 8px;
}
.slice-label {
  font-size: 13px;
  font-weight: 600;
  color: var(--el-text-color-secondary);
}

.color-legend {
  display: flex;
  gap: 12px;
  align-items: center;
  font-size: 12px;
  color: var(--el-text-color-secondary);
  flex-wrap: wrap;
}
.legend-item {
  display: inline-flex;
  align-items: center;
  gap: 5px;
}
.legend-item .dot {
  width: 10px;
  height: 10px;
  border-radius: 2px;
  display: inline-block;
}

.dot.bg-avail { background: #10b981; }
.dot.bg-reg { background: #3b82f6; }
.dot.bg-unreg { background: #f59e0b; }
.dot.bg-conflict { background: #ef4444; }
.dot.bg-reserved { background: #94a3b8; }

.header-right {
  display: flex;
  align-items: center;
  gap: 12px;
}

/* 矩阵网格设计 */
.ipam-grid-container {
  overflow-x: auto;
  padding: 8px 0;
}

.ipam-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(46px, 1fr));
  gap: 6px;
}

.ip-cell {
  height: 42px;
  border-radius: 6px;
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 12px;
  font-weight: 600;
  color: #ffffff;
  position: relative;
  transition: all 0.15s ease;
  user-select: none;
}
.ip-cell:hover {
  transform: scale(1.1);
  z-index: 10;
  box-shadow: 0 4px 10px rgba(0, 0, 0, 0.2);
}

.ip-cell.available {
  background: #10b981;
}
.ip-cell.registered {
  background: #3b82f6;
}
.ip-cell.unregistered_active {
  background: #f59e0b;
  animation: pulse-border 2s infinite;
}
.ip-cell.conflict {
  background: #ef4444;
  animation: pulse-conflict 1.5s infinite;
}
.ip-cell.reserved {
  background: #94a3b8;
  color: #f8fafc;
}

.conflict-badge {
  position: absolute;
  top: 1px;
  right: 3px;
  font-size: 10px;
  font-weight: 800;
  color: #fff;
}

@keyframes pulse-conflict {
  0% { box-shadow: 0 0 0 0 rgba(239, 68, 68, 0.7); }
  70% { box-shadow: 0 0 0 6px rgba(239, 68, 68, 0); }
  100% { box-shadow: 0 0 0 0 rgba(239, 68, 68, 0); }
}

@keyframes pulse-border {
  0% { box-shadow: 0 0 0 0 rgba(245, 158, 11, 0.6); }
  70% { box-shadow: 0 0 0 5px rgba(245, 158, 11, 0); }
  100% { box-shadow: 0 0 0 0 rgba(245, 158, 11, 0); }
}

.ip-tooltip-content {
  padding: 4px;
  max-width: 280px;
}
.tip-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 10px;
  margin-bottom: 4px;
}
.tip-ip { font-size: 13px; font-family: monospace; }
.tip-status { font-size: 11px; padding: 1px 4px; border-radius: 2px; }
.tip-status.available { color: #34d399; }
.tip-status.registered { color: #60a5fa; }
.tip-status.unregistered_active { color: #fbbf24; }
.tip-status.conflict { color: #f87171; }
.tip-status.reserved { color: #cbd5e1; }

.tip-rtt { font-size: 11px; color: #94a3b8; margin-bottom: 4px; }
.tip-sec-title { font-size: 11px; font-weight: 600; margin-bottom: 2px; }
.dev-item {
  font-size: 11px;
  line-height: 1.4;
  margin-bottom: 2px;
}
.dev-type { color: #93c5fd; margin-right: 2px; }
.dev-name { font-weight: 600; margin-right: 4px; }

/* 抽屉样式 */
.cell-drawer-body {
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.drawer-header-box {
  background: var(--el-fill-color-light);
  border-radius: 8px;
  padding: 16px;
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 8px;
}
.drawer-ip {
  font-size: 26px;
  font-weight: 800;
  font-family: monospace;
  color: var(--el-text-color-primary);
}
.drawer-rtt {
  font-size: 13px;
  color: var(--el-text-color-secondary);
}

.alert-box {
  border-radius: 6px;
  padding: 12px 14px;
  font-size: 13px;
}
.alert-box.error { background: rgba(239, 68, 68, 0.1); border: 1px solid rgba(239, 68, 68, 0.3); color: #dc2626; }
.alert-box.warning { background: rgba(245, 158, 11, 0.1); border: 1px solid rgba(245, 158, 11, 0.3); color: #b45309; }
.alert-box.success { background: rgba(16, 185, 129, 0.1); border: 1px solid rgba(16, 185, 129, 0.3); color: #047857; }

.alert-title { font-weight: 700; margin-bottom: 4px; }
.alert-desc { line-height: 1.4; }
.alert-actions { margin-top: 10px; }

.section-title {
  font-weight: 700;
  font-size: 14px;
  margin-bottom: 10px;
}
.dev-cards {
  display: flex;
  flex-direction: column;
  gap: 10px;
}
.dev-card {
  border: 1px solid var(--el-border-color-lighter);
  border-radius: 6px;
  padding: 12px;
  background: var(--el-bg-color);
}
.dev-card-header {
  display: flex;
  align-items: center;
  gap: 8px;
  margin-bottom: 6px;
}
.type-badge {
  font-size: 11px;
  padding: 2px 6px;
  border-radius: 4px;
  background: rgba(59, 130, 246, 0.1);
  color: #2563eb;
  font-weight: 600;
}
.dev-card-header .name {
  font-weight: 700;
  font-size: 14px;
}
.dev-card-rows {
  font-size: 12.5px;
  display: flex;
  flex-direction: column;
  gap: 3px;
}
.dev-card-rows .row {
  display: flex;
  gap: 6px;
}
.dev-card-rows .k { color: var(--el-text-color-secondary); }
.dev-card-rows .v { color: var(--el-text-color-primary); }

.network-device-card {
  border-left: 3px solid #0284c7;
  background: #f0f9ff;
}

.type-badge.network-badge {
  background: rgba(2, 132, 199, 0.15);
  color: #0284c7;
  font-weight: 700;
}

.related-ips-box {
  margin-top: 8px;
  padding-top: 8px;
  border-top: 1px dashed #cbd5e1;
}

.related-title {
  font-size: 11.5px;
  font-weight: 600;
  color: #475569;
  margin-bottom: 6px;
}

.related-tags {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
}

.related-tag {
  display: inline-flex;
  align-items: center;
  gap: 4px;
  background: #ffffff;
  border: 1px solid #94a3b8;
  border-radius: 4px;
  padding: 2px 6px;
  font-size: 11px;
}

.related-tag.current {
  border-color: #0284c7;
  background: #e0f2fe;
  color: #0369a1;
}

.bound-list {
  display: flex;
  flex-direction: column;
  gap: 4px;
}
.bound-item {
  display: flex;
  align-items: center;
  gap: 6px;
  flex-wrap: wrap;
}

.table-pagination-bar {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-top: 14px;
  padding: 10px 16px;
  background: var(--el-fill-color-light);
  border-radius: 6px;
  flex-wrap: wrap;
  gap: 12px;
}
.pagination-info {
  font-size: 13px;
  color: var(--el-text-color-secondary);
}
.pagination-info b {
  color: var(--el-text-color-primary);
}
.filter-tip {
  color: var(--el-text-color-placeholder);
  margin-left: 4px;
}
</style>
