<script setup lang="ts">
import { computed, ref } from 'vue'
import {
  Connection,
  Delete,
  Monitor,
  Refresh,
  Search,
  User
} from '@element-plus/icons-vue'
import { managementApi, type OnlineSession } from '../api'
import { dateTime } from '../lib/format'
import { usePaged } from '../composables/usePaged'
import { useAction } from '../composables/useAction'
import PageHeader from '../components/PageHeader.vue'

const clientTypeFilter = ref('')

const { items, total, page, pageSize, search, loading, error, load } = usePaged(
  managementApi.sessions,
  () => ({
    clientType: clientTypeFilter.value || undefined
  }),
  ['access.changed']
)

const { busy, confirm, run } = useAction()

const selectedSessions = ref<OnlineSession[]>([])

const versions = computed(() => [
  ...items.value.reduce((map, session) => {
    const key = `${session.clientType === 'desktop' ? '桌面客户端' : 'Web 工作区'} ${session.clientVersion}`
    map.set(key, (map.get(key) || 0) + 1)
    return map
  }, new Map<string, number>())
])

const stats = computed(() => {
  const all = items.value as OnlineSession[]
  const desktop = all.filter(s => s.clientType === 'desktop').length
  const web = all.filter(s => s.clientType !== 'desktop').length
  return { desktop, web }
})

async function revoke(id: string, name: string) {
  await confirm(
    `确认强制下线“${name}”的此会话？\n该会话将立即失效，用户需重新登录。`,
    async () => {
      await managementApi.revokeSession(id)
      await load()
    },
    '会话已成功撤销'
  )
}

async function batchRevoke() {
  if (!selectedSessions.value.length) return
  await confirm(
    `确认批量下线选中的 ${selectedSessions.value.length} 个客户端会话？`,
    async () => {
      await run(async () => {
        for (const s of selectedSessions.value) {
          await managementApi.revokeSession(s.id)
        }
        await load()
        selectedSessions.value = []
      }, '批量会话下线完成')
    }
  )
}

function resetFilter() {
  search.value = ''
  clientTypeFilter.value = ''
  void load(true)
}
</script>

<template>
  <div>
    <PageHeader title="在线会话" :count="total" description="实时监视连接到平台的桌面客户端与 Web 管理员会话，支持异常接入强制踢除">
      <el-button :icon="Refresh" :loading="loading" @click="load()">刷新</el-button>
    </PageHeader>

    <!-- 顶部 KPI 统计 -->
    <div class="kpi-grid">
      <div class="kpi-card">
        <div class="kpi-icon-wrap blue"><el-icon><Connection /></el-icon></div>
        <div class="kpi-content">
          <div class="kpi-label">活跃会话总数</div>
          <div class="kpi-value">{{ total }}</div>
          <div class="kpi-sub">当前连接至平台的终端</div>
        </div>
      </div>

      <div class="kpi-card">
        <div class="kpi-icon-wrap purple"><el-icon><Monitor /></el-icon></div>
        <div class="kpi-content">
          <div class="kpi-label">桌面客户端</div>
          <div class="kpi-value">{{ stats.desktop }}</div>
          <div class="kpi-sub">Windows 独立值守客户端</div>
        </div>
      </div>

      <div class="kpi-card">
        <div class="kpi-icon-wrap teal"><el-icon><User /></el-icon></div>
        <div class="kpi-content">
          <div class="kpi-label">Web 管理端</div>
          <div class="kpi-value">{{ stats.web }}</div>
          <div class="kpi-sub">浏览器管理工作台</div>
        </div>
      </div>

      <div class="kpi-card">
        <div class="kpi-icon-wrap amber"><el-icon><Refresh /></el-icon></div>
        <div class="kpi-content">
          <div class="kpi-label">在线版本类型</div>
          <div class="kpi-value">{{ versions.length }}</div>
          <div class="kpi-sub">客户端构建版本分布</div>
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
          placeholder="搜索登录账号..."
          aria-label="搜索在线账号"
          style="width: 240px;"
          @clear="load(true)"
        />

        <el-select v-model="clientTypeFilter" clearable placeholder="全部客户端类型" style="width: 170px;" @change="load(true)">
          <el-option label="桌面客户端 (Desktop)" value="desktop" />
          <el-option label="Web 管理端 (Browser)" value="web" />
        </el-select>

        <el-button type="primary" native-type="submit" :icon="Search">查询</el-button>
        <el-button @click="resetFilter">重置</el-button>

        <div v-if="selectedSessions.length" class="filter-actions">
          <span class="muted" style="font-size: 12.5px;">已选择 {{ selectedSessions.length }} 项：</span>
          <el-button type="danger" size="small" :disabled="busy" @click="batchRevoke">
            批量强制下线
          </el-button>
        </div>
      </form>
    </div>

    <el-alert v-if="error" :title="error" type="error" :closable="false" show-icon style="margin-bottom: 12px;" />

    <!-- 会话数据表格卡片 -->
    <div class="table-card">
      <el-table
        v-loading="loading"
        :data="items"
        empty-text="暂无在线会话"
        @selection-change="selectedSessions = $event"
      >
        <el-table-column type="selection" width="44" />

        <el-table-column prop="username" label="登录账号" min-width="140">
          <template #default="{ row }">
            <strong>{{ row.username }}</strong>
          </template>
        </el-table-column>

        <el-table-column label="终端类型" width="130">
          <template #default="{ row }">
            <el-tag :type="row.clientType === 'desktop' ? 'primary' : 'success'" size="small">
              {{ row.clientType === 'desktop' ? '桌面客户端' : 'Web 管理端' }}
            </el-tag>
          </template>
        </el-table-column>

        <el-table-column prop="clientVersion" label="客户端版本" width="120">
          <template #default="{ row }">
            <span class="hash">{{ row.clientVersion }}</span>
          </template>
        </el-table-column>

        <el-table-column label="登录建立时间" min-width="168">
          <template #default="{ row }">
            <span class="muted">{{ dateTime(row.createdAt) }}</span>
          </template>
        </el-table-column>

        <el-table-column label="最近活跃心跳" min-width="168">
          <template #default="{ row }">
            <span style="font-weight: 500;">{{ dateTime(row.lastSeenAt) }}</span>
          </template>
        </el-table-column>

        <el-table-column label="凭据过期时间" min-width="168">
          <template #default="{ row }">
            <span class="muted">{{ dateTime(row.expiresAt) }}</span>
          </template>
        </el-table-column>

        <el-table-column label="操作" width="100" fixed="right">
          <template #default="{ row }">
            <el-tooltip content="强制下线此终端会话">
              <el-button link type="danger" :icon="Delete" :disabled="busy" @click="revoke(row.id, row.username)">
                下线
              </el-button>
            </el-tooltip>
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
  </div>
</template>
