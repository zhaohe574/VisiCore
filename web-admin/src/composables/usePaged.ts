import { onActivated, onDeactivated, onBeforeUnmount, onMounted, ref, shallowRef } from 'vue'
import type { Page, Query, EventKind, ResourceEvent } from '../api'
import { errorMessage } from '../lib/format'
import { useEvents } from '../stores/events'

export function usePaged<T>(
  loader: (query: Query, signal?: AbortSignal) => Promise<Page<T>>,
  filters: () => Query = () => ({}),
  kinds: (EventKind | string)[] = [],
  enabled: () => boolean = () => true
) {
  const items = shallowRef<T[]>([]), total = ref(0), page = ref(1), pageSize = ref(20), search = ref(''), loading = ref(false), error = ref('')
  let controller: AbortController | undefined
  let generation = 0
  let active = true
  let refreshTimer: ReturnType<typeof setTimeout> | undefined

  async function load(reset = false, silent = false) {
    if (!active) return
    if (!enabled()) { items.value = []; total.value = 0; return }
    if (reset) page.value = 1
    controller?.abort()
    controller = new AbortController()
    const current = ++generation
    if (!silent) {
      loading.value = true
      error.value = ''
    }
    try {
      const data = await loader(
        { ...filters(), search: search.value, page: page.value, pageSize: pageSize.value },
        controller.signal
      )
      if (current !== generation) return
      if (page.value > 1 && !data.items.length && data.total > 0) {
        page.value--
        return load(false, silent)
      }
      items.value = data.items
      total.value = data.total
    } catch (e) {
      if (current === generation && !(e instanceof Error && e.name === 'AbortError')) {
        error.value = errorMessage(e)
      }
    } finally {
      if (current === generation && !silent) {
        loading.value = false
      }
    }
  }

  // 计算需要订阅的底层 SignalR 频道
  const rawKinds: (EventKind | 'reconnected')[] = ['reconnected']
  const hasAssetFilter = kinds.some(k => k === 'asset.changed' || k.startsWith('onu') || k.startsWith('channel') || k.startsWith('computer') || k.startsWith('terminal-box'))
  if (hasAssetFilter) rawKinds.push('asset.changed')
  for (const k of kinds) {
    if (['alarm.changed', 'device.changed', 'media.changed', 'export.changed', 'access.changed'].includes(k as EventKind)) {
      rawKinds.push(k as EventKind)
    }
  }

  const unsubscribe = useEvents().subscribe([...new Set(rawKinds)], (event?: ResourceEvent) => {
    if (!active) return

    // 如果是 asset.changed，按前缀精确过滤
    if (event && event.kind === 'asset.changed') {
      const eventId = String(event.id || '')
      const matches = kinds.some(k => {
        if (k === 'asset.changed') return true
        if (k === 'onu.changed' || k === 'onu:') return eventId.startsWith('onu:') || eventId === 'onu:refresh'
        if (k === 'channel.changed' || k === 'channel:') return eventId.startsWith('channel:') || eventId === 'channel:refresh'
        if (k === 'computer.changed' || k === 'computer:') return eventId.startsWith('computer:') || eventId === 'computer:refresh'
        if (k === 'terminal-box.changed' || k === 'terminal-box:') return eventId.startsWith('terminal-box:') || eventId === 'terminal-box:refresh'
        return false
      })
      if (!matches) return
    }

    clearTimeout(refreshTimer)
    // 静默刷新：不遮罩、不闪烁，防抖 1200ms
    refreshTimer = setTimeout(() => void load(false, true), 1200)
  })

  let mounted = false
  onMounted(() => { mounted = true; void load() })
  onActivated(() => { if (!active && mounted) { active = true; void load() } })
  function pause() {
    active = false
    generation++
    controller?.abort()
    clearTimeout(refreshTimer)
    loading.value = false
  }
  onDeactivated(pause)
  onBeforeUnmount(() => { pause(); unsubscribe() })

  return { items, total, page, pageSize, search, loading, error, load }
}
