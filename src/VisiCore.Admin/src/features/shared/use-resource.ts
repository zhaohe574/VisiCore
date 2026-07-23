import { useCallback, useEffect, useState } from 'react'

/** 业务页统一的数据加载、错误和重试状态。 */
export function useResource<T>(loader: () => Promise<T>, dependencies: readonly unknown[] = []) {
  const [data, setData] = useState<T | null>(null)
  const [error, setError] = useState('')
  const [loading, setLoading] = useState(true)
  const refresh = useCallback(async () => {
    setLoading(true)
    setError('')
    try {
      setData(await loader())
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : '加载失败')
    } finally {
      setLoading(false)
    }
  }, dependencies) // eslint-disable-line react-hooks/exhaustive-deps

  useEffect(() => { void refresh() }, [refresh])
  return { data, error, loading, refresh }
}
