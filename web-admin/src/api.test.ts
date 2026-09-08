import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

const json = (data: unknown, status = 200) => new Response(JSON.stringify(data), { status, headers: { 'Content-Type': 'application/json' } })
let calls: { path: string; init: RequestInit }[]
beforeEach(() => { vi.resetModules(); calls = []; const events = new EventTarget(); vi.stubGlobal('window', events); vi.stubGlobal('CustomEvent', class extends Event { detail: unknown; constructor(name: string, init?: { detail?: unknown }) { super(name); this.detail = init?.detail } }) })
afterEach(() => vi.unstubAllGlobals())
function mockFetch(handler: (path: string, init: RequestInit) => Response | Promise<Response>) { vi.stubGlobal('fetch', vi.fn(async (path: string, init: RequestInit = {}) => { calls.push({ path, init }); return handler(path, init) })) }
describe('Cookie 与 CSRF 契约', () => {
  it('登录前获取令牌，身份变化后重新获取，不发送Authorization', async () => { let identity = 'anonymous'; mockFetch((path) => { if (path.endsWith('/auth/csrf')) return json({ token: identity }); if (path.endsWith('/auth/login')) { identity = 'signed-in'; return json({ user: { id: 1 }, expiresAt: '2030-01-01' }) }; if (path.endsWith('/auth/logout')) { identity = 'signed-out'; return new Response(null, { status: 204 }) }; return json({}) }); const { authApi, api } = await import('./api'); await authApi.login('admin', 'test-secret'); await api('/devices', { method: 'POST', body: '{}' }); await authApi.logout(); await authApi.login('admin', 'test-secret'); const writes = calls.filter(call => call.init.method === 'POST'); expect(new Headers(writes[0].init.headers).get('X-CSRF-Token')).toBe('anonymous'); expect(new Headers(writes[1].init.headers).get('X-CSRF-Token')).toBe('signed-in'); expect(new Headers(writes[3].init.headers).get('X-CSRF-Token')).toBe('signed-out'); for (const call of calls) { expect(call.init.credentials).toBe('include'); expect(new Headers(call.init.headers).has('Authorization')).toBe(false) }; const body = JSON.parse(writes[0].init.body as string); expect(body.clientType).toBe('web'); expect(body.clientVersion).toBe('2.0.0') })
  it('明确CSRF失败重取令牌并只重试一次', async () => { let tokens = 0, writes = 0; mockFetch(path => path.endsWith('/auth/csrf') ? json({ token: `csrf-${++tokens}` }) : ++writes === 1 ? json({ code: 'auth.csrf', message: '校验过期' }, 400) : json({ id: 1 })); const { api } = await import('./api'); expect(await api('/devices', { method: 'POST', body: '{}' })).toEqual({ id: 1 }); expect(tokens).toBe(2); expect(writes).toBe(2) })
  it('业务403不触发重试', async () => { mockFetch(path => path.endsWith('/auth/csrf') ? json({ token: 'csrf' }) : json({ code: 'access.denied', message: '无权限', traceId: 'trace-1' }, 403)); const { api } = await import('./api'); await expect(api('/devices', { method: 'DELETE' })).rejects.toMatchObject({ status: 403, traceId: 'trace-1' }); expect(calls.filter(call => call.path.endsWith('/devices'))).toHaveLength(1) })
  it('并发401合并续期，原请求成功重放', async () => { let authorized = false; mockFetch(async path => { if (path.endsWith('/auth/csrf')) return json({ token: 'csrf' }); if (path.endsWith('/auth/refresh')) { await new Promise(resolve => setTimeout(resolve, 10)); authorized = true; return json({ user: { id: 1 }, expiresAt: '2030-01-01' }) }; return authorized ? json({ ok: true }) : json({ code: 'auth.expired', message: '会话过期' }, 401) }); const { api } = await import('./api'); const values = await Promise.all([api('/devices'), api('/channels')]); expect(values).toEqual([{ ok: true }, { ok: true }]); expect(calls.filter(call => call.path.endsWith('/auth/refresh'))).toHaveLength(1) })
  it('续期失败通知退出且不会无限重试', async () => { const expired = vi.fn(); window.addEventListener('platform-auth-expired', expired); mockFetch(path => path.endsWith('/auth/csrf') ? json({ token: 'csrf' }) : json({ code: 'auth.expired', message: '会话过期' }, 401)); const { api } = await import('./api'); await expect(api('/devices')).rejects.toMatchObject({ status: 401 }); expect(calls.filter(call => call.path.endsWith('/auth/refresh'))).toHaveLength(1); expect(expired).toHaveBeenCalledOnce() })
  it('上传表单保留浏览器生成的multipart边界并携带CSRF', async () => { mockFetch(path => path.endsWith('/auth/csrf') ? json({ token: 'csrf' }) : json({ id: 1 })); const { workflowApi } = await import('./api'); const form = new FormData(); form.append('version', '2.0.0'); await workflowApi.uploadRelease(form); const upload = calls.at(-1)!; expect(upload.init.body).toBe(form); expect(new Headers(upload.init.headers).has('Content-Type')).toBe(false); expect(new Headers(upload.init.headers).get('X-CSRF-Token')).toBe('csrf') })
})
describe('分页和媒体参数', () => {
  it('遍历全部通道，不受单页上限限制', async () => { const { allPages } = await import('./api'); const loader = vi.fn(async ({ page }) => ({ items: page === 1 ? [1, 2] : [3], total: 3, page, pageSize: 2 })); expect(await allPages(loader)).toEqual([1, 2, 3]); expect(loader).toHaveBeenCalledTimes(2) })
  it('保留false与0筛选值', async () => { const { queryString } = await import('./api'); expect(queryString({ online: false, page: 0, search: '', deviceId: undefined })).toBe('?online=false&page=0') })
  it('创建媒体只发送全局channelId和browser配置', async () => { mockFetch(path => path.endsWith('/auth/csrf') ? json({ token: 'csrf' }) : json({ id: 'stream' })); const { mediaApi } = await import('./api'); await mediaApi.live(812, 2); const data = JSON.parse(calls.at(-1)!.init.body as string); expect(data).toEqual({ channelId: 812, streamType: 2, profile: 'browser' }); await mediaApi.control('playback-1', { action: 'seek', position: '2026-09-07T00:00:00Z' }); expect(JSON.parse(calls.at(-1)!.init.body as string)).toEqual({ action: 'seek', position: '2026-09-07T00:00:00Z' }) })
})
describe('驱动插件管理 API 契约', () => {
  it('支持驱动插件的列表、安装包上传、端点登记、探测、导出与删除', async () => {
    mockFetch(path => path.endsWith('/auth/csrf') ? json({ token: 'csrf-token' }) : json({ ok: true }))
    const { managementApi } = await import('./api')
    
    // 列表
    await managementApi.plugins()
    expect(calls.at(-1)!.path.endsWith('/plugins')).toBe(true)

    // 安装包上传
    const fd = new FormData()
    fd.append('file', new Blob(['fake-zip']), 'test.zip')
    await managementApi.installPlugin(fd)
    const installCall = calls.at(-1)!
    expect(installCall.path.endsWith('/plugins/install')).toBe(true)
    expect(installCall.init.body).toBe(fd)
    expect(new Headers(installCall.init.headers).get('X-CSRF-Token')).toBe('csrf-token')

    // 端点登记
    await managementApi.createPlugin({
      id: 'jovision',
      name: '中维世纪驱动',
      vendor: 'Jovision',
      version: '1.0.0',
      endpointUrl: 'http://127.0.0.1:5093'
    })
    const createCall = calls.at(-1)!
    expect(createCall.path.endsWith('/plugins')).toBe(true)
    expect(JSON.parse(createCall.init.body as string).id).toBe('jovision')

    // 探测
    await managementApi.probePlugin('http://127.0.0.1:5092')
    expect(calls.at(-1)!.path.endsWith('/plugins/probe')).toBe(true)

    // 导出 URL
    expect(managementApi.exportPluginUrl('hikvision')).toContain('/api/v2/plugins/hikvision/export')

    // 删除
    await managementApi.deletePlugin('test-driver')
    const deleteCall = calls.at(-1)!
    expect(deleteCall.path.endsWith('/plugins/test-driver')).toBe(true)
    expect(deleteCall.init.method).toBe('DELETE')
  })
})
