import { getCurrentInstance, onActivated, onDeactivated, onBeforeUnmount, onMounted } from 'vue'
import { useEvents } from '../stores/events'
import type { EventKind, ResourceEvent } from '../api'

export interface UseActivePageOptions {
  kinds?: (EventKind | string)[]
  debounceMs?: number
}

/** 缓存页停用时忽略迟到结果，重新进入或收到资产变更时补取。 */
export function useActivePage(
  refresh: (silent?: boolean) => unknown,
  options?: UseActivePageOptions
) {
  let active = true, epoch = 0
  let mounted = false
  const sequences = new Map<string, number>()
  let timer: ReturnType<typeof setTimeout> | undefined

  const debounceMs = options?.debounceMs ?? 1000
  const filterKinds = options?.kinds

  // 计算需要订阅的底层 SignalR 频道
  const rawKinds: (EventKind | 'reconnected')[] = ['reconnected']
  if (!filterKinds || filterKinds.length === 0) {
    rawKinds.push('asset.changed', 'access.changed')
  } else {
    const hasAssetFilter = filterKinds.some(k =>
      k === 'asset.changed' ||
      k.startsWith('onu') ||
      k.startsWith('channel') ||
      k.startsWith('computer') ||
      k.startsWith('terminal-box') ||
      k.startsWith('network-device') ||
      k.startsWith('organization')
    )
    if (hasAssetFilter) rawKinds.push('asset.changed')
    for (const k of filterKinds) {
      if (['alarm.changed', 'device.changed', 'media.changed', 'export.changed', 'access.changed'].includes(k as EventKind)) {
        rawKinds.push(k as EventKind)
      }
    }
  }

  const unsubscribe = useEvents().subscribe([...new Set(rawKinds)], (event?: ResourceEvent) => {
    if (!active) return

    // 如果指定了 kinds 且事件是 asset.changed，按前缀精确过滤
    if (filterKinds && filterKinds.length > 0 && event && event.kind === 'asset.changed') {
      const eventId = String(event.id || '')
      const matches = filterKinds.some(k => {
        if (k === 'asset.changed') return true
        if (k === 'onu.changed' || k === 'onu' || k === 'onu:') return eventId.startsWith('onu:') || eventId === 'onu:refresh'
        if (k === 'channel.changed' || k === 'channel' || k === 'channel:') return eventId.startsWith('channel:') || eventId === 'channel:refresh'
        if (k === 'computer.changed' || k === 'computer' || k === 'computer:') return eventId.startsWith('computer:') || eventId === 'computer:refresh'
        if (k === 'terminal-box.changed' || k === 'terminal-box' || k === 'terminal-box:') return eventId.startsWith('terminal-box:') || eventId === 'terminal-box:refresh'
        if (k === 'network-device.changed' || k === 'network-device' || k === 'network-device:') return eventId.startsWith('network-device:') || eventId === 'network-device:refresh'
        if (k === 'organization.changed' || k === 'organization' || k === 'organization:') return eventId.startsWith('organization:') || eventId === 'organization:refresh'
        return false
      })
      if (!matches) return
    }

    clearTimeout(timer)
    timer = setTimeout(() => {
      if (active) void refresh(true)
    }, debounceMs)
  })

  const pause = () => {
    active = false
    epoch++
    clearTimeout(timer)
  }

  if (getCurrentInstance()) {
    onMounted(() => {
      mounted = true
    })

    onDeactivated(pause)
    onActivated(() => {
      if (!active && mounted) {
        active = true
        void refresh(true)
      }
    })
    onBeforeUnmount(() => {
      pause()
      unsubscribe()
    })
  }

  function ticket(lane = 'default') {
    if (!active) return undefined
    const sequence = (sequences.get(lane) ?? 0) + 1
    sequences.set(lane, sequence)
    return { epoch, lane, sequence }
  }

  function current(value: ReturnType<typeof ticket>) {
    return active && value !== undefined && value.epoch === epoch && sequences.get(value.lane) === value.sequence
  }

  return { ticket, current, isActive: () => active }
}
