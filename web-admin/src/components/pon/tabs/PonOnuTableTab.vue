<script setup lang="ts">
import { computed } from 'vue'
import { Edit, Odometer, Search, SwitchButton, Warning } from '@element-plus/icons-vue'
import { ElMessage } from 'element-plus'
import type { OnuDeviceDto, TerminalBoxDto } from '../../../api'

const props = defineProps<{
  items: OnuDeviceDto[]
  total: number
  loading: boolean
  error?: string | null
  terminalBoxes: TerminalBoxDto[]
  canManage: boolean
  search: string
  statusFilter: string
  terminalBoxFilter?: number
  page: number
  pageSize: number
}>()

const emit = defineEmits<{
  (e: 'update:search', val: string): void
  (e: 'update:statusFilter', val: string): void
  (e: 'update:terminalBoxFilter', val: number | undefined): void
  (e: 'update:page', val: number): void
  (e: 'update:pageSize', val: number): void
  (e: 'query', resetPage?: boolean): void
  (e: 'reset'): void
  (e: 'diagnose', onu: OnuDeviceDto): void
  (e: 'edit', onu: OnuDeviceDto): void
  (e: 'reboot', onu: OnuDeviceDto): void
}>()

const searchModel = computed({
  get: () => props.search,
  set: (val: string) => emit('update:search', val)
})

const statusModel = computed({
  get: () => props.statusFilter,
  set: (val: string) => emit('update:statusFilter', val)
})

const boxModel = computed({
  get: () => props.terminalBoxFilter,
  set: (val: number | undefined) => emit('update:terminalBoxFilter', val)
})

const pageModel = computed({
  get: () => props.page,
  set: (val: number) => emit('update:page', val)
})

const pageSizeModel = computed({
  get: () => props.pageSize,
  set: (val: number) => emit('update:pageSize', val)
})

function getRxPowerClass(rx?: number | null): string {
  if (rx === null || rx === undefined) return 'muted'
  if (rx >= -24 && rx <= -8) return 'rx-normal'
  if (rx < -24 && rx >= -27) return 'rx-warning'
  return 'rx-danger'
}

function copySn(sn: string) {
  navigator.clipboard.writeText(sn)
  ElMessage.success('SN 码已复制到剪贴板')
}
</script>

<template>
  <div class="card" style="margin-bottom: 16px;">
    <div class="search-toolbar">
      <el-input
        v-model="searchModel"
        placeholder="搜索 ONU 名称 / 别名 / SN / PON口 (如 GPON 0/1/0)..."
        clearable
        style="width: 320px;"
        :prefix-icon="Search"
        @clear="emit('query', true)"
        @keyup.enter="emit('query', true)"
      />

      <el-select
        v-model="statusModel"
        placeholder="运行与健康状态"
        clearable
        style="width: 170px;"
        @change="emit('query', true)"
      >
        <el-option label="全部状态" value="" />
        <el-option label="🟢 光网在线 (正常)" value="online" />
        <el-option label="🟡 弱光亚健康" value="sub_health_weak_light" />
        <el-option label="⚡ 供电中断 / 掉电 (Dying Gasp)" value="power_off" />
        <el-option label="💥 分光器/支干中断" value="splitter_broken" />
        <el-option label="🔴 分支断纤 / 链路中断" value="fiber_cut" />
        <el-option label="🚨 主干光缆中断" value="trunk_broken" />
        <el-option label="全部离线" value="offline" />
      </el-select>

      <el-select
        v-model="boxModel"
        placeholder="归属终端箱"
        clearable
        filterable
        style="width: 180px;"
        @change="emit('query', true)"
      >
        <el-option
          v-for="b in terminalBoxes"
          :key="b.id"
          :value="b.id"
          :label="`${b.name} (${b.code})${b.location ? ' - ' + b.location : ''}`"
        />
      </el-select>

      <el-button-group>
        <el-button type="primary" :icon="Search" @click="emit('query', true)">查询</el-button>
        <el-button @click="emit('reset')">重置</el-button>
      </el-button-group>
    </div>

    <div v-if="error" class="error-banner" style="margin-top: 12px;">
      <el-icon><Warning /></el-icon>
      <span>{{ error }}</span>
    </div>

    <!-- ONU 设备列表表格 -->
    <div class="table-card" style="margin-top: 14px;">
      <el-table
        v-loading="loading"
        :data="items"
        row-key="id"
        empty-text="未找到匹配的 ONU 记录（可点击右上角【从华为网管全量同步】拉取现场设备）"
      >
        <el-table-column prop="alias" label="ONU 业务别名 / 物理标识" min-width="210" show-overflow-tooltip>
          <template #default="{ row }">
            <div style="line-height: 1.4;">
              <div style="font-weight: 600; color: var(--primary); display: flex; align-items: center; gap: 4px;">
                {{ row.alias || row.onuName }}
                <el-tag v-if="row.ifDesc" size="small" effect="plain" type="info">{{ row.ifDesc }}</el-tag>
              </div>
              <div class="muted" style="font-size: 11px;">
                {{ row.onuName }} · OLT: {{ row.oltIp }}
                <span v-if="row.description" style="color: #64748b;"> · {{ row.description }}</span>
              </div>
            </div>
          </template>
        </el-table-column>

        <!-- 智能健康根因状态列 -->
        <el-table-column label="健康定界与根因" min-width="160">
          <template #default="{ row }">
            <div style="display: flex; flex-direction: column; align-items: flex-start; gap: 3px;">
              <el-tag v-if="row.healthStatus === 'healthy'" size="small" type="success">
                🟢 运行良好
              </el-tag>
              <el-tag v-else-if="row.healthStatus === 'sub_health_weak_light'" size="small" type="warning" effect="dark">
                🟡 弱光亚健康
              </el-tag>
              <el-tag v-else-if="row.healthStatus === 'power_off'" size="small" type="danger" effect="dark">
                ⚡ 供电中断 / 掉电
              </el-tag>
              <el-tag v-else-if="row.healthStatus === 'splitter_broken'" size="small" type="warning" effect="dark" style="background: #9333ea; border-color: #9333ea; color: #fff;">
                💥 分光/支干中断
              </el-tag>
              <el-tag v-else-if="row.healthStatus === 'trunk_broken'" size="small" type="danger" effect="dark" style="background: #8b0000; border-color: #8b0000;">
                🚨 主干光缆中断
              </el-tag>
              <el-tag v-else-if="row.healthStatus === 'fiber_cut'" size="small" type="danger" effect="plain">
                🔴 分支断纤/中断
              </el-tag>
              <el-tag v-else-if="row.healthStatus === 'jitter_buffering'" size="small" type="info" effect="dark">
                🛡️ 防抖缓冲
              </el-tag>
              <el-tag v-else size="small" :type="row.status === 'online' ? 'success' : 'danger'">
                {{ row.status === 'online' ? '在线' : '离线' }}
              </el-tag>

              <span v-if="row.rootCause" class="muted" style="font-size: 10.5px; line-height: 1.2;">
                {{ row.rootCause }}
              </span>
            </div>
          </template>
        </el-table-column>

        <el-table-column prop="terminalType" label="型号" width="115" show-overflow-tooltip />

        <el-table-column prop="sn" label="SN 码" min-width="165">
          <template #default="{ row }">
            <div style="display: flex; align-items: center; gap: 6px;">
              <span class="monospace" style="font-size: 12px; font-weight: 500;">{{ row.sn }}</span>
              <el-button link type="primary" size="small" @click="copySn(row.sn)">复制</el-button>
            </div>
          </template>
        </el-table-column>

        <el-table-column label="接收光衰 (Rx)" width="140" align="center">
          <template #default="{ row }">
            <template v-if="row.rxPower !== null && row.rxPower !== undefined">
              <span :class="getRxPowerClass(row.rxPower)" style="font-weight: 600; font-size: 12.5px;">
                {{ row.rxPower }} <small>dBm</small>
              </span>
            </template>
            <template v-else-if="row.status === 'online'">
              <el-tooltip content="光链路连通良好，工作于正常安全门限区间 (-8 ~ -24 dBm)。点击查看排障诊断" placement="top">
                <el-tag size="small" type="success" effect="plain" style="cursor: pointer;" @click="emit('diagnose', row as OnuDeviceDto)">
                  正常 (-8~-24dBm)
                </el-tag>
              </el-tooltip>
            </template>
            <template v-else>
              <span class="muted">— (离线)</span>
            </template>
          </template>
        </el-table-column>

        <el-table-column label="承载终端箱" min-width="135" show-overflow-tooltip>
          <template #default="{ row }">
            <el-tag v-if="row.terminalBoxName" size="small" type="info">
              {{ row.terminalBoxName }}
            </el-tag>
            <span v-else class="muted">未分配终端箱</span>
          </template>
        </el-table-column>

        <el-table-column prop="boundChannelCount" label="挂接摄像头" width="95" align="center">
          <template #default="{ row }">
            <el-tag size="small" :type="row.boundChannelCount > 0 ? 'success' : 'info'">
              {{ row.boundChannelCount }} 路
            </el-tag>
          </template>
        </el-table-column>

        <el-table-column label="操作" width="220" fixed="right">
          <template #default="{ row }">
            <div class="table-tools">
              <el-button link type="primary" :icon="Odometer" @click="emit('diagnose', row as OnuDeviceDto)">
                排障诊断
              </el-button>
              <el-button v-if="canManage" link type="primary" :icon="Edit" @click="emit('edit', row as OnuDeviceDto)">
                别名配置
              </el-button>
              <el-button
                v-if="canManage"
                link
                type="danger"
                :icon="SwitchButton"
                @click="emit('reboot', row as OnuDeviceDto)"
              >
                重启
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
  </div>
</template>

<style scoped>
.search-toolbar {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 10px;
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
.table-card {
  background: #ffffff;
  border: 1px solid #e2e8f0;
  border-radius: 6px;
  overflow: hidden;
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

.rx-normal { color: #10b981; }
.rx-warning { color: #f59e0b; }
.rx-danger { color: #ef4444; }
.muted { color: var(--text-muted, #64748b); }
.monospace { font-family: 'JetBrains Mono', monospace; }
</style>
