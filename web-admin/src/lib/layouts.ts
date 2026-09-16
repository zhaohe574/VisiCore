import type { Channel } from '../api'

/** 允许的分屏档位，与 database/v2/010_layout25_and_permissions.sql 的约束一致。 */
export type LayoutCount = 1 | 4 | 6 | 8 | 9 | 10 | 16 | 25

/**
 * 分屏档位定义。8（1+7）与 10（1+9）是 iVMS-4200 的「一大屏 + 多小屏」聚焦档位，25 为 5×5 等分档位：
 * 桌面端按格位跨度渲染，网页端按等分网格渲染并给出友好标签。
 * columns 必须是整数——原实现用 Math.sqrt(count)，8 与 10 会得到小数导致网格失效。
 */
export const layoutOptions: { value: LayoutCount; label: string; columns: number }[] = [
  { value: 1, label: '1', columns: 1 },
  { value: 4, label: '4', columns: 2 },
  { value: 9, label: '9', columns: 3 },
  { value: 16, label: '16', columns: 4 },
  { value: 25, label: '25', columns: 5 },
  { value: 8, label: '1+7', columns: 4 },
  { value: 10, label: '1+9', columns: 5 }
]

export const layoutCounts: LayoutCount[] = layoutOptions.map(option => option.value)

/** 取档位对应的网格列数；未知档位回退到最接近的整数平方根。 */
export function layoutColumns(count: number): number {
  return layoutOptions.find(option => option.value === count)?.columns ?? Math.max(1, Math.round(Math.sqrt(count)))
}

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
