<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, onMounted, ref } from 'vue'
import { useRoute } from 'vue-router'
import {
  Check,
  Close,
  DArrowRight,
  Download,
  Folder,
  FolderOpened,
  FullScreen,
  Refresh,
  RefreshLeft,
  RefreshRight,
  Search,
  Star,
  StarFilled,
  VideoCamera,
  VideoPause,
  VideoPlay
} from '@element-plus/icons-vue'
import {
  allPages,
  managementApi,
  mediaApi,
  workflowApi,
  type Channel,
  type Layout,
  type LiveSession,
  type Organization,
  type PlaybackControl,
  type PlaybackSession,
  type Recording
} from '../api'
import { useAuth } from '../stores/auth'
import { useEvents } from '../stores/events'
import { useAction } from '../composables/useAction'
import { bytes, dateTime, errorMessage, timeRange } from '../lib/format'
import { patrolBatch } from '../lib/media'
import {
  captureLayout,
  layoutColumns,
  layoutOptions,
  patrolChannels,
  restoreLayout,
  type LayoutCount
} from '../lib/layouts'
import PageHeader from '../components/PageHeader.vue'
import StatusBadge from '../components/StatusBadge.vue'
import VideoTile from '../components/VideoTile.vue'
import PtzPanel from '../components/PtzPanel.vue'
import PlaybackTimeline, { type TimelineTrack } from '../components/PlaybackTimeline.vue'

const route = useRoute()
const auth = useAuth()
const { busy, run } = useAction()

const mode = computed<'live' | 'playback'>(() =>
  route.path.endsWith('playback') ? 'playback' : 'live'
)

// 数据源
const channels = ref<Channel[]>([])
const favorites = ref<number[]>([])
const layouts = ref<Layout[]>([])
const organization = ref<Organization>({ workshops: [], areas: [], units: [] })
const search = ref('')
const onlyFavorites = ref(false)
const onlyOnline = ref(false)
const loading = ref(false)
const error = ref('')

// 分屏与格子（最大支持 25 路）
const MAX_SLOTS = 25
const count = ref<LayoutCount>(4)
const selected = ref(0)
const slots = ref<(Channel | undefined)[]>(Array(MAX_SLOTS).fill(undefined))
const streams = ref<(1 | 2)[]>(Array(MAX_SLOTS).fill(2))
const slotRanges = ref<({ start: string; end: string } | undefined)[]>(Array(MAX_SLOTS).fill(undefined))
const sessions = ref<(LiveSession | PlaybackSession | null)[]>(Array(MAX_SLOTS).fill(null))
const revision = ref(0)

type TileInstance = InstanceType<typeof VideoTile>
const tiles = new Map<number, TileInstance>()
const wall = ref<HTMLElement>()

// 回放与检索状态
const searchDate = ref<Date>(new Date())
const searchStartTime = ref<string>('00:00:00')
const recordings = ref<Recording[]>([])
const unified = ref(false)
const speed = ref(1)

// 轮巡与方案
const layoutId = ref<number>()
const patrol = ref<Layout>()
const patrolOffset = ref(0)
const layoutDialog = ref(false)
const layoutName = ref('')

// 组织树全部展开/折叠状态
const treeAllOpen = ref(true)

const currentRange = computed<[Date, Date]>(() => {
  const d = searchDate.value || new Date()
  const start = new Date(d)
  const [h, m, s] = (searchStartTime.value || '00:00:00').split(':').map(Number)
  start.setHours(isNaN(h) ? 0 : h, isNaN(m) ? 0 : m, isNaN(s) ? 0 : s, 0)
  const end = new Date(d)
  end.setHours(23, 59, 59, 999)
  return [start, end]
})

let patrolTimer: ReturnType<typeof setInterval> | undefined
let disposed = false
let searchGeneration = 0
let pendingReload = false
let pendingRestart = false

// 通道过滤
const filtered = computed(() =>
  channels.value.filter(channel => {
    if (onlyFavorites.value && !favorites.value.includes(channel.id)) return false
    if (onlyOnline.value && channel.status !== 'online') return false
    if (!search.value.trim()) return true
    const text = `${channel.alias || ''} ${channel.name} ${channel.deviceName} ${channel.deviceChannel}`.toLowerCase()
    return text.includes(search.value.trim().toLowerCase())
  })
)

interface TreeUnit {
  id: number
  name: string
  channels: Channel[]
}
interface TreeArea {
  id: number
  name: string
  units: TreeUnit[]
  channelCount: number
}
interface TreeWorkshop {
  id: number
  name: string
  areas: TreeArea[]
  channelCount: number
}

const orgTree = computed(() => {
  const channelList = filtered.value
  const unitMap = new Map<number, Channel[]>()
  const unassigned: Channel[] = []
  const validUnitIds = new Set(organization.value.units.map(u => Number(u.id)))

  for (const c of channelList) {
    const uid = c.unitId !== null && c.unitId !== undefined ? Number(c.unitId) : null
    if (uid !== null && validUnitIds.has(uid)) {
      if (!unitMap.has(uid)) unitMap.set(uid, [])
      unitMap.get(uid)!.push(c)
    } else {
      unassigned.push(c)
    }
  }

  const areasByWorkshop = new Map<number, typeof organization.value.areas>()
  for (const a of organization.value.areas) {
    const wsId = Number(a.parentId)
    if (!areasByWorkshop.has(wsId)) areasByWorkshop.set(wsId, [])
    areasByWorkshop.get(wsId)!.push(a)
  }

  const unitsByArea = new Map<number, typeof organization.value.units>()
  for (const u of organization.value.units) {
    const areaId = Number(u.parentId)
    if (!unitsByArea.has(areaId)) unitsByArea.set(areaId, [])
    unitsByArea.get(areaId)!.push(u)
  }

  const workshops: TreeWorkshop[] = []
  for (const ws of organization.value.workshops) {
    const wsId = Number(ws.id)
    const areas = areasByWorkshop.get(wsId) || []
    const treeAreas: TreeArea[] = []
    let wsChannelCount = 0

    for (const a of areas) {
      const aId = Number(a.id)
      const units = unitsByArea.get(aId) || []
      const treeUnits: TreeUnit[] = []
      let areaChannelCount = 0

      for (const u of units) {
        const uId = Number(u.id)
        const uChannels = unitMap.get(uId) || []
        if (uChannels.length > 0) {
          treeUnits.push({ id: uId, name: u.name, channels: uChannels })
          areaChannelCount += uChannels.length
        }
      }

      if (treeUnits.length > 0) {
        treeAreas.push({ id: aId, name: a.name, units: treeUnits, channelCount: areaChannelCount })
        wsChannelCount += areaChannelCount
      }
    }

    if (treeAreas.length > 0) {
      workshops.push({ id: wsId, name: ws.name, areas: treeAreas, channelCount: wsChannelCount })
    }
  }

  return { workshops, unassigned }
})

const selectedChannel = computed(() => slots.value[selected.value])
const selectedSession = computed(() => sessions.value[selected.value] as PlaybackSession | null)
const canExport = computed(() => auth.can('export.create'))
const stream = computed({
  get: () => streams.value[selected.value],
  set: (value: 1 | 2) => {
    streams.value[selected.value] = value
  }
})

async function loadChannels(reconcile = false, restart = false) {
  if (loading.value) {
    pendingReload = true
    pendingRestart ||= restart
    return
  }
  loading.value = true
  error.value = ''
  try {
    const data = await Promise.all([
      allPages(managementApi.channels),
      mediaApi.favorites(),
      mediaApi.layouts(),
      managementApi.organization()
    ])
    if (disposed) return
    channels.value = data[0]
    favorites.value = data[1].map(channel => channel.id)
    layouts.value = data[2]
    organization.value = data[3]

    if (reconcile) {
      const allowed = new Map(channels.value.map(channel => [channel.id, channel]))
      slots.value = slots.value.map(channel => (channel ? allowed.get(channel.id) : undefined))
      if (patrol.value) {
        patrol.value = {
          ...patrol.value,
          channelIds: patrol.value.channelIds.map(id => (id !== null && allowed.has(id) ? id : null))
        }
      }
      if (restart) revision.value++
    }
  } catch (e) {
    if (!disposed) error.value = errorMessage(e)
  } finally {
    if (!disposed) {
      loading.value = false
      if (pendingReload) {
        const restartNext = pendingRestart
        pendingReload = false
        pendingRestart = false
        void loadChannels(true, restartNext)
      }
    }
  }
}

function stopPatrol() {
  clearInterval(patrolTimer)
  patrol.value = undefined
}

function setCount(value: LayoutCount) {
  stopPatrol()
  for (let index = value; index < MAX_SLOTS; index++) {
    slots.value[index] = undefined
    slotRanges.value[index] = undefined
    sessions.value[index] = null
  }
  count.value = value
  selected.value = Math.min(selected.value, value - 1)
}

function assign(id: number, index = selected.value) {
  const channel = channels.value.find(item => item.id === id)
  if (!channel) {
    error.value = '此通道不在当前可访问范围内'
    return
  }
  stopPatrol()
  slots.value[index] = channel
  selected.value = index
  recordings.value = []
  slotRanges.value[index] = undefined
}

function handleChannelDoubleClick(channel: Channel) {
  assign(channel.id, selected.value)
}

function drag(event: DragEvent, channel: Channel) {
  event.dataTransfer?.setData('application/x-platform-channel', String(channel.id))
  if (event.dataTransfer) event.dataTransfer.effectAllowed = 'copy'
}

async function favorite(channel: Channel) {
  await run(async () => {
    const ids = favorites.value.includes(channel.id)
      ? favorites.value.filter(id => id !== channel.id)
      : [...favorites.value, channel.id]
    await mediaApi.saveFavorites(ids)
    favorites.value = ids
  }, '')
}

function setTile(index: number, instance: unknown) {
  if (instance) tiles.set(index, instance as TileInstance)
  else tiles.delete(index)
}

function tickPatrol() {
  const scheme = patrol.value
  const ids = scheme ? patrolChannels(scheme.channelIds) : []
  if (!scheme || !ids.length) {
    stopPatrol()
    return
  }
  const batch = patrolBatch(ids, patrolOffset.value, count.value)
  for (let index = 0; index < count.value; index++) {
    slots.value[index] = channels.value.find(channel => channel.id === batch[index])
  }
  patrolOffset.value = (patrolOffset.value + count.value) % ids.length
}

function applyLayout(id?: number) {
  stopPatrol()
  const scheme = layouts.value.find(item => item.id === id)
  if (!scheme) return
  setCount(scheme.layout)
  layoutId.value = id
  if (scheme.kind === 'patrol') {
    patrol.value = scheme
    patrolOffset.value = 0
    tickPatrol()
    patrolTimer = setInterval(tickPatrol, Math.max(10, Math.min(300, scheme.intervalSeconds)) * 1000)
  } else {
    const restored = restoreLayout(scheme.channelIds, count.value, channels.value)
    for (let index = 0; index < count.value; index++) {
      slots.value[index] = restored[index]
    }
  }
}

async function saveLayout() {
  if (
    await run(async () => {
      if (!layoutName.value.trim()) throw new Error('请输入布局名称')
      await mediaApi.saveLayout(null, {
        name: layoutName.value.trim(),
        kind: 'layout',
        shared: false,
        layout: count.value,
        intervalSeconds: 30,
        channelIds: captureLayout(slots.value, count.value)
      })
      layouts.value = await mediaApi.layouts()
    }, '当前布局已保存')
  ) {
    layoutDialog.value = false
  }
}

async function searchRecordings() {
  const current = ++searchGeneration
  const channel = selectedChannel.value
  await run(async () => {
    if (!channel) throw new Error('请选择查询通道')
    const time = timeRange(currentRange.value)
    const found = await mediaApi.recordings(channel.id, time.start, time.end)
    if (current === searchGeneration && selectedChannel.value?.id === channel.id) {
      recordings.value = found
    }
  }, '')
}

async function startPlayback(recording?: Recording) {
  await run(async () => {
    const time = recording ? { start: recording.start, end: recording.end } : timeRange(currentRange.value)
    const indices = unified.value
      ? Array.from({ length: count.value }, (_, index) => index).filter(index => !!slots.value[index])
      : [selected.value]
    if (!indices.some(index => !!slots.value[index])) throw new Error('请选择回放通道')
    for (const index of indices) {
      if (slots.value[index]) slotRanges.value[index] = { ...time }
    }
    await nextTick()
  }, '')
}

async function control(command: PlaybackControl) {
  await run(async () => {
    const targets = unified.value
      ? [...tiles.values()]
      : [tiles.get(selected.value)].filter((tile): tile is TileInstance => !!tile)
    const results = await Promise.allSettled(targets.map(tile => tile.control(command)))
    const failures = results.filter(result => result.status === 'rejected')
    if (failures.length) {
      throw new Error(
        `${failures.length} 路回放控制失败：${failures.map(result => errorMessage(result.reason)).join('；')}`
      )
    }
  }, '')
}

const clipRange = ref<[string, string] | null>(null)

const timelineTracks = computed<TimelineTrack[]>(() => {
  const result: TimelineTrack[] = []
  for (let i = 0; i < count.value; i++) {
    const ch = slots.value[i]
    const s = sessions.value[i] as PlaybackSession | null
    if (ch) {
      result.push({
        channelId: ch.id,
        channelName: ch.alias || ch.name,
        segments:
          s?.segments ||
          (selected.value === i && recordings.value.length
            ? recordings.value.map(r => ({ start: r.start, end: r.end }))
            : []),
        isActive: selected.value === i
      })
    }
  }
  if (result.length === 0 && selectedChannel.value) {
    result.push({
      channelId: selectedChannel.value.id,
      channelName: selectedChannel.value.alias || selectedChannel.value.name,
      segments: recordings.value.map(r => ({ start: r.start, end: r.end })),
      isActive: true
    })
  }
  return result
})

const currentPlaybackTime = computed(() => selectedSession.value?.currentTime || null)

async function onTimelineSeek(isoString: string) {
  await control({ action: 'seek', position: isoString })
}

async function onTimelineJump(seconds: number) {
  const cur = selectedSession.value?.currentTime
  if (!cur) return
  const targetMs = Date.parse(cur) + seconds * 1000
  await control({ action: 'seek', position: new Date(targetMs).toISOString() })
}

async function onTimelineStepFrame() {
  await control({ action: 'step' })
}

function setPresetDate(preset: 'today' | 'yesterday' | '3days' | '1hour') {
  const now = new Date()
  if (preset === 'today') {
    searchDate.value = now
    searchStartTime.value = '00:00:00'
  } else if (preset === 'yesterday') {
    const d = new Date()
    d.setDate(d.getDate() - 1)
    searchDate.value = d
    searchStartTime.value = '00:00:00'
  } else if (preset === '3days') {
    const d = new Date()
    d.setDate(d.getDate() - 2)
    searchDate.value = d
    searchStartTime.value = '00:00:00'
  } else if (preset === '1hour') {
    searchDate.value = now
    const oneHourAgo = new Date(now.getTime() - 3600 * 1000)
    const pad = (n: number) => String(n).padStart(2, '0')
    searchStartTime.value = `${pad(oneHourAgo.getHours())}:${pad(oneHourAgo.getMinutes())}:${pad(oneHourAgo.getSeconds())}`
  }
  void searchRecordings()
}

async function exportClip() {
  if (!selectedChannel.value || !clipRange.value) return
  await run(async () => {
    await workflowApi.createExport(selectedChannel.value!.id, clipRange.value![0], clipRange.value![1])
  }, '片段导出任务已提交，可在录像导出中查看')
}

async function exportSelected() {
  await run(async () => {
    if (!selectedChannel.value) throw new Error('请选择导出通道')
    const time = timeRange(currentRange.value, 24)
    await workflowApi.createExport(selectedChannel.value.id, time.start, time.end)
  }, '导出任务已提交，可在录像导出中查看')
}

async function clearWall() {
  stopPatrol()
  for (const tile of tiles.values()) await tile.stop()
  slots.value = Array(MAX_SLOTS).fill(undefined)
  sessions.value = Array(MAX_SLOTS).fill(null)
  slotRanges.value = Array(MAX_SLOTS).fill(undefined)
}

async function fullScreen() {
  await run(async () => {
    if (document.fullscreenElement) await document.exitFullscreen()
    else await wall.value?.requestFullscreen()
  }, '')
}

const unsubscribers = [
  useEvents().subscribe(['device.changed'], () => loadChannels(true)),
  useEvents().subscribe(['access.changed', 'reconnected'], () => loadChannels(true, true))
]

onMounted(async () => {
  await loadChannels()
  if (disposed) return
  if (Number(route.query.channelId)) assign(Number(route.query.channelId), 0)
  if (Number(route.query.layoutId)) applyLayout(Number(route.query.layoutId))
})

onBeforeUnmount(() => {
  disposed = true
  searchGeneration++
  stopPatrol()
  unsubscribers.forEach(unsubscribe => unsubscribe())
})
</script>

<template>
  <div class="monitor-page">
    <PageHeader :title="mode === 'live' ? '实时预览' : '录像回放'">
      <!-- 分屏选择快捷组 -->
      <div class="split-selector">
        <button
          v-for="opt in layoutOptions"
          :key="opt.value"
          :class="['split-btn', { active: count === opt.value }]"
          :title="`${opt.label} 分屏`"
          @click="setCount(opt.value)"
        >
          {{ opt.label }}
        </button>
      </div>

      <el-tooltip content="清空当前视频墙">
        <el-button :icon="Close" aria-label="清空视频墙" @click="clearWall" />
      </el-tooltip>
      <el-tooltip content="全屏展示视频墙">
        <el-button :icon="FullScreen" aria-label="视频墙全屏" @click="fullScreen" />
      </el-tooltip>
    </PageHeader>

    <el-alert v-if="error" class="page-alert" :title="error" type="error" :closable="false" show-icon style="margin-bottom: 12px;" />

    <div class="monitor-workspace">
      <!-- 左侧通道选择浏览器与云台控制 -->
      <aside class="channel-browser">
        <div class="channel-browser-head">
          <h2>
            <el-icon><VideoCamera /></el-icon>
            <span>视频通道</span>
            <span class="count-badge">{{ filtered.length }} / {{ channels.length }}</span>
          </h2>
          <el-tooltip content="刷新通道列表">
            <el-button text :icon="Refresh" :loading="loading" aria-label="刷新通道" @click="loadChannels(true)" />
          </el-tooltip>
        </div>

        <div class="channel-browser-filters">
          <el-input
            v-model="search"
            clearable
            :prefix-icon="Search"
            placeholder="搜索通道、别名或设备..."
            aria-label="搜索视频通道"
            size="small"
          />
          <div class="tree-quick-options">
            <el-checkbox v-model="onlyOnline" size="small">仅在线</el-checkbox>
            <el-checkbox v-model="onlyFavorites" size="small">仅收藏</el-checkbox>
            <el-button text size="small" style="padding: 2px 4px; font-size: 11px;" @click="treeAllOpen = !treeAllOpen">
              <el-icon style="margin-right: 2px;"><FolderOpened v-if="treeAllOpen" /><Folder v-else /></el-icon>
              {{ treeAllOpen ? '折叠全部' : '展开全部' }}
            </el-button>
          </div>
        </div>

        <!-- 组织通道树 -->
        <div v-loading="loading" class="channel-tree">
          <el-empty v-if="!filtered.length && !loading" description="未匹配到通道" :image-size="44" />

          <!-- 车间 / 区域 / 单元 树结构 -->
          <details
            v-for="ws in orgTree.workshops"
            :key="`ws-${ws.id}`"
            :open="treeAllOpen"
            class="tree-node tree-workshop"
          >
            <summary>
              <span style="display: flex; align-items: center; gap: 6px;">
                <el-icon class="blue"><Folder /></el-icon>
                <span>{{ ws.name }}</span>
              </span>
              <span class="tree-badge">{{ ws.channelCount }}</span>
            </summary>

            <div class="tree-branch">
              <details
                v-for="area in ws.areas"
                :key="`area-${area.id}`"
                :open="treeAllOpen"
                class="tree-node tree-area"
              >
                <summary>
                  <span style="display: flex; align-items: center; gap: 6px;">
                    <el-icon class="teal"><FolderOpened /></el-icon>
                    <span>{{ area.name }}</span>
                  </span>
                  <span class="tree-badge">{{ area.channelCount }}</span>
                </summary>

                <div class="tree-branch">
                  <details
                    v-for="unit in area.units"
                    :key="`unit-${unit.id}`"
                    :open="treeAllOpen"
                    class="tree-node tree-unit"
                  >
                    <summary>
                      <span>{{ unit.name }}</span>
                      <span class="tree-badge">{{ unit.channels.length }}</span>
                    </summary>

                    <div style="padding-left: 6px; margin-top: 2px;">
                      <div
                        v-for="channel in unit.channels"
                        :key="channel.id"
                        :class="['channel-entry', { active: selectedChannel?.id === channel.id }]"
                        draggable="true"
                        :title="`双击直接播放\n${channel.alias || channel.name} (${channel.deviceName} 通道 ${channel.deviceChannel})`"
                        @dragstart="drag($event, channel)"
                        @dblclick="handleChannelDoubleClick(channel)"
                      >
                        <button class="channel-open" @click="assign(channel.id)">
                          <span :class="['channel-dot', channel.status]" />
                          <span class="channel-name-text">{{ channel.alias || channel.name }}</span>
                          <span v-if="channel.ptzCapable" style="font-size: 10px; color: var(--teal); background: #e6fffa; padding: 1px 4px; border-radius: 2px; margin-left: 4px;">PTZ</span>
                        </button>
                        <el-tooltip :content="favorites.includes(channel.id) ? '取消收藏' : '收藏此通道'">
                          <button
                            :class="['channel-favorite-btn', { favorited: favorites.includes(channel.id) }]"
                            :disabled="busy"
                            aria-label="收藏通道"
                            @click="favorite(channel)"
                          >
                            <el-icon><StarFilled v-if="favorites.includes(channel.id)" /><Star v-else /></el-icon>
                          </button>
                        </el-tooltip>
                      </div>
                    </div>
                  </details>
                </div>
              </details>
            </div>
          </details>

          <!-- 未分配组织通道 -->
          <details
            v-if="orgTree.unassigned.length"
            :open="treeAllOpen"
            class="tree-node tree-unassigned"
          >
            <summary>
              <span style="display: flex; align-items: center; gap: 6px;">
                <el-icon class="amber"><Folder /></el-icon>
                <span>未分配组织</span>
              </span>
              <span class="tree-badge">{{ orgTree.unassigned.length }}</span>
            </summary>
            <div style="padding-left: 10px;">
              <div
                v-for="channel in orgTree.unassigned"
                :key="channel.id"
                :class="['channel-entry', { active: selectedChannel?.id === channel.id }]"
                draggable="true"
                :title="`双击直接播放\n${channel.alias || channel.name} (${channel.deviceName} 通道 ${channel.deviceChannel})`"
                @dragstart="drag($event, channel)"
                @dblclick="handleChannelDoubleClick(channel)"
              >
                <button class="channel-open" @click="assign(channel.id)">
                  <span :class="['channel-dot', channel.status]" />
                  <span class="channel-name-text">{{ channel.alias || channel.name }}</span>
                  <span v-if="channel.ptzCapable" style="font-size: 10px; color: var(--teal); background: #e6fffa; padding: 1px 4px; border-radius: 2px; margin-left: 4px;">PTZ</span>
                </button>
                <el-tooltip :content="favorites.includes(channel.id) ? '取消收藏' : '收藏此通道'">
                  <button
                    :class="['channel-favorite-btn', { favorited: favorites.includes(channel.id) }]"
                    :disabled="busy"
                    aria-label="收藏通道"
                    @click="favorite(channel)"
                  >
                    <el-icon><StarFilled v-if="favorites.includes(channel.id)" /><Star v-else /></el-icon>
                  </button>
                </el-tooltip>
              </div>
            </div>
          </details>
        </div>

        <!-- 云台控制面板 (实时模式下展开) -->
        <PtzPanel v-if="mode === 'live'" :channel="selectedChannel" :allowed="auth.can('ptz.control')" />
      </aside>

      <!-- 右侧视频墙与控制区 -->
      <section class="video-workspace">
        <!-- 视频墙顶部控制栏 -->
        <div class="media-toolbar">
          <template v-if="mode === 'live'">
            <el-select
              v-model="layoutId"
              clearable
              placeholder="选择布局或轮巡方案..."
              style="width: 220px;"
              size="default"
              aria-label="布局与轮巡方案"
              @change="applyLayout"
            >
              <el-option
                v-for="layout in layouts"
                :key="layout.id"
                :value="layout.id"
                :label="`${layout.name}${layout.kind === 'patrol' ? ' · 自动轮巡' : ''}`"
              />
            </el-select>

            <el-tooltip content="将当前画面保存为自定义布局方案">
              <el-button :icon="Check" @click="layoutName = ''; layoutDialog = true">保存当前画面</el-button>
            </el-tooltip>

            <el-button v-if="patrol" type="warning" :icon="VideoPause" @click="stopPatrol">
              停止轮巡 ({{ patrol.name }} · {{ patrol.intervalSeconds }}s)
            </el-button>

            <!-- 码流切换 -->
            <div style="margin-left: auto; display: flex; align-items: center; gap: 8px;">
              <span class="muted" style="font-size: 12px;">清晰度：</span>
              <el-radio-group v-model="stream" size="small">
                <el-radio-button :value="2">子码流 (流畅)</el-radio-button>
                <el-radio-button :value="1">主码流 (超清)</el-radio-button>
              </el-radio-group>
            </div>
          </template>

          <template v-else>
            <!-- 回放控制栏 -->
            <el-date-picker
              v-model="searchDate"
              type="date"
              placeholder="选择回放日期"
              :clearable="false"
              format="YYYY-MM-DD"
              style="width: 140px;"
            />
            <el-time-picker
              v-model="searchStartTime"
              placeholder="开始时间"
              :clearable="false"
              format="HH:mm:ss"
              value-format="HH:mm:ss"
              style="width: 120px;"
            />

            <el-button-group>
              <el-button size="small" @click="setPresetDate('1hour')">近1小时</el-button>
              <el-button size="small" @click="setPresetDate('today')">今天</el-button>
              <el-button size="small" @click="setPresetDate('yesterday')">昨天</el-button>
              <el-button size="small" @click="setPresetDate('3days')">近3天</el-button>
            </el-button-group>

            <el-button :icon="Search" :loading="busy" @click="searchRecordings">检索录像</el-button>
            <el-button type="primary" :icon="VideoPlay" :disabled="!selectedChannel || busy" @click="startPlayback()">
              开始回放
            </el-button>

            <div style="margin-left: auto; display: flex; align-items: center; gap: 8px;">
              <el-tooltip v-if="canExport" content="将所选时段提交为完整导出任务">
                <el-button :icon="Download" :disabled="!selectedChannel || busy" @click="exportSelected">
                  整段导出
                </el-button>
              </el-tooltip>
              <el-button v-if="clipRange && canExport" type="warning" :icon="Download" @click="exportClip">
                导出框选片段
              </el-button>
            </div>
          </template>
        </div>

        <!-- 视频墙网格 -->
        <div ref="wall" class="video-wall" :style="{ '--columns': layoutColumns(count) }">
          <VideoTile
            v-for="index in count"
            :key="`${revision}:${index}`"
            :ref="instance => setTile(index - 1, instance)"
            :index="index - 1"
            :channel="slots[index - 1]"
            :mode="mode"
            :stream-type="streams[index - 1]"
            :range="slotRanges[index - 1]"
            :selected="selected === index - 1"
            @select="selected = index - 1; recordings = []"
            @close="slots[index - 1] = undefined; slotRanges[index - 1] = undefined; stopPatrol()"
            @drop-channel="assign($event, index - 1)"
            @changed="sessions[index - 1] = $event"
          />
        </div>

        <!-- 录像回放时间轴与播放控制 -->
        <template v-if="mode === 'playback'">
          <div class="playback-control-bar">
            <el-switch v-model="unified" active-text="多窗口联动控制" inactive-text="仅当前聚焦窗口" />

            <div style="display: flex; align-items: center; gap: 6px; margin: 0 12px;">
              <el-tooltip content="后退 10 秒">
                <el-button :icon="RefreshLeft" :disabled="busy" @click="onTimelineJump(-10)" />
              </el-tooltip>
              <el-tooltip content="暂停回放">
                <el-button :icon="VideoPause" :disabled="busy" @click="control({ action: 'pause' })" />
              </el-tooltip>
              <el-tooltip content="继续回放">
                <el-button type="primary" :icon="VideoPlay" :disabled="busy" @click="control({ action: 'resume' })" />
              </el-tooltip>
              <el-tooltip content="前进 10 秒">
                <el-button :icon="RefreshRight" :disabled="busy" @click="onTimelineJump(10)" />
              </el-tooltip>
              <el-tooltip content="单帧逐帧步进">
                <el-button :icon="DArrowRight" :disabled="busy" @click="onTimelineStepFrame" />
              </el-tooltip>
            </div>

            <div style="display: flex; align-items: center; gap: 8px;">
              <span class="muted" style="font-size: 12px;">倍速：</span>
              <el-select
                v-model="speed"
                style="width: 110px;"
                size="small"
                aria-label="回放倍速"
                @change="control({ action: 'speed', speed })"
              >
                <el-option v-for="value in [0.25, 0.5, 1, 2, 4, 8]" :key="value" :value="value" :label="`${value}x 倍速`" />
              </el-select>
            </div>

            <div style="margin-left: auto; display: flex; align-items: center; gap: 10px;">
              <StatusBadge v-if="selectedSession" :value="selectedSession.state" />
              <strong style="font-size: 13px; color: var(--text-primary);">
                {{ selectedChannel ? (selectedChannel.alias || selectedChannel.name) : '未选中任何通道' }}
              </strong>
            </div>
          </div>

          <!-- 多轨可视化时间轴 -->
          <div style="margin-top: 10px; background: #ffffff; border: 1px solid var(--border); border-radius: var(--radius-lg); padding: 12px 16px; box-shadow: var(--shadow-card);">
            <PlaybackTimeline
              :tracks="timelineTracks"
              :current-time="currentPlaybackTime"
              :clip-range="clipRange"
              :base-date="searchDate"
              @seek="onTimelineSeek"
              @jump="onTimelineJump"
              @step-frame="onTimelineStepFrame"
              @clip-change="clipRange = $event"
            />
          </div>

          <!-- 录像分段检索结果表格卡片 -->
          <section class="table-card" style="margin-top: 14px;">
            <div style="padding: 12px 18px; display: flex; align-items: center; justify-content: space-between; border-bottom: 1px solid var(--border-light);">
              <h2 style="margin: 0; font-size: 14px;">录像切片明细</h2>
              <span class="muted" style="font-size: 12px;">共检出 {{ recordings.length }} 个存储切片</span>
            </div>

            <el-table :data="recordings" max-height="240" empty-text="未查询到任何录像记录">
              <el-table-column label="开始时间" min-width="170">
                <template #default="{ row }">{{ dateTime(row.start) }}</template>
              </el-table-column>
              <el-table-column label="结束时间" min-width="170">
                <template #default="{ row }">{{ dateTime(row.end) }}</template>
              </el-table-column>
              <el-table-column label="文件大小" width="110">
                <template #default="{ row }">{{ bytes(row.fileSize) }}</template>
              </el-table-column>
              <el-table-column label="码流" width="90">
                <template #default="{ row }">
                  <el-tag size="small" :type="row.streamType === 1 ? 'primary' : 'info'">
                    {{ row.streamType === 1 ? '主码流' : '子码流' }}
                  </el-tag>
                </template>
              </el-table-column>
              <el-table-column label="操作" width="90" fixed="right">
                <template #default="{ row }">
                  <el-button link type="primary" :icon="VideoPlay" @click="startPlayback(row as Recording)">
                    定位回放
                  </el-button>
                </template>
              </el-table-column>
            </el-table>
          </section>
        </template>
      </section>
    </div>

    <!-- 保存当前布局弹窗 -->
    <el-dialog v-model="layoutDialog" title="保存为自定义布局方案" width="440px">
      <el-form label-position="top">
        <el-form-item label="布局方案名称" required>
          <el-input v-model="layoutName" maxlength="100" placeholder="例如：车间主通道监控" clearable />
        </el-form-item>
        <p class="muted" style="font-size: 12px; margin: 0;">
          保存后将记录当前选中的 {{ count }} 分屏窗口以及绑定的通道信息，可在任意终端一键还原。
        </p>
      </el-form>
      <template #footer>
        <el-button @click="layoutDialog = false">取消</el-button>
        <el-button type="primary" :loading="busy" @click="saveLayout">确认保存</el-button>
      </template>
    </el-dialog>
  </div>
</template>
