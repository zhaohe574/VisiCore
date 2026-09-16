<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, reactive, ref, watch } from 'vue'
import {
  ArrowDown,
  ArrowUp,
  Collection,
  Delete,
  Edit,
  Plus,
  Refresh,
  Star,
  StarFilled,
  VideoPlay
} from '@element-plus/icons-vue'
import { allPages, managementApi, mediaApi, type Channel, type Layout } from '../api'
import { useAction } from '../composables/useAction'
import { useEvents } from '../stores/events'
import { useAuth } from '../stores/auth'
import { errorMessage } from '../lib/format'
import { layoutOptions, layoutSlots, patrolChannels } from '../lib/layouts'
import PageHeader from '../components/PageHeader.vue'
import StatusBadge from '../components/StatusBadge.vue'

const auth = useAuth()
const { busy, run, confirm } = useAction()

const layouts = ref<Layout[]>([])
const channels = ref<Channel[]>([])
const favorites = ref<Channel[]>([])
const error = ref('')
const loading = ref(false)

const dialog = ref(false)
const editing = ref<number | null>(null)
const selectedChannel = ref<number>()

const form = reactive<Omit<Layout, 'id'>>({
  name: '',
  kind: 'layout',
  shared: false,
  layout: 4,
  intervalSeconds: 30,
  channelIds: [null, null, null, null]
})

const available = computed(() => channels.value.filter(channel => !form.channelIds.includes(channel.id)))
const canShare = computed(() => auth.can('layout.share'))

async function load() {
  loading.value = true
  error.value = ''
  try {
    const data = await Promise.all([
      mediaApi.layouts(),
      mediaApi.favorites(),
      allPages(managementApi.channels)
    ])
    layouts.value = data[0]
    favorites.value = data[1]
    channels.value = data[2]
  } catch (e) {
    error.value = errorMessage(e)
  } finally {
    loading.value = false
  }
}

function edit(layout?: Layout) {
  editing.value = layout?.id || null
  Object.assign(form, {
    name: layout?.name || '',
    kind: layout?.kind || 'layout',
    shared: layout?.shared || false,
    layout: layout?.layout || 4,
    intervalSeconds: layout?.intervalSeconds || 30
  })
  form.channelIds =
    form.kind === 'layout'
      ? layoutSlots(layout?.channelIds || [], form.layout)
      : patrolChannels(layout?.channelIds || [])
  selectedChannel.value = undefined
  dialog.value = true
}

function addChannel() {
  if (selectedChannel.value && !form.channelIds.includes(selectedChannel.value)) {
    form.channelIds.push(selectedChannel.value)
    selectedChannel.value = undefined
  }
}

function move(index: number, direction: number) {
  const other = index + direction
  if (other >= 0 && other < form.channelIds.length) {
    ;[form.channelIds[index], form.channelIds[other]] = [form.channelIds[other], form.channelIds[index]]
  }
}

async function save() {
  if (
    await run(async () => {
      if (!form.name.trim()) throw new Error('请输入方案名称')
      if (form.kind === 'patrol' && !patrolChannels(form.channelIds).length) {
        throw new Error('自动轮巡方案至少需要添加一路有效视频通道')
      }
      if (!Number.isInteger(form.intervalSeconds) || form.intervalSeconds < 10 || form.intervalSeconds > 300) {
        throw new Error('轮巡切换停留间隔应介于 10～300 秒之间')
      }
      await mediaApi.saveLayout(editing.value, {
        ...form,
        channelIds:
          form.kind === 'layout' ? layoutSlots(form.channelIds, form.layout) : patrolChannels(form.channelIds),
        name: form.name.trim(),
        shared: canShare.value && form.shared
      })
      await load()
    }, '方案已成功保存')
  ) {
    dialog.value = false
  }
}

watch(
  () => form.layout,
  () => {
    if (form.kind === 'layout') form.channelIds = layoutSlots(form.channelIds, form.layout)
  },
  { flush: 'sync' }
)

watch(
  () => form.kind,
  kind => {
    form.channelIds = kind === 'layout' ? layoutSlots(form.channelIds, form.layout) : patrolChannels(form.channelIds)
  },
  { flush: 'sync' }
)

async function remove(layout: Layout) {
  await confirm(`确认删除方案“${layout.name}”？`, async () => {
    await mediaApi.deleteLayout(layout.id)
    await load()
  }, '方案已删除')
}

async function unfavorite(channel: Channel) {
  await run(async () => {
    await mediaApi.saveFavorites(favorites.value.filter(item => item.id !== channel.id).map(item => item.id))
    favorites.value = await mediaApi.favorites()
  }, '已移出收藏')
}

const unsubscribe = useEvents().subscribe(['access.changed', 'device.changed', 'reconnected'], load)

onMounted(load)
onBeforeUnmount(unsubscribe)
</script>

<template>
  <div>
    <PageHeader title="收藏与轮巡" description="管理个人重点监控通道收藏夹，以及多路分屏组合与自动循环轮巡方案">
      <el-button :icon="Refresh" :loading="loading" @click="load">刷新</el-button>
      <el-button type="primary" :icon="Plus" @click="edit()">新建方案</el-button>
    </PageHeader>

    <el-alert v-if="error" :title="error" type="error" :closable="false" show-icon style="margin-bottom: 16px;" />

    <!-- 个人收藏通道卡片 -->
    <section class="filter-card" style="margin-bottom: 20px; padding: 20px;">
      <div style="display: flex; align-items: center; justify-content: space-between; margin-bottom: 16px;">
        <div style="display: flex; align-items: center; gap: 8px;">
          <el-icon class="amber" style="font-size: 18px;"><StarFilled /></el-icon>
          <h2 style="margin: 0; font-size: 15px;">个人收藏重点通道</h2>
        </div>
        <span class="muted" style="font-size: 12.5px;">已收藏 {{ favorites.length }} 路通道</span>
      </div>

      <el-empty v-if="!favorites.length" description="暂无收藏通道，在实时预览或通道列表中点击星标即可快速收藏" :image-size="54" />

      <div v-else style="display: grid; grid-template-columns: repeat(auto-fill, minmax(280px, 1fr)); gap: 12px;">
        <div
          v-for="channel in favorites"
          :key="channel.id"
          class="kpi-card"
          style="padding: 12px 16px; margin-bottom: 0;"
        >
          <div style="flex: 1; min-width: 0;">
            <strong style="font-size: 13.5px; color: var(--text-primary); display: block; overflow: hidden; text-overflow: ellipsis; white-space: nowrap;">
              {{ channel.alias || channel.name }}
            </strong>
            <small class="muted" style="font-size: 11.5px; display: block; margin-top: 2px;">
              {{ channel.deviceName }} · 通道 {{ channel.deviceChannel }}
            </small>
          </div>

          <StatusBadge :value="channel.status" />

          <router-link :to="{ path: '/app/live', query: { channelId: channel.id } }">
            <el-tooltip content="在实时预览中打开">
              <el-button text :icon="VideoPlay" type="primary" />
            </el-tooltip>
          </router-link>

          <el-tooltip content="移出个人收藏">
            <el-button text :icon="StarFilled" style="color: #f59e0b;" :disabled="busy" @click="unfavorite(channel)" />
          </el-tooltip>
        </div>
      </div>
    </section>

    <!-- 布局与轮巡方案表格卡片 -->
    <div class="table-card">
      <div style="padding: 16px 20px; display: flex; align-items: center; justify-content: space-between; border-bottom: 1px solid var(--border-light);">
        <div style="display: flex; align-items: center; gap: 8px;">
          <el-icon class="blue" style="font-size: 18px;"><Collection /></el-icon>
          <h2 style="margin: 0; font-size: 15px;">分屏布局与轮巡方案</h2>
        </div>
        <span class="muted" style="font-size: 12.5px;">已定义 {{ layouts.length }} 个方案</span>
      </div>

      <el-table v-loading="loading" :data="layouts" empty-text="暂无定义的布局或轮巡方案">
        <el-table-column prop="name" label="方案名称" min-width="170">
          <template #default="{ row }">
            <strong>{{ row.name }}</strong>
          </template>
        </el-table-column>

        <el-table-column label="方案类型" width="110">
          <template #default="{ row }">
            <el-tag :type="row.kind === 'patrol' ? 'warning' : 'primary'" size="small">
              {{ row.kind === 'patrol' ? '循环轮巡' : '固定分屏' }}
            </el-tag>
          </template>
        </el-table-column>

        <el-table-column label="共享范围" width="100">
          <template #default="{ row }">
            <span :class="row.shared ? 'teal' : 'muted'">{{ row.shared ? '全局共享' : '个人私有' }}</span>
          </template>
        </el-table-column>

        <el-table-column label="窗口规格" width="110">
          <template #default="{ row }">
            <span>{{ row.layout }} 分屏</span>
          </template>
        </el-table-column>

        <el-table-column label="关联通道数" width="120">
          <template #default="{ row }">
            <span style="font-weight: 600;">{{ patrolChannels(row.channelIds).length }}</span> 路
          </template>
        </el-table-column>

        <el-table-column label="轮巡切换间隔" width="130">
          <template #default="{ row }">
            <span v-if="row.kind === 'patrol'">{{ row.intervalSeconds }} 秒</span>
            <span v-else class="muted">—</span>
          </template>
        </el-table-column>

        <el-table-column label="操作" width="180" fixed="right">
          <template #default="{ row }">
            <div class="table-tools">
              <router-link :to="{ path: '/app/live', query: { layoutId: row.id } }">
                <el-button link type="primary" :icon="VideoPlay">载入</el-button>
              </router-link>
              <template v-if="!row.shared || canShare">
                <el-button link type="primary" :icon="Edit" @click="edit(row as Layout)">编辑</el-button>
                <el-button link type="danger" :icon="Delete" :disabled="busy" @click="remove(row as Layout)">删除</el-button>
              </template>
            </div>
          </template>
        </el-table-column>
      </el-table>
    </div>

    <!-- 方案编辑弹窗 -->
    <el-dialog
      v-model="dialog"
      :title="editing ? '编辑监控方案' : '新建监控方案'"
      width="680px"
      destroy-on-close
    >
      <el-form label-position="top">
        <div style="display: grid; grid-template-columns: repeat(2, 1fr); gap: 0 16px;">
          <el-form-item label="方案名称" required>
            <el-input v-model="form.name" maxlength="100" placeholder="例如：重点生产线巡更" clearable />
          </el-form-item>

          <el-form-item label="方案工作模式">
            <el-radio-group v-model="form.kind">
              <el-radio-button value="layout">固定分屏布局</el-radio-button>
              <el-radio-button value="patrol">定时自动轮巡</el-radio-button>
            </el-radio-group>
          </el-form-item>

          <el-form-item label="视频分屏规格">
            <el-segmented v-model="form.layout" :options="layoutOptions" />
          </el-form-item>

          <el-form-item v-if="form.kind === 'patrol'" label="轮巡切换间隔（秒）">
            <el-input-number v-model="form.intervalSeconds" :min="10" :max="300" :precision="0" style="width: 100%;" />
          </el-form-item>

          <el-form-item v-if="canShare" label="是否设为全员共享方案">
            <el-switch v-model="form.shared" active-text="共享给全平台" inactive-text="仅限自己可见" />
          </el-form-item>
        </div>

        <div v-if="form.kind === 'layout'" style="margin-top: 10px;">
          <h3 style="margin-bottom: 10px;">窗口画面与通道绑定</h3>
          <div style="display: grid; grid-template-columns: repeat(2, 1fr); gap: 10px 16px; max-height: 280px; overflow-y: auto;">
            <el-form-item
              v-for="(_, index) in form.channelIds"
              :key="index"
              :label="`分屏窗口 ${index + 1}`"
              style="margin-bottom: 8px;"
            >
              <el-select
                :model-value="form.channelIds[index]"
                clearable
                filterable
                placeholder="留空则作为空白窗口"
                style="width: 100%;"
                @update:model-value="value => form.channelIds[index] = typeof value === 'number' ? value : null"
              >
                <el-option
                  v-for="channel in channels"
                  :key="channel.id"
                  :value="channel.id"
                  :label="`${channel.deviceName} / ${channel.alias || channel.name}`"
                />
              </el-select>
            </el-form-item>
          </div>
        </div>

        <template v-else>
          <el-form-item label="添加参与轮巡的通道" required style="margin-top: 10px;">
            <div style="display: flex; gap: 8px; width: 100%;">
              <el-select v-model="selectedChannel" filterable placeholder="选择要加入轮巡序列的通道..." style="flex: 1;">
                <el-option
                  v-for="channel in available"
                  :key="channel.id"
                  :value="channel.id"
                  :label="`${channel.deviceName} / ${channel.alias || channel.name}`"
                />
              </el-select>
              <el-button type="primary" :icon="Plus" :disabled="!selectedChannel" @click="addChannel">
                添加
              </el-button>
            </div>
          </el-form-item>

          <div style="border: 1px solid var(--border); border-radius: var(--radius-md); max-height: 240px; overflow-y: auto; padding: 6px 12px;">
            <div v-if="!form.channelIds.length" class="muted" style="text-align: center; padding: 20px;">
              尚未添加通道
            </div>
            <div
              v-for="(id, index) in form.channelIds"
              :key="index"
              style="display: flex; align-items: center; gap: 8px; padding: 6px 0; border-bottom: 1px solid var(--border-light);"
            >
              <span style="font-weight: 600; width: 24px; color: var(--text-muted);">{{ index + 1 }}.</span>
              <span style="flex: 1; min-width: 0; font-size: 13px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap;">
                {{ channels.find(c => c.id === id)?.alias || channels.find(c => c.id === id)?.name || `通道 ${id}` }}
              </span>
              <el-button text size="small" :icon="ArrowUp" :disabled="index === 0" @click="move(index, -1)" />
              <el-button text size="small" :icon="ArrowDown" :disabled="index === form.channelIds.length - 1" @click="move(index, 1)" />
              <el-button text type="danger" size="small" :icon="Delete" @click="form.channelIds.splice(index, 1)" />
            </div>
          </div>
        </template>
      </el-form>

      <template #footer>
        <el-button @click="dialog = false">取消</el-button>
        <el-button type="primary" :loading="busy" @click="save">保存方案</el-button>
      </template>
    </el-dialog>
  </div>
</template>
