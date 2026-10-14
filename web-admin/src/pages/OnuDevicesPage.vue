<script setup lang="ts">
import { onMounted, ref, watch } from 'vue'
import { useRoute } from 'vue-router'
import { Clock, Cpu, Refresh } from '@element-plus/icons-vue'
import { ElMessage } from 'element-plus'
import {
  ponApi,
  type OnuDeviceDto,
  type TerminalBoxDto
} from '../api'
import { usePaged } from '../composables/usePaged'
import { useAction } from '../composables/useAction'
import { useAuth } from '../stores/auth'
import PageHeader from '../components/PageHeader.vue'
import OnuDiagnosisDialog from '../components/pon/OnuDiagnosisDialog.vue'
import OnuEditDialog from '../components/pon/OnuEditDialog.vue'
import DeviceOfflineHistoryTable from '../components/pon/DeviceOfflineHistoryTable.vue'
import PonOnuTableTab from '../components/pon/tabs/PonOnuTableTab.vue'

const route = useRoute()
const auth = useAuth()
const { confirm } = useAction()

// Tab 切换：全量 ONU 设备台账、设备掉线与恢复历史
const activeTab = ref<'onus' | 'history'>('onus')

// 下拉终端箱关联数据
const terminalBoxes = ref<TerminalBoxDto[]>([])
const loadingBoxes = ref(false)
const syncingEsight = ref(false)
const selectedBoxHistoryId = ref<number | null>(null)

// 筛选与分页
const statusFilter = ref<string>('')
const terminalBoxFilter = ref<number | undefined>()

const { items, total, page, pageSize, search, loading, error, load } = usePaged(
  ponApi.onus,
  () => ({
    status: statusFilter.value || undefined,
    terminalBoxId: terminalBoxFilter.value ?? undefined
  }),
  ['onu.changed']
)

// 对话框控制
const diagnosisOpen = ref(false)
const diagnosingOnu = ref<any>(null)

const editOnuOpen = ref(false)
const editingOnu = ref<OnuDeviceDto | null>(null)

async function loadBoxes() {
  loadingBoxes.value = true
  try {
    terminalBoxes.value = await ponApi.terminalBoxes()
  } catch {
  } finally {
    loadingBoxes.value = false
  }
}

async function syncFromEsight() {
  syncingEsight.value = true
  try {
    const res = await ponApi.syncEsight()
    ElMessage.success(`官方北向 OpenAPI 同步完成！共更新/入库 ${res.count} 台 ONU 设备`)
    await Promise.all([loadBoxes(), load(true)])
  } catch (err: any) {
    ElMessage.error(err.message || '从华为网管同步失败，请检查网管服务与网络连接')
  } finally {
    syncingEsight.value = false
  }
}

function openDiagnosis(onu: any) {
  diagnosingOnu.value = onu
  diagnosisOpen.value = true
}

function openEditOnu(onu: OnuDeviceDto) {
  editingOnu.value = onu
  editOnuOpen.value = true
}

async function rebootOnu(onu: any) {
  await confirm(
    `确认向华为网管下发远程维护/重启指令？\n将远程维护 ONU【${onu.alias || onu.onuName}】(SN: ${onu.sn})，预计中断光纤网络 1~2 分钟。无需现场开箱。`,
    async () => {
      await ponApi.rebootOnu(onu.id)
      ElMessage.success('远程维护指令已成功执行')
      await load()
    },
    'ONU 重启成功'
  )
}

function resetOnuFilters() {
  search.value = ''
  statusFilter.value = ''
  terminalBoxFilter.value = undefined
  load(true)
}

function applyRouteQuery() {
  if (route.query.tab === 'history') {
    activeTab.value = 'history'
  } else if (route.query.tab === 'onus') {
    activeTab.value = 'onus'
  }
  if (route.query.terminalBoxId) {
    terminalBoxFilter.value = Number(route.query.terminalBoxId)
    selectedBoxHistoryId.value = Number(route.query.terminalBoxId)
  }
  if (route.query.search) {
    search.value = String(route.query.search)
    activeTab.value = 'onus'
    load(true).then(() => {
      const q = String(route.query.search)
      const match = items.value.find(i => String(i.id) === q || i.sn === q || i.onuName === q || (i.alias && i.alias === q))
      if (match) {
        openDiagnosis(match)
      }
    })
  }
}

watch(
  () => [route.query.tab, route.query.search, route.query.terminalBoxId],
  () => applyRouteQuery()
)

onMounted(() => {
  loadBoxes()
  applyRouteQuery()
})
</script>

<template>
  <div class="page-container onu-devices-page">
    <PageHeader
      title="ONU 设备台账与光网运维"
      subtitle="全网接入 ONU 终端资产台账、接收光功率排障诊断、远程维护重启与设备掉线恢复历史日志"
    >
      <template #actions>
        <el-button-group>
          <el-button :icon="Refresh" :loading="loading" @click="load(true)">
            刷新数据
          </el-button>
          <el-button
            v-if="auth.can('pon.manage')"
            type="warning"
            :loading="syncingEsight"
            :icon="Refresh"
            @click="syncFromEsight"
          >
            从华为网管全量同步
          </el-button>
        </el-button-group>
      </template>
    </PageHeader>

    <el-tabs v-model="activeTab" class="tabs-card" style="margin-top: 16px;">
      <!-- TAB 1: 全量 ONU 设备台账与智能根因运维 -->
      <el-tab-pane label="全量 ONU 设备台账" name="onus">
        <template #label>
          <span class="tab-label-wrap">
            <el-icon><Cpu /></el-icon>
            <span>全量 ONU 设备台账</span>
          </span>
        </template>
        <PonOnuTableTab
          v-model:search="search"
          v-model:status-filter="statusFilter"
          v-model:terminal-box-filter="terminalBoxFilter"
          v-model:page="page"
          v-model:page-size="pageSize"
          :items="items"
          :total="total"
          :loading="loading"
          :error="error"
          :terminal-boxes="terminalBoxes"
          :can-manage="auth.can('pon.manage')"
          @query="load"
          @reset="resetOnuFilters"
          @diagnose="openDiagnosis"
          @edit="openEditOnu"
          @reboot="rebootOnu"
        />
      </el-tab-pane>

      <!-- TAB 2: 设备掉线与恢复历史记录 -->
      <el-tab-pane name="history">
        <template #label>
          <span class="tab-label-wrap">
            <el-icon><Clock /></el-icon>
            <span>设备掉线与恢复历史</span>
          </span>
        </template>
        <div class="card" style="padding: 16px;">
          <DeviceOfflineHistoryTable
            :terminal-boxes="terminalBoxes"
            :initial-terminal-box-id="selectedBoxHistoryId"
          />
        </div>
      </el-tab-pane>
    </el-tabs>

    <!-- ONU 智能诊断定界抽屉/弹窗 -->
    <OnuDiagnosisDialog
      v-model="diagnosisOpen"
      :onu="diagnosingOnu"
      @reboot="rebootOnu"
    />

    <!-- ONU 编辑抽屉/弹窗 -->
    <OnuEditDialog
      v-model="editOnuOpen"
      :onu="editingOnu"
      :terminal-boxes="terminalBoxes"
      @saved="() => load()"
    />
  </div>
</template>

<style scoped>
.onu-devices-page {
  padding-bottom: 24px;
}

.tab-label-wrap {
  display: inline-flex;
  align-items: center;
  gap: 6px;
}
</style>
