import { afterEach, describe, expect, it, vi } from 'vitest'
import { PtzLease, type PtzTransport } from './ptzLease'
function deferred() { let resolve!: () => void; const promise = new Promise<void>(yes => { resolve = yes }); return { promise, resolve } }
afterEach(() => vi.useRealTimers())
describe('云台租约', () => {
  it('每两秒续租，松开后停止续租', async () => { vi.useFakeTimers(); const api: PtzTransport = { start: vi.fn(async () => undefined), stop: vi.fn(async () => undefined) }; const lease = new PtzLease(vi.fn(), api); await lease.start(10, 'up', 4); await vi.advanceTimersByTimeAsync(4000); expect(api.start).toHaveBeenCalledTimes(3); await lease.stop(); await vi.advanceTimersByTimeAsync(5000); expect(api.start).toHaveBeenCalledTimes(3); expect(api.stop).toHaveBeenCalledWith(10, false) })
  it('启动响应晚于松开时再次停止云台', async () => { const pending = deferred(), api: PtzTransport = { start: vi.fn(() => pending.promise), stop: vi.fn(async () => undefined) }; const lease = new PtzLease(vi.fn(), api); await lease.start(10, 'up', 4); const stopping = lease.stop(); expect(api.stop).toHaveBeenCalledTimes(1); pending.resolve(); await stopping; expect(api.stop).toHaveBeenCalledTimes(2) })
  it('换通道等待中松开不会启动第二路云台', async () => { const pending = deferred(), api: PtzTransport = { start: vi.fn(async () => undefined), stop: vi.fn().mockReturnValueOnce(pending.promise).mockResolvedValue(undefined) }; const lease = new PtzLease(vi.fn(), api); await lease.start(10, 'up', 4); await Promise.resolve(); const switching = lease.start(20, 'right', 4); await lease.stop(); pending.resolve(); await switching; expect(api.start).toHaveBeenCalledTimes(1) })
})
