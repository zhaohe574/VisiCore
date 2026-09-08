import type { Channel } from '../api'
export function layoutSlots(ids: (number | null | undefined)[], count: number): (number | null)[] {
  return Array.from({ length: count }, (_, index) => ids[index] ?? null)
}
export function captureLayout(channels: (Channel | undefined)[], count: number) {
  return layoutSlots(channels.map(channel => channel?.id ?? null), count)
}
export function restoreLayout(ids: (number | null)[], count: number, channels: Channel[]): (Channel | undefined)[] {
  const byId = new Map(channels.map(channel => [channel.id, channel]))
  return layoutSlots(ids, count).map(id => id === null ? undefined : byId.get(id))
}
export function patrolChannels(ids: (number | null)[]): number[] { return ids.filter((id): id is number => id !== null) }
