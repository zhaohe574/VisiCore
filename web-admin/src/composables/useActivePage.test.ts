import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { useActivePage } from './useActivePage'
import { useEvents } from '../stores/events'
import type { ResourceEvent } from '../api'

describe('useActivePage composable', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.useFakeTimers()
  })

  it('provides tickets and tracks sequence correctly', () => {
    const refresh = vi.fn()
    const lifecycle = useActivePage(refresh)

    const ticket1 = lifecycle.ticket('kanban')
    expect(ticket1).toBeDefined()
    expect(lifecycle.current(ticket1)).toBe(true)

    const ticket2 = lifecycle.ticket('kanban')
    expect(ticket2?.sequence).toBe((ticket1?.sequence ?? 0) + 1)
    expect(lifecycle.current(ticket1)).toBe(false)
    expect(lifecycle.current(ticket2)).toBe(true)
  })

  it('filters events based on kinds option', async () => {
    const refresh = vi.fn()
    useActivePage(refresh, {
      kinds: ['terminal-box', 'onu'],
      debounceMs: 500
    })

    const events = useEvents()

    // 发送与终端箱无关的资产变更事件，应被忽略
    events.emit('asset.changed', {
      id: 'channel:100',
      version: 1,
      kind: 'asset.changed'
    } as ResourceEvent)

    vi.advanceTimersByTime(600)
    expect(refresh).not.toHaveBeenCalled()

    // 发送终端箱资产变更事件，应触发静默刷新
    events.emit('asset.changed', {
      id: 'terminal-box:2',
      version: 2,
      kind: 'asset.changed'
    } as ResourceEvent)

    vi.advanceTimersByTime(200)
    expect(refresh).not.toHaveBeenCalled() // 还在防抖期内

    vi.advanceTimersByTime(400)
    expect(refresh).toHaveBeenCalledTimes(1)
    expect(refresh).toHaveBeenCalledWith(true) // 必须是 silent 模式
  })

  it('debounces rapid event floods into a single refresh', () => {
    const refresh = vi.fn()
    useActivePage(refresh, {
      kinds: ['terminal-box'],
      debounceMs: 1000
    })

    const events = useEvents()

    // 连续触发 5 次终端箱变更
    for (let i = 1; i <= 5; i++) {
      events.emit('asset.changed', {
        id: `terminal-box:${i}`,
        version: i,
        kind: 'asset.changed'
      } as ResourceEvent)
      vi.advanceTimersByTime(100)
    }

    // 尚未达到最后一次的防抖窗口
    expect(refresh).not.toHaveBeenCalled()

    // 快进防抖时间
    vi.advanceTimersByTime(1000)
    expect(refresh).toHaveBeenCalledTimes(1)
    expect(refresh).toHaveBeenCalledWith(true)
  })
})
