import type { Section } from './types'

const routeBySection: Record<Section, string> = {
  overview: '/admin',
  observability: '/admin/observability',
  credentials: '/admin/credentials',
  edgeAgents: '/admin/edge-agents',
  operations: '/admin/operations',
  backups: '/admin/backups',
  https: '/admin/https',
  assets: '/admin/assets',
  plugins: '/admin/plugins',
  access: '/admin/access',
  workers: '/admin/workers',
  exports: '/admin/exports',
  alerts: '/admin/alerts',
  audit: '/admin/audit'
}

const sectionByRoute = new Map(Object.entries(routeBySection).map(([section, path]) => [path, section as Section]))

export function sectionFromPath(pathname: string): Section {
  const normalized = pathname.replace(/\/+$/, '') || '/admin'
  return sectionByRoute.get(normalized) ?? 'overview'
}

export function sectionPath(section: Section) {
  return routeBySection[section]
}
