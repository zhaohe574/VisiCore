export const assetLabels: Record<string, string> = {
  channel: '摄像头', device: '录像机', computer: '电脑', 'terminal-box': '终端箱', onu: 'ONU',
  'network-device': '网络设备', 'virtual-machine': '虚拟机', 'ip-pool': 'IP 池'
}
export const assetRoutes: Record<string, string> = {
  channel: '/app/organization', device: '/app/devices', computer: '/app/computers',
  'terminal-box': '/app/terminal-boxes', onu: '/app/onus', 'network-device': '/app/network-devices',
  'virtual-machine': '/app/network-devices', 'ip-pool': '/app/ip-pools'
}

export function safeSource(value: unknown): string | undefined {
  return typeof value === 'string' && /^\/app(?:\/|\?|$)/.test(value) && !/[\\\r\n]/.test(value) ? value : undefined
}
