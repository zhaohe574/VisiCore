<script setup lang="ts">
import { computed, ref } from 'vue'
import { Box, Search } from '@element-plus/icons-vue'
import type { Organization, TerminalBoxDto } from '../../../api'
import { buildFullOrgCascaderOptions, matchesOrgScope } from '../../../lib/organization'

const props = defineProps<{
  terminalBoxes: TerminalBoxDto[]
  organization: Organization
  loading: boolean
  canManage: boolean
}>()

const emit = defineEmits<{
  (e: 'create'): void
  (e: 'edit', box: TerminalBoxDto): void
  (e: 'topology', boxId: number): void
}>()

const orgScopeModel = ref<string | undefined>(undefined)
const search = ref('')

const orgCascaderOptions = computed(() => buildFullOrgCascaderOptions(props.organization))

const filteredBoxes = computed(() => {
  return props.terminalBoxes.filter(b => {
    if (orgScopeModel.value && !matchesOrgScope(b, orgScopeModel.value, props.organization)) return false
    if (search.value.trim()) {
      const q = search.value.trim().toLowerCase()
      const matchCode = b.code.toLowerCase().includes(q)
      const matchName = b.name.toLowerCase().includes(q)
      const matchLoc = (b.location && b.location.toLowerCase().includes(q)) ?? false
      const matchArea = (b.areaName && b.areaName.toLowerCase().includes(q)) ?? false
      const matchUnit = (b.unitName && b.unitName.toLowerCase().includes(q)) ?? false
      if (!matchCode && !matchName && !matchLoc && !matchArea && !matchUnit) return false
    }
    return true
  })
})
</script>

<template>
  <div class="card">
    <div class="table-header-wrap">
      <div style="font-weight: 600; font-size: 14.5px; color: var(--text-primary); display: flex; align-items: center; gap: 6px;">
        <el-icon><Box /></el-icon>
        <span>现场物理网络终端箱列表</span>
        <span class="muted" style="font-size: 12px; font-weight: normal;">
          (共 {{ filteredBoxes.length }} / {{ terminalBoxes.length }} 个终端箱)
        </span>
      </div>

      <div style="display: flex; align-items: center; gap: 12px; flex-wrap: wrap;">
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
          placeholder="搜索终端箱编号 / 名称 / 位置..."
          clearable
          style="width: 240px;"
          :prefix-icon="Search"
        />
      </div>
    </div>

    <el-table
      v-loading="loading"
      :data="filteredBoxes"
      size="default"
      style="cursor: pointer;"
      @row-click="(row: TerminalBoxDto) => emit('topology', row.id)"
    >
      <el-table-column prop="code" label="终端箱编号" width="140">
        <template #default="{ row }">
          <strong class="monospace" style="color: var(--primary);">{{ row.code }}</strong>
        </template>
      </el-table-column>
      <el-table-column prop="name" label="终端箱名称" min-width="160" show-overflow-tooltip />
      <el-table-column prop="location" label="安装物理位置" min-width="180" show-overflow-tooltip />
      <el-table-column prop="areaName" label="归属区域" min-width="150" show-overflow-tooltip>
        <template #default="{ row }">
          <el-tag v-if="row.areaName || row.unitName" size="small" type="info">{{ row.areaName || row.unitName }}</el-tag>
          <span v-else class="muted">—</span>
        </template>
      </el-table-column>
      <el-table-column prop="onuCount" label="容纳 ONU" width="105" align="center">
        <template #default="{ row }">
          <el-tag size="small">{{ row.onuCount }} 台</el-tag>
        </template>
      </el-table-column>
      <el-table-column prop="channelCount" label="下挂摄像头" width="105" align="center">
        <template #default="{ row }">
          <el-tag size="small" type="success">{{ row.channelCount }} 路</el-tag>
        </template>
      </el-table-column>
      <el-table-column prop="computerCount" label="下挂电脑" width="105" align="center">
        <template #default="{ row }">
          <el-tag size="small" type="primary">{{ row.computerCount || 0 }} 台</el-tag>
        </template>
      </el-table-column>
      <el-table-column label="操作" width="180" align="center">
        <template #default="{ row }">
          <el-button link type="primary" size="small" @click.stop="emit('topology', row.id)">运维工作台</el-button>
          <el-button v-if="canManage" link type="primary" size="small" @click.stop="emit('edit', row as TerminalBoxDto)">编辑</el-button>
        </template>
      </el-table-column>
    </el-table>
  </div>
</template>

<style scoped>
.card {
  background: #ffffff;
  border: 1px solid #e2e8f0;
  border-radius: 8px;
  padding: 16px;
}
.table-header-wrap {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 16px;
  flex-wrap: wrap;
  gap: 12px;
}
.monospace {
  font-family: 'JetBrains Mono', monospace;
}
.muted {
  color: var(--text-muted, #64748b);
}
</style>
