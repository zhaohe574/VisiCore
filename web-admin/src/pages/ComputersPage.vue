<script setup lang="ts">
import { computed, onMounted, onActivated, onDeactivated, onBeforeUnmount, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import {
  CircleCheck,
  Connection,
  Cpu,
  Delete,
  Download,
  Edit,
  Location,
  Monitor,
  Plus,
  Refresh,
  Search,
  VideoPlay,
  Warning
} from '@element-plus/icons-vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import PageHeader from '../components/PageHeader.vue'
import ComputerDialog from '../components/computers/ComputerDialog.vue'
import TerminalBoxWorkbenchDrawer from '../components/pon/TerminalBoxWorkbenchDrawer.vue'
import {
  allPages,
  assetApi,
  computerApi,
  managementApi,
  ponApi,
  type Computer,
  type AssetSummary,
  type OnuDeviceDto,
  type Organization,
  type TerminalBoxDto
} from '../api'
import { buildUnitCascaderOptions } from '../lib/organization'
import { useAuth } from '../stores/auth'
import { useAction } from '../composables/useAction'

const auth = useAuth()
const route = useRoute(), router = useRouter()
const summary = ref<AssetSummary>()
const selected = ref<Computer[]>([])
const batchOpen = ref(false), batchBusy = ref(false), batchOrg = ref(false), batchNetwork = ref(false)
const batchUnit = ref<number>(), batchBox = ref<number>(), batchOnu = ref<number>(), batchPort = ref(1), batchSwitch = ref(false)
let active = true, generation = 0
let controller: AbortController | undefined
const { confirm } = useAction()

const loading = ref(false)
const probingAll = ref(false)
const search = ref(String(route.query.search || ''))
const onlineFilter = ref('')
const orgFilter = ref<number | string | null>(null)
const page = ref(1)
const pageSize = ref(20)
const total = ref(0)
const items = ref<Computer[]>([])

const organization = ref<Organization>({ workshops: [], areas: [], units: [] })
const terminalBoxes = ref<TerminalBoxDto[]>([])
const onus = ref<OnuDeviceDto[]>([])

// 弹窗与抽屉
const dialogOpen = ref(false)
const editingComputer = ref<Computer | null>(null)

const workbenchDrawerOpen = ref(false)
const viewingBoxId = ref<number | null>(null)
const viewingOnuId = ref<number | null>(null)

function openWorkbench(boxId?: number | null, onuId?: number | null) {
  viewingBoxId.value = boxId ?? null
  viewingOnuId.value = onuId ?? null
  workbenchDrawerOpen.value = true
}

const cascaderOptions = computed(() => buildUnitCascaderOptions(organization.value))

// 统计总览
const stats = computed(() => summary.value || { total: 0, online: 0, offline: 0, unknown: 0 })

async function loadOptions() {
  try {
    const [orgRes, boxesRes, onusRes] = await Promise.all([
      managementApi.organization(),
      ponApi.terminalBoxes().catch(() => []),
      allPages<OnuDeviceDto>(q => ponApi.onus(q)).catch(() => [])
    ])
    organization.value = orgRes
    terminalBoxes.value = boxesRes
    onus.value = onusRes
  } catch (err: any) {
    // 忽略部分字典加载异常
  }
}

async function load(resetPage = false) {
  if (!active) return
  controller?.abort(); controller = new AbortController()
  const current = ++generation
  if (resetPage) page.value = 1
  loading.value = true
  try {
    let unitId: number | undefined
    if (typeof orgFilter.value === 'number') unitId = orgFilter.value

    const query = {
      page: page.value,
      pageSize: pageSize.value,
      search: search.value.trim() || undefined,
      unitId,
      status: onlineFilter.value || undefined
    }
    const [res, counts] = await Promise.all([computerApi.list(query, controller.signal), assetApi.summary({ ...query, type: 'computer' }, controller.signal)])
    if (current !== generation) return
    items.value = res.items
    total.value = res.total
    summary.value = counts
  } catch (err: any) {
    if (current === generation && err.name !== 'AbortError') ElMessage.error(err.message || '加载电脑台账失败')
  } finally {
    if (current === generation) loading.value = false
  }
}
async function submitBatch() {
  if (!batchOrg.value && !batchNetwork.value) { ElMessage.warning('请选择需要修改的内容。'); return }
  batchBusy.value = true
  try {
    await computerApi.batchUpdate({ computerIds: selected.value.map(c => c.id), updateOrganization: batchOrg.value,
      workshopId: null, areaId: null, unitId: batchUnit.value || null, updateNetworkBinding: batchNetwork.value,
      terminalBoxId: batchBox.value || null, onuDeviceId: batchOnu.value || null, startPortNumber: batchPort.value, isSwitchPort: batchSwitch.value })
    batchOpen.value = false; selected.value = []; await load(); ElMessage.success('整批电脑已更新。')
  } catch (e: any) { ElMessage.error(e.message || '批量修改失败，整批变更已回滚。') }
  finally { batchBusy.value = false }
}

function handleAdd() {
  editingComputer.value = null
  dialogOpen.value = true
}

function handleEdit(comp: Computer) {
  editingComputer.value = comp
  dialogOpen.value = true
}

async function handleDelete(comp: Computer) {
  await confirm(
    `确定删除电脑登记「${comp.name}」(${comp.ip})？`,
    async () => {
      await computerApi.delete(comp.id)
      await load()
    },
    `已删除电脑「${comp.name}」`
  )
}

async function probeSingle(comp: Computer) {
  try {
    const res = await computerApi.probe(comp.id)
    comp.status = res.status as any
    comp.responseTimeMs = res.responseTimeMs
    comp.lastProbedAt = res.probedAt
    if (res.status === 'online') {
      ElMessage.success(`电脑「${comp.name}」在线，响应时延 ${res.responseTimeMs} ms`)
    } else {
      ElMessage.warning(`电脑「${comp.name}」(${comp.ip}) Ping 超时无响应`)
    }
  } catch (err: any) {
    ElMessage.error(err.message || '探测失败')
  }
}

async function probeAll() {
  probingAll.value = true
  try {
    const results = await computerApi.probeAll()
    const onlineNum = results.filter(r => r.status === 'online').length
    ElMessage.success(`批量 Ping 探测完成：在线 ${onlineNum} 台，离线 ${results.length - onlineNum} 台`)
    await load()
  } catch (err: any) {
    ElMessage.error(err.message || '批量探测失败')
  } finally {
    probingAll.value = false
  }
}

function exportCsv() {
  if (!items.value.length) {
    ElMessage.warning('暂无数据可导出')
    return
  }
  const headers = ['计算机名称', 'IP地址', 'MAC地址', '责任人', '部门', '操作系统', '车间', '区域', '工位单元', '物理位置', '终端箱', 'ONU设备', '端口', '在线状态', '时延(ms)', '备注']
  const rows = items.value.map(c => [
    `"${c.name}"`,
    `"${c.ip}"`,
    `"${c.mac || ''}"`,
    `"${c.owner || ''}"`,
    `"${c.department || ''}"`,
    `"${c.os || ''}"`,
    `"${c.workshopName || ''}"`,
    `"${c.areaName || ''}"`,
    `"${c.unitName || ''}"`,
    `"${c.location || ''}"`,
    `"${c.terminalBoxName || ''}"`,
    `"${c.onuAlias || c.onuName || ''}"`,
    `"${c.isSwitchPort ? 'SW-P' + c.portNumber : 'LAN' + c.portNumber}"`,
    `"${c.status === 'online' ? '在线' : c.status === 'offline' ? '离线' : '未知'}"`,
    `"${c.responseTimeMs ?? ''}"`,
    `"${(c.remarks || '').replace(/"/g, '""')}"`
  ])

  const csvContent = '\uFEFF' + [headers.join(','), ...rows.map(r => r.join(','))].join('\n')
  const blob = new Blob([csvContent], { type: 'text/csv;charset=utf-8;' })
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = `电脑登记台账_${new Date().toISOString().slice(0, 10)}.csv`
  a.click()
  URL.revokeObjectURL(url)
}

function copyText(text: string) {
  navigator.clipboard.writeText(text)
  ElMessage.success('已复制到剪贴板')
}

onMounted(async () => {
  await loadOptions()
  await load()
})
onDeactivated(() => { active = false; generation++; controller?.abort(); loading.value = false })
onActivated(() => { if (!active) { active = true; void load() } })
onBeforeUnmount(() => { generation++; controller?.abort() })
</script>

<template>
  <div class="computers-page">
    <PageHeader
      title="电脑登记台账"
      subtitle="集中登记管理生产与办公终端电脑，记录 IP、MAC、责任人及 POL 全光网络接入链路"
    >
      <template #extra>
        <el-button
          v-if="auth.can('computer.manage')"
          type="primary"
          :icon="Plus"
          @click="handleAdd"
        >
          新增电脑登记
        </el-button>
        <el-button
          :icon="VideoPlay"
          :loading="probingAll"
          @click="probeAll"
        >
          全网 Ping 探测
        </el-button>
        <el-button :icon="Download" @click="exportCsv">
          导出 CSV
        </el-button>
        <el-button :icon="Refresh" :loading="loading" @click="load()">
          刷新
        </el-button>
      </template>
    </PageHeader>

    <!-- 统计指标行 -->
    <div class="stats-row">
      <div class="stat-card">
        <div class="stat-icon total"><el-icon :size="24"><Monitor /></el-icon></div>
        <div class="stat-info">
          <div class="stat-value">{{ stats.total }}</div>
          <div class="stat-label">电脑登记总数</div>
        </div>
      </div>
      <div class="stat-card" style="cursor: pointer;" @click="onlineFilter = 'online'; load(true)">
        <div class="stat-icon online"><el-icon :size="24"><CircleCheck /></el-icon></div>
        <div class="stat-info">
          <div class="stat-value text-emerald-600">{{ stats.online }}</div>
          <div class="stat-label">{{ stats.total > items.length ? `当前页在线 (${stats.online}/${items.length})` : '网络在线 (正常响应)' }}</div>
        </div>
      </div>
      <div class="stat-card" style="cursor: pointer;" @click="onlineFilter = 'offline'; load(true)">
        <div class="stat-icon offline"><el-icon :size="24"><Warning /></el-icon></div>
        <div class="stat-info">
          <div class="stat-value text-amber-600">{{ stats.offline }}</div>
          <div class="stat-label">{{ stats.total > items.length ? `当前页离线 (${stats.offline}/${items.length})` : '离线 / 超时无响应' }}</div>
        </div>
      </div>
      <div class="stat-card" style="cursor: pointer;" @click="onlineFilter = 'unknown'; load(true)">
        <div class="stat-icon unknown"><el-icon :size="24"><Connection /></el-icon></div>
        <div class="stat-info">
          <div class="stat-value text-slate-500">{{ stats.unknown }}</div>
          <div class="stat-label">{{ stats.total > items.length ? `当前页未探测 (${stats.unknown}/${items.length})` : '尚未探测状态' }}</div>
        </div>
      </div>
    </div>

    <!-- 筛选面板与表格 -->
    <el-card shadow="never" class="table-card">
      <div class="table-toolbar">
        <div class="toolbar-left">
          <el-input
            v-model="search"
            placeholder="搜索计算机名、IP、MAC、责任人、部门..."
            :prefix-icon="Search"
            clearable
            style="width: 280px;"
            @clear="load(true)"
            @keyup.enter="load(true)"
          />
          <el-cascader
            v-model="orgFilter"
            :options="cascaderOptions"
            :props="{ checkStrictly: false, emitPath: false }"
            placeholder="筛选归属工位单元"
            clearable
            filterable
            style="width: 220px;"
            @change="load(true)"
          />
          <el-select
            v-model="onlineFilter"
            placeholder="在线状态"
            clearable
            style="width: 140px;"
            @change="load(true)"
          >
            <el-option label="全部状态" value="" />
            <el-option label="网络在线" value="online" />
            <el-option label="离线超时" value="offline" />
            <el-option label="未探测" value="unknown" />
          </el-select>
          <el-button type="primary" :icon="Search" @click="load(true)">查询</el-button>
          <el-button @click="search = ''; onlineFilter = ''; orgFilter = null; load(true)">重置</el-button>
        </div>
      </div>

      <el-table
        v-loading="loading"
        :data="items"
        stripe
        class="comp-table"
        @selection-change="selected = $event"
      >
        <el-table-column v-if="auth.can('computer.manage')" type="selection" width="48" />
        <el-table-column label="在线状态" width="110" align="center">
          <template #default="{ row }">
            <div class="status-indicator-wrap">
              <span
                class="status-dot"
                :class="{
                  online: row.status === 'online',
                  offline: row.status === 'offline',
                  unknown: row.status === 'unknown'
                }"
              />
              <span class="status-text">
                {{ row.status === 'online' ? (row.responseTimeMs ? `${row.responseTimeMs}ms` : '在线') : row.status === 'offline' ? '离线' : '未测' }}
              </span>
            </div>
          </template>
        </el-table-column>

        <el-table-column label="计算机名称" prop="name" min-width="160">
          <template #default="{ row }">
            <div class="comp-name-cell">
              <span class="comp-name">{{ row.name }}</span>
              <span v-if="row.os" class="comp-os-tag">{{ row.os }}</span>
            </div>
          </template>
        </el-table-column>

        <el-table-column label="IP 地址" min-width="150">
          <template #default="{ row }">
            <span class="ip-code cursor-pointer" title="点击复制" @click="copyText(row.ip)">
              {{ row.ip }}
            </span>
          </template>
        </el-table-column>

        <el-table-column label="MAC 地址" min-width="160">
          <template #default="{ row }">
            <span v-if="row.mac" class="mac-code">{{ row.mac }}</span>
            <span v-else class="text-slate-400">-</span>
          </template>
        </el-table-column>

        <el-table-column label="责任人 / 部门" min-width="150">
          <template #default="{ row }">
            <div class="owner-cell">
              <span class="font-medium">{{ row.owner || '-' }}</span>
              <span v-if="row.department" class="text-slate-400 text-xs">({{ row.department }})</span>
            </div>
          </template>
        </el-table-column>

        <el-table-column label="空间层级归属" min-width="180">
          <template #default="{ row }">
            <div v-if="row.unitName || row.areaName || row.workshopName" class="hierarchy-cell">
              <span v-if="row.workshopName" class="hierarchy-tag ws">{{ row.workshopName }}</span>
              <span v-if="row.areaName" class="hierarchy-tag area">{{ row.areaName }}</span>
              <span v-if="row.unitName" class="hierarchy-tag unit">{{ row.unitName }}</span>
            </div>
            <span v-else class="text-slate-400">未划分组织</span>
          </template>
        </el-table-column>

        <el-table-column label="物理具体位置" prop="location" min-width="160" show-overflow-tooltip>
          <template #default="{ row }">
            <span v-if="row.location" class="location-text">
              <el-icon><Location /></el-icon> {{ row.location }}
            </span>
            <span v-else class="text-slate-400">-</span>
          </template>
        </el-table-column>

        <el-table-column label="全光接入拓扑" min-width="210" show-overflow-tooltip>
          <template #default="{ row }">
            <div v-if="row.terminalBoxName || row.onuName" class="topo-cell">
              <span
                v-if="row.terminalBoxName"
                class="topo-tag box"
                style="cursor: pointer;"
                :title="`点击打开网络终端箱工作台 (${row.terminalBoxName})`"
                @click.stop="openWorkbench(row.terminalBoxId, row.onuDeviceId)"
              >
                箱: {{ row.terminalBoxName }}
              </span>
              <span
                v-if="row.onuAlias || row.onuName"
                class="topo-tag onu"
                style="cursor: pointer;"
                :title="`点击打开网络终端箱/ONU工作台 (${row.onuAlias || row.onuName})`"
                @click.stop="openWorkbench(row.terminalBoxId, row.onuDeviceId)"
              >
                ONU: {{ row.onuAlias || row.onuName }}
              </span>
              <span class="topo-tag port">
                {{ row.isSwitchPort ? `SW-P${row.portNumber}` : `LAN${row.portNumber}` }}
              </span>
            </div>
            <span v-else class="text-slate-400">未挂载链路</span>
          </template>
        </el-table-column>

        <el-table-column
          label="操作"
          width="180"
          fixed="right"
          align="right"
        >
          <template #default="{ row }">
            <el-button
              type="primary"
              link
              size="small"
              :icon="VideoPlay"
              title="即时探测网络是否在线"
              @click="probeSingle(row as any)"
            >
              Ping
            </el-button>
            <el-button
              v-if="auth.can('computer.manage')"
              type="primary"
              link
              size="small"
              :icon="Edit"
              @click="handleEdit(row as any)"
            >
              编辑
            </el-button>
            <el-button
              v-if="auth.can('computer.manage')"
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
      <div class="pagination-wrap">
        <el-pagination
          v-model:current-page="page"
          v-model:page-size="pageSize"
          :total="total"
          :page-sizes="[10, 20, 50, 100]"
          layout="total, sizes, prev, pager, next, jumper"
          @current-change="load()"
          @size-change="load(true)"
        />
      </div>
    </el-card>

    <!-- 电脑增改弹窗 -->
    <div v-if="selected.length && auth.can('computer.manage')" class="filter-card filter-bar"><span>已选择 {{ selected.length }} 台电脑</span><el-button type="primary" @click="batchOpen = true">批量修改归属与网络</el-button></div>
    <el-dialog v-model="batchOpen" title="批量变更预览" width="760px">
      <el-alert title="整批校验并事务提交。任一记录冲突时，全部记录保持原状。" type="info" :closable="false" />
      <el-checkbox v-model="batchOrg">调整组织归属</el-checkbox><el-cascader v-if="batchOrg" v-model="batchUnit" :options="cascaderOptions" :props="{ emitPath: false }" clearable placeholder="空值表示解除归属" />
      <div style="margin: 16px 0;"><el-checkbox v-model="batchNetwork">调整网络接入</el-checkbox></div>
      <div v-if="batchNetwork" class="filter-bar"><el-select v-model="batchBox" clearable placeholder="终端箱" @change="batchOnu = undefined"><el-option v-for="b in terminalBoxes" :key="b.id" :label="b.name" :value="b.id" /></el-select><el-select v-model="batchOnu" clearable placeholder="ONU"><el-option v-for="o in onus.filter(o => o.terminalBoxId === batchBox)" :key="o.id" :label="o.alias || o.onuName" :value="o.id" /></el-select><el-input-number v-model="batchPort" :min="1" /><el-checkbox v-model="batchSwitch">下挂交换机</el-checkbox></div>
      <p class="muted">端口按资产编号顺序连续分配，提交时重新校验占用。</p>
      <el-table :data="[...selected].sort((a,b) => a.id - b.id)"><el-table-column prop="name" label="受影响电脑" /><el-table-column prop="ip" label="IP" /><el-table-column label="原归属"><template #default="{ row }">{{ row.unitName || row.areaName || row.workshopName || '未分配' }}</template></el-table-column><el-table-column label="原接入"><template #default="{ row }">{{ row.terminalBoxName || '—' }} ／ {{ row.onuName || '—' }} ／ {{ row.portNumber }}</template></el-table-column><el-table-column label="变更后"><template #default="{ $index }">{{ batchOrg ? (organization.units.find(u => u.id === batchUnit)?.name || '解除归属') : '保留归属' }}<br />{{ batchNetwork ? (batchOnu ? 'ONU ' + batchOnu + ' ／ ' + (batchPort + $index) + ' 口' : '解除网络绑定') : '保留网络' }}</template></el-table-column></el-table>
      <template #footer><el-button @click="batchOpen = false">返回修改</el-button><el-button type="primary" :loading="batchBusy" @click="submitBatch">校验并整批提交</el-button></template>
    </el-dialog>
    <ComputerDialog
      v-model="dialogOpen"
      :computer="editingComputer"
      :organization="organization"
      :terminal-boxes="terminalBoxes"
      :onus="onus"
      @saved="load()"
    />

    <!-- 终端箱运维工作台抽屉 -->
    <TerminalBoxWorkbenchDrawer
      v-model="workbenchDrawerOpen"
      :box-id="viewingBoxId"
      :onu-id="viewingOnuId"
      @refresh="load()"
    />
  </div>
</template>

<style scoped>
.computers-page {
  padding: 16px 20px 32px;
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.stats-row {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(220px, 1fr));
  gap: 16px;
}

.stat-card {
  display: flex;
  align-items: center;
  gap: 14px;
  background: var(--el-bg-color);
  border: 1px solid var(--el-border-color-lighter);
  border-radius: 8px;
  padding: 14px 18px;
  box-shadow: 0 1px 3px rgba(0, 0, 0, 0.04);
}

.stat-icon {
  width: 48px;
  height: 48px;
  border-radius: 10px;
  display: flex;
  align-items: center;
  justify-content: center;
}
.stat-icon.total { background: rgba(59, 130, 246, 0.12); color: #2563eb; }
.stat-icon.online { background: rgba(16, 185, 129, 0.12); color: #059669; }
.stat-icon.offline { background: rgba(245, 158, 11, 0.12); color: #d97706; }
.stat-icon.unknown { background: rgba(148, 163, 184, 0.15); color: #64748b; }

.stat-value {
  font-size: 24px;
  font-weight: 700;
  line-height: 1.2;
}
.stat-label {
  font-size: 13px;
  color: var(--el-text-color-secondary);
  margin-top: 2px;
}

.table-card {
  border-radius: 8px;
}

.table-toolbar {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 16px;
  flex-wrap: wrap;
  gap: 12px;
}

.toolbar-left {
  display: flex;
  align-items: center;
  gap: 12px;
  flex-wrap: wrap;
}

.status-indicator-wrap {
  display: inline-flex;
  align-items: center;
  gap: 6px;
}

.status-dot {
  width: 9px;
  height: 9px;
  border-radius: 50%;
  display: inline-block;
}
.status-dot.online { background: #10b981; box-shadow: 0 0 0 3px rgba(16, 185, 129, 0.2); }
.status-dot.offline { background: #ef4444; box-shadow: 0 0 0 3px rgba(239, 68, 68, 0.2); }
.status-dot.unknown { background: #94a3b8; }

.status-text {
  font-size: 12px;
  font-weight: 500;
}

.comp-name-cell {
  display: flex;
  flex-direction: column;
  gap: 2px;
}
.comp-name {
  font-weight: 600;
  color: var(--el-text-color-primary);
}
.comp-os-tag {
  font-size: 11px;
  color: var(--el-text-color-placeholder);
}

.ip-code {
  font-family: monospace;
  font-size: 13px;
  font-weight: 600;
  color: #2563eb;
  background: rgba(37, 99, 235, 0.08);
  padding: 2px 6px;
  border-radius: 4px;
}
.ip-code:hover {
  text-decoration: underline;
}

.mac-code {
  font-family: monospace;
  font-size: 12px;
  color: var(--el-text-color-secondary);
}

.hierarchy-cell {
  display: flex;
  gap: 4px;
  flex-wrap: wrap;
}
.hierarchy-tag {
  font-size: 11px;
  padding: 1px 5px;
  border-radius: 3px;
}
.hierarchy-tag.ws { background: rgba(59, 130, 246, 0.1); color: #2563eb; }
.hierarchy-tag.area { background: rgba(16, 185, 129, 0.1); color: #059669; }
.hierarchy-tag.unit { background: rgba(245, 158, 11, 0.1); color: #d97706; }

.topo-cell {
  display: flex;
  gap: 4px;
  flex-wrap: wrap;
}
.topo-tag {
  font-size: 11px;
  padding: 1px 6px;
  border-radius: 3px;
  background: var(--el-fill-color);
  color: var(--el-text-color-regular);
}
.topo-tag.box { background: rgba(99, 102, 241, 0.1); color: #4f46e5; }
.topo-tag.onu { background: rgba(14, 165, 233, 0.1); color: #0284c7; }
.topo-tag.port { background: rgba(245, 158, 11, 0.1); color: #b45309; }

.location-text {
  display: inline-flex;
  align-items: center;
  gap: 4px;
  font-size: 12.5px;
  color: var(--el-text-color-regular);
}

.pagination-wrap {
  margin-top: 16px;
  display: flex;
  justify-content: flex-end;
}
</style>
