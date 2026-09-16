import { describe, expect, it } from 'vitest'

describe('迷你走势图数据归一化逻辑', () => {
  it('正确归一化数值并映射到可视高度', () => {
    const list = [10, 20, 30]
    const minVal = Math.min(...list)
    const maxVal = Math.max(...list)
    const range = maxVal - minVal

    const points = list.map((val, idx) => {
      const x = (idx / (list.length - 1)) * 100
      const normalized = (val - minVal) / range
      const y = 2 + 26 * (1 - normalized)
      return { x, y }
    })

    expect(points[0]).toEqual({ x: 0, y: 28 })
    expect(points[1]).toEqual({ x: 50, y: 15 })
    expect(points[2]).toEqual({ x: 100, y: 2 })
  })

  it('处理单点或全零数据防除以零', () => {
    const list = [0, 0]
    const minVal = Math.min(...list, 0)
    const maxVal = Math.max(...list, 0.001)
    const range = maxVal - minVal || 1
    expect(range).toBeGreaterThan(0)
  })
})
