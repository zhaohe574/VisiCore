<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, onMounted, ref } from 'vue'
import { useRoute } from 'vue-router'
import { Check, Close, Download, FullScreen, Refresh, Search, Star, StarFilled, VideoPause, VideoPlay } from '@element-plus/icons-vue'
import { allPages, managementApi, mediaApi, workflowApi, type Channel, type Layout, type LiveSession, type Organization, type PlaybackControl, type PlaybackSession, type Recording } from '../api'
import { useAuth } from '../stores/auth'
import { useEvents } from '../stores/events'
import { useAction } from '../composables/useAction'
import { bytes, dateTime, errorMessage, timeRange } from '../lib/format'
import { patrolBatch } from '../lib/media'
import { captureLayout, patrolChannels, restoreLayout } from '../lib/layouts'
import PageHeader from '../components/PageHeader.vue'
import StatusBadge from '../components/StatusBadge.vue'
import VideoTile from '../components/VideoTile.vue'
import PtzPanel from '../components/PtzPanel.vue'
const route = useRoute(), auth = useAuth(), { busy, run } = useAction()
const mode = computed<'live' | 'playback'>(() => route.path.endsWith('playback') ? 'playback' : 'live')
const channels = ref<Channel[]>([]), favorites = ref<number[]>([]), layouts = ref<Layout[]>([]), organization = ref<Organization>({ workshops: [], areas: [], units: [] }), search = ref(''), onlyFavorites = ref(false), loading = ref(false), error = ref('')
const count = ref<1 | 4 | 9 | 16>(4), selected = ref(0), slots = ref<(Channel | undefined)[]>(Array(16).fill(undefined)), streams = ref<(1 | 2)[]>(Array(16).fill(2)), slotRanges = ref<({ start: string; end: string } | undefined)[]>(Array(16).fill(undefined)), sessions = ref<(LiveSession | PlaybackSession | null)[]>(Array(16).fill(null)), revision = ref(0)
type TileInstance = InstanceType<typeof VideoTile>
const tiles = new Map<number, TileInstance>(), wall = ref<HTMLElement>(), range = ref<[Date, Date] | null>([new Date(Date.now() - 3600000), new Date()]), recordings = ref<Recording[]>([]), unified = ref(false), speed = ref(1), layoutId = ref<number>(), patrol = ref<Layout>(), patrolOffset = ref(0), layoutDialog = ref(false), layoutName = ref('')
let patrolTimer: ReturnType<typeof setInterval> | undefined, disposed = false, searchGeneration = 0, pendingReload = false, pendingRestart = false
const filtered = computed(() => channels.value.filter(channel => (!onlyFavorites.value || favorites.value.includes(channel.id)) && `${channel.alias || ''} ${channel.name} ${channel.deviceName} ${channel.deviceChannel}`.toLowerCase().includes(search.value.toLowerCase())))
interface TreeUnit { id: number; name: string; channels: Channel[] }
interface TreeArea { id: number; name: string; units: TreeUnit[]; channelCount: number }
interface TreeWorkshop { id: number; name: string; areas: TreeArea[]; channelCount: number }
const orgTree = computed(() => {
  const channelList = filtered.value, unitMap = new Map<number, Channel[]>(), unassigned: Channel[] = []
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
    if (treeAreas.length > 0) workshops.push({ id: wsId, name: ws.name, areas: treeAreas, channelCount: wsChannelCount })
  }
  return { workshops, unassigned }
})
const selectedChannel = computed(() => slots.value[selected.value])
const selectedSession = computed(() => sessions.value[selected.value] as PlaybackSession | null)
const canExport = computed(() => auth.can('export.create'))
const stream = computed({ get: () => streams.value[selected.value], set: (value: 1 | 2) => { streams.value[selected.value] = value } })
async function loadChannels(reconcile = false, restart = false) {
  if (loading.value) { pendingReload = true; pendingRestart ||= restart; return }
  loading.value = true; error.value = ''
  try {
    const data = await Promise.all([allPages(managementApi.channels), mediaApi.favorites(), mediaApi.layouts(), managementApi.organization()])
    if (disposed) return
    channels.value = data[0]; favorites.value = data[1].map(channel => channel.id); layouts.value = data[2]; organization.value = data[3]
    if (reconcile) {
      const allowed = new Map(channels.value.map(channel => [channel.id, channel]))
      slots.value = slots.value.map(channel => channel ? allowed.get(channel.id) : undefined)
      if (patrol.value) patrol.value = { ...patrol.value, channelIds: patrol.value.channelIds.map(id => id !== null && allowed.has(id) ? id : null) }
      if (restart) revision.value++
    }
  } catch (e) { if (!disposed) error.value = errorMessage(e) } finally { if (!disposed) { loading.value = false; if (pendingReload) { const restartNext = pendingRestart; pendingReload = false; pendingRestart = false; void loadChannels(true, restartNext) } } }
}
function stopPatrol() { clearInterval(patrolTimer); patrol.value = undefined }
function setCount(value: 1 | 4 | 9 | 16) { stopPatrol(); for (let index = value; index < 16; index++) { slots.value[index] = undefined; slotRanges.value[index] = undefined; sessions.value[index] = null }; count.value = value; selected.value = Math.min(selected.value, value - 1) }
function assign(id: number, index = selected.value) { const channel = channels.value.find(item => item.id === id); if (!channel) { error.value = '此通道不在当前可访问范围内'; return }; stopPatrol(); slots.value[index] = channel; selected.value = index; recordings.value = []; slotRanges.value[index] = undefined }
function drag(event: DragEvent, channel: Channel) { event.dataTransfer?.setData('application/x-platform-channel', String(channel.id)); if (event.dataTransfer) event.dataTransfer.effectAllowed = 'copy' }
async function favorite(channel: Channel) { await run(async () => { const ids = favorites.value.includes(channel.id) ? favorites.value.filter(id => id !== channel.id) : [...favorites.value, channel.id]; await mediaApi.saveFavorites(ids); favorites.value = ids }, '') }
function setTile(index: number, instance: unknown) { if (instance) tiles.set(index, instance as TileInstance); else tiles.delete(index) }
function tickPatrol() {
  const scheme = patrol.value
  const ids = scheme ? patrolChannels(scheme.channelIds) : []
  if (!scheme || !ids.length) { stopPatrol(); return }
  const batch = patrolBatch(ids, patrolOffset.value, count.value)
  for (let index = 0; index < count.value; index++) slots.value[index] = channels.value.find(channel => channel.id === batch[index])
  patrolOffset.value = (patrolOffset.value + count.value) % ids.length
}
function applyLayout(id?: number) {
  stopPatrol()
  const scheme = layouts.value.find(item => item.id === id)
  if (!scheme) return
  setCount(scheme.layout); layoutId.value = id
  if (scheme.kind === 'patrol') { patrol.value = scheme; patrolOffset.value = 0; tickPatrol(); patrolTimer = setInterval(tickPatrol, Math.max(10, Math.min(300, scheme.intervalSeconds)) * 1000) }
  else { const restored = restoreLayout(scheme.channelIds, count.value, channels.value); for (let index = 0; index < count.value; index++) slots.value[index] = restored[index] }
}
async function saveLayout() {
  if (await run(async () => { if (!layoutName.value.trim()) throw new Error('请输入布局名称'); await mediaApi.saveLayout(null, { name: layoutName.value.trim(), kind: 'layout', shared: false, layout: count.value, intervalSeconds: 30, channelIds: captureLayout(slots.value, count.value) }); layouts.value = await mediaApi.layouts() }, '当前布局已保存')) layoutDialog.value = false
}
async function searchRecordings() {
  const current = ++searchGeneration, channel = selectedChannel.value
  await run(async () => { if (!channel) throw new Error('请选择查询通道'); const time = timeRange(range.value); const found = await mediaApi.recordings(channel.id, time.start, time.end); if (current === searchGeneration && selectedChannel.value?.id === channel.id) recordings.value = found }, '')
}
async function startPlayback(recording?: Recording) {
  await run(async () => { const time = recording ? { start: recording.start, end: recording.end } : timeRange(range.value); const indices = unified.value ? Array.from({ length: count.value }, (_, index) => index).filter(index => !!slots.value[index]) : [selected.value]; if (!indices.some(index => !!slots.value[index])) throw new Error('请选择回放通道'); for (const index of indices) if (slots.value[index]) slotRanges.value[index] = { ...time }; await nextTick() }, '')
}
async function control(command: PlaybackControl) {
  await run(async () => {
    const targets = unified.value ? [...tiles.values()] : [tiles.get(selected.value)].filter((tile): tile is TileInstance => !!tile)
    const results = await Promise.allSettled(targets.map(tile => tile.control(command)))
    const failures = results.filter(result => result.status === 'rejected')
    if (failures.length) throw new Error(`${failures.length} 路回放控制失败：${failures.map(result => errorMessage(result.reason)).join('；')}`)
  }, '')
}
async function seekAll(value: number | number[]) {
  if (typeof value !== 'number') return
  // 使用当前 session 的实际录像范围计算绝对时间，而非 DatePicker 选择的全天范围；
  // 否则定位时间可能落在录像段范围之外，后端会拒绝 (ArgumentException: 定位时间不在回放范围内)。
  const session = selectedSession.value
  if (!session?.start || !session?.end) return
  const startMs = Date.parse(session.start), endMs = Date.parse(session.end)
  await control({ action: 'seek', position: new Date(startMs + (endMs - startMs) * value / 100).toISOString() })
}

async function exportSelected() { await run(async () => { if (!selectedChannel.value) throw new Error('请选择导出通道'); const time = timeRange(range.value, 24); await workflowApi.createExport(selectedChannel.value.id, time.start, time.end) }, '导出任务已提交，可在录像导出中查看') }
async function clearWall() { stopPatrol(); for (const tile of tiles.values()) await tile.stop(); slots.value = Array(16).fill(undefined); sessions.value = Array(16).fill(null); slotRanges.value = Array(16).fill(undefined) }
async function fullScreen() { await run(async () => { if (document.fullscreenElement) await document.exitFullscreen(); else await wall.value?.requestFullscreen() }, '') }
const unsubscribers = [useEvents().subscribe(['device.changed'], () => loadChannels(true)), useEvents().subscribe(['access.changed', 'reconnected'], () => loadChannels(true, true))]
onMounted(async () => { await loadChannels(); if (disposed) return; if (Number(route.query.channelId)) assign(Number(route.query.channelId), 0); if (Number(route.query.layoutId)) applyLayout(Number(route.query.layoutId)) })
onBeforeUnmount(() => { disposed = true; searchGeneration++; stopPatrol(); unsubscribers.forEach(unsubscribe => unsubscribe()) })
</script>
<template><div class="monitor-page"><PageHeader :title="mode === 'live' ? '实时预览' : '录像回放'"><el-segmented :model-value="count" :options="[1, 4, 9, 16]" aria-label="视频分屏数量" @change="setCount($event as 1 | 4 | 9 | 16)" /><el-tooltip content="清空视频墙"><el-button :icon="Close" aria-label="清空视频墙" @click="clearWall" /></el-tooltip><el-tooltip content="视频墙全屏"><el-button :icon="FullScreen" aria-label="视频墙全屏" @click="fullScreen" /></el-tooltip></PageHeader><el-alert v-if="error" class="page-alert" :title="error" type="error" :closable="false" show-icon />
<div class="monitor-workspace"><aside class="channel-browser"><div class="channel-browser-head"><h2>视频通道 <span>{{ channels.length }}</span></h2><el-tooltip content="刷新通道"><el-button text :icon="Refresh" :loading="loading" aria-label="刷新通道" @click="loadChannels(true)" /></el-tooltip></div><el-input v-model="search" clearable :prefix-icon="Search" placeholder="搜索通道、别名或设备" aria-label="搜索视频通道" /><el-checkbox v-model="onlyFavorites" class="favorites-filter">仅显示收藏</el-checkbox><div v-loading="loading" class="channel-tree"><el-empty v-if="!filtered.length && !loading" description="暂无可用通道" :image-size="50" /><details v-for="ws in orgTree.workshops" :key="`ws-${ws.id}`" open class="tree-node tree-workshop"><summary>{{ ws.name }}<span>{{ ws.channelCount }}</span></summary><div class="tree-branch"><details v-for="area in ws.areas" :key="`area-${area.id}`" open class="tree-node tree-area"><summary>{{ area.name }}<span>{{ area.channelCount }}</span></summary><div class="tree-branch"><details v-for="unit in area.units" :key="`unit-${unit.id}`" open class="tree-node tree-unit"><summary>{{ unit.name }}<span>{{ unit.channels.length }}</span></summary><div class="tree-leaf"><div v-for="channel in unit.channels" :key="channel.id" :class="['channel-entry', { active: selectedChannel?.id === channel.id }]" draggable="true" @dragstart="drag($event, channel)"><button class="channel-open" :title="`${channel.alias ? channel.alias + ' (' + channel.name + ')' : channel.name} · ${channel.deviceName} · 通道 ${channel.deviceChannel}`" @click="assign(channel.id)"><span :class="['channel-dot', channel.status]" /><span>{{ channel.alias || channel.name }}</span><small>{{ channel.deviceChannel }}</small></button><el-tooltip :content="favorites.includes(channel.id) ? '取消收藏' : '收藏通道'"><button class="channel-favorite" :aria-label="favorites.includes(channel.id) ? '取消收藏' : '收藏通道'" :disabled="busy" @click="favorite(channel)"><el-icon><StarFilled v-if="favorites.includes(channel.id)" /><Star v-else /></el-icon></button></el-tooltip></div></div></details></div></details></div></details><details v-if="orgTree.unassigned.length" open class="tree-node tree-unassigned"><summary>未分配组织<span>{{ orgTree.unassigned.length }}</span></summary><div class="tree-leaf"><div v-for="channel in orgTree.unassigned" :key="channel.id" :class="['channel-entry', { active: selectedChannel?.id === channel.id }]" draggable="true" @dragstart="drag($event, channel)"><button class="channel-open" :title="`${channel.alias ? channel.alias + ' (' + channel.name + ')' : channel.name} · ${channel.deviceName} · 通道 ${channel.deviceChannel}`" @click="assign(channel.id)"><span :class="['channel-dot', channel.status]" /><span>{{ channel.alias || channel.name }}</span><small>{{ channel.deviceChannel }}</small></button><el-tooltip :content="favorites.includes(channel.id) ? '取消收藏' : '收藏通道'"><button class="channel-favorite" :aria-label="favorites.includes(channel.id) ? '取消收藏' : '收藏通道'" :disabled="busy" @click="favorite(channel)"><el-icon><StarFilled v-if="favorites.includes(channel.id)" /><Star v-else /></el-icon></button></el-tooltip></div></div></details></div><PtzPanel v-if="mode === 'live'" :channel="selectedChannel" :allowed="auth.can('ptz.control')" /></aside>
<section class="video-workspace"><div class="media-toolbar"><template v-if="mode === 'live'"><el-select v-model="layoutId" clearable placeholder="选择布局或轮巡方案" aria-label="布局与轮巡方案" @change="applyLayout"><el-option v-for="layout in layouts" :key="layout.id" :value="layout.id" :label="`${layout.name}${layout.kind === 'patrol' ? ' · 轮巡' : ''}`" /></el-select><el-tooltip content="保存当前布局"><el-button :icon="Check" aria-label="保存当前布局" @click="layoutName = ''; layoutDialog = true" /></el-tooltip><el-button v-if="patrol" :icon="VideoPause" @click="stopPatrol">停止轮巡</el-button><span v-if="patrol" class="patrol-label">{{ patrol.name }} · {{ patrol.intervalSeconds }} 秒</span><el-radio-group v-model="stream" size="small" class="stream-switch"><el-radio-button :value="2">子码流</el-radio-button><el-radio-button :value="1">主码流</el-radio-button></el-radio-group></template><template v-else><el-date-picker v-model="range" type="datetimerange" start-placeholder="开始时间" end-placeholder="结束时间" format="YYYY-MM-DD HH:mm:ss" /><el-button :icon="Search" :loading="busy" @click="searchRecordings">查询录像</el-button><el-button type="primary" :icon="VideoPlay" :disabled="!selectedChannel || busy" @click="startPlayback()">回放</el-button><el-tooltip v-if="canExport" content="导出所选时段"><el-button :icon="Download" :disabled="!selectedChannel || busy" aria-label="导出所选时段" @click="exportSelected" /></el-tooltip></template></div>
<div ref="wall" class="video-wall" :style="{ '--columns': Math.sqrt(count) }"><VideoTile v-for="index in count" :key="`${revision}:${index}`" :ref="instance => setTile(index - 1, instance)" :index="index - 1" :channel="slots[index - 1]" :mode="mode" :stream-type="streams[index - 1]" :range="slotRanges[index - 1]" :selected="selected === index - 1" @select="selected = index - 1; recordings = []" @close="slots[index - 1] = undefined; slotRanges[index - 1] = undefined; stopPatrol()" @drop-channel="assign($event, index - 1)" @changed="sessions[index - 1] = $event" /></div>
<template v-if="mode === 'playback'"><div class="playback-controls"><el-switch v-model="unified" active-text="统一控制" inactive-text="当前窗口" /><el-tooltip content="暂停回放"><el-button :icon="VideoPause" :disabled="busy" aria-label="暂停回放" @click="control({ action: 'pause' })" /></el-tooltip><el-tooltip content="继续回放"><el-button :icon="VideoPlay" :disabled="busy" aria-label="继续回放" @click="control({ action: 'resume' })" /></el-tooltip><el-select v-model="speed" class="speed-select" aria-label="回放倍速" @change="control({ action: 'speed', speed })"><el-option v-for="value in [0.25, 0.5, 1, 2, 4, 8]" :key="value" :value="value" :label="`${value} 倍速`" /></el-select><StatusBadge v-if="selectedSession" :value="selectedSession.state" /><span class="muted">{{ selectedChannel ? (selectedChannel.alias || selectedChannel.name) : '未选择通道' }}</span></div><div v-if="unified" class="unified-timeline"><el-slider :model-value="selectedSession?.progress || 0" :show-tooltip="false" aria-label="统一定位录像" @change="seekAll" /><div><span>{{ range ? dateTime(range[0].toISOString()) : '—' }}</span><span>{{ range ? dateTime(range[1].toISOString()) : '—' }}</span></div></div><section class="recordings-section"><div class="section-heading"><h2>录像检索</h2><span class="muted">{{ recordings.length }} 段</span></div><el-table :data="recordings" max-height="260" empty-text="暂无检索结果"><el-table-column label="开始时间" min-width="170"><template #default="{ row }">{{ dateTime(row.start) }}</template></el-table-column><el-table-column label="结束时间" min-width="170"><template #default="{ row }">{{ dateTime(row.end) }}</template></el-table-column><el-table-column label="大小" width="110"><template #default="{ row }">{{ bytes(row.fileSize) }}</template></el-table-column><el-table-column label="码流" width="90"><template #default="{ row }">{{ row.streamType === 1 ? '主码流' : '子码流' }}</template></el-table-column><el-table-column label="操作" width="85" fixed="right"><template #default="{ row }"><el-button link type="primary" :icon="VideoPlay" @click="startPlayback(row as Recording)">回放</el-button></template></el-table-column></el-table></section></template></section></div><el-dialog v-model="layoutDialog" title="保存当前布局" width="420px"><el-form label-position="top"><el-form-item label="布局名称" required><el-input v-model="layoutName" maxlength="100" /></el-form-item></el-form><template #footer><el-button @click="layoutDialog = false">取消</el-button><el-button type="primary" :loading="busy" @click="saveLayout">保存</el-button></template></el-dialog></div></template>
