<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { Search, Warning } from '@element-plus/icons-vue'
import type { Organization, PonKanbanResponseDto } from '../../../api'
import { buildFullOrgCascaderOptions, matchesOrgScope } from '../../../lib/organization'
import TerminalBoxKanbanCard from '../TerminalBoxKanbanCard.vue'

const props = withDefaults(
  defineProps<{
    kanbanData: PonKanbanResponseDto | null
    organization: Organization
    loading: boolean
    canManage: boolean
    initialStatusFilter?: 'all' | 'alert' | 'healthy'
  }>(),
  {
    initialStatusFilter: 'all'
  }
)

const emit = defineEmits<{
  (e: 'diagnoseOnu', onu: any): void
  (e: 'rebootOnu', onu: any): void
  (e: 'viewTopology', boxOrId: any): void
  (e: 'editBox', box: any): void
  (e: 'viewHistory', boxOrId: any): void
  (e: 'update:statusFilter', filter: 'all' | 'alert' | 'healthy'): void
}>()

const statusFilter = ref<'all' | 'alert' | 'healthy'>(props.initialStatusFilter)
const orgScopeModel = ref<string | undefined>(undefined)
const search = ref('')

const orgCascaderOptions = computed(() => buildFullOrgCascaderOptions(props.organization))

watch(() => props.initialStatusFilter, (val) => {
  if (val && val !== statusFilter.value) {
    statusFilter.value = val
  }
})

watch(statusFilter, (val) => {
  emit('update:statusFilter', val)
})

const alertBoxCount = computed(() => {
  if (!props.kanbanData) return 0
  return props.kanbanData.summary.dangerBoxes + props.kanbanData.summary.warningBoxes
})

const filteredBoxes = computed(() => {
  if (!props.kanbanData) return []
  return props.kanbanData.boxes.filter(b => {
    if (statusFilter.value === 'alert' && b.healthState !== 'danger' && b.healthState !== 'warning') return false
    if (statusFilter.value === 'healthy' && b.healthState !== 'healthy') return false
    if (orgScopeModel.value && !matchesOrgScope(b, orgScopeModel.value, props.organization)) return false
    if (search.value.trim()) {
      const q = search.value.trim().toLowerCase()
      const matchBox = b.name.toLowerCase().includes(q) || b.code.toLowerCase().includes(q) || (b.location && b.location.toLowerCase().includes(q))
      const matchOnu = b.onus.some(o => o.onuName.toLowerCase().includes(q) || (o.alias && o.alias.toLowerCase().includes(q)) || o.sn.toLowerCase().includes(q))
      const matchCam = b.directCameras.concat(b.onus.flatMap(o => o.cameras)).some(c => c.name.toLowerCase().includes(q) || (c.alias && c.alias.toLowerCase().includes(q)) || (c.ip && c.ip.toLowerCase().includes(q)))
      const matchComp = (b.directComputers || []).concat(b.onus.flatMap(o => o.computers || [])).some(c =>
        c.name.toLowerCase().includes(q) ||
        (c.ip && c.ip.toLowerCase().includes(q)) ||
        (c.owner && c.owner.toLowerCase().includes(q)) ||
        (c.department && c.department.toLowerCase().includes(q))
      )
      if (!matchBox && !matchOnu && !matchCam && !matchComp) return false
    }
    return true
  })
})
</script>

<template>
  <div class="kanban-panel">
    <div class="kanban-toolbar">
      <div class="toolbar-left">
        <el-radio-group v-model="statusFilter" size="default">
          <el-radio-button value="all">
            全部终端箱 ({{ kanbanData?.boxes.length ?? 0 }})
          </el-radio-button>
          <el-radio-button value="alert">
            <span style="color: #b91c1c; font-weight: 600;">🔴 仅看存在掉线箱体 ({{ alertBoxCount }})</span>
          </el-radio-button>
          <el-radio-button value="healthy">
            <span style="color: #047857;">🟢 仅看全正常箱体 ({{ kanbanData?.summary.healthyBoxes ?? 0 }})</span>
          </el-radio-button>
        </el-radio-group>

        <!-- 车间 -> 区域 -> 单元 多级级联筛选 -->
        <el-cascader
          v-model="orgScopeModel"
          :options="orgCascaderOptions"
          :props="{ checkStrictly: true, emitPath: false }"
          placeholder="筛选区域与车间"
          clearable
          filterable
          style="width: 220px;"
        />

        <el-input
          v-model="search"
          placeholder="搜索终端箱名 / 编号 / ONU SN / 摄像头 / 电脑 IP..."
          clearable
          style="width: 340px;"
          :prefix-icon="Search"
        />
      </div>
    </div>

    <div v-if="alertBoxCount > 0 && statusFilter === 'all'" class="alert-banner">
      <el-icon><Warning /></el-icon>
      <span>
        全网当前共有 <strong>{{ alertBoxCount }}</strong> 个网络终端箱存在掉线故障
        （其中 <strong>{{ kanbanData?.summary.dangerBoxes ?? 0 }}</strong> 箱存在 ONU 离线/掉电，
        <strong>{{ kanbanData?.summary.warningBoxes ?? 0 }}</strong> 箱存在摄像头掉线）。
      </span>
      <el-button
        type="danger"
        size="small"
        link
        style="margin-left: 8px; font-weight: 600;"
        @click="statusFilter = 'alert'"
      >
        点击一键聚焦故障箱体 &rarr;
      </el-button>
    </div>

    <div v-loading="loading" class="kanban-cards-list">
      <div v-if="filteredBoxes.length === 0" class="kanban-empty">
        <el-empty description="未找到符合筛选条件的终端箱" />
      </div>

      <template v-else>
        <TerminalBoxKanbanCard
          v-for="b in filteredBoxes"
          :key="b.id"
          :box="b"
          @diagnose-onu="emit('diagnoseOnu', $event)"
          @reboot-onu="emit('rebootOnu', $event)"
          @view-topology="emit('viewTopology', $event)"
          @edit-box="emit('editBox', $event)"
          @view-history="emit('viewHistory', $event)"
        />
      </template>
    </div>
  </div>
</template>

<style scoped>
.kanban-panel {
  display: flex;
  flex-direction: column;
  gap: 14px;
}
.kanban-toolbar {
  display: flex;
  justify-content: flex-start;
  align-items: center;
  flex-wrap: wrap;
  gap: 12px;
  background: #ffffff;
  border: 1px solid var(--border, #e2e8f0);
  border-radius: 8px;
  padding: 12px 16px;
}
.toolbar-left {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 12px;
  width: 100%;
}

.alert-banner {
  background: #fef2f2;
  border: 1px solid #fecaca;
  color: #991b1b;
  border-radius: 6px;
  padding: 10px 14px;
  display: flex;
  align-items: center;
  gap: 8px;
  font-size: 13px;
}

.kanban-cards-list {
  display: flex;
  flex-direction: column;
  gap: 12px;
}
.kanban-empty {
  background: #ffffff;
  border: 1px solid #e2e8f0;
  border-radius: 8px;
  padding: 40px;
}
</style>
