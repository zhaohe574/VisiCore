import { useEffect, useMemo, useRef, useState } from 'react'
import { AlertTriangle, ChevronLeft, ChevronRight, Clock3, MapPin, RefreshCw, Search } from 'lucide-react'
import { ApiError, formatTime, publicApi } from './api'
import type { PublicOfflineDeviceList } from './types'

const deviceTypeLabels: Record<string, string> = {
  camera: '摄像头',
  recorder: '录像机',
  matrix: '视频矩阵',
  encoder: '编码器',
  decoder: '解码器',
  gateway: '视频网关',
  other: '接入设备'
}

export function PublicOfflineDevicesPage() {
  const [region, setRegion] = useState('')
  const [name, setName] = useState('')
  const [debouncedName, setDebouncedName] = useState('')
  const [deviceType, setDeviceType] = useState('')
  const [page, setPage] = useState(1)
  const [refreshVersion, setRefreshVersion] = useState(0)
  const [data, setData] = useState<PublicOfflineDeviceList | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const requestVersion = useRef(0)

  useEffect(() => {
    const originalTitle = document.title
    document.title = '掉线设备 - 企业视频平台'
    return () => { document.title = originalTitle }
  }, [])

  useEffect(() => {
    const timer = window.setTimeout(() => setDebouncedName(name.trim()), 260)
    return () => window.clearTimeout(timer)
  }, [name])

  useEffect(() => {
    const timer = window.setInterval(() => setRefreshVersion(value => value + 1), 60_000)
    return () => window.clearInterval(timer)
  }, [])

  const query = useMemo(() => {
    const params = new URLSearchParams({ page: String(page), pageSize: '50' })
    if (region) params.set('region', region)
    if (debouncedName) params.set('name', debouncedName)
    if (deviceType) params.set('deviceType', deviceType)
    return params.toString()
  }, [region, debouncedName, deviceType, page])

  useEffect(() => {
    const version = ++requestVersion.current
    setLoading(true)
    setError('')
    void publicApi.get<PublicOfflineDeviceList>(`/api/v1/public/offline-devices?${query}`)
      .then(result => {
        if (version !== requestVersion.current) return
        setData(result)
      })
      .catch(reason => {
        if (version !== requestVersion.current) return
        setError(reason instanceof ApiError || reason instanceof Error ? reason.message : '掉线设备列表暂时不可用。')
      })
      .finally(() => {
        if (version === requestVersion.current) setLoading(false)
      })
  }, [query, refreshVersion])

  const totalPages = data ? Math.max(1, Math.ceil(data.total / data.pageSize)) : 1
  const changeRegion = (value: string) => { setRegion(value); setPage(1) }
  const changeDeviceType = (value: string) => { setDeviceType(value); setPage(1) }
  const changeName = (value: string) => { setName(value); setPage(1) }
  const retry = () => setRefreshVersion(value => value + 1)

  return <main className="public-offline-page">
    <header className="public-offline-header">
      <div className="public-offline-header__content">
        <div className="public-brand"><span className="public-brand__mark"><AlertTriangle size={19} /></span><span>企业视频平台</span></div>
        <div className="public-offline-header__title"><div><p>设备状态</p><h1>掉线设备</h1></div><button className="public-refresh-button" type="button" title="刷新列表" aria-label="刷新列表" onClick={retry} disabled={loading}><RefreshCw size={18} className={loading ? 'spin' : ''} /></button></div>
        <div className="public-summary"><span><strong>{data?.total ?? '—'}</strong> 台确认离线</span><small>{data ? `更新于 ${formatTime(data.generatedAt)}` : '正在获取状态'}</small></div>
      </div>
    </header>
    <section className="public-offline-content" aria-live="polite">
      <div className="public-filter-bar">
        <label className="public-filter"><span>区域</span><select value={region} onChange={event => changeRegion(event.target.value)}><option value="">全部区域</option>{(data?.regions ?? []).map(item => <option key={item} value={item}>{item}</option>)}</select></label>
        <label className="public-filter public-filter--search"><span>名称</span><div><Search size={16} /><input value={name} onChange={event => changeName(event.target.value)} placeholder="设备名称" maxLength={128} /></div></label>
        <label className="public-filter"><span>设备类型</span><select value={deviceType} onChange={event => changeDeviceType(event.target.value)}><option value="">全部类型</option>{(data?.deviceTypes ?? []).map(item => <option key={item} value={item}>{deviceTypeLabel(item)}</option>)}</select></label>
      </div>

      {error && <div className="public-error"><AlertTriangle size={18} /><span>{error}</span><button type="button" onClick={retry}>重试</button></div>}
      {!error && !data && <div className="public-loading"><RefreshCw size={19} className="spin" /><span>正在加载</span></div>}
      {!error && data && <section className="public-device-list" aria-label="掉线设备列表">
        <div className="public-device-list__head"><span>设备名称</span><span>区域</span><span>设备类型</span><span>掉线时长</span></div>
        {data.items.length === 0
          ? <div className="public-empty"><Clock3 size={23} /><strong>暂无确认掉线设备</strong></div>
          : data.items.map((item, index) => <article className="public-device-row" key={`${item.region}-${item.deviceType}-${item.name}-${index}`}>
            <div className="public-device-row__name"><span className="public-device-label">设备名称</span><strong>{item.name}</strong></div>
            <div className="public-device-row__region"><span className="public-device-label">区域</span><span><MapPin size={15} />{item.region}</span></div>
            <div><span className="public-device-label">设备类型</span><span className="public-device-type">{deviceTypeLabel(item.deviceType)}</span></div>
            <div className="public-device-row__duration"><span className="public-device-label">掉线时长</span><strong><Clock3 size={16} />{durationLabel(item.offlineDurationSeconds)}</strong></div>
          </article>)}
      </section>}

      {data && data.total > 0 && <nav className="public-pagination" aria-label="掉线设备分页"><button type="button" title="上一页" aria-label="上一页" onClick={() => setPage(value => Math.max(1, value - 1))} disabled={page <= 1}><ChevronLeft size={18} /></button><span>第 {page} / {totalPages} 页</span><button type="button" title="下一页" aria-label="下一页" onClick={() => setPage(value => Math.min(totalPages, value + 1))} disabled={page >= totalPages}><ChevronRight size={18} /></button></nav>}
    </section>
  </main>
}

function deviceTypeLabel(value: string) {
  return deviceTypeLabels[value] ?? '接入设备'
}

function durationLabel(totalSeconds: number) {
  const seconds = Math.max(0, Math.floor(totalSeconds))
  if (seconds < 60) return '不足 1 分钟'
  const minutes = Math.floor(seconds / 60)
  if (minutes < 60) return `${minutes} 分钟`
  const hours = Math.floor(minutes / 60)
  const remainingMinutes = minutes % 60
  if (hours < 24) return remainingMinutes === 0 ? `${hours} 小时` : `${hours} 小时 ${remainingMinutes} 分钟`
  const days = Math.floor(hours / 24)
  const remainingHours = hours % 24
  return remainingHours === 0 ? `${days} 天` : `${days} 天 ${remainingHours} 小时`
}
