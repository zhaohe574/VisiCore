import { describe, expect, it } from 'vitest'
import { errorMessage, localizedError, safeRedirect, timeRange } from './format'
describe('时间范围与页面跳转', () => {
  it('导出接受24小时，拒绝超过24小时或反向区间', () => { expect(timeRange([new Date('2026-01-01T00:00:00+08:00'), new Date('2026-01-02T00:00:00+08:00')], 24)).toEqual({ start: '2025-12-31T16:00:00.000Z', end: '2026-01-01T16:00:00.000Z' }); expect(() => timeRange([new Date(0), new Date(86400001)], 24)).toThrow('24'); expect(() => timeRange([new Date(1), new Date(0)])).toThrow('晚于') })
  it('拒绝无效时间', () => { expect(() => timeRange(null)).toThrow(); expect(() => timeRange([new Date('invalid'), new Date()])).toThrow() })
  it('登录后仅跳转平台工作区', () => { expect(safeRedirect('/app/live?channelId=123')).toBe('/app/live?channelId=123'); for (const value of ['https://example.com', '//example.com', '/application', ['//example.com']]) expect(safeRedirect(value)).toBe('/app') })
})

describe('界面错误中文映射', () => {
  it.each([
    ['Failed to load resource', '视频加载失败'],
    ['Failed to fetch', '无法连接平台服务'],
    ['Failed to fetch dynamically imported module: /assets/page.js', '页面资源加载失败'],
    ['NotSupportedError: unsupported codec', '浏览器不支持此视频格式'],
    ['No space left on device', '存储空间不足'],
    ['Unhandled internal exception', '操作失败'],
  ])('映射外部错误：%s', (message, expected) => { expect(errorMessage(new Error(message))).toContain(expected) })
  it('保留中文业务提示与请求追踪编号', () => {
    expect(errorMessage(Object.assign(new Error('没有通道访问权限'), { traceId: 'trace-203' }))).toBe('没有通道访问权限（追踪编号：trace-203）')
    expect(localizedError('Forbidden', 403)).toBe('没有执行此操作的权限')
  })
})
