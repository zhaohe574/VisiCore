import type { PublicRelease, Release } from '../api'
export function releasePackage(release: PublicRelease | undefined, kind: 'msi' | 'zip'): Release | undefined {
  if (!release) return undefined
  const packages = release.packages?.length ? release.packages : [release]
  return packages.find(item => item.version === release.version && item.fileName.toLowerCase().endsWith(`.${kind}`))
}
