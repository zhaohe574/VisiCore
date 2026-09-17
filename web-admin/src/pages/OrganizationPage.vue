<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue'
import { useRoute } from 'vue-router'
import {
  CircleCheck,
  CopyDocument,
  Delete,
  Edit,
  Folder,
  Hide,
  InfoFilled,
  Plus,
  Refresh,
  Search,
  VideoCamera,
  VideoPlay,
  View,
  Warning
} from '@element-plus/icons-vue'
import { ElMessage } from 'element-plus'
import {
  allPages,
  managementApi,
  type Channel,
  type Device,
  type Organization,
  type OrganizationKind,
  type OrganizationNode
} from '../api'
import {
  buildUnitCascaderOptions,
  getUnitHierarchy as formatUnitHierarchy,
  getUnitParentPath
} from '../lib/organization'
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
    unitId: unitId.value ?? undefined,
    online: online.value
  }),
  ['device.changed', 'access.changed']
)

const selected = ref<Channel[]>([])
const assignTo = ref<number | null>(null)
const assignmentOpen = ref(false)
const nodeDialog = ref(false)
const editing = ref<number | null>(null)

const detailDrawer = ref(false)
const currentChannel = ref<Channel | null>(null)
const showPassword = ref(false)

const editDialog = ref(false)
const editTarget = ref<Channel | null>(null)
const editForm = reactive({
  alias: '',
  unitId: null as number | null,
  ip: '',
  username: '',
  password: '',
  remark: ''
})

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

// 单元下钻级联选择树
const unitCascaderOptions = computed(() => buildUnitCascaderOptions(organization.value))

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

function showDetail(channel: Channel) {
  currentChannel.value = channel
  showPassword.value = false
  detailDrawer.value = true
}

function openEdit(channel: Channel) {
  editTarget.value = channel
  editForm.alias = channel.alias || ''
  editForm.unitId = channel.unitId ?? null
  editForm.ip = channel.ip || ''
  editForm.username = channel.username || ''
  editForm.password = channel.password || ''
  editForm.remark = channel.remark || ''
  editDialog.value = true
}

async function saveChannel() {
  if (!editTarget.value) return
  if (
    await run(async () => {
      const updated = await managementApi.updateChannel(editTarget.value!.id, {
        alias: editForm.alias.trim() || null,
        ip: editForm.ip.trim() || null,
        username: editForm.username.trim() || null,
        password: editForm.password || null,
        remark: editForm.remark.trim() || null
      })
      if (editForm.unitId !== (editTarget.value!.unitId ?? null)) {
        await managementApi.assign([editTarget.value!.id], editForm.unitId ?? null)
        updated.unitId = editForm.unitId ?? null
      }
      if (currentChannel.value && currentChannel.value.id === editTarget.value!.id) {
        Object.assign(currentChannel.value, updated)
      }
      await load()
    }, '通道配置已成功保存')
  ) {
    editDialog.value = false
  }
}

async function copyText(text?: string | null, label = '内容') {
  if (!text) {
    ElMessage.warning(`暂无${label}`)
    return
  }
  try {
    await navigator.clipboard.writeText(text)
    ElMessage.success(`${label}已复制到剪贴板`)
  } catch {
    ElMessage.error('复制失败，请手动复制')
  }
}

function getUnitHierarchy(unitId?: number | null): string {
  return formatUnitHierarchy(organization.value, unitId)
}

function getUnitParent(unitId?: number | null): string {
  return getUnitParentPath(organization.value, unitId)
}

function getDevice(channel?: Channel | Record<string, any> | null): Device | undefined {
  if (!channel) return undefined
  return devices.value.find(d => d.id === channel.deviceId)
}

function getChannelIp(channel?: Channel | Record<string, any> | null): string {
  if (!channel || !channel.ip) return '—'
  return String(channel.ip)
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

            <el-cascader
              v-model="unitId"
              :options="unitCascaderOptions"
              :props="{ emitPath: false, checkStrictly: false }"
              clearable
              filterable
              placeholder="全部单元（可下钻）"
              style="width: 220px;"
              aria-label="单元筛选"
              @change="load(true)"
            />

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

            <el-table-column prop="alias" label="通道别名" min-width="140" show-overflow-tooltip>
              <template #default="{ row }">
                <span v-if="row.alias" style="font-weight: 600; color: var(--primary);">
                  {{ row.alias }}
                </span>
                <span v-else class="muted">未设置别名</span>
              </template>
            </el-table-column>

            <el-table-column prop="name" label="设备原始名称" min-width="140" show-overflow-tooltip />

            <el-table-column label="IP 地址" min-width="135" show-overflow-tooltip>
              <template #default="{ row }">
                <span class="monospace" style="font-size: 12.5px; font-weight: 500;">
                  {{ getChannelIp(row) }}
                </span>
              </template>
            </el-table-column>

            <el-table-column prop="deviceName" label="归属设备" min-width="130" show-overflow-tooltip />

            <el-table-column prop="deviceChannel" label="通道号" width="75" align="center" />

            <el-table-column label="在线状态" width="95" align="center">
              <template #default="{ row }">
                <StatusBadge :value="row.status" />
              </template>
            </el-table-column>

            <el-table-column label="划拨单元" min-width="170">
              <template #default="{ row }">
                <div v-if="row.unitId" class="unit-cell" :title="getUnitHierarchy(row.unitId)">
                  <el-tag size="small" type="success" effect="plain" style="font-weight: 500;">
                    {{ organization.units.find(u => u.id === row.unitId)?.name || `单元 ${row.unitId}` }}
                  </el-tag>
                  <span v-if="getUnitParent(row.unitId)" class="unit-parent-label">
                    {{ getUnitParent(row.unitId) }}
                  </span>
                </div>
                <el-tag v-else size="small" type="info">未分配</el-tag>
              </template>
            </el-table-column>

            <el-table-column prop="codec" label="编码" width="75" align="center" />

            <el-table-column label="操作" width="240" fixed="right">
              <template #default="{ row }">
                <div class="table-tools">
                  <router-link :to="{ path: '/app/live', query: { channelId: row.id } }">
                    <el-button link type="primary" :icon="VideoPlay">预览</el-button>
                  </router-link>
                  <el-button link type="primary" :icon="InfoFilled" @click="showDetail(row as Channel)">
                    详细信息
                  </el-button>
                  <el-button v-if="auth.can('channel.assign')" link type="primary" :icon="Edit" @click="openEdit(row as Channel)">
                    编辑
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

    <!-- 通道编辑与凭据弹窗 -->
    <el-dialog v-model="editDialog" title="编辑通道信息与备注" width="520px" destroy-on-close>
      <el-form label-position="top">
        <div style="display: grid; grid-template-columns: 1fr 1fr; gap: 12px;">
          <el-form-item label="通道原名">
            <el-input :model-value="editTarget?.name" disabled />
          </el-form-item>
          <el-form-item label="归属设备">
            <el-input :model-value="editTarget?.deviceName" disabled />
          </el-form-item>
        </div>

        <el-form-item label="通道业务别名">
          <el-input
            v-model="editForm.alias"
            maxlength="100"
            clearable
            placeholder="如：东门入口高清（留空恢复原名）"
          />
        </el-form-item>

        <el-form-item label="划拨业务单元">
          <el-cascader
            v-model="editForm.unitId"
            :options="unitCascaderOptions"
            :props="{ emitPath: false, checkStrictly: false }"
            clearable
            filterable
            placeholder="未划拨（支持车间下钻，留空解除划拨）"
            style="width: 100%;"
          />
          <div v-if="editForm.unitId" style="margin-top: 4px; font-size: 12px;" class="muted">
            已选层级：{{ getUnitHierarchy(editForm.unitId) }}
          </div>
        </el-form-item>

        <div style="display: grid; grid-template-columns: 1fr 1fr; gap: 12px;">
          <el-form-item label="通道独立 IP">
            <el-input
              v-model="editForm.ip"
              maxlength="253"
              clearable
              placeholder="未单独配置则保持滞空"
            />
          </el-form-item>

          <el-form-item label="访问账号">
            <el-input
              v-model="editForm.username"
              maxlength="128"
              clearable
              placeholder="未单独配置则保持滞空"
            />
          </el-form-item>
        </div>

        <el-form-item label="访问密码">
          <el-input
            v-model="editForm.password"
            type="password"
            show-password
            maxlength="256"
            clearable
            placeholder="未单独配置则保持滞空"
          />
        </el-form-item>

        <el-form-item label="备注说明">
          <el-input
            v-model="editForm.remark"
            type="textarea"
            :rows="3"
            maxlength="500"
            show-word-limit
            placeholder="可填写点位物理位置、用途说明、维护信息等..."
          />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="editDialog = false">取消</el-button>
        <el-button type="primary" :loading="busy" @click="saveChannel">保存通道信息</el-button>
      </template>
    </el-dialog>

    <!-- 批量分配单元弹窗 -->
    <el-dialog v-model="assignmentOpen" title="批量划拨通道至单元" width="480px">
      <el-form label-position="top">
        <el-form-item label="目标单元（支持车间 / 区域层级下钻与名称搜索）">
          <el-cascader
            v-model="assignTo"
            :options="unitCascaderOptions"
            :props="{ emitPath: false, checkStrictly: false }"
            clearable
            filterable
            placeholder="请选择划拨目标单元（留空解除划拨）"
            style="width: 100%;"
          />
        </el-form-item>
        <div v-if="assignTo" style="margin-bottom: 12px; font-size: 13px;">
          <span class="muted">已选完整归属：</span>
          <el-tag type="success" size="small">{{ getUnitHierarchy(assignTo) }}</el-tag>
        </div>
        <div v-else style="margin-bottom: 12px; font-size: 13px;">
          <el-tag type="warning" size="small">未选择单元：若点击确认，将解除所选通道的单元关联</el-tag>
        </div>
      </el-form>
      <p class="muted" style="font-size: 13px; margin: 0;">
        本次操作将批量更新所勾选的 <strong>{{ selected.length }}</strong> 路通道的归属单元。
      </p>
      <template #footer>
        <el-button @click="assignmentOpen = false">取消</el-button>
        <el-button type="primary" :loading="busy" @click="assign">确认划拨</el-button>
      </template>
    </el-dialog>

    <!-- 通道详细信息抽屉 -->
    <el-drawer
      v-model="detailDrawer"
      title="通道详细信息"
      size="560px"
      destroy-on-close
    >
      <div v-if="currentChannel">
        <div style="display: flex; align-items: flex-start; justify-content: space-between; margin-bottom: 20px; padding-bottom: 16px; border-bottom: 1px solid var(--border);">
          <div>
            <h3 style="margin: 0 0 6px; font-size: 16px; color: var(--text-primary);">
              {{ currentChannel.alias || currentChannel.name }}
            </h3>
            <div style="display: flex; align-items: center; gap: 8px; font-size: 12px;" class="muted">
              <span>原始名称: {{ currentChannel.name }}</span>
              <span>·</span>
              <span>通道号: #{{ currentChannel.deviceChannel }}</span>
            </div>
          </div>
          <StatusBadge :value="currentChannel.status" />
        </div>

        <!-- 摄像头探知规格 -->
        <div style="margin-bottom: 24px;">
          <h4 style="margin: 0 0 12px; font-size: 14px; font-weight: 600; color: var(--text-primary); display: flex; align-items: center; gap: 6px;">
            <el-icon><VideoCamera /></el-icon> 摄像头探知规格
          </h4>
          <dl class="data-list">
            <div>
              <dt>摄像头型号</dt>
              <dd>
                <el-tag v-if="currentChannel.model" size="small" type="primary">
                  {{ currentChannel.model }}
                </el-tag>
                <span v-else class="muted">—</span>
              </dd>
            </div>
            <div>
              <dt>摄像头原名</dt>
              <dd>{{ currentChannel.name || '—' }}</dd>
            </div>
            <div>
              <dt>业务别名</dt>
              <dd>{{ currentChannel.alias || '—' }}</dd>
            </div>
            <div>
              <dt>探测通道号</dt>
              <dd class="monospace">#{{ currentChannel.deviceChannel }}</dd>
            </div>
            <div>
              <dt>视频编码</dt>
              <dd class="monospace">{{ currentChannel.codec || 'H.264' }}</dd>
            </div>
            <div>
              <dt>形态与云台能力</dt>
              <dd>
                <el-tag size="small" :type="currentChannel.ptzCapable ? 'success' : 'info'">
                  {{ currentChannel.ptzCapable ? '球机 / 云台摄像机 (PTZ)' : '定焦 / 枪机 / 半球' }}
                </el-tag>
              </dd>
            </div>
            <div>
              <dt>所属业务组织</dt>
              <dd>{{ getUnitHierarchy(currentChannel.unitId) }}</dd>
            </div>
          </dl>
        </div>

        <!-- 归属录像机与主机 -->
        <div style="margin-bottom: 24px;">
          <h4 style="margin: 0 0 12px; font-size: 14px; font-weight: 600; color: var(--text-primary); display: flex; align-items: center; gap: 6px;">
            <el-icon><Folder /></el-icon> 归属录像机 / 存储设备
          </h4>
          <dl class="data-list">
            <div>
              <dt>录像机名称</dt>
              <dd>{{ currentChannel.deviceName || getDevice(currentChannel)?.name || '—' }} (ID: {{ currentChannel.deviceId }})</dd>
            </div>
            <div>
              <dt>录像机型号</dt>
              <dd>
                <el-tag v-if="currentChannel.deviceModel || getDevice(currentChannel)?.model" size="small" type="info">
                  {{ currentChannel.deviceModel || getDevice(currentChannel)?.model }}
                </el-tag>
                <span v-else class="muted">—</span>
              </dd>
            </div>
            <div>
              <dt>设备服务端口</dt>
              <dd class="monospace">{{ currentChannel.devicePort ?? getDevice(currentChannel)?.port ?? '—' }}</dd>
            </div>
            <div>
              <dt>设备序列号</dt>
              <dd class="monospace">{{ currentChannel.deviceSerial || getDevice(currentChannel)?.serialNumber || '—' }}</dd>
            </div>
            <div>
              <dt>协议驱动插件</dt>
              <dd>{{ currentChannel.pluginName || getDevice(currentChannel)?.pluginName || '内置海康驱动' }}</dd>
            </div>
          </dl>
        </div>

        <!-- 网络与访问凭据 -->
        <div style="margin-bottom: 24px;">
          <h4 style="margin: 0 0 12px; font-size: 14px; font-weight: 600; color: var(--text-primary); display: flex; align-items: center; gap: 6px;">
            <el-icon><InfoFilled /></el-icon> 网络与访问凭据 (独立备注)
          </h4>
          <dl class="data-list">
            <div>
              <dt>通道独立 IP</dt>
              <dd style="display: flex; align-items: center; gap: 8px; justify-content: flex-end;">
                <template v-if="currentChannel.ip">
                  <span class="monospace" style="font-weight: 600;">
                    {{ currentChannel.ip }}
                  </span>
                  <el-button
                    link
                    type="primary"
                    :icon="CopyDocument"
                    size="small"
                    @click="copyText(currentChannel.ip, 'IP地址')"
                  >
                    复制
                  </el-button>
                </template>
                <span v-else class="muted">—</span>
              </dd>
            </div>
            <div>
              <dt>独立访问账号</dt>
              <dd style="display: flex; align-items: center; gap: 8px; justify-content: flex-end;">
                <template v-if="currentChannel.username">
                  <span class="monospace">{{ currentChannel.username }}</span>
                  <el-button
                    link
                    type="primary"
                    :icon="CopyDocument"
                    size="small"
                    @click="copyText(currentChannel.username, '账号')"
                  >
                    复制
                  </el-button>
                </template>
                <span v-else class="muted">—</span>
              </dd>
            </div>
            <div>
              <dt>独立访问密码</dt>
              <dd style="display: flex; align-items: center; gap: 8px; justify-content: flex-end;">
                <template v-if="currentChannel.password">
                  <span class="monospace">
                    {{ showPassword ? currentChannel.password : '••••••••••••' }}
                  </span>
                  <el-button
                    link
                    type="primary"
                    :icon="showPassword ? Hide : View"
                    size="small"
                    @click="showPassword = !showPassword"
                  >
                    {{ showPassword ? '隐藏' : '显示' }}
                  </el-button>
                  <el-button
                    link
                    type="primary"
                    :icon="CopyDocument"
                    size="small"
                    @click="copyText(currentChannel.password, '密码')"
                  >
                    复制
                  </el-button>
                </template>
                <span v-else class="muted">—</span>
              </dd>
            </div>
            <div>
              <dt>现场备注说明</dt>
              <dd style="white-space: pre-wrap; word-break: break-all; max-width: 320px;">
                {{ currentChannel.remark || '—' }}
              </dd>
            </div>
          </dl>
        </div>
      </div>

      <template #footer>
        <div style="display: flex; justify-content: space-between; align-items: center;">
          <router-link v-if="currentChannel" :to="{ path: '/app/live', query: { channelId: currentChannel.id } }">
            <el-button type="success" plain :icon="VideoPlay">打开实时监控</el-button>
          </router-link>
          <div style="display: flex; gap: 8px;">
            <el-button v-if="auth.can('channel.assign') && currentChannel" type="primary" :icon="Edit" @click="openEdit(currentChannel)">
              编辑备注与凭据
            </el-button>
            <el-button @click="detailDrawer = false">关闭</el-button>
          </div>
        </div>
      </template>
    </el-drawer>
  </div>
</template>

<style scoped>
.unit-cell {
  display: inline-flex;
  flex-direction: column;
  align-items: flex-start;
  gap: 2px;
  line-height: 1.2;
}

.unit-parent-label {
  font-size: 11.5px;
  color: var(--text-secondary, #6b7280);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
  max-width: 170px;
}
</style>
