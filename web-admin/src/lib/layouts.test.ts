import { describe, expect, it } from 'vitest'
import type { Channel } from '../api'
import { captureLayout, layoutSlots, patrolChannels, restoreLayout } from './layouts'

const channels = [101, 203].map(id => ({ id, name: `通道 ${id}` } as Channel))
describe('窗口位置持久化', () => {
  it('保存和恢复中间及末尾空窗口', () => {
    const slots = [channels[0], undefined, channels[1], undefined]
    const saved = captureLayout(slots, 4)
    expect(saved).toEqual([101, null, 203, null])
    expect(restoreLayout(saved, 4, channels)).toEqual(slots)
  })
  it('保留首位空窗口和重复通道', () => {
    const slots = [undefined, channels[0], channels[0], undefined]
    expect(restoreLayout(captureLayout(slots, 4), 4, channels)).toEqual(slots)
  })
  it('权限撤销后只清空对应窗口', () => {
    expect(restoreLayout([101, null, 203, null], 4, [channels[1]])).toEqual([undefined, undefined, channels[1], undefined])
  })
  it('旧短数组补空，减少分屏时裁剪尾部', () => {
    expect(layoutSlots([101, 203], 4)).toEqual([101, 203, null, null])
    expect(layoutSlots([101, null, 203, null], 1)).toEqual([101])
  })
  it('允许全空固定布局，轮巡只提取非空通道', () => {
    expect(captureLayout([], 4)).toEqual([null, null, null, null])
    expect(patrolChannels([null, 101, null, 203])).toEqual([101, 203])
    expect(patrolChannels([null, null])).toEqual([])
  })
})
