import http from 'node:http'

// 仅监听本机，提供视口验收数据，不连接设备或生产数据库。
const now = new Date().toISOString()
const channels = Array.from({ length: 16 }, (_, index) => ({ id: index + 1, channelNumber: index + 1, enabled: true, online: index % 4 !== 3, name: `测试车间机组${index + 1}监控通道`, model: 'DS-2DC4220IW-D', unitId: index < 8 ? 1 : null, ptzCapable: true }))
const permissions = ['device.read', 'channel.read', 'live.view', 'playback.view', 'ptz.control', 'alarm.read', 'alarm.ack', 'statistics.read', 'area.read', 'area.manage', 'channel.assign', 'user.manage', 'role.manage', 'desktop.release.manage']
const device = { id: 1, deviceId: 1, deviceKey: '测试录像机', model: 'DS-A72124R', serialNumber: 'QA-LOCAL-ONLY', ip: '127.0.0.1', servicePort: 8000, status: 'online', digitalChannels: 16, analogChannels: 0, alarmInputCount: 4, alarmOutputCount: 2, lastSeenAt: now }
const alarms = Array.from({ length: 8 }, (_, index) => ({ id: index + 1, eventType: index % 2 ? '移动侦测' : '视频信号丢失', occurredAt: now, state: index % 3 ? 'new' : 'resolved', payload: '{}', channelId: index + 1, channelNumber: index + 1, imageAvailable: false }))
const server = http.createServer(async (request, response) => {
  const url = new URL(request.url, 'http://127.0.0.1')
  const path = url.pathname
  const chunks = []
  for await (const chunk of request) chunks.push(chunk)
  const raw = Buffer.concat(chunks).toString()
  let body = {}
  try { if (raw) body = JSON.parse(raw) } catch { }
  const send = (value, status = 200) => { response.writeHead(status, { 'Content-Type': 'application/json' }); response.end(value === undefined ? undefined : JSON.stringify(value)) }
  const user = { id: 1, username: 'qa', displayName: '本地验收', permissions }
  if (path === '/api/auth/login' || path === '/api/auth/refresh') return send({ accessToken: 'local-qa-only', expiresAt: new Date(Date.now() + 8 * 3600000).toISOString(), user })
  if (path === '/api/desktop-releases/latest') return send({}, 404)
  if (path.startsWith('/hubs/')) return send({ error: '本地视口验收不提供 SignalR 服务' }, 503)
  if (!request.headers.authorization) return send({}, 401)
  if (path === '/api/auth/me') return send(user)
  if (path === '/api/auth/logout') return send(undefined, 204)
  if (path === '/api/channels') return send(channels)
  if (path === '/api/devices') return send(device)
  if (path === '/api/stats') return send({ users: 3, roles: 2, workshops: 1, areas: 1, units: 1, channels: 16, alarmsToday: 8, unacknowledgedAlarms: 5, activeLiveSessions: 0 })
  if (path === '/api/device-stats') return send({ ...device, deviceCount: 1, onlineDevices: 1, offlineDevices: 0, channels: 16, onlineChannels: 12, offlineChannels: 4, alarmsToday: 8, unacknowledgedAlarms: 5, onlineTrend: Array.from({ length: 24 }, (_, index) => ({ timestamp: new Date(Date.now() - index * 3600000).toISOString(), online: 12, total: 16 })) })
  if (path === '/api/system-stats') return send({ hostName: '本地验收', osDescription: 'Windows x64', architecture: 'X64', processorCount: 8, serverTime: now, uptimeSeconds: 600, loadAverage: { one: 1, five: 1, fifteen: 1, percent: 12 }, memory: { totalBytes: 16000000000, availableBytes: 8000000000, usedBytes: 8000000000, usedPercent: 50 }, disk: { path: '/', totalBytes: 200000000000, freeBytes: 100000000000, usedBytes: 100000000000, usedPercent: 50 }, process: { workingSetBytes: 200000000, cpuSeconds: 12 }, network: { receivedBytes: 1000000, transmittedBytes: 1000000, receivedBytesPerSecond: 1000, transmittedBytesPerSecond: 2000 } })
  if (path === '/api/alarms') return send(alarms.filter(item => (!url.searchParams.get('state') || item.state === url.searchParams.get('state')) && (!url.searchParams.get('channel') || item.channelNumber === Number(url.searchParams.get('channel'))) && (!url.searchParams.get('eventType') || item.eventType === url.searchParams.get('eventType'))))
  if (/^\/api\/alarms\/\d+$/.test(path)) return send({ ...alarms[0], imageLength: 0 })
  if (path === '/api/workshops') return send([[1, '测试车间', 'QA', 'active']])
  if (path === '/api/areas') return send([[1, 1, '测试区域', 'A', 'active']])
  if (path === '/api/units') return send([[1, 1, '测试机组', 'U', 'active']])
  if (path === '/api/channels/unassigned') return send(channels.slice(8).map(item => [item.id, 1, item.channelNumber, item.name]))
  if (path === '/api/users') return send([{ ...user, status: 'active', phone: null, lastLoginAt: now, roleIds: [1], roleNames: ['测试管理员'] }])
  if (path === '/api/roles') return send([{ id: 1, name: '测试管理员', code: 'qa', status: 'active', userCount: 1, permissionCodes: permissions }])
  if (path === '/api/permissions') return send(permissions.map(code => ({ code, name: code, resourceType: code.split('.')[0], operationType: code.split('.')[1] })))
  if (path === '/api/desktop-releases') return send([])
  if (path === '/api/recordings/search') return send([{ fileName: '测试录像.mp4', start: body.start, end: body.end, fileSize: 1234567, fileType: 1, streamType: 2, fileIndex: 1 }])
  if (path.startsWith('/api/live-sessions') || path.startsWith('/api/playback-sessions')) return send({ error: '本地验收未连接真实媒体服务' }, 503)
  return send([], 200)
})
server.listen(5186, '127.0.0.1', () => console.log('本地视口验收 API：http://127.0.0.1:5186，测试账号：qa'))
