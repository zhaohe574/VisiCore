<script setup lang="ts">
import { nextTick, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import {
  Box,
  Connection,
  Delete,
  Edit,
  Expand,
  Fold,
  Location,
  Monitor,
  OfficeBuilding,
  Plus,
  Search,
  Sort,
  VideoCamera
} from '@element-plus/icons-vue'
import type { ElTree } from 'element-plus'
import type { Organization, OrganizationKind, OrganizationNode } from '../../../api'
import type { OrgTreeNode } from '../../../lib/organization'

const props = defineProps<{
  organization: Organization
  treeData: OrgTreeNode[]
  totalStats: {
    channels: number
    computers: number
    terminalBoxes: number
    networkDevices: number
  }
  canManageArea: boolean
}>()

const emit = defineEmits<{
  (e: 'createWorkshop'): void
  (e: 'createArea', parentWorkshop?: OrganizationNode): void
  (e: 'createUnit', parentArea?: OrganizationNode): void
  (e: 'editNode', kind: OrganizationKind, node: OrganizationNode): void
  (e: 'removeNode', kind: OrganizationKind, node: OrganizationNode): void
  (e: 'sortUnit', unit: OrganizationNode): void
}>()

const router = useRouter()
const treeRef = ref<InstanceType<typeof ElTree>>()
const treeSearch = ref('')
const treeExpanded = ref(true)
const showZeroBadges = ref(false)

// 维护被用户展开的节点 key 集合（保持持久记忆，防止数据更新时被冲刷自动打开）
const expandedKeySet = ref<Set<string>>(new Set())
const isInitialExpanded = ref(false)

function collectKeysWithChildren(nodes: OrgTreeNode[], keys: Set<string>) {
  for (const n of nodes) {
    if (n.children && n.children.length > 0) {
      keys.add(n.key)
      collectKeysWithChildren(n.children, keys)
    }
  }
}

function applyExpandedState() {
  const nodes = (treeRef.value as any)?.store?.nodesMap
  if (!nodes) return
  for (const key in nodes) {
    nodes[key].expanded = expandedKeySet.value.has(key)
  }
}

function syncTreeExpandedState() {
  treeExpanded.value = expandedKeySet.value.size > 0
}

watch(
  () => props.treeData,
  (newData) => {
    if (!newData || newData.length === 0) return

    if (!isInitialExpanded.value) {
      const initialKeys = new Set<string>()
      collectKeysWithChildren(newData, initialKeys)
      expandedKeySet.value = initialKeys
      isInitialExpanded.value = true
      treeExpanded.value = true
    }

    nextTick(() => {
      applyExpandedState()
    })
  },
  { immediate: true }
)

watch(treeSearch, (val) => {
  treeRef.value?.filter(val)
  if (!val) {
    nextTick(() => {
      applyExpandedState()
    })
  }
})

function filterTreeNode(value: string, data: any): boolean {
  if (!value) return true
  const query = value.trim().toLowerCase()
  return (
    data.name.toLowerCase().includes(query) ||
    (data.code ? data.code.toLowerCase().includes(query) : false)
  )
}

function onNodeExpand(data: OrgTreeNode) {
  if (data?.key) {
    expandedKeySet.value.add(data.key)
    syncTreeExpandedState()
  }
}

function onNodeCollapse(data: OrgTreeNode) {
  if (data?.key) {
    expandedKeySet.value.delete(data.key)
    syncTreeExpandedState()
  }
}

function toggleExpandAll() {
  treeExpanded.value = !treeExpanded.value
  if (treeExpanded.value) {
    const allKeys = new Set<string>()
    collectKeysWithChildren(props.treeData, allKeys)
    expandedKeySet.value = allKeys
  } else {
    expandedKeySet.value.clear()
  }
  applyExpandedState()
}

function nodeKindName(kind: string): string {
  if (kind === 'workshops') return '车间'
  if (kind === 'areas') return '区域'
  return '工位单元'
}

function hasAnyAssets(counts?: { channels: number; computers: number; terminalBoxes: number; networkDevices: number }) {
  if (!counts) return false
  return counts.channels > 0 || counts.computers > 0 || counts.terminalBoxes > 0 || counts.networkDevices > 0
}

function goToAsset(data: OrgTreeNode, type: 'channel' | 'computer' | 'box' | 'net-device') {
  if (type === 'channel') {
    if (data.kind === 'workshops') router.push({ path: '/app/channels', query: { workshopId: String(data.id) } })
    else if (data.kind === 'areas') router.push({ path: '/app/channels', query: { areaId: String(data.id) } })
    else router.push({ path: '/app/channels', query: { unitId: String(data.id) } })
  } else if (type === 'computer') {
    if (data.kind === 'workshops') router.push({ path: '/app/computers', query: { workshopId: String(data.id) } })
    else if (data.kind === 'areas') router.push({ path: '/app/computers', query: { areaId: String(data.id) } })
    else router.push({ path: '/app/computers', query: { unitId: String(data.id) } })
  } else if (type === 'box') {
    if (data.kind === 'areas') router.push({ path: '/app/terminal-boxes', query: { areaId: String(data.id) } })
    else router.push({ path: '/app/terminal-boxes' })
  } else if (type === 'net-device') {
    if (data.kind === 'workshops') router.push({ path: '/app/network-devices', query: { workshopId: String(data.id) } })
    else if (data.kind === 'areas') router.push({ path: '/app/network-devices', query: { areaId: String(data.id) } })
    else router.push({ path: '/app/network-devices' })
  }
}

defineExpose({
  setCurrentKey: (key: string | null) => treeRef.value?.setCurrentKey(key as any)
})
</script>

<template>
  <div class="card org-tree-panel">
    <!-- 顶部工具栏与空间架构及全景资产统计 -->
    <div class="tree-toolbar">
      <div class="toolbar-left">
        <el-input
          v-model="treeSearch"
          placeholder="按名称或编码过滤车间、区域、工位单元..."
          :prefix-icon="Search"
          clearable
          class="tree-search-input"
        />
        <el-button
          :icon="treeExpanded ? Fold : Expand"
          @click="toggleExpandAll"
        >
          {{ treeExpanded ? '全部折叠' : '全部展开' }}
        </el-button>
        <el-checkbox
          v-model="showZeroBadges"
          label="显示空资产 (零值)"
          size="small"
          class="ml-2 zero-toggle"
        />
        <el-tag size="small" type="info" effect="plain" class="sort-tag" title="当前拓扑各层级（车间、区域、工位单元）均严格按编码标识 (code) 自然排序">
          <el-icon style="margin-right: 4px;"><Sort /></el-icon>按编码标识排序
        </el-tag>
      </div>

      <div class="toolbar-right">
        <div class="stats-pills">
          <span class="stat-pill ws" title="全厂车间数">
            <el-icon><OfficeBuilding /></el-icon>
            <span>车间</span>
            <b>{{ organization.workshops.length }}</b>
          </span>
          <span class="stat-pill ar" title="功能区域数">
            <el-icon><Location /></el-icon>
            <span>区域</span>
            <b>{{ organization.areas.length }}</b>
          </span>
          <span class="stat-pill un" title="工位单元数">
            <el-icon><Box /></el-icon>
            <span>单元</span>
            <b>{{ organization.units.length }}</b>
          </span>
          <span class="stat-divider">·</span>
          <span class="stat-pill ch is-clickable" title="监控通道总数 (点击直达通道台账)" @click="router.push('/app/channels')">
            <el-icon><VideoCamera /></el-icon>
            <span>通道</span>
            <b>{{ totalStats.channels }}</b>
            <span class="stat-sub">路</span>
          </span>
          <span class="stat-pill comp is-clickable" title="工位电脑总数 (点击直达工位电脑台账)" @click="router.push('/app/computers')">
            <el-icon><Monitor /></el-icon>
            <span>电脑</span>
            <b>{{ totalStats.computers }}</b>
            <span class="stat-sub">台</span>
          </span>
          <span class="stat-pill box is-clickable" title="网络终端箱总数 (点击直达网络终端箱台账)" @click="router.push('/app/terminal-boxes')">
            <el-icon><Box /></el-icon>
            <span>终端箱</span>
            <b>{{ totalStats.terminalBoxes }}</b>
            <span class="stat-sub">个</span>
          </span>
          <span class="stat-pill netdev is-clickable" title="网络设备总数 (点击直达网络设备台账)" @click="router.push('/app/network-devices')">
            <el-icon><Connection /></el-icon>
            <span>设备</span>
            <b>{{ totalStats.networkDevices }}</b>
            <span class="stat-sub">台</span>
          </span>
        </div>
      </div>
    </div>

    <!-- 空间层级拓扑树主体 -->
    <div class="tree-container">
      <el-empty
        v-if="!organization.workshops.length && !organization.areas.length && !organization.units.length"
        description="尚未创建组织架构，点击下方新建顶级车间"
        :image-size="64"
      >
        <el-button v-if="canManageArea" type="primary" :icon="Plus" @click="emit('createWorkshop')">
          新建车间
        </el-button>
      </el-empty>
      <el-tree
        v-else
        ref="treeRef"
        :data="treeData"
        node-key="key"
        :filter-node-method="filterTreeNode"
        highlight-current
        :expand-on-click-node="true"
        :indent="28"
        class="organization-tree structured-tree"
        @node-expand="onNodeExpand"
        @node-collapse="onNodeCollapse"
      >
        <template #default="{ data }: { data: OrgTreeNode }">
          <div
            class="custom-tree-node"
            :class="[
              `node-level-${data.kind}`,
              { 'is-disabled': data.status === 'disabled' }
            ]"
          >
            <!-- 节点左侧：图标、层级类型、名称、编码、停用标识 -->
            <div class="node-main">
              <!-- 图标容器 -->
              <div class="node-icon-box" :class="data.kind">
                <el-icon v-if="data.kind === 'workshops'"><OfficeBuilding /></el-icon>
                <el-icon v-else-if="data.kind === 'areas'"><Location /></el-icon>
                <el-icon v-else><Box /></el-icon>
              </div>

              <!-- 层级专属标签 -->
              <span class="node-kind-badge" :class="data.kind">
                {{ data.kind === 'workshops' ? '车间 (顶级)' : data.kind === 'areas' ? '区域' : '单元' }}
              </span>

              <!-- 节点名称 -->
              <span class="node-title" :title="data.name">{{ data.name }}</span>

              <!-- 组织编码 -->
              <span v-if="data.code" class="node-code monospace" :title="`组织编码: ${data.code}`">
                {{ data.code }}
              </span>

              <!-- 停用标签 -->
              <el-tag v-if="data.status === 'disabled'" size="small" type="info" effect="plain" class="disabled-tag">
                停用
              </el-tag>
            </div>

            <!-- 节点右侧：四类资产统计徽标 + 行级操作按钮 -->
            <div class="node-meta">
              <!-- 四类资产多维统计徽标组 -->
              <div class="asset-badges-group">
                <!-- 1. 监控通道 -->
                <el-tooltip
                  v-if="showZeroBadges || data.assetCounts.channels > 0"
                  :content="`查看该${nodeKindName(data.kind)}下关联的监控通道 (${data.assetCounts.channels} 路)`"
                  placement="top"
                >
                  <span
                    class="asset-badge channel"
                    :class="{ 'is-zero': data.assetCounts.channels === 0 }"
                    @click.stop="goToAsset(data, 'channel')"
                  >
                    <el-icon :size="12"><VideoCamera /></el-icon>
                    <span>{{ data.assetCounts.channels }} 路通道</span>
                  </span>
                </el-tooltip>

                <!-- 2. 工位电脑 -->
                <el-tooltip
                  v-if="showZeroBadges || data.assetCounts.computers > 0"
                  :content="`查看该${nodeKindName(data.kind)}下绑定的工位电脑 (${data.assetCounts.computers} 台)`"
                  placement="top"
                >
                  <span
                    class="asset-badge computer"
                    :class="{ 'is-zero': data.assetCounts.computers === 0 }"
                    @click.stop="goToAsset(data, 'computer')"
                  >
                    <el-icon :size="12"><Monitor /></el-icon>
                    <span>{{ data.assetCounts.computers }} 台电脑</span>
                  </span>
                </el-tooltip>

                <!-- 3. 网络终端箱 -->
                <el-tooltip
                  v-if="showZeroBadges || data.assetCounts.terminalBoxes > 0"
                  :content="`查看该${nodeKindName(data.kind)}下部署的网络终端箱 (${data.assetCounts.terminalBoxes} 个)`"
                  placement="top"
                >
                  <span
                    class="asset-badge box"
                    :class="{ 'is-zero': data.assetCounts.terminalBoxes === 0 }"
                    @click.stop="goToAsset(data, 'box')"
                  >
                    <el-icon :size="12"><Box /></el-icon>
                    <span>{{ data.assetCounts.terminalBoxes }} 个终端箱</span>
                  </span>
                </el-tooltip>

                <!-- 4. 网络设备 -->
                <el-tooltip
                  v-if="showZeroBadges || data.assetCounts.networkDevices > 0"
                  :content="`查看该${nodeKindName(data.kind)}下部署的网络设备 (${data.assetCounts.networkDevices} 台)`"
                  placement="top"
                >
                  <span
                    class="asset-badge net-device"
                    :class="{ 'is-zero': data.assetCounts.networkDevices === 0 }"
                    @click.stop="goToAsset(data, 'net-device')"
                  >
                    <el-icon :size="12"><Connection /></el-icon>
                    <span>{{ data.assetCounts.networkDevices }} 台设备</span>
                  </span>
                </el-tooltip>

                <!-- 若全为 0 且未开启全部显示，展示无挂靠状态 (车间层除外) -->
                <span
                  v-if="!showZeroBadges && !hasAnyAssets(data.assetCounts)"
                  class="empty-asset-hint"
                >
                  无挂靠资产
                </span>
              </div>

              <!-- 悬停操作按钮组 -->
              <div class="node-actions" @click.stop>
                <!-- 车间操作 -->
                <template v-if="data.kind === 'workshops'">
                  <el-tooltip content="在该车间下新增功能区域" placement="top">
                    <el-button
                      v-if="canManageArea"
                      link
                      type="primary"
                      :icon="Plus"
                      size="small"
                      @click.stop="emit('createArea', data.rawNode)"
                    >
                      添加区域
                    </el-button>
                  </el-tooltip>
                  <el-tooltip content="编辑车间属性" placement="top">
                    <el-button
                      v-if="canManageArea"
                      link
                      type="primary"
                      :icon="Edit"
                      size="small"
                      @click.stop="emit('editNode', 'workshops', data.rawNode)"
                    >
                      编辑
                    </el-button>
                  </el-tooltip>
                  <el-tooltip content="删除此车间" placement="top">
                    <el-button
                      v-if="canManageArea"
                      link
                      type="danger"
                      :icon="Delete"
                      size="small"
                      @click.stop="emit('removeNode', 'workshops', data.rawNode)"
                    >
                      删除
                    </el-button>
                  </el-tooltip>
                </template>

                <!-- 区域操作 -->
                <template v-else-if="data.kind === 'areas'">
                  <el-tooltip content="在该区域下新增工位单元" placement="top">
                    <el-button
                      v-if="canManageArea"
                      link
                      type="primary"
                      :icon="Plus"
                      size="small"
                      @click.stop="emit('createUnit', data.rawNode)"
                    >
                      添加单元
                    </el-button>
                  </el-tooltip>
                  <el-tooltip content="编辑区域属性" placement="top">
                    <el-button
                      v-if="canManageArea"
                      link
                      type="primary"
                      :icon="Edit"
                      size="small"
                      @click.stop="emit('editNode', 'areas', data.rawNode)"
                    >
                      编辑
                    </el-button>
                  </el-tooltip>
                  <el-tooltip content="删除此区域" placement="top">
                    <el-button
                      v-if="canManageArea"
                      link
                      type="danger"
                      :icon="Delete"
                      size="small"
                      @click.stop="emit('removeNode', 'areas', data.rawNode)"
                    >
                      删除
                    </el-button>
                  </el-tooltip>
                </template>

                <!-- 单元操作 -->
                <template v-else-if="data.kind === 'units'">
                  <el-tooltip content="调整此单元下监控通道排序" placement="top">
                    <el-button
                      link
                      type="warning"
                      :icon="Sort"
                      size="small"
                      @click.stop="emit('sortUnit', data.rawNode)"
                    >
                      通道排序
                    </el-button>
                  </el-tooltip>
                  <el-tooltip content="编辑单元属性" placement="top">
                    <el-button
                      v-if="canManageArea"
                      link
                      type="primary"
                      :icon="Edit"
                      size="small"
                      @click.stop="emit('editNode', 'units', data.rawNode)"
                    >
                      编辑
                    </el-button>
                  </el-tooltip>
                  <el-tooltip content="删除此工位单元" placement="top">
                    <el-button
                      v-if="canManageArea"
                      link
                      type="danger"
                      :icon="Delete"
                      size="small"
                      @click.stop="emit('removeNode', 'units', data.rawNode)"
                    >
                      删除
                    </el-button>
                  </el-tooltip>
                </template>
              </div>
            </div>
          </div>
        </template>
      </el-tree>
    </div>
  </div>
</template>

<style scoped>
.org-tree-panel {
  padding: 0;
  width: 100%;
  border-radius: 8px;
  background: #ffffff;
  border: 1px solid var(--el-border-color-lighter);
  box-shadow: 0 1px 3px rgba(0, 0, 0, 0.05);
  display: flex;
  flex-direction: column;
}

.tree-toolbar {
  padding: 14px 20px;
  background: var(--el-fill-color-light);
  border-bottom: 1px solid var(--el-border-color-lighter);
  display: flex;
  align-items: center;
  justify-content: space-between;
  flex-wrap: wrap;
  gap: 16px;
}

.toolbar-left {
  display: flex;
  align-items: center;
  gap: 12px;
  flex: 1;
  max-width: 680px;
}

.tree-search-input {
  flex: 1;
  min-width: 200px;
}

.zero-toggle {
  margin-left: 4px;
  flex-shrink: 0;
  color: var(--el-text-color-secondary);
}

.sort-tag {
  flex-shrink: 0;
  font-size: 11px;
  color: var(--el-text-color-secondary);
  border-color: var(--el-border-color-lighter);
  background: var(--el-fill-color-blank);
}

.toolbar-right {
  display: flex;
  align-items: center;
}

.stats-pills {
  display: flex;
  align-items: center;
  gap: 8px;
  flex-wrap: wrap;
}

.stat-divider {
  color: var(--el-border-color);
  font-weight: 300;
  font-size: 13px;
}

.stat-pill {
  display: inline-flex;
  align-items: center;
  gap: 5px;
  font-size: 12px;
  padding: 4px 10px;
  border-radius: 6px;
  background: #ffffff;
  border: 1px solid var(--el-border-color-lighter);
  color: var(--el-text-color-regular);
  transition: all 0.15s ease;
}

.stat-pill.is-clickable {
  cursor: pointer;
}
.stat-pill.is-clickable:hover {
  transform: translateY(-1px);
  box-shadow: 0 2px 5px rgba(0, 0, 0, 0.06);
}

.stat-pill b {
  font-weight: 700;
  color: var(--el-text-color-primary);
}

.stat-pill.ws {
  color: #2563eb;
  border-color: rgba(37, 99, 235, 0.2);
  background: rgba(37, 99, 235, 0.04);
}
.stat-pill.ws b { color: #1d4ed8; }

.stat-pill.ar {
  color: #0d9488;
  border-color: rgba(13, 148, 136, 0.2);
  background: rgba(13, 148, 136, 0.04);
}
.stat-pill.ar b { color: #0f766e; }

.stat-pill.un {
  color: #7c3aed;
  border-color: rgba(124, 58, 237, 0.2);
  background: rgba(124, 58, 237, 0.04);
}
.stat-pill.un b { color: #6d28d9; }

.stat-pill.ch {
  color: #0284c7;
  border-color: rgba(2, 132, 199, 0.2);
  background: rgba(2, 132, 199, 0.04);
}
.stat-pill.ch b { color: #0369a1; }

.stat-pill.comp {
  color: #059669;
  border-color: rgba(16, 185, 129, 0.2);
  background: rgba(16, 185, 129, 0.04);
}
.stat-pill.comp b { color: #047857; }

.stat-pill.box {
  color: #d97706;
  border-color: rgba(245, 158, 11, 0.2);
  background: rgba(245, 158, 11, 0.04);
}
.stat-pill.box b { color: #b45309; }

.stat-pill.netdev {
  color: #8b5cf6;
  border-color: rgba(139, 92, 246, 0.2);
  background: rgba(139, 92, 246, 0.04);
}
.stat-pill.netdev b { color: #7c3aed; }

.stat-sub {
  font-size: 11px;
  font-weight: normal;
  color: var(--el-text-color-secondary);
}

.tree-container {
  padding: 16px 20px;
  min-height: 480px;
}

/* 树容器与节点通用设置 */
:deep(.structured-tree .el-tree-node) {
  position: relative;
}

:deep(.structured-tree .el-tree-node__content) {
  height: auto;
  border-radius: 6px;
  padding-right: 12px;
  transition: all 0.15s ease;
}

:deep(.structured-tree .el-tree-node.is-current > .el-tree-node__content) {
  background-color: var(--el-color-primary-light-9) !important;
}

/* ====================================================
   Level 1: 车间节点 (顶层宏观大板块)
   ==================================================== */
:deep(.structured-tree .el-tree-node__content:has(.node-level-workshops)) {
  background: linear-gradient(90deg, #f0f7ff 0%, #f8fafc 100%);
  border: 1px solid #bfdbfe;
  border-left: 5px solid #2563eb;
  border-radius: 8px;
  min-height: 48px;
  margin-top: 14px;
  margin-bottom: 6px;
  box-shadow: 0 1px 3px rgba(37, 99, 235, 0.06);
}

:deep(.structured-tree .el-tree-node__content:has(.node-level-workshops):hover) {
  background: linear-gradient(90deg, #e0f2fe 0%, #f1f5f9 100%);
  border-color: #93c5fd;
}

.custom-tree-node.node-level-workshops .node-title {
  font-size: 15px;
  font-weight: 700;
  color: #0f172a;
  letter-spacing: 0.3px;
}

.custom-tree-node.node-level-workshops .node-icon-box {
  background: #2563eb;
  color: #ffffff;
  box-shadow: 0 2px 4px rgba(37, 99, 235, 0.3);
}

/* ====================================================
   Level 2: 区域节点 (二级功能分区)
   ==================================================== */
:deep(.structured-tree .el-tree-node__content:has(.node-level-areas)) {
  background: #ffffff;
  border: 1px solid #e2e8f0;
  border-left: 3px solid #0d9488;
  border-radius: 6px;
  min-height: 42px;
  margin: 4px 0 2px 0;
}

:deep(.structured-tree .el-tree-node__content:has(.node-level-areas):hover) {
  background: #f0fdfa;
  border-color: #99f6e4;
}

.custom-tree-node.node-level-areas .node-title {
  font-size: 14px;
  font-weight: 600;
  color: #1e293b;
}

.custom-tree-node.node-level-areas .node-icon-box {
  background: rgba(13, 148, 136, 0.12);
  color: #0d9488;
}

/* ====================================================
   Level 3: 工位单元节点 (末级执行单元)
   ==================================================== */
:deep(.structured-tree .el-tree-node__content:has(.node-level-units)) {
  background: #ffffff;
  border: 1px dashed #e2e8f0;
  border-left: 2px solid #8b5cf6;
  border-radius: 4px;
  min-height: 36px;
  margin: 2px 0;
}

:deep(.structured-tree .el-tree-node__content:has(.node-level-units):hover) {
  background: #faf5ff;
  border-color: #ddd6fe;
  border-style: solid;
}

.custom-tree-node.node-level-units .node-title {
  font-size: 13.5px;
  font-weight: 500;
  color: #475569;
}

.custom-tree-node.node-level-units .node-icon-box {
  background: rgba(139, 92, 246, 0.1);
  color: #7c3aed;
  width: 22px;
  height: 22px;
  font-size: 12px;
}

/* 节点自定义行排版 */
.custom-tree-node {
  flex: 1;
  display: flex;
  align-items: center;
  justify-content: space-between;
  min-width: 0;
  gap: 12px;
  padding: 4px 0;
}

.custom-tree-node.is-disabled .node-title {
  color: var(--el-text-color-disabled);
  text-decoration: line-through;
}

.node-main {
  display: flex;
  align-items: center;
  gap: 8px;
  min-width: 0;
  flex: 1;
}

/* 图标容器通用 */
.node-icon-box {
  width: 26px;
  height: 26px;
  border-radius: 6px;
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 14px;
  flex-shrink: 0;
  transition: all 0.2s;
}

/* 层级标识 Tag */
.node-kind-badge {
  font-size: 11px;
  font-weight: 600;
  padding: 1px 7px;
  border-radius: 4px;
  flex-shrink: 0;
}

.node-kind-badge.workshops {
  background: #2563eb;
  color: #ffffff;
}

.node-kind-badge.areas {
  background: #ccfbf1;
  color: #0f766e;
  border: 1px solid rgba(15, 118, 110, 0.2);
}

.node-kind-badge.units {
  background: #f3e8ff;
  color: #6b21a8;
  border: 1px solid rgba(107, 33, 168, 0.2);
}

.node-title {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.node-code {
  font-size: 11px;
  color: var(--el-text-color-secondary);
  background: var(--el-fill-color);
  padding: 1px 6px;
  border-radius: 4px;
  border: 1px solid var(--el-border-color-lighter);
  flex-shrink: 0;
}

.disabled-tag {
  transform: scale(0.85);
  transform-origin: left center;
}

.node-meta {
  display: flex;
  align-items: center;
  gap: 12px;
  flex-shrink: 0;
}

/* 多维资产徽标组 */
.asset-badges-group {
  display: flex;
  align-items: center;
  gap: 6px;
}

.asset-badge {
  display: inline-flex;
  align-items: center;
  gap: 4px;
  font-size: 11.5px;
  padding: 2px 7px;
  border-radius: 6px;
  font-weight: 500;
  cursor: pointer;
  transition: all 0.2s ease;
  border: 1px solid transparent;
  user-select: none;
  white-space: nowrap;
}

/* 监控通道: 经典科技蓝 */
.asset-badge.channel {
  background: rgba(37, 99, 235, 0.08);
  color: #2563eb;
  border-color: rgba(37, 99, 235, 0.18);
}
.asset-badge.channel:hover {
  background: #2563eb;
  color: #ffffff;
  box-shadow: 0 2px 6px rgba(37, 99, 235, 0.25);
}

/* 工位电脑: 翡翠绿 */
.asset-badge.computer {
  background: rgba(16, 185, 129, 0.08);
  color: #059669;
  border-color: rgba(16, 185, 129, 0.18);
}
.asset-badge.computer:hover {
  background: #10b981;
  color: #ffffff;
  box-shadow: 0 2px 6px rgba(16, 185, 129, 0.25);
}

/* 网络终端箱: 活力琥珀 */
.asset-badge.box {
  background: rgba(245, 158, 11, 0.08);
  color: #d97706;
  border-color: rgba(245, 158, 11, 0.18);
}
.asset-badge.box:hover {
  background: #f59e0b;
  color: #ffffff;
  box-shadow: 0 2px 6px rgba(245, 158, 11, 0.25);
}

/* 网络设备: 紫罗兰 */
.asset-badge.net-device {
  background: rgba(139, 92, 246, 0.08);
  color: #7c3aed;
  border-color: rgba(139, 92, 246, 0.18);
}
.asset-badge.net-device:hover {
  background: #8b5cf6;
  color: #ffffff;
  box-shadow: 0 2px 6px rgba(139, 92, 246, 0.25);
}

/* 零资产弱化展示 (当开启显示零值时使用) */
.asset-badge.is-zero {
  background: var(--el-fill-color);
  color: var(--el-text-color-placeholder);
  border-color: transparent;
  opacity: 0.6;
}
.asset-badge.is-zero:hover {
  opacity: 1;
  background: var(--el-fill-color-darker);
  color: var(--el-text-color-secondary);
  box-shadow: none;
}

/* 暂无挂靠资产轻量文字 */
.empty-asset-hint {
  font-size: 11.5px;
  color: var(--el-text-color-placeholder);
  padding: 2px 6px;
  opacity: 0.65;
}

.node-actions {
  display: flex;
  align-items: center;
  gap: 4px;
  opacity: 0;
  transition: opacity 0.15s ease;
}

:deep(.structured-tree .el-tree-node__content:hover) .node-actions {
  opacity: 1;
}

.monospace {
  font-family: 'JetBrains Mono', monospace;
}
</style>
