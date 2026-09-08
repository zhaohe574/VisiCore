import { describe, expect, it, vi } from 'vitest'
import { channelOnline, patrolBatch, sameSource, timelineSegments } from './media'
import { MediaLease, type MediaRequest, type MediaTransport } from './mediaLease'
import { ApiError, type Channel, type LiveSession } from '../api'

function deferred<T>() { let resolve!: (value: T) => void, reject!: (error: unknown) => void; const promise = new Promise<T>((yes, no) => { resolve = yes; reject = no }); return { promise, resolve, reject } }
const session = (id: string, channelId = 10): LiveSession => ({ id, channelId, streamType: 2, state: 'playing', expiresAt: '2030-01-01T00:00:00Z', rtspUrl: '', httpFlvUrl: `/media/${id}.flv`, hlsUrl: '', codec: 'H.264', transcoded: false })
const request = (channelId = 10): MediaRequest => ({ channelId, streamType: 2, mode: 'live' })
function mockTransport(): MediaTransport { return { start: vi.fn(async () => session('a')), stop: vi.fn(async () => undefined), renew: vi.fn(async value => value), getPlayback: vi.fn(), control: vi.fn() } }

describe('多设备通道与轮巡', () => {
  it('使用全局通道编号区分设备内同号通道', () => { expect(sameSource({ channelId: 10, streamType: 2 }, { channelId: 11, streamType: 2 })).toBe(false); expect(sameSource({ channelId: 10, streamType: 1 }, { channelId: 10, streamType: 2 })).toBe(false) })
  it('轮巡循环取通道，不足分屏时不重复填充', () => { expect(patrolBatch([1, 2, 3], 2, 4)).toEqual([3, 1, 2]); expect(patrolBatch([], 0, 4)).toEqual([]) })
  it('未在线通道不能启动实时播放', () => { expect(channelOnline({ status: 'offline' } as Channel)).toBe(false); expect(channelOnline({ status: 'online' } as Channel)).toBe(true) })
  it('保留录像缺口并裁剪超出时间范围的片段', () => { const result = timelineSegments([{ start: '2026-01-01T00:00:00Z', end: '2026-01-01T00:20:00Z' }, { start: '2026-01-01T00:40:00Z', end: '2026-01-01T02:00:00Z' }], '2026-01-01T00:00:00Z', '2026-01-01T01:00:00Z'); expect(result).toHaveLength(2); expect(result[0].width).toBeCloseTo(100 / 3); expect(result[1].left).toBeCloseTo(200 / 3); expect(result[1].width).toBeCloseTo(100 / 3) })
})
describe('媒体租约与并发释放', () => {
  it('退出早于启动响应时释放迟到会话', async () => { const pending = deferred<LiveSession>(), api = mockTransport(), changed = vi.fn(); api.start = vi.fn(() => pending.promise); const lease = new MediaLease(changed, api); const opening = lease.open(request()); await lease.close(); pending.resolve(session('late')); await opening; expect(api.stop).toHaveBeenCalledWith(expect.objectContaining({ id: 'late' }), 'live', false); expect(lease.session).toBeNull() })
  it('快速换台时仅保留最后一次启动结果', async () => { const first = deferred<LiveSession>(), second = deferred<LiveSession>(), api = mockTransport(); api.start = vi.fn().mockReturnValueOnce(first.promise).mockReturnValueOnce(second.promise); const lease = new MediaLease(vi.fn(), api); const a = lease.open(request(10)); const b = lease.open(request(20)); second.resolve(session('new', 20)); await b; first.resolve(session('old', 10)); await a; expect(lease.session?.channelId).toBe(20); expect(api.stop).toHaveBeenCalledWith(expect.objectContaining({ id: 'old' }), 'live', false) })
  it('重复停止不重复释放远端会话', async () => { const api = mockTransport(), lease = new MediaLease(vi.fn(), api); await lease.open(request()); await lease.close(); await lease.close(); expect(api.stop).toHaveBeenCalledTimes(1) })
  it('停止网络失败保留待清理记录并在下一次重试', async () => { const api = mockTransport(), lease = new MediaLease(vi.fn(), api); api.stop = vi.fn().mockRejectedValueOnce(new ApiError(0, 'network_error', '断线')).mockResolvedValue(undefined); await lease.open(request()); await expect(lease.close()).rejects.toThrow('断线'); expect(lease.session).toBeNull(); await lease.retryStops(); expect(api.stop).toHaveBeenCalledTimes(2) })
  it('停止已不存在的会话视为清理完成', async () => { const api = mockTransport(), lease = new MediaLease(vi.fn(), api); api.stop = vi.fn().mockRejectedValue(new ApiError(404, 'media.missing', '不存在')); await lease.open(request()); await expect(lease.close()).resolves.toBeUndefined(); expect(lease.session).toBeNull() })
  it('并发续期只发送一次且停止后不复活会话', async () => { const pending = deferred<LiveSession>(), api = mockTransport(); api.renew = vi.fn(() => pending.promise); const lease = new MediaLease(vi.fn(), api); await lease.open(request()); const renewal = lease.refresh(); await lease.refresh(); await lease.close(); pending.resolve(session('a')); await renewal; expect(api.renew).toHaveBeenCalledTimes(1); expect(lease.session).toBeNull() })
  it('页面卸载使用保活请求释放媒体', async () => { const api = mockTransport(), lease = new MediaLease(vi.fn(), api); await lease.open(request()); await lease.close(true); expect(api.stop).toHaveBeenCalledWith(expect.objectContaining({ id: 'a' }), 'live', true) })
})
