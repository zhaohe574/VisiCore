import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import { test } from 'node:test'
import { transform } from '../web-admin/node_modules/esbuild/lib/main.js'

const storage = new Map()
globalThis.localStorage = { getItem: key => storage.get(key) ?? null, setItem: (key, value) => storage.set(key, value), removeItem: key => storage.delete(key) }
globalThis.window = new EventTarget()
const source = await readFile(new URL('../web-admin/src/api.ts', import.meta.url), 'utf8')
const compiled = await transform(source, { loader: 'ts', format: 'esm', define: { 'import.meta.env.VITE_API_BASE': '""' } })
const { api, refreshSession } = await import(`data:text/javascript;base64,${Buffer.from(compiled.code).toString('base64')}`)
const json = (value, status = 200) => new Response(JSON.stringify(value), { status })

await test('并发请求只轮换一次令牌，迟到的 401 使用新令牌重试', async () => {
  storage.set('video-platform-token', 'old')
  let refreshes = 0
  globalThis.fetch = async (path, init) => {
    if (path === '/api/auth/refresh') { refreshes++; await new Promise(resolve => setTimeout(resolve, 15)); return json({ accessToken: 'new' }) }
    if (init.headers.get('Authorization') === 'Bearer old') {
      await new Promise(resolve => setTimeout(resolve, path.endsWith('slow') ? 40 : 0))
      return json({}, 401)
    }
    return json({ ok: true })
  }
  const result = await Promise.all([api('/api/fast'), api('/api/slow')])
  assert.deepEqual(result, [{ ok: true }, { ok: true }])
  assert.equal(refreshes, 1)
  assert.equal(storage.get('video-platform-token'), 'new')
})

await test('退出后到达的刷新响应不能恢复登录', async () => {
  storage.set('video-platform-token', 'old')
  let finish
  globalThis.fetch = () => new Promise(resolve => { finish = resolve })
  const request = refreshSession()
  storage.delete('video-platform-token')
  finish(json({ accessToken: 'late' }))
  assert.equal(await request, null)
  assert.equal(storage.has('video-platform-token'), false)
})

await test('主动续期去重，网络失败保留原会话', async () => {
  storage.set('video-platform-token', 'valid')
  let refreshes = 0
  globalThis.fetch = async () => { refreshes++; throw new TypeError('测试断网') }
  const result = await Promise.allSettled([refreshSession(), refreshSession()])
  assert.equal(refreshes, 1)
  assert.ok(result.every(item => item.status === 'rejected'))
  assert.equal(storage.get('video-platform-token'), 'valid')
})

await test('取消请求保留 AbortError，不伪装成网络故障', async () => {
  globalThis.fetch = async () => { throw new DOMException('已取消', 'AbortError') }
  await assert.rejects(api('/api/alarms'), error => error.name === 'AbortError')
})

await test('失效会话清除登录并通知界面', async () => {
  let expired = 0
  window.addEventListener('platform-auth-expired', () => expired++, { once: true })
  globalThis.fetch = async () => json({}, 401)
  await assert.rejects(refreshSession(), /401/)
  assert.equal(expired, 1)
  assert.equal(storage.has('video-platform-token'), false)
})
