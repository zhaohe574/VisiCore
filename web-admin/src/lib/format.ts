export function dateTime(value?: string | null) {
  if (!value) return '—'
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? '—' : new Intl.DateTimeFormat('zh-CN', { year: 'numeric', month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit', second: '2-digit', hour12: false }).format(date)
}
export function bytes(value?: number | null) {
  if (value === null || value === undefined) return '—'
  if (value < 1024) return `${value} B`
  const units = ['KB', 'MB', 'GB', 'TB']
  let size = value / 1024, i = 0
  while (size >= 1024 && i < units.length - 1) { size /= 1024; i++ }
  return `${size.toFixed(1)} ${units[i]}`
}
export const percent = (part: number | null, total: number | null) => total && total > 0 && part !== null ? Math.max(0, Math.min(100, Math.round(part / total * 100))) : 0
export function localizedError(value: unknown, status?: number): string {
  const message = typeof value === 'string' ? value.trim() : ''
  if (/dynamically imported module|importing a module|Loading chunk|ChunkLoadError|module script/i.test(message)) return '页面资源加载失败，请刷新页面后重试'
  if (/Failed to fetch|NetworkError|network request failed|ERR_NETWORK|ERR_CONNECTION|Load failed|fetch failed/i.test(message)) return '无法连接平台服务，请检查网络连接'
  if (/NotAllowedError|play\(\).*user.*interact|user.*interact.*document/i.test(message)) return '浏览器阻止了自动播放，请点击播放后重试'
  if (/NotSupportedError|MEDIA_ERR_SRC_NOT_SUPPORTED|unsupported.*codec|codec.*not supported/i.test(message)) return '浏览器不支持此视频格式，请使用桌面客户端'
  if (/Failed to load|MEDIA_ERR|MediaError|DemuxException|IOException|FormatError|decode.*error/i.test(message)) return '视频加载失败，请重新连接；持续失败时请检查设备状态'
  if (/timeout|timed out|TimeoutError/i.test(message)) return '请求超时，请稍后重试'
  if (/AbortError|operation was aborted|request.*aborted/i.test(message)) return '操作已取消'
  if (/ENOSPC|no space left|disk.*full/i.test(message)) return '存储空间不足，请联系管理员清理导出空间'
  if (/Unexpected token|Unexpected end of JSON|JSON.*parse/i.test(message)) return '服务端返回了无效数据，请稍后重试'
  if (/[\u3400-\u9fff]/.test(message)) return message
  const statuses: Record<number, string> = { 401: '登录已过期，请重新登录', 403: '没有执行此操作的权限', 404: '请求的资源不存在或已删除', 409: '资源状态已变化，请刷新后重试', 413: '上传文件超过允许大小', 429: '当前资源已达配额，请稍后重试', 502: '平台服务暂时不可用，请稍后重试', 503: '平台服务暂时不可用，请稍后重试' }
  return statuses[status || 0] || '操作失败，请稍后重试'
}
export function errorMessage(error: unknown) {
  if (error instanceof Error) {
    const trace = 'traceId' in error && error.traceId ? `（追踪编号：${error.traceId}）` : ''
    const status = 'status' in error && typeof error.status === 'number' ? error.status : undefined
    return `${localizedError(error.message, status)}${trace}`
  }
  return localizedError(error)
}
export function timeRange(range: [Date, Date] | null, maxHours?: number): { start: string; end: string } {
  if (!range || range.some(date => !Number.isFinite(date.getTime()))) throw new Error('请选择有效的开始与结束时间')
  const duration = range[1].getTime() - range[0].getTime()
  if (duration <= 0) throw new Error('结束时间必须晚于开始时间')
  if (maxHours && duration > maxHours * 3600000) throw new Error(`单次时间范围不能超过 ${maxHours} 小时`)
  return { start: range[0].toISOString(), end: range[1].toISOString() }
}
export function safeRedirect(value: unknown) { return typeof value === 'string' && /^\/app(?:\/|$)/.test(value) ? value : '/app' }
export function safeDownloadUrl(value: string | undefined, fallback: string) {
  if (!value) return fallback
  try { const url = new URL(value, window.location.origin); return ['https:', 'http:'].includes(url.protocol) ? url.href : fallback } catch { return fallback }
}
