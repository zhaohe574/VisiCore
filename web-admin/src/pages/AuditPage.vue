<script setup lang="ts">
import { computed, ref } from 'vue'
import {
  Download,
  List,
  Refresh,
  Search,
  User
} from '@element-plus/icons-vue'
import { ElMessage } from 'element-plus'
import { managementApi, type Audit } from '../api'
import { dateTime } from '../lib/format'
import { usePaged } from '../composables/usePaged'
import PageHeader from '../components/PageHeader.vue'

const range = ref<[Date, Date] | null>(null)
const action = ref('')

const { items, total, page, pageSize, search, loading, error, load } = usePaged(
  managementApi.audit,
  () => ({
    action: action.value || undefined,
    from: range.value?.[0].toISOString(),
    to: range.value?.[1].toISOString()
  })
)

const detailDialog = ref(false)
const selectedAudit = ref<Audit | null>(null)

const stats = computed(() => {
  const all = items.value as Audit[]
  const loginCount = all.filter(a => a.action.toLowerCase().includes('login') || a.action.includes('登录')).length
  const deviceCount = all.filter(a => a.resource?.toLowerCase().includes('device') || a.action.includes('设备')).length
  return { loginCount, deviceCount }
})

function viewDetail(row: Audit) {
  selectedAudit.value = row
  detailDialog.value = true
}

function exportLogs() {
  if (!items.value.length) {
    ElMessage.warning('当前无数据可导出')
    return
  }
  const dataStr = 'data:text/json;charset=utf-8,' + encodeURIComponent(JSON.stringify(items.value, null, 2))
  const downloadAnchor = document.createElement('a')
  downloadAnchor.setAttribute('href', dataStr)
  downloadAnchor.setAttribute('download', `visicore-audit-${new Date().toISOString().slice(0, 10)}.json`)
  document.body.appendChild(downloadAnchor)
  downloadAnchor.click()
  downloadAnchor.remove()
  ElMessage.success('审计日志已导出为 JSON 文件')
}

function resetFilter() {
  search.value = ''
  action.value = ''
  range.value = null
  void load(true)
}
</script>

<template>
  <div>
    <PageHeader title="操作审计" :count="total" description="完整记录管理员与操作人员的关键业务变更、设备同步、鉴权访问与安全日志">
      <el-button :icon="Download" @click="exportLogs">导出本页日志</el-button>
      <el-button :icon="Refresh" :loading="loading" @click="load()">刷新</el-button>
    </PageHeader>

    <!-- 顶部 KPI 统计 -->
    <div class="kpi-grid">
      <div class="kpi-card">
        <div class="kpi-icon-wrap blue"><el-icon><List /></el-icon></div>
        <div class="kpi-content">
          <div class="kpi-label">审计记录总数</div>
          <div class="kpi-value">{{ total }}</div>
          <div class="kpi-sub">符合检索条件的日志条目</div>
        </div>
      </div>

      <div class="kpi-card">
        <div class="kpi-icon-wrap teal"><el-icon><User /></el-icon></div>
        <div class="kpi-content">
          <div class="kpi-label">登录与认证操作</div>
          <div class="kpi-value">{{ stats.loginCount }}</div>
          <div class="kpi-sub">当前页账号鉴权日志</div>
        </div>
      </div>

      <div class="kpi-card">
        <div class="kpi-icon-wrap purple"><el-icon><Refresh /></el-icon></div>
        <div class="kpi-content">
          <div class="kpi-label">设备与通道变更</div>
          <div class="kpi-value">{{ stats.deviceCount }}</div>
          <div class="kpi-sub">当前页硬件资源配置</div>
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
          placeholder="搜索操作账号、资源或摘要..."
          aria-label="搜索审计"
          style="width: 260px;"
          @clear="load(true)"
        />

        <el-input
          v-model="action"
          clearable
          placeholder="操作行为类型..."
          style="width: 170px;"
          aria-label="操作类型"
        />

        <el-date-picker
          v-model="range"
          type="datetimerange"
          start-placeholder="开始时间"
          end-placeholder="结束时间"
          format="YYYY-MM-DD HH:mm"
          style="width: 320px;"
        />

        <el-button type="primary" native-type="submit" :icon="Search">查询</el-button>
        <el-button @click="resetFilter">重置</el-button>
      </form>
    </div>

    <el-alert v-if="error" :title="error" type="error" :closable="false" show-icon style="margin-bottom: 12px;" />

    <!-- 审计数据表格卡片 -->
    <div class="table-card">
      <el-table v-loading="loading" :data="items" empty-text="暂无审计日志记录">
        <el-table-column label="操作时间" min-width="170">
          <template #default="{ row }">
            <span style="font-weight: 500;">{{ dateTime(row.createdAt) }}</span>
          </template>
        </el-table-column>

        <el-table-column prop="username" label="操作账号" min-width="130">
          <template #default="{ row }">
            <strong>{{ row.username }}</strong>
          </template>
        </el-table-column>

        <el-table-column prop="action" label="操作行为" min-width="160" show-overflow-tooltip>
          <template #default="{ row }">
            <el-tag size="small" type="info">{{ row.action }}</el-tag>
          </template>
        </el-table-column>

        <el-table-column prop="resource" label="操作资源对象" min-width="140" show-overflow-tooltip>
          <template #default="{ row }">
            <span class="hash">{{ row.resource || '—' }}</span>
          </template>
        </el-table-column>

        <el-table-column prop="summary" label="行为内容摘要" min-width="280" show-overflow-tooltip />

        <el-table-column prop="clientIp" label="操作客户端 IP" min-width="140">
          <template #default="{ row }">
            <span class="hash">{{ row.clientIp }}</span>
          </template>
        </el-table-column>

        <el-table-column label="详情" width="80" fixed="right">
          <template #default="{ row }">
            <el-button link type="primary" @click="viewDetail(row as Audit)">查看</el-button>
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

    <!-- 审计日志详情弹窗 -->
    <el-dialog v-model="detailDialog" title="操作审计日志明细" width="560px">
      <dl v-if="selectedAudit" class="data-list">
        <div><dt>操作时间</dt><dd>{{ dateTime(selectedAudit.createdAt) }}</dd></div>
        <div><dt>操作人员</dt><dd><strong>{{ selectedAudit.username }}</strong></dd></div>
        <div><dt>客户端来源 IP</dt><dd class="hash">{{ selectedAudit.clientIp }}</dd></div>
        <div><dt>操作类型</dt><dd><el-tag size="small">{{ selectedAudit.action }}</el-tag></dd></div>
        <div><dt>关联资源</dt><dd class="hash">{{ selectedAudit.resource || '—' }}</dd></div>
        <div><dt>摘要内容</dt><dd class="preserve-lines">{{ selectedAudit.summary }}</dd></div>
      </dl>
      <template #footer>
        <el-button type="primary" @click="detailDialog = false">确定</el-button>
      </template>
    </el-dialog>
  </div>
</template>
