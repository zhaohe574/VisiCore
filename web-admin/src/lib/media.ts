import type { Channel, TimeSegment } from '../types'
export function patrolBatch(ids: number[], offset: number, count: number) {
  if (!ids.length) return []
  return Array.from({ length: Math.min(count, ids.length) }, (_, index) => ids[(offset + index) % ids.length])
}
export function timelineSegments(segments: TimeSegment[], start: string, end: string) {
  const from = Date.parse(start), to = Date.parse(end), duration = to - from
  if (!Number.isFinite(duration) || duration <= 0) return []
  return segments.map(segment => ({ from: Math.max(from, Date.parse(segment.start)), to: Math.min(to, Date.parse(segment.end)) }))
    .filter(segment => Number.isFinite(segment.from) && Number.isFinite(segment.to) && segment.to > segment.from)
    .sort((a, b) => a.from - b.from)
    .map(segment => ({ left: (segment.from - from) / duration * 100, width: (segment.to - segment.from) / duration * 100 }))
}
export function channelOnline(channel: Channel) { return ['online', 'active'].includes(channel.status) }
export function sameSource(a: { channelId: number; streamType: number }, b: { channelId: number; streamType: number }) { return a.channelId === b.channelId && a.streamType === b.streamType }
