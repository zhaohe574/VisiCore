<script setup lang="ts">
import { onMounted, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import {
  Cpu,
  Monitor,
  Refresh,
  TrendCharts
} from '@element-plus/icons-vue'
import { ElMessage } from 'element-plus'
import {
  ponApi,
  type ApplyTopologyCorrectionRequest,
  type AutoTopologyBindingSuggestionDto,
  type AutoTopologyDiscoverResultDto,
  type OltDeviceDto,
  type OpticalHealthIndexDto,
  type PonStatisticsDto
} from '../api'
import { useAction } from '../composables/useAction'
import { useAuth } from '../stores/auth'
import PageHeader from '../components/PageHeader.vue'
import PonHealthTab from '../components/pon/tabs/PonHealthTab.vue'
import PonTopologyTab from '../components/pon/tabs/PonTopologyTab.vue'
import PonAutoDiscoverTab from '../components/pon/tabs/PonAutoDiscoverTab.vue'

const route = useRoute()
const router = useRouter()
const auth = useAuth()
const { confirm } = useAction()

// Tab 切换：全网健康大屏、OLT拓扑、自动纠偏
const activeTab = ref<'health' | 'topology' | 'autodiscover'>('health')

// 基础统计与光链路数据
const statistics = ref<PonStatisticsDto | null>(null)
const syncingEsight = ref(false)

// 全网健康度大屏数据
const healthIndexData = ref<OpticalHealthIndexDto | null>(null)
const loadingHealthIndex = ref(false)

// OLT 网元与 PON 口数据
const oltList = ref<OltDeviceDto[]>([])
const loadingOlts = ref(false)

// 拓扑自动发现与纠偏数据
const autoDiscoverResult = ref<AutoTopologyDiscoverResultDto | null>(null)
const loadingDiscover = ref(false)
const applyingCorrections = ref(false)

async function loadHealthIndex() {
  loadingHealthIndex.value = true
  try {
    healthIndexData.value = await ponApi.healthIndex()
  } catch (err: any) {
    ElMessage.error(err.message || '加载全网健康指数失败')
  } finally {
    loadingHealthIndex.value = false
  }
}

async function loadOlts() {
  loadingOlts.value = true
  try {
    oltList.value = await ponApi.olts()
  } catch (err: any) {
    ElMessage.error(err.message || '加载 OLT 网元与 PON 端口拓扑失败')
  } finally {
    loadingOlts.value = false
  }
}

async function loadStatistics() {
  try {
    statistics.value = await ponApi.statistics()
  } catch {}
}

async function runAutoDiscover() {
  loadingDiscover.value = true
  try {
    autoDiscoverResult.value = await ponApi.autoDiscoverTopology()
    ElMessage.success(`智能拓扑分析完成！评估了 ${autoDiscoverResult.value.totalEvaluated} 路通道，生成 ${autoDiscoverResult.value.suggestedCount} 条纠偏建议`)
  } catch (err: any) {
    ElMessage.error(err.message || '自动拓扑发现失败')
  } finally {
    loadingDiscover.value = false
  }
}

async function applyCorrections(itemsToApply: AutoTopologyBindingSuggestionDto[]) {
  if (itemsToApply.length === 0) {
    ElMessage.warning('请选择需要应用的拓扑纠偏项')
    return
  }
  await confirm(`确认采纳并应用选中的 ${itemsToApply.length} 项光链路承载绑定纠偏？`, async () => {
    applyingCorrections.value = true
    try {
      const payload: ApplyTopologyCorrectionRequest = {
        bindings: itemsToApply.map(s => ({
          channelId: s.channelId,
          onuDeviceId: s.suggestedOnuId,
          terminalBoxId: s.suggestedTerminalBoxId,
          portNumber: s.suggestedPortNumber,
          isSwitchPort: false,
          portLabel: `LAN${s.suggestedPortNumber}`
        }))
      }
      const res = await ponApi.applyTopologyCorrections(payload)
      ElMessage.success(`成功应用 ${res.appliedCount} 条光链路拓扑纠偏绑定！`)
      await runAutoDiscover()
    } catch (err: any) {
      ElMessage.error(err.message || '应用拓扑纠偏失败')
    } finally {
      applyingCorrections.value = false
    }
  })
}

function filterByPonPort(portId: number) {
  router.push({ path: '/app/onus', query: { search: `GPON 0/1/${portId}` } })
}

async function syncFromEsight() {
  syncingEsight.value = true
  try {
    const res = await ponApi.syncEsight()
    ElMessage.success(`官方北向 OpenAPI 同步完成！共更新/入库 ${res.count} 台 ONU 设备`)
    await Promise.all([loadHealthIndex(), loadOlts(), loadStatistics()])
  } catch (err: any) {
    ElMessage.error(err.message || '从华为网管同步失败，请检查网管服务与网络连接')
  } finally {
    syncingEsight.value = false
  }
}

async function refreshAll() {
  await Promise.all([loadHealthIndex(), loadOlts(), loadStatistics()])
}

function applyRouteQuery() {
  if (route.query.tab === 'topology' || route.query.tab === 'autodiscover' || route.query.tab === 'health') {
    activeTab.value = route.query.tab as any
  }
}

watch(
  () => route.query.tab,
  () => applyRouteQuery()
)

onMounted(() => {
  loadHealthIndex()
  loadOlts()
  loadStatistics()
  applyRouteQuery()
})
</script>

<template>
  <div class="page-container pon-page">
    <PageHeader
      title="华为 POL 全光网络管理"
      subtitle="基于华为 iMaster NeoSight 官方北向 OpenAPI 网关 (32102)，呈现 OLT/GPON 端口机架拓扑、全网光链路健康度大屏与智能拓扑纠偏"
    >
      <template #actions>
        <el-button-group>
          <el-button :icon="Refresh" :loading="loadingHealthIndex || loadingOlts" @click="refreshAll">
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
      <!-- TAB 1: 全网光链路健康度大屏 -->
      <el-tab-pane name="health">
        <template #label>
          <span class="tab-label-wrap">
            <el-icon><TrendCharts /></el-icon>
            <span>全网光链路健康大屏</span>
          </span>
        </template>
        <PonHealthTab
          :health-index-data="healthIndexData"
          :loading="loadingHealthIndex"
          :syncing-esight="syncingEsight"
          @refresh="loadHealthIndex"
          @sync="syncFromEsight"
        />
      </el-tab-pane>

      <!-- TAB 2: OLT 核心机架与 PON 端口拓扑 -->
      <el-tab-pane name="topology">
        <template #label>
          <span class="tab-label-wrap">
            <el-icon><Cpu /></el-icon>
            <span>OLT核心与PON端口拓扑</span>
          </span>
        </template>
        <PonTopologyTab
          :olts="oltList"
          :loading="loadingOlts"
          @select-port="filterByPonPort"
        />
      </el-tab-pane>

      <!-- TAB 3: MAC/IP 自动拓扑发现与纠偏 -->
      <el-tab-pane name="autodiscover">
        <template #label>
          <span class="tab-label-wrap">
            <el-icon><Monitor /></el-icon>
            <span>智能拓扑发现与纠偏</span>
            <el-badge v-if="autoDiscoverResult?.suggestedCount" :value="autoDiscoverResult.suggestedCount" type="primary" class="tab-badge" />
          </span>
        </template>
        <PonAutoDiscoverTab
          :result="autoDiscoverResult"
          :loading="loadingDiscover"
          :applying="applyingCorrections"
          @discover="runAutoDiscover"
          @apply="applyCorrections"
        />
      </el-tab-pane>
    </el-tabs>
  </div>
</template>

<style scoped>
.pon-page {
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
