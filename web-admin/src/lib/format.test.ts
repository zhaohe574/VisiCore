import { describe, expect, it } from 'vitest'
import { duration, errorMessage, formatLinkSpeed, localizedError, networkSpeed, safeRedirect, timeRange } from './format'
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

describe('网速与时长格式化', () => {
  it('格式化实时网速', () => {
    expect(networkSpeed(0)).toBe('0 B/s')
    expect(networkSpeed(512)).toBe('512 B/s')
    expect(networkSpeed(1024)).toBe('1.0 KB/s')
    expect(networkSpeed(1024 * 1024 * 2.5)).toBe('2.5 MB/s')
    expect(networkSpeed(1024 * 1024 * 1024 * 1.2)).toBe('1.2 GB/s')
    expect(networkSpeed(null)).toBe('0 B/s')
  })
  it('格式化时长', () => {
    expect(duration(30)).toBe('30 秒')
    expect(duration(120)).toBe('2 分钟')
    expect(duration(3660)).toBe('1 小时 1 分钟')
    expect(duration(86400 * 2 + 3600 * 3)).toBe('2 天 3 小时')
    expect(duration(null)).toBe('—')
  })
  it('格式化协商网速与自适应虚拟网卡', () => {
    expect(formatLinkSpeed(-1)).toBe('虚拟网卡 (动态不限速)')
    expect(formatLinkSpeed(4294967295 * 1000000)).toBe('虚拟网卡 (动态不限速)')
    expect(formatLinkSpeed(4294967295)).toBe('虚拟网卡 (动态不限速)')
    expect(formatLinkSpeed(0)).toBe('—')
    expect(formatLinkSpeed(null)).toBe('—')
    expect(formatLinkSpeed(undefined)).toBe('—')
    expect(formatLinkSpeed(100_000_000)).toBe('100 Mbps')
    expect(formatLinkSpeed(1_000_000_000)).toBe('1 Gbps')
    expect(formatLinkSpeed(2_500_000_000)).toBe('2.5 Gbps')
    expect(formatLinkSpeed(10_000_000_000)).toBe('10 Gbps')
  })
})

