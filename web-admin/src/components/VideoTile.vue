<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, onMounted, ref, shallowRef, watch } from 'vue'
import { Camera, Close, FullScreen, Microphone, Mute, Refresh, VideoCamera, VideoPause, VideoPlay } from '@element-plus/icons-vue'
import { ApiError, type Channel, type LiveSession, type PlaybackControl, type PlaybackSession } from '../api'
import { MediaLease } from '../lib/mediaLease'
import { channelOnline, timelineSegments } from '../lib/media'
import { dateTime, errorMessage } from '../lib/format'
import { ElMessage } from 'element-plus'
import type Mpegts from 'mpegts.js'
const props = defineProps<{ index: number; channel?: Channel; mode: 'live' | 'playback'; streamType: 1 | 2; range?: { start: string; end: string }; selected: boolean }>()
const emit = defineEmits<{ select: []; close: []; dropChannel: [id: number]; changed: [session: LiveSession | PlaybackSession | null] }>()
const root = ref<HTMLElement>(), video = ref<HTMLVideoElement>(), session = shallowRef<LiveSession | PlaybackSession | null>(null), error = ref(''), busy = ref(false), muted = ref(true), ready = ref(false), localPaused = ref(false), retryCount = ref(0)
let player: ReturnType<typeof Mpegts.createPlayer> | undefined, playerGeneration = 0, generation = 0, disposed = false, renewAt = Date.now(), retryTimer: ReturnType<typeof setTimeout> | undefined, timer: ReturnType<typeof setInterval> | undefined
const lease = new MediaLease(value => { session.value = value; emit('changed', value) })
const playback = computed(() => props.mode === 'playback' ? session.value as PlaybackSession | null : null)
const segments = computed(() => playback.value ? timelineSegments(playback.value.segments || [], playback.value.start, playback.value.end) : [])
const statusText = computed(() => ({ starting: '连接中', paused: '已暂停', gap: '当前时间没有录像', completed: '回放已结束', failed: '媒体会话失败', stopped: '已停止' }[session.value?.state || ''] || ''))
const blockingStatus = computed(() => ['starting', 'gap', 'completed', 'failed', 'stopped'].includes(session.value?.state || ''))

function destroyPlayer() {
  playerGeneration++; ready.value = false
  if (player) { try { player.pause(); player.unload(); player.detachMediaElement(); player.destroy() } catch { /* 断线后仍继续释放其他本地资源。 */ } player = undefined }
  if (video.value) { video.value.pause(); video.value.removeAttribute('src'); video.value.load() }
}
async function attach() {
  destroyPlayer()
  const current = playerGeneration, source = session.value
  await nextTick()
  if (!source || !video.value || disposed || current !== playerGeneration) return
  error.value = ''; localPaused.value = false
  try {
    const { default: mpegts } = await import('mpegts.js')
    if (disposed || current !== playerGeneration || !video.value) return
    if (!mpegts.isSupported() || !source.httpFlvUrl) throw new Error('当前浏览器不支持此视频格式，请使用支持媒体扩展的浏览器或桌面客户端')
    player = mpegts.createPlayer({ type: 'flv', isLive: props.mode === 'live', url: source.httpFlvUrl, withCredentials: true }, { enableWorker: true, enableStashBuffer: props.mode === 'playback', stashInitialSize: 128 * 1024, autoCleanupSourceBuffer: true, autoCleanupMaxBackwardDuration: 30, autoCleanupMinBackwardDuration: 10, liveBufferLatencyChasing: props.mode === 'live' })
    player.on(mpegts.Events.ERROR, (_type: string, _detail: string, info: unknown) => {
      if (current !== playerGeneration || disposed) return
      error.value = '视频连接中断，正在尝试恢复'; ready.value = false
      const code = info && typeof info === 'object' && 'code' in info ? Number(info.code) : 0
      if (![401, 403].includes(code)) scheduleRetry()
      else { error.value = '视频访问权限已失效'; void stop() }
    })
    player.attachMediaElement(video.value); player.load()
    await Promise.race([
      player.play(),
      new Promise((_, reject) => setTimeout(() => reject(new Error('视频播放启动超时，请重试')), 10000))
    ])
  } catch (e) {
    if (current !== playerGeneration || disposed) return
    error.value = e instanceof Error && e.name === 'NotAllowedError' ? '浏览器暂停了自动播放，请点击播放' : errorMessage(e)
  }
}
function scheduleRetry() {
  clearTimeout(retryTimer)
  if (retryCount.value >= 3 || disposed) { error.value = '视频连接失败，请重试'; return }
  retryCount.value++
  const resumePos = playback.value?.currentTime
  retryTimer = setTimeout(() => { if (!disposed && props.channel) void start(false, resumePos) }, Math.min(10000, retryCount.value * 2000))
}
async function start(resetRetry = true, resumePosition?: string) {
  const current = ++generation
  clearTimeout(retryTimer); destroyPlayer(); error.value = ''; busy.value = true
  if (resetRetry) retryCount.value = 0
  try {
    if (!props.channel) { await lease.close(); return }
    if (props.mode === 'live' && !channelOnline(props.channel)) { await lease.close(); error.value = '通道离线'; return }
    if (props.mode === 'playback' && !props.range) { await lease.close(); return }
    const startAt = (props.mode === 'playback' && resumePosition && resumePosition > props.range!.start && resumePosition < props.range!.end)
      ? resumePosition
      : props.range?.start
    await lease.open({ channelId: props.channel.id, mode: props.mode, streamType: props.streamType, start: startAt, end: props.range?.end })
    renewAt = Date.now()
    if (session.value?.state === 'failed') {
      const sessErr = session.value && 'error' in session.value ? (session.value as PlaybackSession).error : null
      error.value = sessErr || '媒体会话失败，请重试'
      return
    }
    if (current === generation && !disposed) await attach()
  } catch (e) {
    if (current === generation && !disposed) {
      error.value = errorMessage(e)
      if (e instanceof ApiError && [0, 502, 503, 504].includes(e.status)) scheduleRetry()
    }
  } finally { if (current === generation) busy.value = false }
}
async function stop(keepalive = false) {
  generation++; clearTimeout(retryTimer); destroyPlayer(); busy.value = false
  try { await lease.close(keepalive) } catch (e) { error.value = errorMessage(e) }
}
async function refresh() {
  if (disposed || busy.value) return
  try {
    await lease.retryStops()
    if (!session.value) return
    const renew = Date.now() - renewAt >= 30000
    const previousUrl = session.value.httpFlvUrl
    await lease.refresh(renew)
    if (renew) renewAt = Date.now()
    if (session.value?.state === 'failed') { error.value = '媒体会话失败，正在恢复'; scheduleRetry() }
    if (session.value?.httpFlvUrl !== previousUrl) await attach()
  } catch (e) {
    error.value = errorMessage(e)
    if (e instanceof ApiError && [401, 403, 404, 410].includes(e.status)) await stop()
  }
}
async function control(command: PlaybackControl) {
  if (!session.value || props.mode !== 'playback') return
  busy.value = true
  try {
    await lease.control(command)
    if (command.action === 'pause') {
      // 仅暂停本地视频渲染，保持 HTTP-FLV 连接不断
      video.value?.pause()
    } else if (command.action === 'resume') {
      // 仅恢复本地视频渲染，无需重连
      try { await video.value?.play() } catch { /* NotAllowedError 忽略，用户可点击播放 */ }
    }
    // seek: 后端会重启 pipeline 并产生新的流状态，refresh() 会在 httpFlvUrl 变化时自动触发 attach()；
    // speed: 后端重启 pipeline，同上；
    // 此处不主动调用 attach()，避免与后端管道重建时序冲突导致播放器重载循环。
    if (command.action === 'seek' || command.action === 'speed') {
      // 主动等待后端 pipeline 重建完成（状态回到 playing），然后重新 attach
      destroyPlayer()
      let waited = 0
      while (waited < 10000) {
        await new Promise(resolve => setTimeout(resolve, 500))
        waited += 500
        await lease.refresh(false)
        const s = (session.value as PlaybackSession | null)
        if (!s || disposed) break
        if (s.state === 'playing' || s.state === 'paused') { await attach(); break }
        if (s.state === 'failed' || s.state === 'stopped') { error.value = s.error || '回放重载失败'; break }
      }
    }
  } catch (e) { error.value = errorMessage(e); throw e }
  finally { busy.value = false }
}
async function togglePause() {
  if (props.mode === 'playback') { try { await control({ action: playback.value?.state === 'paused' ? 'resume' : 'pause' }) } catch { /* 窗口中显示控制错误。 */ } }
  else if (video.value) { if (video.value.paused) { try { await video.value.play(); localPaused.value = false } catch (e) { error.value = errorMessage(e) } } else { video.value.pause(); localPaused.value = true } }
}
async function fullScreen() { try { if (document.fullscreenElement === root.value) await document.exitFullscreen(); else await root.value?.requestFullscreen() } catch (e) { ElMessage.error(errorMessage(e)) } }
function screenshot() {
  const element = video.value
  if (!element || !element.videoWidth) return
  try {
    const canvas = document.createElement('canvas'); canvas.width = element.videoWidth; canvas.height = element.videoHeight
    canvas.getContext('2d')!.drawImage(element, 0, 0)
    canvas.toBlob(blob => { if (!blob) { ElMessage.error('截图失败'); return }; const url = URL.createObjectURL(blob), anchor = document.createElement('a'); anchor.href = url; anchor.download = `${props.channel?.name || '视频'}-${Date.now()}.png`; anchor.click(); setTimeout(() => URL.revokeObjectURL(url), 1000) }, 'image/png')
  } catch { ElMessage.error('此媒体来源不允许截图') }
}
function drop(event: DragEvent) { const id = Number(event.dataTransfer?.getData('application/x-platform-channel')); if (Number.isSafeInteger(id) && id > 0) emit('dropChannel', id) }
function seek(value: number | number[]) { if (!props.range || typeof value !== 'number') return; const position = new Date(Date.parse(props.range.start) + value / 100 * (Date.parse(props.range.end) - Date.parse(props.range.start))).toISOString(); void control({ action: 'seek', position }).catch(e => { ElMessage.error(errorMessage(e)) }) }
const stopEvent = () => void stop()
const hideEvent = () => void stop(true)
watch(() => [props.channel?.id, props.mode, props.streamType, props.range?.start, props.range?.end], () => void start())
onMounted(() => { void start(); timer = setInterval(() => void refresh(), 3000); window.addEventListener('pagehide', hideEvent); window.addEventListener('platform-media-stop', stopEvent); window.addEventListener('platform-access-changed', stopEvent) })
onBeforeUnmount(() => { disposed = true; clearInterval(timer); void stop(); window.removeEventListener('pagehide', hideEvent); window.removeEventListener('platform-media-stop', stopEvent); window.removeEventListener('platform-access-changed', stopEvent) })
defineExpose({ control, stop, retry: start })
</script>
<template><section ref="root" :class="['video-tile', { selected, 'has-channel': channel }]" :aria-label="`视频窗口 ${index + 1}`" tabindex="0" @click="emit('select')" @keydown.enter="emit('select')" @dblclick="fullScreen" @dragover.prevent @drop.prevent="drop"><header><span class="tile-number">{{ index + 1 }}</span><strong>{{ channel?.alias || channel?.name || '未选择通道' }}</strong><span v-if="session" class="codec-badge">{{ session.transcoded ? 'H.264 兼容' : session.codec }}</span><el-tooltip v-if="channel" content="关闭通道"><button class="tile-icon" aria-label="关闭通道" @click.stop="emit('close')"><el-icon><Close /></el-icon></button></el-tooltip></header><div class="video-surface"><video ref="video" :muted="muted" autoplay playsinline crossorigin="use-credentials" @playing="ready = true; error = ''; retryCount = 0" @waiting="ready = false" /><div v-if="!channel || busy || error || (!ready && !session) || blockingStatus" class="video-placeholder"><el-icon v-if="!busy"><VideoCamera /></el-icon><span>{{ busy ? '正在连接视频' : error || statusText || (channel && mode === 'playback' ? '等待录像查询' : channel ? '等待视频' : `窗口 ${index + 1}`) }}</span><el-button v-if="channel && error && !busy" size="small" :icon="Refresh" @click.stop="start()">重试</el-button></div></div><footer><span v-if="session?.state === 'paused'" class="codec-badge">已暂停</span><span class="tile-device">{{ channel?.deviceName || 'VisiCore（视枢）' }}</span><template v-if="session"><el-tooltip :content="(playback?.state === 'paused' || localPaused) ? '继续播放' : '暂停播放'"><button class="tile-icon" :aria-label="(playback?.state === 'paused' || localPaused) ? '继续播放' : '暂停播放'" @click.stop="togglePause"><el-icon><VideoPlay v-if="playback?.state === 'paused' || localPaused" /><VideoPause v-else /></el-icon></button></el-tooltip><el-tooltip :content="muted ? '开启声音' : '静音'"><button class="tile-icon" :aria-label="muted ? '开启声音' : '静音'" @click.stop="muted = !muted"><el-icon><Mute v-if="muted" /><Microphone v-else /></el-icon></button></el-tooltip><el-tooltip content="截图"><button class="tile-icon" :disabled="!ready" aria-label="截图" @click.stop="screenshot"><el-icon><Camera /></el-icon></button></el-tooltip></template><el-tooltip content="全屏"><button class="tile-icon" aria-label="全屏" @click.stop="fullScreen"><el-icon><FullScreen /></el-icon></button></el-tooltip></footer><div v-if="playback" class="tile-timeline" @click.stop><div class="timeline-track"><span v-for="(segment, key) in segments" :key="key" :style="{ left: `${segment.left}%`, width: `${segment.width}%` }" /></div><el-slider :model-value="Math.max(0, Math.min(100, playback.progress || 0))" :show-tooltip="false" aria-label="录像进度" @change="seek" /><small>{{ dateTime(playback.currentTime) }}</small></div></section></template>
