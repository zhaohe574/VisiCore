<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import {
  Check,
  Close,
  Download,
  Files,
  Loading,
  Plus,
  Refresh,
  Search,
  VideoCamera,
  Warning
} from '@element-plus/icons-vue'
import { workflowApi, type ExportJob } from '../api'
import { bytes, dateTime, errorMessage, timeRange } from '../lib/format'
import { usePaged } from '../composables/usePaged'
import { useAction } from '../composables/useAction'
import PageHeader from '../components/PageHeader.vue'
import StatusBadge from '../components/StatusBadge.vue'
import ChannelPicker from '../components/ChannelPicker.vue'

const { busy, run, confirm } = useAction()
const dialog = ref(false)
const channelId = ref<number>()
const range = ref<[Date, Date] | null>(null)
const statusFilter = ref('')

const { items, total, page, pageSize, search, loading, error, load } = usePaged(
  workflowApi.exports,
  () => ({
    status: statusFilter.value || undefined
  }),
  ['export.changed', 'access.changed']
)

const stats = computed(() => {
  const all = items.value as ExportJob[]
  const running = all.filter(j => ['queued', 'running'].includes(j.state)).length
  const completed = all.filter(j => j.state === 'completed').length
  const failed = all.filter(j => j.state === 'failed').length
  const totalBytes = all.filter(j => j.state === 'completed').reduce((sum, j) => sum + (j.fileSize || 0), 0)
  return { running, completed, failed, totalBytes }
})

async function create() {
  if (
    await run(async () => {
      if (!channelId.value) throw new Error('请选择导出视频通道')
      if (!range.value) throw new Error('请选择导出的录像时间范围')
      const time = timeRange(range.value, 24)
      await workflowApi.createExport(channelId.value, time.start, time.end)
      await load(true)
    }, '录像导出任务已提交处理')
  ) {
    dialog.value = false
    channelId.value = undefined
    range.value = null
  }
}

async function cancel(job: ExportJob) {
  await confirm(`取消通道“${job.channelName}”的录像导出任务？`, async () => {
    await workflowApi.cancelExport(job.id)
    await load()
  }, '导出任务已取消')
}

async function retry(job: ExportJob) {
  await run(async () => {
    await workflowApi.retryExport(job.id)
    await load()
  }, '导出任务已重新加入排队队列')
}

function expired(job: ExportJob) {
  return !!job.expiresAt && Date.parse(job.expiresAt) <= Date.now()
}

function resetFilter() {
  search.value = ''
  statusFilter.value = ''
  void load(true)
}

let timer: ReturnType<typeof setInterval>

onMounted(() => {
  timer = setInterval(() => {
    if (items.value.some(job => ['running', 'queued'].includes(job.state)) && !loading.value) {
      void load()
    }
  }, 10000)
})

onBeforeUnmount(() => clearInterval(timer))
</script>

<template>
  <div>
    <PageHeader title="录像导出" :count="total" description="异步将海康/第三方存储历史录像按时段剪切合并为标准 MP4 视频文件">
      <el-button :icon="Refresh" :loading="loading" @click="load()">刷新</el-button>
      <el-button type="primary" :icon="Plus" @click="dialog = true">新建导出任务</el-button>
    </PageHeader>

    <!-- 顶部 KPI 统计 -->
    <div class="kpi-grid">
      <div class="kpi-card">
        <div class="kpi-icon-wrap blue"><el-icon><Files /></el-icon></div>
        <div class="kpi-content">
          <div class="kpi-label">导出任务总数</div>
          <div class="kpi-value">{{ total }}</div>
          <div class="kpi-sub">历史录像提取记录</div>
        </div>
      </div>

      <div class="kpi-card">
        <div class="kpi-icon-wrap amber"><el-icon><Loading /></el-icon></div>
        <div class="kpi-content">
          <div class="kpi-label">排队 / 剪切中</div>
          <div class="kpi-value">{{ stats.running }}</div>
          <div class="kpi-sub">后台 Worker 实时处理</div>
        </div>
      </div>

      <div class="kpi-card">
        <div class="kpi-icon-wrap teal"><el-icon><Check /></el-icon></div>
        <div class="kpi-content">
          <div class="kpi-label">已生成成品</div>
          <div class="kpi-value">{{ stats.completed }}</div>
          <div class="kpi-sub">可随时下载至本地</div>
        </div>
      </div>

      <div class="kpi-card">
        <div class="kpi-icon-wrap purple"><el-icon><Download /></el-icon></div>
        <div class="kpi-content">
          <div class="kpi-label">已占用存储量</div>
          <div class="kpi-value">{{ bytes(stats.totalBytes) }}</div>
          <div class="kpi-sub">当前页导出成品容量</div>
        </div>
      </div>
    </div>

    <!-- 筛选卡片 -->
    <div class="filter-card">
      <form class="filter-bar" @submit.prevent="load(true)">
        <el-input
          v-model="search"
          clearable
          :prefix-icon="Search"
          placeholder="搜索通道名称..."
          aria-label="搜索导出任务"
          style="width: 240px;"
          @clear="load(true)"
        />

        <el-select v-model="statusFilter" clearable placeholder="全部任务状态" style="width: 160px;" @change="load(true)">
          <el-option label="排队中" value="queued" />
          <el-option label="执行中" value="running" />
          <el-option label="已完成" value="completed" />
          <el-option label="执行失败" value="failed" />
          <el-option label="已取消" value="cancelled" />
        </el-select>

        <el-button type="primary" native-type="submit" :icon="Search">查询</el-button>
        <el-button @click="resetFilter">重置</el-button>
      </form>
    </div>

    <el-alert v-if="error" :title="error" type="error" :closable="false" show-icon style="margin-bottom: 12px;" />

    <!-- 导出任务数据表格卡片 -->
    <div class="table-card">
      <el-table v-loading="loading" :data="items" empty-text="暂无导出任务记录">
        <el-table-column prop="channelName" label="目标通道" min-width="160" show-overflow-tooltip>
          <template #default="{ row }">
            <strong>{{ row.channelName }}</strong>
          </template>
        </el-table-column>

        <el-table-column label="剪切录像时段" min-width="240">
          <template #default="{ row }">
            <div style="font-size: 12.5px;">{{ dateTime(row.start) }}</div>
            <div class="muted" style="font-size: 11.5px;">至 {{ dateTime(row.end) }}</div>
          </template>
        </el-table-column>

        <el-table-column label="任务状态" width="112">
          <template #default="{ row }">
            <StatusBadge :value="row.state" />
          </template>
        </el-table-column>

        <el-table-column label="处理进度" min-width="190">
          <template #default="{ row }">
            <el-progress
              :percentage="Math.max(0, Math.min(100, Math.round(row.progress)))"
              :status="row.state === 'failed' ? 'exception' : row.state === 'completed' ? 'success' : undefined"
              :striped="row.state === 'running'"
              :striped-flow="row.state === 'running'"
            />
            <small v-if="row.error" class="field-error">{{ errorMessage(row.error) }}</small>
          </template>
        </el-table-column>

        <el-table-column label="文件大小" width="110">
          <template #default="{ row }">
            <span style="font-weight: 600;">{{ row.fileSize ? bytes(row.fileSize) : '—' }}</span>
          </template>
        </el-table-column>

        <el-table-column label="文件保留期限" min-width="170">
          <template #default="{ row }">
            <span class="muted">{{ dateTime(row.expiresAt) }}</span>
          </template>
        </el-table-column>

        <el-table-column label="操作" width="130" fixed="right">
          <template #default="{ row }">
            <div class="table-tools">
              <a
                v-if="row.state === 'completed' && !expired(row as ExportJob)"
                :href="workflowApi.exportUrl(row.id)"
                target="_blank"
                download
              >
                <el-button link type="primary" :icon="Download">下载</el-button>
              </a>
              <span v-else-if="row.state === 'completed'" class="muted" style="font-size: 12px;">已过期</span>

              <el-button
                v-if="['queued', 'running'].includes(row.state)"
                link
                type="danger"
                :icon="Close"
                :disabled="busy"
                @click="cancel(row as ExportJob)"
              >
                取消
              </el-button>

              <el-button
                v-if="['failed', 'cancelled'].includes(row.state)"
                link
                type="primary"
                :icon="Refresh"
                :disabled="busy"
                @click="retry(row as ExportJob)"
              >
                重试
              </el-button>
            </div>
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
          @current-change="load()"
          @size-change="load(true)"
        />
      </div>
    </div>

    <!-- 新建导出任务弹窗 -->
    <el-dialog v-model="dialog" title="新建录像剪切导出任务" width="560px">
      <el-form label-position="top">
        <el-form-item label="监控通道" required>
          <ChannelPicker v-model="channelId" />
        </el-form-item>

        <el-form-item label="录像时间范围（单次最多 24 小时）" required>
          <el-date-picker
            v-model="range"
            type="datetimerange"
            start-placeholder="开始时间"
            end-placeholder="结束时间"
            format="YYYY-MM-DD HH:mm:ss"
            style="width: 100%;"
          />
        </el-form-item>
      </el-form>

      <p class="muted" style="font-size: 12px; margin: 0;">
        提示：任务提交后，Worker 服务将自动连接对应 NVR / 流媒体分发器进行多流混流与 MP4 转封装，处理完成后可在本列表直接下载。
      </p>

      <template #footer>
        <el-button @click="dialog = false">取消</el-button>
        <el-button type="primary" :loading="busy" @click="create">提交导出</el-button>
      </template>
    </el-dialog>
  </div>
</template>
