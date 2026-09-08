import { ApiError, mediaApi, type LiveSession, type PlaybackControl, type PlaybackSession } from '../api'
export interface MediaRequest { channelId: number; streamType: 1 | 2; mode: 'live' | 'playback'; start?: string; end?: string }
type Session = LiveSession | PlaybackSession
export interface MediaTransport {
  start(request: MediaRequest): Promise<Session>
  stop(session: Session, mode: MediaRequest['mode'], keepalive: boolean): Promise<void>
  renew(session: Session, mode: MediaRequest['mode']): Promise<Session>
  getPlayback(id: string): Promise<PlaybackSession>
  control(id: string, control: PlaybackControl): Promise<PlaybackSession>
}
const transport: MediaTransport = {
  start: request => request.mode === 'live' ? mediaApi.live(request.channelId, request.streamType) : mediaApi.playback(request.channelId, request.start!, request.end!),
  stop: (session, mode, keepalive) => mode === 'live' ? mediaApi.stopLive(session.id, keepalive) : mediaApi.stopPlayback(session.id, keepalive),
  renew: (session, mode) => mode === 'live' ? mediaApi.renewLive(session.id) : mediaApi.renewPlayback(session.id),
  getPlayback: mediaApi.getPlayback, control: mediaApi.control,
}

// 启动请求返回时再次核对代次，避免快速换台或离开页面后泄漏远端会话。
export class MediaLease {
  private generation = 0
  private active: { session: Session; request: MediaRequest } | null = null
  private refreshing = false
  private pendingStops = new Map<string, { session: Session; mode: MediaRequest['mode'] }>()
  constructor(private changed: (session: Session | null) => void, private transportApi: MediaTransport = transport) {}
  get session() { return this.active?.session || null }
  private async stop(session: Session, mode: MediaRequest['mode'], keepalive = false) {
    try { await this.transportApi.stop(session, mode, keepalive); this.pendingStops.delete(session.id) }
    catch (error) {
      if (error instanceof ApiError && [404, 410, 401, 403].includes(error.status)) { this.pendingStops.delete(session.id); return }
      this.pendingStops.set(session.id, { session, mode })
      throw error
    }
  }
  async open(request: MediaRequest) {
    const generation = ++this.generation
    const previous = this.active; this.active = null; this.changed(null)
    if (previous) await this.stop(previous.session, previous.request.mode)
    if (generation !== this.generation) return
    if (this.pendingStops.size) await this.retryStops()
    if (generation !== this.generation) return
    const session = await this.transportApi.start(request)
    if (generation !== this.generation) { await this.stop(session, request.mode); return }
    this.active = { session, request }; this.changed(session)
  }
  async close(keepalive = false) {
    this.generation++
    const current = this.active; this.active = null; this.changed(null)
    if (current) await this.stop(current.session, current.request.mode, keepalive)
    await this.retryStops(keepalive)
  }
  async retryStops(keepalive = false) {
    for (const item of [...this.pendingStops.values()]) await this.stop(item.session, item.mode, keepalive)
  }
  async refresh(renew = true) {
    if (this.refreshing || !this.active) return
    const current = this.active, generation = this.generation
    this.refreshing = true
    try {
      const session = renew ? await this.transportApi.renew(current.session, current.request.mode) : current.request.mode === 'playback' ? await this.transportApi.getPlayback(current.session.id) : current.session
      if (generation === this.generation && this.active?.session.id === current.session.id) { this.active.session = session; this.changed(session) }
    } finally { this.refreshing = false }
  }
  async control(control: PlaybackControl) {
    const current = this.active, generation = this.generation
    if (!current || current.request.mode !== 'playback') return
    let session = await this.transportApi.control(current.session.id, control)
    if (!session) session = await this.transportApi.getPlayback(current.session.id)
    if (generation === this.generation && this.active?.session.id === current.session.id) { this.active.session = session; this.changed(session) }
  }
}
