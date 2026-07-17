import type { LoginResponse } from './types'

const tokenKey = 'video-platform-admin-token'
const userKey = 'video-platform-admin-user'
const passwordChangeRequiredKey = 'video-platform-admin-password-change-required'

export const session = {
  token: () => sessionStorage.getItem(tokenKey),
  username: () => sessionStorage.getItem(userKey),
  requiresPasswordChange: () => sessionStorage.getItem(passwordChangeRequiredKey) === 'true',
  save: (login: LoginResponse) => {
    sessionStorage.setItem(tokenKey, login.accessToken)
    sessionStorage.setItem(userKey, login.username)
    sessionStorage.setItem(passwordChangeRequiredKey, String(login.requiresPasswordChange))
  },
  clear: () => {
    sessionStorage.removeItem(tokenKey)
    sessionStorage.removeItem(userKey)
    sessionStorage.removeItem(passwordChangeRequiredKey)
  }
}

export class ApiError extends Error {
  constructor(public status: number, message: string) {
    super(message)
  }
}

type ApiErrorBody = { message?: string; detail?: string; title?: string; errors?: Record<string, string[]> }

function isJsonResponse(response: Response) {
  return response.headers.get('Content-Type')?.toLowerCase().includes('json') ?? false
}

function nonJsonResponseMessage(status: number) {
  if (status >= 200 && status < 300) {
    return '中心服务版本不匹配：接口返回了页面内容，请执行“本地启动 -Build”更新中心 API。'
  }
  return `接口返回了非 JSON 响应（${status}），请检查中心服务版本和路由。`
}

async function readErrorMessage(response: Response, fallback: string) {
  if (!isJsonResponse(response)) return nonJsonResponseMessage(response.status)
  try {
    const body = await response.json() as ApiErrorBody
    return body.message ?? body.detail ?? Object.values(body.errors ?? {}).flat()[0] ?? body.title ?? fallback
  } catch {
    return fallback
  }
}

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const headers = new Headers(init?.headers)
  if (init?.body && !headers.has('Content-Type')) headers.set('Content-Type', 'application/json')
  const token = session.token()
  if (token) headers.set('Authorization', `Bearer ${token}`)
  const response = await fetch(path, { ...init, headers })
  if (response.status === 401) {
    session.clear()
    window.dispatchEvent(new Event('platform-auth-expired'))
  }
  if (!response.ok) {
    const message = await readErrorMessage(response, `请求失败（${response.status}）`)
    throw new ApiError(response.status, message)
  }
  if (response.status === 204) return undefined as T
  if (!isJsonResponse(response)) {
    throw new ApiError(response.status, nonJsonResponseMessage(response.status))
  }
  try {
    return await response.json() as T
  } catch {
    throw new ApiError(response.status, '中心服务返回了无效 JSON，请检查中心服务状态。')
  }
}

async function publicRequest<T>(path: string): Promise<T> {
  const response = await fetch(path, { cache: 'no-store' })
  if (!response.ok) {
    const message = await readErrorMessage(response, `请求失败（${response.status}）`)
    throw new ApiError(response.status, message)
  }
  if (!isJsonResponse(response)) {
    throw new ApiError(response.status, nonJsonResponseMessage(response.status))
  }
  try {
    return await response.json() as T
  } catch {
    throw new ApiError(response.status, '中心服务返回了无效 JSON，请检查中心服务状态。')
  }
}

export const api = {
  login: (username: string, password: string) => request<LoginResponse>('/api/v1/auth/login', {
    method: 'POST', body: JSON.stringify({ username, password })
  }),
  changePassword: (currentPassword: string, newPassword: string) => request<void>('/api/v1/auth/password', {
    method: 'PUT', body: JSON.stringify({ currentPassword, newPassword })
  }),
  logout: () => request<void>('/api/v1/auth/logout', { method: 'POST' }),
  get: <T>(path: string) => request<T>(path),
  post: <T>(path: string, body?: unknown) => request<T>(path, { method: 'POST', body: body === undefined ? undefined : JSON.stringify(body) }),
  put: <T>(path: string, body: unknown) => request<T>(path, { method: 'PUT', body: JSON.stringify(body) }),
  patch: <T>(path: string, body: unknown) => request<T>(path, { method: 'PATCH', body: JSON.stringify(body) }),
  delete: <T>(path: string) => request<T>(path, { method: 'DELETE' }),
  download: async (path: string) => {
    const headers = new Headers()
    const token = session.token()
    if (token) headers.set('Authorization', `Bearer ${token}`)
    const response = await fetch(path, { headers })
    if (response.status === 401) {
      session.clear()
      window.dispatchEvent(new Event('platform-auth-expired'))
    }
    if (!response.ok) {
      let message = `下载失败（${response.status}）`
      try {
        const body = await response.json() as { message?: string; detail?: string; title?: string }
        message = body.message ?? body.detail ?? body.title ?? message
      } catch {
        // 非 JSON 错误保留通用提示。
      }
      throw new ApiError(response.status, message)
    }
    const disposition = response.headers.get('Content-Disposition') ?? ''
    const match = /filename="?([^";]+)"?/i.exec(disposition)
    return { blob: await response.blob(), fileName: match?.[1] ?? '录像导出.mp4' }
  }
}

export const publicApi = {
  get: <T>(path: string) => publicRequest<T>(path)
}

export function formatTime(value?: string | null) {
  if (!value) return '—'
  return new Intl.DateTimeFormat('zh-CN', { dateStyle: 'medium', timeStyle: 'medium', hour12: false }).format(new Date(value))
}

export function connectivityLabel(value: number | string) {
  const labels: Record<string, string> = { '0': '未知', '1': '在线', '2': '疑似离线', '3': '离线', '4': '恢复中', Unknown: '未知', Online: '在线', SuspectedOffline: '疑似离线', Offline: '离线', Recovering: '恢复中' }
  return labels[String(value)] ?? String(value)
}
