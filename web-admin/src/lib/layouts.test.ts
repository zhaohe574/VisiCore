import { describe, expect, it } from 'vitest'
import type { Channel } from '../api'
import { captureLayout, layoutColumns, layoutCounts, layoutOptions, layoutSlots, patrolChannels, restoreLayout } from './layouts'

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

describe('分屏档位', () => {
  it('档位取值与数据库迁移 010 的约束一致', () => {
    expect(layoutCounts).toEqual([1, 4, 9, 16, 25, 8, 10])
  })
  it('聚焦与等分档位使用整数列数，不再依赖 Math.sqrt', () => {
    expect(layoutColumns(8)).toBe(4)
    expect(layoutColumns(10)).toBe(5)
    expect(layoutColumns(25)).toBe(5)
    expect(Number.isInteger(layoutColumns(8))).toBe(true)
    expect(Number.isInteger(layoutColumns(10))).toBe(true)
    expect(Number.isInteger(layoutColumns(25))).toBe(true)
  })
  it('每个档位都有标签与整数列数', () => {
    for (const option of layoutOptions) {
      expect(option.label.length).toBeGreaterThan(0)
      expect(Number.isInteger(option.columns)).toBe(true)
      expect(option.columns).toBeGreaterThan(0)
    }
  })
  it('未知档位回退到最接近的整数平方根', () => {
    expect(layoutColumns(5)).toBe(2)
    expect(layoutColumns(1)).toBe(1)
  })
  it('聚焦档位同样支持空窗口持久化', () => {
    const slots = [channels[0], undefined, channels[1], undefined, undefined, channels[1], undefined, channels[0]]
    expect(captureLayout(slots, 8)).toEqual([101, null, 203, null, null, 203, null, 101])
    expect(restoreLayout(captureLayout(slots, 8), 8, channels)).toEqual(slots)
  })
})
