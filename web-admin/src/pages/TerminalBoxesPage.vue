<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { useActivePage } from '../composables/useActivePage'
import { useRoute } from 'vue-router'
import { Box, DataBoard, Plus, Refresh } from '@element-plus/icons-vue'
import { ElMessage } from 'element-plus'
import {
  managementApi,
  ponApi,
  type Organization,
  type PonKanbanResponseDto,
  type TerminalBoxDto
} from '../api'
import { useAction } from '../composables/useAction'
import { useAuth } from '../stores/auth'
import PageHeader from '../components/PageHeader.vue'
import TerminalBoxDialog from '../components/pon/TerminalBoxDialog.vue'
import TerminalBoxTopologyDrawer from '../components/pon/TerminalBoxTopologyDrawer.vue'
import OnuDiagnosisDialog from '../components/pon/OnuDiagnosisDialog.vue'
import DeviceOfflineHistoryTable from '../components/pon/DeviceOfflineHistoryTable.vue'
import PonKanbanTab from '../components/pon/tabs/PonKanbanTab.vue'
import PonBoxTableTab from '../components/pon/tabs/PonBoxTableTab.vue'

const route = useRoute()
const auth = useAuth()
const { confirm } = useAction()

// Tab 切换：看板视图、台账视图
const activeTab = ref<'kanban' | 'boxes'>('kanban')

// 基础数据
const organization = ref<Organization>({ workshops: [], areas: [], units: [] })
const terminalBoxes = ref<TerminalBoxDto[]>([])
const loadingBoxes = ref(false)
const hasLoadedBoxes = ref(false)

// 看板数据
const kanbanData = ref<PonKanbanResponseDto | null>(null)
const loadingKanban = ref(false)
const hasLoadedKanban = ref(false)
const kanbanStatusFilter = ref<'all' | 'alert' | 'healthy'>('all')

// 弹窗与抽屉控制
const boxDialogOpen = ref(false)
const editingBox = ref<TerminalBoxDto | null>(null)

const topologyDrawerOpen = ref(false)
const viewingBoxId = ref<number | null>(null)

const diagnosisOpen = ref(false)
const diagnosingOnu = ref<any>(null)

const historyDrawerOpen = ref(false)
const selectedBoxHistoryId = ref<number | null>(null)

const alertBoxCount = computed(() => {
  if (!kanbanData.value) return 0
  return kanbanData.value.summary.dangerBoxes + kanbanData.value.summary.warningBoxes
})

let inFlightBoxes: Promise<void> | null = null
let inFlightKanban: Promise<void> | null = null
let inFlightOrg: Promise<void> | null = null

async function loadBoxes(silent = false) {
  if (inFlightBoxes) return inFlightBoxes
  const ticket = lifecycle.ticket('boxes')
  if (ticket === undefined) return
  if (!silent) loadingBoxes.value = true

  inFlightBoxes = (async () => {
    try {
      const data = await ponApi.terminalBoxes()
      if (lifecycle.current(ticket)) {
        terminalBoxes.value = data
        hasLoadedBoxes.value = true
      }
    } catch (err: any) {
      if (lifecycle.current(ticket) && !silent) {
        ElMessage.error(err.message || '加载网络终端箱台账失败')
      }
    } finally {
      if (lifecycle.current(ticket) && !silent) {
        loadingBoxes.value = false
      }
      inFlightBoxes = null
    }
  })()
  return inFlightBoxes
}

async function loadKanban(silent = false) {
  if (inFlightKanban) return inFlightKanban
  const ticket = lifecycle.ticket('kanban')
  if (ticket === undefined) return
  if (!silent) loadingKanban.value = true

  inFlightKanban = (async () => {
    try {
      const data = await ponApi.kanban()
      if (lifecycle.current(ticket)) {
        kanbanData.value = data
        hasLoadedKanban.value = true
      }
    } catch (err: any) {
      if (lifecycle.current(ticket) && !silent) {
        ElMessage.error(err.message || '加载终端箱设备看板失败')
      }
    } finally {
      if (lifecycle.current(ticket) && !silent) {
        loadingKanban.value = false
      }
      inFlightKanban = null
    }
  })()
  return inFlightKanban
}

async function loadOrg() {
  if (organization.value.workshops.length > 0) return
  if (inFlightOrg) return inFlightOrg
  inFlightOrg = (async () => {
    try {
      organization.value = await managementApi.organization()
    } catch {
      // 容错处理
    } finally {
      inFlightOrg = null
    }
  })()
  return inFlightOrg
}

async function refreshAll(silent = false) {
  if (organization.value.workshops.length === 0) {
    void loadOrg()
  }
  const tasks: Promise<void>[] = []
  if (activeTab.value === 'kanban') {
    tasks.push(loadKanban(silent))
    if (hasLoadedBoxes.value) tasks.push(loadBoxes(true))
  } else {
    tasks.push(loadBoxes(silent))
    if (hasLoadedKanban.value) tasks.push(loadKanban(true))
  }
  await Promise.all(tasks)
}

const lifecycle = useActivePage(
  (silent) => refreshAll(silent ?? true),
  {
    kinds: ['terminal-box', 'onu', 'reconnected'],
    debounceMs: 1200
  }
)

function openCreateBox() {
  editingBox.value = null
  boxDialogOpen.value = true
}

function openEditBox(box: any) {
  editingBox.value = box
  boxDialogOpen.value = true
}

function viewTopology(boxOrId: number | { id: number }) {
  viewingBoxId.value = typeof boxOrId === 'number' ? boxOrId : boxOrId.id
  topologyDrawerOpen.value = true
}

function onViewBoxHistory(boxOrId: number | { id: number }) {
  selectedBoxHistoryId.value = typeof boxOrId === 'number' ? boxOrId : boxOrId.id
  historyDrawerOpen.value = true
  if (!hasLoadedBoxes.value) {
    void loadBoxes(true)
  }
}

function openDiagnosis(onu: any) {
  diagnosingOnu.value = onu
  diagnosisOpen.value = true
}

async function rebootOnu(onu: any) {
  await confirm(
    `确认向华为网管下发远程维护/重启指令？\n将远程维护 ONU【${onu.alias || onu.onuName}】(SN: ${onu.sn})，预计中断光纤网络 1~2 分钟。无需现场开箱。`,
    async () => {
      await ponApi.rebootOnu(onu.id)
      ElMessage.success('远程维护指令已成功执行')
      await loadKanban(false)
    },
    'ONU 重启成功'
  )
}

async function onBoxSaved() {
  await Promise.all([loadBoxes(false), loadKanban(false)])
}

function applyRouteQuery() {
  if (route.query.tab === 'boxes') {
    activeTab.value = 'boxes'
  } else if (route.query.tab === 'kanban') {
    activeTab.value = 'kanban'
  }
  if (route.query.boxId) {
    viewTopology(Number(route.query.boxId))
  }
}

watch(
  () => route.query,
  () => applyRouteQuery()
)

watch(activeTab, (tab) => {
  if (tab === 'boxes' && !hasLoadedBoxes.value) {
    void loadBoxes(false)
  } else if (tab === 'kanban' && !hasLoadedKanban.value) {
    void loadKanban(false)
  }
})

onMounted(() => {
  applyRouteQuery()
  void loadOrg()
  if (activeTab.value === 'boxes') {
    void loadBoxes(false)
  } else {
    void loadKanban(false)
  }
})
</script>

<template>
  <div class="page-container terminal-boxes-page">
    <PageHeader
      title="网络终端箱设施与监控"
      subtitle="现场物理网络终端箱资产运行看板与台账，支持箱体单元划分、接线拓扑与掉线告警监控"
    >
      <template #actions>
        <el-button-group>
          <el-button :icon="Refresh" :loading="loadingBoxes || loadingKanban" @click="() => refreshAll(false)">
            刷新数据
          </el-button>
          <el-button
            v-if="auth.can('pon.manage')"
            type="primary"
            :icon="Plus"
            @click="openCreateBox"
          >
            新建终端箱
          </el-button>
        </el-button-group>
      </template>
    </PageHeader>

    <el-tabs v-model="activeTab" class="tabs-card" style="margin-top: 16px;">
      <!-- TAB 1: 终端箱设备监控看板 -->
      <el-tab-pane name="kanban">
        <template #label>
          <span class="tab-label-wrap">
            <el-icon><DataBoard /></el-icon>
            <span>终端箱设备监控看板</span>
            <el-badge
              v-if="alertBoxCount > 0"
              :value="alertBoxCount"
              type="danger"
              class="tab-badge"
            />
          </span>
        </template>
        <PonKanbanTab
          :kanban-data="kanbanData"
          :organization="organization"
          :loading="loadingKanban"
          :can-manage="auth.can('pon.manage')"
          :initial-status-filter="kanbanStatusFilter"
          @diagnose-onu="openDiagnosis"
          @reboot-onu="rebootOnu"
          @view-topology="viewTopology"
          @edit-box="openEditBox"
          @view-history="onViewBoxHistory"
          @update:status-filter="kanbanStatusFilter = $event"
        />
      </el-tab-pane>

      <!-- TAB 2: 网络终端箱台账 -->
      <el-tab-pane name="boxes">
        <template #label>
          <span class="tab-label-wrap">
            <el-icon><Box /></el-icon>
            <span>网络终端箱物理台账</span>
          </span>
        </template>
        <PonBoxTableTab
          :terminal-boxes="terminalBoxes"
          :organization="organization"
          :loading="loadingBoxes"
          :can-manage="auth.can('pon.manage')"
          @edit="openEditBox"
          @topology="viewTopology"
        />
      </el-tab-pane>
    </el-tabs>

    <!-- 新建/编辑终端箱弹窗 -->
    <TerminalBoxDialog
      v-model="boxDialogOpen"
      :box="editingBox"
      :organization="organization"
      @saved="onBoxSaved"
    />

    <!-- 箱内接线拓扑抽屉 -->
    <TerminalBoxTopologyDrawer
      v-model="topologyDrawerOpen"
      :box-id="viewingBoxId"
    />

    <!-- ONU 智能诊断定界抽屉/弹窗 -->
    <OnuDiagnosisDialog
      v-model="diagnosisOpen"
      :onu="diagnosingOnu"
      @reboot="rebootOnu"
    />

    <!-- 终端箱历史掉线抽屉 -->
    <el-drawer
      v-model="historyDrawerOpen"
      title="终端箱设备掉线与恢复历史"
      size="65%"
      destroy-on-close
    >
      <div style="padding: 12px 16px;">
        <DeviceOfflineHistoryTable
          :terminal-boxes="terminalBoxes"
          :initial-terminal-box-id="selectedBoxHistoryId"
        />
      </div>
    </el-drawer>
  </div>
</template>

<style scoped>
.terminal-boxes-page {
  padding-bottom: 24px;
}

.tab-label-wrap {
  display: inline-flex;
  align-items: center;
  gap: 6px;
}

.tab-badge {
  margin-left: 4px;
}
</style>
