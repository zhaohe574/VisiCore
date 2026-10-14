<script setup lang="ts">
import { onMounted, ref, watch } from 'vue'
import {
  Clock,
  Connection,
  InfoFilled,
  Refresh,
  Search,
  VideoCamera,
  Warning
} from '@element-plus/icons-vue'
import { ponApi, type DeviceOfflineHistoryDto, type TerminalBoxDto } from '../../api'

const props = defineProps<{
  initialTerminalBoxId?: number | null
  terminalBoxes: TerminalBoxDto[]
}>()

const loading = ref(false)
const items = ref<DeviceOfflineHistoryDto[]>([])
const total = ref(0)
const page = ref(1)
const pageSize = ref(20)

// 过滤条件
const terminalBoxId = ref<number | undefined>(props.initialTerminalBoxId ?? undefined)
const deviceType = ref<string>('all')
const statusFilter = ref<string>('all')
const search = ref<string>('')

watch(
  () => props.initialTerminalBoxId,
  (val) => {
    terminalBoxId.value = val ?? undefined
    void loadData(true)
  }
)

async function loadData(resetPage = false) {
  if (resetPage) page.value = 1
  loading.value = true
  try {
    const res = await ponApi.offlineHistory({
      terminalBoxId: terminalBoxId.value,
      deviceType: deviceType.value === 'all' ? undefined : deviceType.value,
      status: statusFilter.value === 'all' ? undefined : statusFilter.value,
      search: search.value.trim() || undefined,
      page: page.value,
      pageSize: pageSize.value
    })
    items.value = res.items
    total.value = res.total
  } catch {
  } finally {
    loading.value = false
  }
}

function resetFilters() {
  terminalBoxId.value = undefined
  deviceType.value = 'all'
  statusFilter.value = 'all'
  search.value = ''
  void loadData(true)
}

function formatDuration(seconds?: number | null, isOngoing = false, downTime?: string): string {
  if (isOngoing && downTime) {
    const diff = Math.floor((Date.now() - new Date(downTime).getTime()) / 1000)
    if (diff <= 0) return '刚刚掉线'
    const d = Math.floor(diff / 86400)
    const h = Math.floor((diff % 86400) / 3600)
    const m = Math.floor((diff % 3600) / 60)
    if (d > 0) return `持续 ${d}天${h}小时 (未恢复)`
    if (h > 0) return `持续 ${h}小时${m}分 (未恢复)`
    return `持续 ${m}分钟 (未恢复)`
  }

  if (seconds === null || seconds === undefined || seconds < 0) return '—'
  if (seconds < 60) return `${seconds} 秒`
  const minutes = Math.floor(seconds / 60)
  if (minutes < 60) return `${minutes} 分钟`
  const hours = Math.floor(minutes / 60)
  const remMinutes = minutes % 60
  if (hours < 24) return `${hours} 小时 ${remMinutes} 分`
  const days = Math.floor(hours / 24)
  const remHours = hours % 24
  return `${days} 天 ${remHours} 小时`
}

function formatTime(iso?: string | null): string {
  if (!iso) return '—'
  const d = new Date(iso)
  if (isNaN(d.getTime())) return iso
  const pad = (n: number) => String(n).padStart(2, '0')
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())} ${pad(d.getHours())}:${pad(d.getMinutes())}:${pad(d.getSeconds())}`
}

onMounted(() => {
  void loadData(true)
})
</script>

<template>
  <div class="history-view">
    <!-- 顶部过滤工具栏 -->
    <div class="history-toolbar">
      <el-select
        v-model="terminalBoxId"
        placeholder="全部终端箱"
        clearable
        filterable
        style="width: 200px;"
        @change="loadData(true)"
      >
        <el-option label="全部终端箱" :value="undefined as any" />
        <el-option
          v-for="b in terminalBoxes"
          :key="b.id"
          :value="b.id"
          :label="`${b.name} (${b.code})${b.location ? ' - ' + b.location : ''}`"
        />
      </el-select>

      <el-select
        v-model="deviceType"
        style="width: 140px;"
        @change="loadData(true)"
      >
        <el-option label="全部设备类型" value="all" />
        <el-option label="仅 ONU 光猫" value="onu" />
        <el-option label="仅 摄像头" value="camera" />
      </el-select>

      <el-select
        v-model="statusFilter"
        style="width: 150px;"
        @change="loadData(true)"
      >
        <el-option label="全部事件状态" value="all" />
        <el-option label="⚠️ 仍在掉线中" value="ongoing" />
        <el-option label="✅ 已恢复上线" value="recovered" />
      </el-select>

      <el-input
        v-model="search"
        placeholder="搜索设备名称 / SN / IP / 掉线原因..."
        clearable
        style="width: 260px;"
        :prefix-icon="Search"
        @clear="loadData(true)"
        @keyup.enter="loadData(true)"
      />

      <el-button-group>
        <el-button type="primary" :icon="Search" @click="loadData(true)">查询</el-button>
        <el-button @click="resetFilters">重置</el-button>
        <el-button :icon="Refresh" :loading="loading" @click="loadData()">刷新</el-button>
      </el-button-group>
    </div>

    <!-- 历史表格 -->
    <div class="table-card" style="margin-top: 14px;">
      <el-table
        v-loading="loading"
        :data="items"
        row-key="id"
        empty-text="暂无匹配的设备掉线与恢复历史记录"
      >
        <el-table-column label="发生时间" width="170">
          <template #default="{ row }">
            <span class="monospace" style="font-size: 12.5px; color: #1e293b;">
              {{ formatTime(row.downTime) }}
            </span>
          </template>
        </el-table-column>

        <el-table-column label="归属终端箱" min-width="150" show-overflow-tooltip>
          <template #default="{ row }">
            <div v-if="row.terminalBoxName" style="display: flex; align-items: center; gap: 6px;">
              <el-tag size="small" type="primary" effect="plain">{{ row.terminalBoxCode || 'TB' }}</el-tag>
              <span style="font-weight: 500;">{{ row.terminalBoxName }}</span>
            </div>
            <span v-else class="muted">未绑定终端箱</span>
          </template>
        </el-table-column>

        <el-table-column label="设备类型" width="110" align="center">
          <template #default="{ row }">
            <el-tag v-if="row.deviceType === 'onu'" size="small" type="primary">
              <el-icon style="margin-right: 2px;"><Connection /></el-icon>
              ONU 光猫
            </el-tag>
            <el-tag v-else size="small" type="success">
              <el-icon style="margin-right: 2px;"><VideoCamera /></el-icon>
              摄像头
            </el-tag>
          </template>
        </el-table-column>

        <el-table-column label="受影响设备" min-width="200" show-overflow-tooltip>
          <template #default="{ row }">
            <div style="line-height: 1.4;">
              <div style="font-weight: 600; color: #1e293b;">
                {{ row.deviceName }}
              </div>
              <div v-if="row.deviceIdentifier" class="muted monospace" style="font-size: 11.5px;">
                {{ row.deviceType === 'onu' ? `SN: ${row.deviceIdentifier}` : `IP: ${row.deviceIdentifier}` }}
              </div>
            </div>
          </template>
        </el-table-column>

        <el-table-column label="掉线根因 / 说明" min-width="180" show-overflow-tooltip>
          <template #default="{ row }">
            <el-tag
              size="small"
              :type="row.upTime ? 'info' : 'danger'"
              effect="plain"
              style="font-weight: 500;"
            >
              {{ row.reason || '掉线' }}
            </el-tag>
          </template>
        </el-table-column>

        <el-table-column label="恢复上线时间" width="170">
          <template #default="{ row }">
            <span v-if="row.upTime" class="monospace" style="font-size: 12.5px; color: #10b981;">
              {{ formatTime(row.upTime) }}
            </span>
            <span v-else class="ongoing-badge">
              ● 仍未恢复
            </span>
          </template>
        </el-table-column>

        <el-table-column label="影响持续时长" width="160" align="center">
          <template #default="{ row }">
            <span
              v-if="!row.upTime"
              class="ongoing-duration-pill"
            >
              {{ formatDuration(row.durationSeconds, true, row.downTime) }}
            </span>
            <span v-else class="duration-text">
              {{ formatDuration(row.durationSeconds) }}
            </span>
          </template>
        </el-table-column>

        <el-table-column label="当前状态" width="120" align="center">
          <template #default="{ row }">
            <el-tag v-if="row.upTime" size="small" type="success" effect="dark">
              已恢复上线
            </el-tag>
            <el-tag v-else size="small" type="danger" effect="dark">
              持续掉线中
            </el-tag>
          </template>
        </el-table-column>
      </el-table>

      <div class="pagination-bar">
        <el-pagination
          v-model:current-page="page"
          v-model:page-size="pageSize"
          :total="total"
          :page-sizes="[15, 30, 50, 100]"
          layout="total, sizes, prev, pager, next, jumper"
          @current-change="loadData()"
          @size-change="loadData(true)"
        />
      </div>
    </div>
  </div>
</template>

<style scoped>
.history-toolbar {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 10px;
}
.ongoing-badge {
  color: #ef4444;
  font-weight: 600;
  font-size: 12px;
}
.ongoing-duration-pill {
  color: #b91c1c;
  background: #fee2e2;
  font-size: 11.5px;
  font-weight: 600;
  padding: 2px 8px;
  border-radius: 12px;
  display: inline-block;
}
.duration-text {
  font-family: 'JetBrains Mono', monospace;
  font-size: 12px;
  color: #475569;
}
.monospace {
  font-family: 'JetBrains Mono', monospace;
}
.muted {
  color: #94a3b8;
}
.pagination-bar {
  display: flex;
  justify-content: flex-end;
  padding: 12px 0;
}
</style>
