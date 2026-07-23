import { RefreshCw } from 'lucide-react'
import { api, formatTime } from '../../api'
import type { PlatformObservability } from '../../types'
import { Button, EmptyState, ErrorState, LoadingState, PageHeader, Panel } from '../../ui'
import { useResource } from '../shared/use-resource'

function entries(values: Record<string, number>) {
  return Object.entries(values).sort(([left], [right]) => left.localeCompare(right, 'zh-CN'))
}

export function ObservabilityPage() {
  const resource = useResource(() => api.get<PlatformObservability>('/api/v1/admin/observability/overview'), [])
  if (resource.loading) return <LoadingState />
  if (resource.error || !resource.data) return <ErrorState message={resource.error || '无法读取运营指标。'} retry={resource.refresh} />

  const overview = resource.data
  return <>
    <PageHeader title="运行指标" description="会话、边缘心跳、升级失败码和备份结果的实时聚合。" actions={<Button variant="secondary" icon={<RefreshCw size={16} />} onClick={() => void resource.refresh()}>刷新</Button>} />
    <div className="metric-grid operations-metrics">
      <article className="metric"><span>活跃媒体会话</span><strong>{overview.activeStreamSessions}</strong><small>未撤销且未过期</small></article>
      <article className="metric"><span>在线边缘节点</span><strong>{overview.onlineEdgeAgents}</strong><small>两分钟内有心跳</small></article>
      <article className="metric metric--alert"><span>超时边缘节点</span><strong>{overview.staleEdgeAgents}</strong><small>超过两分钟未心跳</small></article>
    </div>
    <div className="split-grid">
      <Panel title="升级失败码"><MetricTable empty="暂无升级失败记录" rows={entries(overview.upgradeFailures)} /></Panel>
      <Panel title="备份结果"><MetricTable empty="暂无备份结果" rows={entries(overview.backupResults)} /></Panel>
    </div>
    <p className="page-note">采集时间：{formatTime(overview.collectedAt)}</p>
  </>
}

function MetricTable({ rows, empty }: { rows: Array<[string, number]>; empty: string }) {
  if (rows.length === 0) return <EmptyState title={empty} />
  return <div className="table-scroll"><table><thead><tr><th>类别</th><th>数量</th></tr></thead><tbody>{rows.map(([name, count]) => <tr key={name}><td><code>{name}</code></td><td><strong>{count}</strong></td></tr>)}</tbody></table></div>
}
