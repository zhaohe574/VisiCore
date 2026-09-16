<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue'
import { useRoute } from 'vue-router'
import {
  CircleCheck,
  Delete,
  Edit,
  Folder,
  Plus,
  Refresh,
  Search,
  VideoCamera,
  VideoPlay,
  Warning
} from '@element-plus/icons-vue'
import {
  allPages,
  managementApi,
  type Channel,
  type Device,
  type Organization,
  type OrganizationKind,
  type OrganizationNode
} from '../api'
import { usePaged } from '../composables/usePaged'
import { useAction } from '../composables/useAction'
import { useAuth } from '../stores/auth'
import PageHeader from '../components/PageHeader.vue'
import StatusBadge from '../components/StatusBadge.vue'

const route = useRoute()
const auth = useAuth()
const { busy, run, confirm } = useAction()

const organization = ref<Organization>({ workshops: [], areas: [], units: [] })
const devices = ref<Device[]>([])

const deviceId = ref<number | undefined>(Number(route.query.deviceId) || undefined)
const unitId = ref<number>()
const online = ref<string>('')

const { items, total, page, pageSize, search, loading, error, load } = usePaged(
  managementApi.channels,
  () => ({
    deviceId: deviceId.value,
    unitId: unitId.value,
    online: online.value
  }),
  ['device.changed', 'access.changed']
)

const selected = ref<Channel[]>([])
const assignTo = ref<number | null>(null)
const assignmentOpen = ref(false)
const nodeDialog = ref(false)
const editing = ref<number | null>(null)

const aliasDialog = ref(false)
const aliasTarget = ref<Channel | null>(null)
const channelAlias = ref('')

const kind = ref<OrganizationKind>('workshops')
const orgTab = ref<OrganizationKind>('workshops')
const names: Record<OrganizationKind, string> = { workshops: '车间', areas: '区域', units: '单元' }

const form = reactive({
  name: '',
  code: '',
  status: 'active',
  parentId: null as number | null
})

const parents = computed(() =>
  kind.value === 'areas'
    ? organization.value.workshops
    : kind.value === 'units'
      ? organization.value.areas
      : []
)

// 通道统计概览
const stats = computed(() => {
  const all = items.value as Channel[]
  const onlineCount = all.filter(c => c.status === 'online').length
  const offlineCount = all.filter(c => c.status !== 'online').length
  const unassignedCount = all.filter(c => c.unitId === null || c.unitId === undefined).length
  return { onlineCount, offlineCount, unassignedCount }
})

async function loadOrganization() {
  organization.value = await managementApi.organization()
}

async function loadOptions() {
  await run(async () => {
    await loadOrganization()
    if (auth.can('device.read')) devices.value = await allPages(managementApi.devices)
  }, '')
}

function editNode(node?: OrganizationNode) {
  kind.value = orgTab.value
  editing.value = node?.id || null
  Object.assign(form, {
    name: node?.name || '',
    code: node?.code || '',
    status: node?.status || 'active',
    parentId: node?.parentId ?? null
  })
  nodeDialog.value = true
}

async function saveNode() {
  if (
    await run(async () => {
      if (!form.name.trim() || !form.code.trim()) throw new Error('请填写组织名称和编码')
      if (kind.value !== 'workshops' && !form.parentId) throw new Error('请选择上级组织节点')
      await managementApi.saveNode(kind.value, editing.value, {
        ...form,
        name: form.name.trim(),
        code: form.code.trim(),
        parentId: kind.value === 'workshops' ? null : form.parentId
      })
      await loadOrganization()
    }, '组织节点已保存')
  ) {
    nodeDialog.value = false
  }
}

async function removeNode(node: OrganizationNode) {
  await confirm(
    `确认删除${names[orgTab.value]}“${node.name}”？\n若包含下级组织或通道关联，可能会受到限制。`,
    async () => {
      await managementApi.deleteNode(orgTab.value, node.id)
      await loadOrganization()
      await load()
    },
    '组织已删除'
  )
}

async function assign() {
  if (
    await run(async () => {
      await managementApi.assign(selected.value.map(c => c.id), assignTo.value ?? null)
      await load()
    }, '通道分配已成功更新')
  ) {
    assignmentOpen.value = false
    selected.value = []
  }
}

function editAlias(channel: Channel) {
  aliasTarget.value = channel
  channelAlias.value = channel.alias || ''
  aliasDialog.value = true
}

async function saveAlias() {
  if (!aliasTarget.value) return
  if (
    await run(async () => {
      await managementApi.updateChannel(aliasTarget.value!.id, {
        alias: channelAlias.value.trim() || null
      })
      await load()
    }, '通道别名已保存')
  ) {
    aliasDialog.value = false
  }
}

function parentName(node: OrganizationNode) {
  return [...organization.value.workshops, ...organization.value.areas].find(
    parent => parent.id === node.parentId
  )?.name || '—'
}

function resetFilters() {
  search.value = ''
  deviceId.value = undefined
  unitId.value = undefined
  online.value = ''
  void load(true)
}

onMounted(loadOptions)
</script>

<template>
  <div>
    <PageHeader title="组织与通道" :count="total" description="维护车间、区域、单元三级业务组织结构，进行通道别名命名与批量单元划拨">
      <el-button :icon="Refresh" :loading="loading" @click="loadOptions(); load()">刷新</el-button>
    </PageHeader>

    <!-- 顶部 KPI 概览 -->
    <div class="kpi-grid">
      <div class="kpi-card">
        <div class="kpi-icon-wrap blue"><el-icon><VideoCamera /></el-icon></div>
        <div class="kpi-content">
          <div class="kpi-label">监控通道总数</div>
          <div class="kpi-value">{{ total }}</div>
          <div class="kpi-sub">全部同步接入通道</div>
        </div>
      </div>

      <div class="kpi-card">
        <div class="kpi-icon-wrap teal"><el-icon><CircleCheck /></el-icon></div>
        <div class="kpi-content">
          <div class="kpi-label">在线通道</div>
          <div class="kpi-value">{{ stats.onlineCount }}</div>
          <div class="kpi-sub">当前页在线视频流</div>
        </div>
      </div>

      <div class="kpi-card">
        <div class="kpi-icon-wrap red"><el-icon><Warning /></el-icon></div>
        <div class="kpi-content">
          <div class="kpi-label">离线通道</div>
          <div class="kpi-value">{{ stats.offlineCount }}</div>
          <div class="kpi-sub">信号丢失或设备断网</div>
        </div>
      </div>

      <div class="kpi-card">
        <div class="kpi-icon-wrap amber"><el-icon><Folder /></el-icon></div>
        <div class="kpi-content">
          <div class="kpi-label">待分配通道</div>
          <div class="kpi-value">{{ stats.unassignedCount }}</div>
          <div class="kpi-sub">尚未划拨至具体车间单元</div>
        </div>
      </div>
    </div>

    <!-- 左右分栏工作区 -->
    <div class="organization-workspace" style="display: grid; grid-template-columns: 320px minmax(0, 1fr); gap: 16px;">
      <!-- 左栏：三级业务组织管理卡片 -->
      <aside class="filter-card" style="margin-bottom: 0; padding: 18px;">
        <div style="display: flex; align-items: center; justify-content: space-between; margin-bottom: 12px;">
          <h2 style="margin: 0; font-size: 15px;">业务组织架构</h2>
          <el-button v-if="auth.can('area.manage')" type="primary" size="small" :icon="Plus" @click="editNode()">
            添加{{ names[orgTab] }}
          </el-button>
        </div>

        <el-tabs v-model="orgTab">
          <el-tab-pane v-for="(name, key) in names" :key="key" :name="key" :label="name" />
        </el-tabs>

        <div v-if="!organization[orgTab].length" class="muted" style="text-align: center; padding: 32px 0;">
          暂无{{ names[orgTab] }}节点
        </div>

        <div style="max-height: 520px; overflow-y: auto; display: flex; flex-direction: column; gap: 8px;">
          <div
            v-for="node in organization[orgTab]"
            :key="node.id"
            class="channel-entry"
            style="padding: 10px 12px; background: #f8fafc; border: 1px solid var(--border); border-radius: var(--radius-md);"
          >
            <div style="flex: 1; min-width: 0;">
              <strong style="font-size: 13px; color: var(--text-primary); display: block; overflow: hidden; text-overflow: ellipsis; white-space: nowrap;">
                {{ node.name }}
              </strong>
              <small class="muted" style="font-size: 11px;">
                {{ node.code }}
                <template v-if="node.parentId"> · 上级: {{ parentName(node) }}</template>
              </small>
            </div>

            <div v-if="auth.can('area.manage')" class="table-tools">
              <el-tooltip content="编辑组织">
                <el-button link type="primary" :icon="Edit" aria-label="编辑组织" @click="editNode(node)" />
              </el-tooltip>
              <el-tooltip content="删除组织">
                <el-button link type="danger" :icon="Delete" :disabled="busy" aria-label="删除组织" @click="removeNode(node)" />
              </el-tooltip>
            </div>
          </div>
        </div>
      </aside>

      <!-- 右栏：通道数据表格与划拨 -->
      <section style="min-width: 0;">
        <!-- 筛选卡片 -->
        <div class="filter-card">
          <form class="filter-bar" @submit.prevent="load(true)">
            <el-input
              v-model="search"
              clearable
              :prefix-icon="Search"
              placeholder="搜索通道名称或别名..."
              aria-label="搜索通道"
              style="width: 220px;"
              @clear="load(true)"
            />

            <el-select
              v-if="devices.length"
              v-model="deviceId"
              clearable
              placeholder="全部设备"
              style="width: 170px;"
              aria-label="设备筛选"
              @change="load(true)"
            >
              <el-option v-for="device in devices" :key="device.id" :value="device.id" :label="device.name" />
            </el-select>

            <el-select
              v-model="unitId"
              clearable
              placeholder="全部单元"
              style="width: 160px;"
              aria-label="单元筛选"
              @change="load(true)"
            >
              <el-option v-for="unit in organization.units" :key="unit.id" :value="unit.id" :label="unit.name" />
            </el-select>

            <el-select
              v-model="online"
              clearable
              placeholder="全部在线状态"
              style="width: 140px;"
              aria-label="在线状态筛选"
              @change="load(true)"
            >
              <el-option label="在线" value="true" />
              <el-option label="离线" value="false" />
            </el-select>

            <el-button type="primary" native-type="submit" :icon="Search">查询</el-button>
            <el-button @click="resetFilters">重置</el-button>

            <div v-if="auth.can('channel.assign') && selected.length" class="filter-actions">
              <span class="muted" style="font-size: 12.5px;">已选 {{ selected.length }} 路通道：</span>
              <el-button type="primary" size="small" @click="assignTo = null; assignmentOpen = true">
                批量分配到单元
              </el-button>
            </div>
          </form>
        </div>

        <el-alert v-if="error" :title="error" type="error" :closable="false" show-icon style="margin-bottom: 12px;" />

        <!-- 通道表格卡片 -->
        <div class="table-card">
          <el-table
            v-loading="loading"
            :data="items"
            row-key="id"
            empty-text="未找到匹配的通道记录"
            @selection-change="selected = $event"
          >
            <el-table-column v-if="auth.can('channel.assign')" type="selection" width="44" />

            <el-table-column prop="alias" label="通道别名" min-width="150" show-overflow-tooltip>
              <template #default="{ row }">
                <span v-if="row.alias" style="font-weight: 600; color: var(--primary);">
                  {{ row.alias }}
                </span>
                <span v-else class="muted">未设置别名</span>
              </template>
            </el-table-column>

            <el-table-column prop="name" label="设备原始名称" min-width="150" show-overflow-tooltip />

            <el-table-column prop="deviceName" label="归属设备" min-width="140" show-overflow-tooltip />

            <el-table-column prop="deviceChannel" label="通道号" width="75" />

            <el-table-column label="在线状态" width="95">
              <template #default="{ row }">
                <StatusBadge :value="row.status" />
              </template>
            </el-table-column>

            <el-table-column label="划拨单元" min-width="130">
              <template #default="{ row }">
                <el-tag v-if="row.unitId" size="small" type="success">
                  {{ organization.units.find(u => u.id === row.unitId)?.name || `单元 ${row.unitId}` }}
                </el-tag>
                <el-tag v-else size="small" type="info">未分配</el-tag>
              </template>
            </el-table-column>

            <el-table-column prop="codec" label="编码" width="75" />

            <el-table-column label="操作" width="130" fixed="right">
              <template #default="{ row }">
                <div class="table-tools">
                  <router-link :to="{ path: '/app/live', query: { channelId: row.id } }">
                    <el-button link type="primary" :icon="VideoPlay">预览</el-button>
                  </router-link>
                  <el-button v-if="auth.can('channel.assign')" link type="primary" :icon="Edit" @click="editAlias(row as Channel)">
                    别名
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
      </section>
    </div>

    <!-- 组织节点创建/编辑弹窗 -->
    <el-dialog
      v-model="nodeDialog"
      :title="`${editing ? '编辑' : '新增'}${names[kind]}`"
      width="480px"
      destroy-on-close
    >
      <el-form label-position="top">
        <el-form-item label="名称" required>
          <el-input v-model="form.name" maxlength="100" placeholder="例如：总装一号车间" clearable />
        </el-form-item>

        <el-form-item label="编码标识" required>
          <el-input v-model="form.code" maxlength="64" placeholder="例如：WS-01" clearable />
        </el-form-item>

        <el-form-item v-if="kind !== 'workshops'" label="上级组织节点" required>
          <el-select v-model="form.parentId" filterable placeholder="选择上级组织" style="width: 100%;">
            <el-option v-for="parent in parents" :key="parent.id" :value="parent.id" :label="parent.name" />
          </el-select>
        </el-form-item>

        <el-form-item label="组织状态">
          <el-switch v-model="form.status" active-value="active" inactive-value="disabled" active-text="启用" />
        </el-form-item>
      </el-form>

      <template #footer>
        <el-button @click="nodeDialog = false">取消</el-button>
        <el-button type="primary" :loading="busy" @click="saveNode">确认保存</el-button>
      </template>
    </el-dialog>

    <!-- 通道别名编辑弹窗 -->
    <el-dialog v-model="aliasDialog" title="设置通道业务别名" width="440px" destroy-on-close>
      <el-form label-position="top">
        <el-form-item label="通道原名">
          <el-input :model-value="aliasTarget?.name" disabled />
        </el-form-item>
        <el-form-item label="所属设备">
          <el-input :model-value="aliasTarget?.deviceName" disabled />
        </el-form-item>
        <el-form-item label="业务别名">
          <el-input
            v-model="channelAlias"
            maxlength="100"
            clearable
            placeholder="留空则恢复默认原名（如：东门入口高清）"
          />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="aliasDialog = false">取消</el-button>
        <el-button type="primary" :loading="busy" @click="saveAlias">保存别名</el-button>
      </template>
    </el-dialog>

    <!-- 批量分配单元弹窗 -->
    <el-dialog v-model="assignmentOpen" title="批量划拨通道至单元" width="460px">
      <el-form label-position="top">
        <el-form-item label="目标单元">
          <el-select v-model="assignTo" clearable filterable placeholder="不选择单元则解除当前分配" style="width: 100%;">
            <el-option v-for="unit in organization.units" :key="unit.id" :value="unit.id" :label="unit.name" />
          </el-select>
        </el-form-item>
      </el-form>
      <p class="muted" style="font-size: 13px; margin: 0;">
        本次操作将批量更新所勾选的 <strong>{{ selected.length }}</strong> 路通道的归属单元。
      </p>
      <template #footer>
        <el-button @click="assignmentOpen = false">取消</el-button>
        <el-button type="primary" :loading="busy" @click="assign">确认划拨</el-button>
      </template>
    </el-dialog>
  </div>
</template>
