import { describe, expect, it } from 'vitest'
import { releasePackage } from './releases'
import type { PublicRelease } from '../api'
describe('公开安装包选择', () => {
  const release = { id: 1, version: '2.0.0', fileName: '京华客户端.msi' } as PublicRelease
  it('兼容没有packages的单文件发布接口', () => { expect(releasePackage(release, 'msi')?.id).toBe(1); expect(releasePackage(release, 'zip')).toBeUndefined() })
  it('分别提供同一版本MSI与ZIP入口', () => { const result = { ...release, packages: [release, { ...release, id: 2, fileName: '京华客户端.ZIP' }] }; expect(releasePackage(result, 'msi')?.id).toBe(1); expect(releasePackage(result, 'zip')?.id).toBe(2) })
  it('不把其他版本或不存在的安装包显示为可下载', () => { expect(releasePackage(undefined, 'msi')).toBeUndefined(); expect(releasePackage({ ...release, packages: [{ ...release, version: '1.0.0', fileName: 'old.zip' }] }, 'zip')).toBeUndefined() })
})
