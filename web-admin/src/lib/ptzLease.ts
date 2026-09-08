import { mediaApi, type PtzCommand } from '../api'
export interface PtzTransport {
  start(channelId: number, command: PtzCommand, speed: number): Promise<unknown>
  stop(channelId: number, keepalive?: boolean): Promise<unknown>
}
interface PtzOperation { channelId: number; command: PtzCommand; speed: number; request: Promise<unknown> | null }
export class PtzLease {
  private generation = 0
  private operation: PtzOperation | null = null
  private timer: ReturnType<typeof setInterval> | undefined
  constructor(private changed: (active: boolean, error?: unknown) => void, private transport: PtzTransport = { start: mediaApi.ptz, stop: mediaApi.stopPtz }) {}
  private async stopOperation(operation: PtzOperation, keepalive = false) {
    const firstStop = this.transport.stop(operation.channelId, keepalive)
    if (!operation.request || keepalive) { await firstStop; return }
    // 松开、失焦与延迟的启动响应可能交错，启动完成后必须再次发停止。
    const results = await Promise.allSettled([firstStop, operation.request])
    await this.transport.stop(operation.channelId)
    if (results[0].status === 'rejected') throw results[0].reason
  }
  async start(channelId: number, command: PtzCommand, speed: number) {
    const generation = ++this.generation, previous = this.operation
    clearInterval(this.timer); this.operation = null; this.changed(false)
    try { if (previous) await this.stopOperation(previous) }
    catch (e) { this.changed(false, e); return }
    if (generation !== this.generation) return
    const operation: PtzOperation = { channelId, command, speed, request: null }
    this.operation = operation; this.changed(true)
    const send = async () => {
      if (generation !== this.generation || operation.request) return
      try {
        operation.request = this.transport.start(channelId, command, speed)
        await operation.request
      } catch (e) {
        if (generation === this.generation) { await this.stop(); this.changed(false, e) }
      } finally { operation.request = null }
    }
    void send()
    if (generation === this.generation) this.timer = setInterval(() => void send(), 2000)
  }
  async stop(keepalive = false) {
    this.generation++; clearInterval(this.timer)
    const previous = this.operation; this.operation = null; this.changed(false)
    if (previous) { try { await this.stopOperation(previous, keepalive) } catch (e) { this.changed(false, e) } }
  }
}
