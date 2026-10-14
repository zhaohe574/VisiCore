<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import {
  Box,
  Location,
  OfficeBuilding,
  Plus,
  Refresh
} from '@element-plus/icons-vue'
import {
  allPages,
  computerApi,
  managementApi,
  networkDeviceApi,
  ponApi,
  type Channel,
  type Computer,
  type NetworkDevice,
  type Organization,
  type OrganizationKind,
  type OrganizationNode,
  type TerminalBoxDto
} from '../api'
import {
  buildOrganizationTree,
  type OrgTreeNode
} from '../lib/organization'
import { useActivePage } from '../composables/useActivePage'
import { useAction } from '../composables/useAction'
import { useAuth } from '../stores/auth'
import PageHeader from '../components/PageHeader.vue'
import OrgTreePane from '../components/organization/panes/OrgTreePane.vue'
import OrganizationNodeDialog from '../components/organization/OrganizationNodeDialog.vue'
import ChannelSortDialog from '../components/organization/ChannelSortDialog.vue'

const auth = useAuth()
const { confirm } = useAction()

const loading = ref(false)
const organization = ref<Organization>({ workshops: [], areas: [], units: [] })
const allChannels = ref<Channel[]>([])
const allComputers = ref<Computer[]>([])
const allBoxes = ref<TerminalBoxDto[]>([])
const allNetworkDevices = ref<NetworkDevice[]>([])

// 弹窗状态
const nodeDialog = ref(false)
const nodeDialogKind = ref<OrganizationKind>('workshops')
const editingNode = ref<OrganizationNode | null>(null)
const nodeDialogInitialParentId = ref<number | null>(null)

const sortDialog = ref(false)
const sortUnitId = ref<number | null>(null)
const sortUnitName = ref('')

// 组织架构统计数据与拓扑树（自底向上汇总四类核心资产）
const treeData = computed<OrgTreeNode[]>(() =>
  buildOrganizationTree(organization.value, {
    channels: allChannels.value,
    computers: allComputers.value,
    terminalBoxes: allBoxes.value,
    networkDevices: allNetworkDevices.value
  })
)

const totalStats = computed(() => ({
  channels: allChannels.value.length,
  computers: allComputers.value.length,
  terminalBoxes: allBoxes.value.length,
  networkDevices: allNetworkDevices.value.length
}))

// 节点增删改操作
function createWorkshop() {
  nodeDialogKind.value = 'workshops'
  editingNode.value = null
  nodeDialogInitialParentId.value = null
  nodeDialog.value = true
}

function createArea(parentWorkshop?: OrganizationNode) {
  nodeDialogKind.value = 'areas'
  editingNode.value = null
  nodeDialogInitialParentId.value = parentWorkshop?.id || null
  nodeDialog.value = true
}

function createUnit(parentArea?: OrganizationNode) {
  nodeDialogKind.value = 'units'
  editingNode.value = null
  nodeDialogInitialParentId.value = parentArea?.id || null
  nodeDialog.value = true
}

function editNode(kind: OrganizationKind, node: OrganizationNode) {
  nodeDialogKind.value = kind
  editingNode.value = node
  nodeDialogInitialParentId.value = node.parentId || null
  nodeDialog.value = true
}

async function removeNode(kind: OrganizationKind, node: OrganizationNode) {
  const names: Record<OrganizationKind, string> = { workshops: '车间', areas: '区域', units: '工位单元' }
  await confirm(
    `确定删除${names[kind]}「${node.name}」？此操作不可逆。`,
    async () => {
      await managementApi.deleteNode(kind, node.id)
      await loadAllData()
    },
    `${names[kind]}已删除`
  )
}

function openSortDialog(targetUnit: OrganizationNode) {
  sortUnitId.value = targetUnit.id
  sortUnitName.value = targetUnit.name
  sortDialog.value = true
}

async function loadOrganization() {
  const value = await managementApi.organization()
  organization.value = value
}

async function loadChannels() {
  try {
    const value = await allPages(managementApi.channels)
    allChannels.value = value
  } catch {}
}

async function loadComputers() {
  try {
    const value = await allPages(computerApi.list)
    allComputers.value = value
  } catch {}
}

async function loadBoxes() {
  try {
    const value = await ponApi.terminalBoxes()
    allBoxes.value = value
  } catch {}
}

async function loadNetworkDevices() {
  try {
    const value = await allPages(networkDeviceApi.list)
    allNetworkDevices.value = value
  } catch {}
}

async function loadAllData() {
  const ticket = lifecycle.ticket('all')
  if (ticket === undefined) return
  loading.value = true
  try {
    await Promise.all([
      loadOrganization(),
      loadChannels(),
      loadComputers(),
      loadBoxes(),
      loadNetworkDevices()
    ])
  } finally {
    if (lifecycle.current(ticket)) loading.value = false
  }
}

const lifecycle = useActivePage(loadAllData, {
  kinds: [
    'organization',
    'channel',
    'computer',
    'terminal-box',
    'network-device',
    'access.changed'
  ]
})

async function onNodeSaved() {
  await loadAllData()
}

onMounted(async () => {
  await loadAllData()
})
</script>

<template>
  <div class="organization-page">
    <PageHeader
      title="组织架构拓扑"
      subtitle="维护全厂车间、区域与工位单元三级空间骨架，把控各空间节点属性、层级关系及下辖资产归属"
    >
      <template #extra>
        <el-dropdown
          v-if="auth.can('area.manage')"
          @command="(cmd: any) => cmd === 'ws' ? createWorkshop() : cmd === 'ar' ? createArea() : createUnit()"
        >
          <el-button type="primary" :icon="Plus">
            新建空间节点
          </el-button>
          <template #dropdown>
            <el-dropdown-menu>
              <el-dropdown-item command="ws" :icon="OfficeBuilding">新建车间 (顶级)</el-dropdown-item>
              <el-dropdown-item command="ar" :icon="Location">新建区域 (二级)</el-dropdown-item>
              <el-dropdown-item command="un" :icon="Box">新建工位单元 (末级)</el-dropdown-item>
            </el-dropdown-menu>
          </template>
        </el-dropdown>
        <el-button :icon="Refresh" :loading="loading" @click="loadAllData">
          刷新数据
        </el-button>
      </template>
    </PageHeader>

    <div class="org-layout">
      <OrgTreePane
        :organization="organization"
        :tree-data="treeData"
        :total-stats="totalStats"
        :can-manage-area="auth.can('area.manage')"
        @create-workshop="createWorkshop"
        @create-area="createArea"
        @create-unit="createUnit"
        @edit-node="editNode"
        @remove-node="removeNode"
        @sort-unit="openSortDialog"
      />
    </div>

    <!-- 节点增改与通道排序弹窗 -->
    <OrganizationNodeDialog
      v-model="nodeDialog"
      :kind="nodeDialogKind"
      :editing-node="editingNode"
      :organization="organization"
      :initial-parent-id="nodeDialogInitialParentId"
      @saved="onNodeSaved"
    />

    <ChannelSortDialog
      v-model="sortDialog"
      :unit-id="sortUnitId"
      :unit-name="sortUnitName"
      @saved="loadChannels"
    />
  </div>
</template>

<style scoped>
.organization-page {
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.org-layout {
  width: 100%;
}
</style>
