import type { Channel, Computer, NetworkDevice, Organization, OrganizationKind, OrganizationNode, TerminalBoxDto } from '../api'

export interface UnitCascaderOption {
  value: number | string
  label: string
  disabled?: boolean
  children?: UnitCascaderOption[]
  [key: string]: unknown
}

export interface AreaCascaderOption {
  value: number | string
  label: string
  disabled?: boolean
  children?: AreaCascaderOption[]
  [key: string]: unknown
}

/**
 * 组织架构节点按编码标识 (code) 自然排序比较器。
 * 规则：优先按编码标识 code 自然升序排序 (支持 "01", "02", "10" 等数字及英文字母混排)；
 * 若无编码或编码相同，则次级按名称 (name) 排序，最后按 ID 排序。
 */
export function compareByCode<T extends { code?: string | null; name?: string; id?: number }>(a: T, b: T): number {
  const codeA = (a.code || '').trim()
  const codeB = (b.code || '').trim()
  if (codeA && codeB) {
    const cmp = codeA.localeCompare(codeB, undefined, { numeric: true, sensitivity: 'base' })
    if (cmp !== 0) return cmp
  } else if (codeA) {
    return -1 // 有编码的节点优先排在前面
  } else if (codeB) {
    return 1
  }
  const nameA = (a.name || '').trim()
  const nameB = (b.name || '').trim()
  const nameCmp = nameA.localeCompare(nameB, undefined, { numeric: true })
  if (nameCmp !== 0) return nameCmp
  return (a.id || 0) - (b.id || 0)
}

/**
 * 构建车间 -> 区域 -> 单元的三级级联选择树。
 * 叶子节点为单元（value 为数字 ID），上级节点（车间、区域）value 带有前缀防止 ID 碰撞。
 * 仅叶子单元可被最终选中；无下级单元的组织节点被自动禁用以防误选。
 */
export function buildUnitCascaderOptions(org: Organization): UnitCascaderOption[] {
  const { workshops = [], areas = [], units = [] } = org

  // 按区域 ID 映射单元
  const areaUnitsMap = new Map<number, { option: UnitCascaderOption; raw: OrganizationNode }[]>()
  for (const a of areas) {
    areaUnitsMap.set(a.id, [])
  }

  const orphanedUnits: { option: UnitCascaderOption; raw: OrganizationNode }[] = []
  for (const u of units) {
    const unitOption: UnitCascaderOption = {
      value: u.id,
      label: u.name
    }
    if (u.parentId && areaUnitsMap.has(u.parentId)) {
      areaUnitsMap.get(u.parentId)!.push({ option: unitOption, raw: u })
    } else {
      orphanedUnits.push({ option: unitOption, raw: u })
    }
  }

  // 按车间 ID 映射区域
  const workshopAreasMap = new Map<number, { option: UnitCascaderOption; raw: OrganizationNode }[]>()
  for (const w of workshops) {
    workshopAreasMap.set(w.id, [])
  }

  const orphanedAreas: { option: UnitCascaderOption; raw: OrganizationNode }[] = []
  for (const a of areas) {
    const childRawUnits = areaUnitsMap.get(a.id) || []
    childRawUnits.sort((x, y) => compareByCode(x.raw, y.raw))
    const childUnits = childRawUnits.map(x => x.option)
    const hasUnits = childUnits.length > 0
    const areaOption: UnitCascaderOption = {
      value: `area-${a.id}`,
      label: a.name,
      disabled: !hasUnits,
      children: hasUnits ? childUnits : undefined
    }

    if (a.parentId && workshopAreasMap.has(a.parentId)) {
      workshopAreasMap.get(a.parentId)!.push({ option: areaOption, raw: a })
    } else {
      orphanedAreas.push({ option: areaOption, raw: a })
    }
  }

  const result: UnitCascaderOption[] = []

  // 车间节点（按编码标识排序）
  const sortedWorkshops = workshops.slice().sort(compareByCode)
  for (const w of sortedWorkshops) {
    const childRawAreas = workshopAreasMap.get(w.id) || []
    childRawAreas.sort((x, y) => compareByCode(x.raw, y.raw))
    const childAreas = childRawAreas.map(x => x.option)
    const hasAvailableUnits = childAreas.some(a => (a.children?.length ?? 0) > 0)
    result.push({
      value: `workshop-${w.id}`,
      label: w.name,
      disabled: !hasAvailableUnits,
      children: childAreas.length > 0 ? childAreas : undefined
    })
  }

  // 兜底：未归属车间的区域（按编码标识排序）
  if (orphanedAreas.length > 0) {
    orphanedAreas.sort((x, y) => compareByCode(x.raw, y.raw))
    const childAreas = orphanedAreas.map(x => x.option)
    const hasUnits = childAreas.some(a => (a.children?.length ?? 0) > 0)
    result.push({
      value: 'orphaned-areas',
      label: '未归属车间的区域',
      disabled: !hasUnits,
      children: childAreas
    })
  }

  // 兜底：未归属区域的单元（按编码标识排序）
  if (orphanedUnits.length > 0) {
    orphanedUnits.sort((x, y) => compareByCode(x.raw, y.raw))
    result.push({
      value: 'orphaned-units',
      label: '未归属区域的单元',
      children: orphanedUnits.map(x => x.option)
    })
  }

  return result
}

/**
 * 构建车间 -> 区域的二级级联选择树。
 * 叶子节点为区域（value 为数字 ID），顶级节点（车间）value 带有前缀防止 ID 碰撞。
 * 仅叶子区域可被最终选中；无下级区域的车间节点被自动禁用以防误选。
 */
export function buildAreaCascaderOptions(org: Organization): AreaCascaderOption[] {
  const { workshops = [], areas = [] } = org

  // 按车间 ID 映射区域
  const workshopAreasMap = new Map<number, { option: AreaCascaderOption; raw: OrganizationNode }[]>()
  for (const w of workshops) {
    workshopAreasMap.set(w.id, [])
  }

  const orphanedAreas: { option: AreaCascaderOption; raw: OrganizationNode }[] = []
  for (const a of areas) {
    const areaOption: AreaCascaderOption = {
      value: a.id,
      label: a.name
    }
    if (a.parentId && workshopAreasMap.has(a.parentId)) {
      workshopAreasMap.get(a.parentId)!.push({ option: areaOption, raw: a })
    } else {
      orphanedAreas.push({ option: areaOption, raw: a })
    }
  }

  const result: AreaCascaderOption[] = []

  // 车间节点（按编码标识排序）
  const sortedWorkshops = workshops.slice().sort(compareByCode)
  for (const w of sortedWorkshops) {
    const childRawAreas = workshopAreasMap.get(w.id) || []
    childRawAreas.sort((x, y) => compareByCode(x.raw, y.raw))
    const childAreas = childRawAreas.map(x => x.option)
    result.push({
      value: `workshop-${w.id}`,
      label: w.name,
      disabled: childAreas.length === 0,
      children: childAreas.length > 0 ? childAreas : undefined
    })
  }

  // 兜底：未归属车间的区域（按编码标识排序）
  if (orphanedAreas.length > 0) {
    orphanedAreas.sort((x, y) => compareByCode(x.raw, y.raw))
    result.push({
      value: 'orphaned-areas',
      label: '未归属车间的区域',
      children: orphanedAreas.map(x => x.option)
    })
  }

  return result
}

/**
 * 获取区域的完整层级路径：车间 / 区域
 */
export function getAreaHierarchy(org: Organization, areaId?: number | null): string {
  if (!areaId) return '未划拨区域'
  const area = org.areas?.find(a => a.id === areaId)
  if (!area) return `区域 ${areaId}`
  const workshop = org.workshops?.find(w => w.id === area.parentId)
  if (!workshop) return area.name
  return `${workshop.name} / ${area.name}`
}

/**
 * 获取单元的完整层级路径：车间 / 区域 / 单元
 */
export function getUnitHierarchy(org: Organization, unitId?: number | null): string {
  if (!unitId) return '未划拨单元'
  const unit = org.units?.find(u => u.id === unitId)
  if (!unit) return `单元 ${unitId}`
  const area = org.areas?.find(a => a.id === unit.parentId)
  if (!area) return unit.name
  const workshop = org.workshops?.find(w => w.id === area.parentId)
  if (!workshop) return `${area.name} / ${unit.name}`
  return `${workshop.name} / ${area.name} / ${unit.name}`
}

/**
 * 获取单元的上级归属路径：车间 / 区域
 */
export function getUnitParentPath(org: Organization, unitId?: number | null): string {
  if (!unitId) return ''
  const unit = org.units?.find(u => u.id === unitId)
  if (!unit) return ''
  const area = org.areas?.find(a => a.id === unit.parentId)
  if (!area) return ''
  const workshop = org.workshops?.find(w => w.id === area.parentId)
  if (!workshop) return area.name
  return `${workshop.name} / ${area.name}`
}

export interface AreaUnitGroup {
  areaId: number | null
  areaName: string
  workshopName?: string
  areaNode?: OrganizationNode
  units: OrganizationNode[]
}

/**
 * 获取单元所属区域节点
 */
export function getUnitArea(org: Organization, unitId?: number | null): OrganizationNode | undefined {
  if (!unitId) return undefined
  const unit = org.units?.find(u => u.id === unitId)
  if (!unit || !unit.parentId) return undefined
  return org.areas?.find(a => a.id === unit.parentId)
}

/**
 * 获取单元所属车间节点
 */
export function getUnitWorkshop(org: Organization, unitId?: number | null): OrganizationNode | undefined {
  const area = getUnitArea(org, unitId)
  if (!area || !area.parentId) return undefined
  return org.workshops?.find(w => w.id === area.parentId)
}

/**
 * 获取单元名称显示文本
 */
export function getUnitName(org: Organization, unitId?: number | null): string {
  if (!unitId) return '未分配'
  const unit = org.units?.find(u => u.id === unitId)
  return unit ? unit.name : `单元 ${unitId}`
}

/**
 * 将单元按所属区域进行结构化分组
 */
export function groupUnitsByArea(org: Organization): AreaUnitGroup[] {
  const { workshops = [], areas = [], units = [] } = org
  const groups: AreaUnitGroup[] = []
  const areaMap = new Map<number, AreaUnitGroup>()

  const sortedAreas = areas.slice().sort(compareByCode)
  for (const area of sortedAreas) {
    const workshop = workshops.find(w => w.id === area.parentId)
    const grp: AreaUnitGroup = {
      areaId: area.id,
      areaName: area.name,
      workshopName: workshop?.name,
      areaNode: area,
      units: []
    }
    areaMap.set(area.id, grp)
  }

  const unassignedUnits: OrganizationNode[] = []
  for (const unit of units) {
    if (unit.parentId && areaMap.has(unit.parentId)) {
      areaMap.get(unit.parentId)!.units.push(unit)
    } else {
      unassignedUnits.push(unit)
    }
  }

  for (const grp of areaMap.values()) {
    grp.units.sort(compareByCode)
    groups.push(grp)
  }

  if (unassignedUnits.length > 0) {
    unassignedUnits.sort(compareByCode)
    groups.push({
      areaId: null,
      areaName: '未分配区域',
      units: unassignedUnits
    })
  }

  return groups
}

export type OrgNodeKind = 'workshops' | 'areas' | 'units'

export interface OrgNodeAssetCounts {
  channels: number
  computers: number
  terminalBoxes: number
  networkDevices: number
}

export interface OrgAssetSources {
  channels?: Channel[]
  computers?: Computer[]
  terminalBoxes?: TerminalBoxDto[]
  networkDevices?: NetworkDevice[]
}

export interface OrgTreeNode {
  key: string
  id: number
  name: string
  code: string
  kind: OrgNodeKind
  status: string
  parentId: number | null
  channelCount: number
  assetCounts: OrgNodeAssetCounts
  children?: OrgTreeNode[]
  rawNode: OrganizationNode
}

export interface ChannelCounts {
  unitCounts: Map<number, number>
  unassignedCount: number
  totalCount: number
  onlineCount: number
  offlineCount: number
}

/**
 * 统计全量通道中各单元的通道数以及未划拨/在线数
 */
export function calculateOrgChannelCounts(channels: Channel[]): ChannelCounts {
  const unitCounts = new Map<number, number>()
  let unassignedCount = 0
  let onlineCount = 0
  let offlineCount = 0

  for (const c of channels) {
    if (c.status === 'online') onlineCount++
    else offlineCount++

    if (c.unitId !== null && c.unitId !== undefined) {
      unitCounts.set(c.unitId, (unitCounts.get(c.unitId) || 0) + 1)
    } else {
      unassignedCount++
    }
  }

  return {
    unitCounts,
    unassignedCount,
    totalCount: channels.length,
    onlineCount,
    offlineCount
  }
}

/**
 * 构建完整的组织拓扑树 (车间 -> 区域 -> 单元)，并自动向上汇总通道、电脑、终端箱、网络设备等多维资产
 */
export function buildOrganizationTree(
  org: Organization,
  assetStatsOrUnitCounts: Map<number, number> | OrgAssetSources = new Map()
): OrgTreeNode[] {
  const { workshops = [], areas = [], units = [] } = org

  // 解析传入是简单 unitCounts Map 还是完整的四类资产列表
  let unitChannelCounts = new Map<number, number>()
  let unitComputerCounts = new Map<number, number>()
  let unitBoxCounts = new Map<number, number>()
  let unitNetDevCounts = new Map<number, number>()

  let areaComputerCounts = new Map<number, number>()
  let areaBoxCounts = new Map<number, number>()
  let areaNetDevCounts = new Map<number, number>()

  let wsComputerCounts = new Map<number, number>()
  let wsNetDevCounts = new Map<number, number>()

  if (assetStatsOrUnitCounts instanceof Map) {
    unitChannelCounts = assetStatsOrUnitCounts
  } else if (assetStatsOrUnitCounts) {
    const { channels = [], computers = [], terminalBoxes = [], networkDevices = [] } = assetStatsOrUnitCounts

    // 统计通道 (按工位单元归属)
    for (const c of channels) {
      if (c.unitId) {
        unitChannelCounts.set(c.unitId, (unitChannelCounts.get(c.unitId) || 0) + 1)
      }
    }

    // 统计工位电脑 (可直属单元、区域或车间)
    for (const comp of computers) {
      if (comp.unitId) {
        unitComputerCounts.set(comp.unitId, (unitComputerCounts.get(comp.unitId) || 0) + 1)
      } else if (comp.areaId) {
        areaComputerCounts.set(comp.areaId, (areaComputerCounts.get(comp.areaId) || 0) + 1)
      } else if (comp.workshopId) {
        wsComputerCounts.set(comp.workshopId, (wsComputerCounts.get(comp.workshopId) || 0) + 1)
      }
    }

    // 统计终端箱 (可属单元或区域)
    for (const box of terminalBoxes) {
      if (box.unitId) {
        unitBoxCounts.set(box.unitId, (unitBoxCounts.get(box.unitId) || 0) + 1)
      } else if (box.areaId) {
        areaBoxCounts.set(box.areaId, (areaBoxCounts.get(box.areaId) || 0) + 1)
      }
    }

    // 统计网络设备 (通常属区域或车间)
    for (const dev of networkDevices) {
      if ((dev as any).unitId) {
        unitNetDevCounts.set((dev as any).unitId, (unitNetDevCounts.get((dev as any).unitId) || 0) + 1)
      } else if (dev.areaId) {
        areaNetDevCounts.set(dev.areaId, (areaNetDevCounts.get(dev.areaId) || 0) + 1)
      } else if (dev.workshopId) {
        wsNetDevCounts.set(dev.workshopId, (wsNetDevCounts.get(dev.workshopId) || 0) + 1)
      }
    }
  }

  // 1. 构建工位单元节点映射（按所属区域分组）
  const unitsByAreaId = new Map<number, OrgTreeNode[]>()
  const orphanedUnits: OrgTreeNode[] = []

  for (const u of units) {
    const assetCounts: OrgNodeAssetCounts = {
      channels: unitChannelCounts.get(u.id) || 0,
      computers: unitComputerCounts.get(u.id) || 0,
      terminalBoxes: unitBoxCounts.get(u.id) || 0,
      networkDevices: unitNetDevCounts.get(u.id) || 0
    }
    const node: OrgTreeNode = {
      key: `units-${u.id}`,
      id: u.id,
      name: u.name,
      code: u.code,
      kind: 'units',
      status: u.status,
      parentId: u.parentId,
      channelCount: assetCounts.channels,
      assetCounts,
      rawNode: u
    }
    if (u.parentId) {
      if (!unitsByAreaId.has(u.parentId)) unitsByAreaId.set(u.parentId, [])
      unitsByAreaId.get(u.parentId)!.push(node)
    } else {
      orphanedUnits.push(node)
    }
  }

  // 2. 构建区域节点映射（汇总下辖工位单元 + 区域直属资产）
  const areasByWorkshopId = new Map<number, OrgTreeNode[]>()
  const orphanedAreas: OrgTreeNode[] = []

  for (const a of areas) {
    const rawChildUnits = unitsByAreaId.get(a.id) || []
    // 工位单元按编码标识严格排序
    const childUnits = rawChildUnits.slice().sort(compareByCode)
    const assetCounts: OrgNodeAssetCounts = {
      channels: childUnits.reduce((acc, u) => acc + u.assetCounts.channels, 0),
      computers: childUnits.reduce((acc, u) => acc + u.assetCounts.computers, 0) + (areaComputerCounts.get(a.id) || 0),
      terminalBoxes: childUnits.reduce((acc, u) => acc + u.assetCounts.terminalBoxes, 0) + (areaBoxCounts.get(a.id) || 0),
      networkDevices: childUnits.reduce((acc, u) => acc + u.assetCounts.networkDevices, 0) + (areaNetDevCounts.get(a.id) || 0)
    }

    const node: OrgTreeNode = {
      key: `areas-${a.id}`,
      id: a.id,
      name: a.name,
      code: a.code,
      kind: 'areas',
      status: a.status,
      parentId: a.parentId,
      channelCount: assetCounts.channels,
      assetCounts,
      children: childUnits.length ? childUnits : undefined,
      rawNode: a
    }
    if (a.parentId) {
      if (!areasByWorkshopId.has(a.parentId)) areasByWorkshopId.set(a.parentId, [])
      areasByWorkshopId.get(a.parentId)!.push(node)
    } else {
      orphanedAreas.push(node)
    }
  }

  // 3. 构建车间节点列表（汇总下辖区域 + 车间直属资产）
  const result: OrgTreeNode[] = []
  // 车间按编码标识严格排序
  const sortedWorkshops = workshops.slice().sort(compareByCode)

  for (const w of sortedWorkshops) {
    const rawChildAreas = areasByWorkshopId.get(w.id) || []
    // 区域按编码标识严格排序
    const childAreas = rawChildAreas.slice().sort(compareByCode)
    const assetCounts: OrgNodeAssetCounts = {
      channels: childAreas.reduce((acc, a) => acc + a.assetCounts.channels, 0),
      computers: childAreas.reduce((acc, a) => acc + a.assetCounts.computers, 0) + (wsComputerCounts.get(w.id) || 0),
      terminalBoxes: childAreas.reduce((acc, a) => acc + a.assetCounts.terminalBoxes, 0),
      networkDevices: childAreas.reduce((acc, a) => acc + a.assetCounts.networkDevices, 0) + (wsNetDevCounts.get(w.id) || 0)
    }

    result.push({
      key: `workshops-${w.id}`,
      id: w.id,
      name: w.name,
      code: w.code,
      kind: 'workshops',
      status: w.status,
      parentId: null,
      channelCount: assetCounts.channels,
      assetCounts,
      children: childAreas.length ? childAreas : undefined,
      rawNode: w
    })
  }

  // 4. 容错处理：若存在未挂载到车间的孤儿区域，以顶层形式展示（按编码标识排序）
  if (orphanedAreas.length > 0) {
    orphanedAreas.sort(compareByCode)
    const orphanChannels = orphanedAreas.reduce((acc, a) => acc + a.assetCounts.channels, 0)
    const orphanComputers = orphanedAreas.reduce((acc, a) => acc + a.assetCounts.computers, 0)
    const orphanBoxes = orphanedAreas.reduce((acc, a) => acc + a.assetCounts.terminalBoxes, 0)
    const orphanNetDevs = orphanedAreas.reduce((acc, a) => acc + a.assetCounts.networkDevices, 0)

    result.push({
      key: 'workshops-orphaned-areas',
      id: -1,
      name: '未归属车间的区域',
      code: 'ORPHAN-AREAS',
      kind: 'workshops',
      status: 'active',
      parentId: null,
      channelCount: orphanChannels,
      assetCounts: {
        channels: orphanChannels,
        computers: orphanComputers,
        terminalBoxes: orphanBoxes,
        networkDevices: orphanNetDevs
      },
      children: orphanedAreas,
      rawNode: { id: -1, name: '未归属车间的区域', code: 'ORPHAN-AREAS', status: 'active', parentId: null }
    })
  }

  // 5. 容错处理：若存在未挂载到区域的孤儿单元（按编码标识排序）
  if (orphanedUnits.length > 0) {
    orphanedUnits.sort(compareByCode)
    const orphanChannels = orphanedUnits.reduce((acc, u) => acc + u.assetCounts.channels, 0)
    const orphanComputers = orphanedUnits.reduce((acc, u) => acc + u.assetCounts.computers, 0)
    const orphanBoxes = orphanedUnits.reduce((acc, u) => acc + u.assetCounts.terminalBoxes, 0)
    const orphanNetDevs = orphanedUnits.reduce((acc, u) => acc + u.assetCounts.networkDevices, 0)

    result.push({
      key: 'workshops-orphaned-units',
      id: -2,
      name: '未归属区域的单元',
      code: 'ORPHAN-UNITS',
      kind: 'workshops',
      status: 'active',
      parentId: null,
      channelCount: orphanChannels,
      assetCounts: {
        channels: orphanChannels,
        computers: orphanComputers,
        terminalBoxes: orphanBoxes,
        networkDevices: orphanNetDevs
      },
      children: orphanedUnits,
      rawNode: { id: -2, name: '未归属区域的单元', code: 'ORPHAN-UNITS', status: 'active', parentId: null }
    })
  }

  return result
}

export interface BreadcrumbItem {
  kind: OrgNodeKind | 'all' | 'unassigned'
  id?: number
  name: string
}

/**
 * 解析当前选中节点的完整面包屑导航路径
 */
export function getNodeBreadcrumb(
  org: Organization,
  kind?: OrgNodeKind | 'all' | 'unassigned' | null,
  id?: number | null
): BreadcrumbItem[] {
  const rootItem: BreadcrumbItem = { kind: 'all', name: '全部组织' }
  if (!kind || kind === 'all') {
    return [rootItem]
  }

  if (kind === 'unassigned') {
    return [rootItem, { kind: 'unassigned', name: '未划拨通道' }]
  }

  if (id === null || id === undefined || id <= 0) {
    return [rootItem]
  }

  if (kind === 'workshops') {
    const ws = org.workshops?.find(w => w.id === id)
    return [rootItem, { kind: 'workshops', id, name: ws ? ws.name : `车间 ${id}` }]
  }

  if (kind === 'areas') {
    const area = org.areas?.find(a => a.id === id)
    const workshop = area?.parentId ? org.workshops?.find(w => w.id === area.parentId) : undefined
    const items: BreadcrumbItem[] = [rootItem]
    if (workshop) {
      items.push({ kind: 'workshops', id: workshop.id, name: workshop.name })
    }
    items.push({ kind: 'areas', id, name: area ? area.name : `区域 ${id}` })
    return items
  }

  if (kind === 'units') {
    const unit = org.units?.find(u => u.id === id)
    const area = unit?.parentId ? org.areas?.find(a => a.id === unit.parentId) : undefined
    const workshop = area?.parentId ? org.workshops?.find(w => w.id === area.parentId) : undefined
    const items: BreadcrumbItem[] = [rootItem]
    if (workshop) {
      items.push({ kind: 'workshops', id: workshop.id, name: workshop.name })
    }
    if (area) {
      items.push({ kind: 'areas', id: area.id, name: area.name })
    }
    items.push({ kind: 'units', id, name: unit ? unit.name : `单元 ${id}` })
    return items
  }

  return [rootItem]
}

export interface OrgTreeTableRow {
  id: number
  uniqueKey: string
  name: string
  code: string
  kind: OrganizationKind
  status: string
  parentId?: number | null
  parentName?: string
  childCount: number
  channelCount: number
  computerCount: number
  children?: OrgTreeTableRow[]
}

/**
 * 构建用于组织架构 Tree-Table 的树状表格层级数据。
 */
export function buildOrgTreeTable(
  org: Organization,
  channels: { unitId?: number | null }[] = [],
  computers: { workshopId?: number | null; areaId?: number | null; unitId?: number | null }[] = []
): OrgTreeTableRow[] {
  const { workshops = [], areas = [], units = [] } = org

  // 通道按单元统计
  const unitChannelMap = new Map<number, number>()
  for (const c of channels) {
    if (c.unitId) {
      unitChannelMap.set(c.unitId, (unitChannelMap.get(c.unitId) || 0) + 1)
    }
  }

  // 电脑按单元/区域/车间统计
  const unitCompMap = new Map<number, number>()
  const areaCompMap = new Map<number, number>()
  const wsCompMap = new Map<number, number>()
  for (const comp of computers) {
    if (comp.unitId) unitCompMap.set(comp.unitId, (unitCompMap.get(comp.unitId) || 0) + 1)
    if (comp.areaId) areaCompMap.set(comp.areaId, (areaCompMap.get(comp.areaId) || 0) + 1)
    if (comp.workshopId) wsCompMap.set(comp.workshopId, (wsCompMap.get(comp.workshopId) || 0) + 1)
  }

  return workshops.slice().sort(compareByCode).map(w => {
    const wsAreas = areas.filter(a => a.parentId === w.id).sort(compareByCode)
    let wsChannelTotal = 0
    let wsCompTotal = wsCompMap.get(w.id) || 0

    const areaNodes: OrgTreeTableRow[] = wsAreas.map(a => {
      const arUnits = units.filter(u => u.parentId === a.id).sort(compareByCode)
      let arChannelTotal = 0
      let arCompTotal = areaCompMap.get(a.id) || 0

      const unitNodes: OrgTreeTableRow[] = arUnits.map(u => {
        const uChannels = unitChannelMap.get(u.id) || 0
        const uComps = unitCompMap.get(u.id) || 0
        arChannelTotal += uChannels
        arCompTotal += uComps

        return {
          id: u.id,
          uniqueKey: `unit-${u.id}`,
          name: u.name,
          code: u.code,
          kind: 'units' as OrganizationKind,
          status: u.status,
          parentId: u.parentId,
          parentName: a.name,
          childCount: 0,
          channelCount: uChannels,
          computerCount: uComps
        }
      })

      wsChannelTotal += arChannelTotal
      wsCompTotal += arCompTotal

      return {
        id: a.id,
        uniqueKey: `area-${a.id}`,
        name: a.name,
        code: a.code,
        kind: 'areas' as OrganizationKind,
        status: a.status,
        parentId: a.parentId,
        parentName: w.name,
        childCount: unitNodes.length,
        channelCount: arChannelTotal,
        computerCount: arCompTotal,
        children: unitNodes.length > 0 ? unitNodes : undefined
      }
    })

    return {
      id: w.id,
      uniqueKey: `workshop-${w.id}`,
      name: w.name,
      code: w.code,
      kind: 'workshops' as OrganizationKind,
      status: w.status,
      parentId: null,
      parentName: undefined,
      childCount: areaNodes.length,
      channelCount: wsChannelTotal,
      computerCount: wsCompTotal,
      children: areaNodes.length > 0 ? areaNodes : undefined
    }
  })
}
/**
 * 构建支持任意级别（车间/区域/单元）单选的三级级联选项，用于全宽通道表格筛选（按编码标识排序）。
 */
export function buildFullOrgCascaderOptions(org: Organization) {
  const { workshops = [], areas = [], units = [] } = org
  return workshops.slice().sort(compareByCode).map(w => {
    const wsAreas = areas.filter(a => a.parentId === w.id).sort(compareByCode)
    return {
      value: `workshop:${w.id}`,
      label: w.name,
      children: wsAreas.length > 0 ? wsAreas.map(a => {
        const arUnits = units.filter(u => u.parentId === a.id).sort(compareByCode)
        return {
          value: `area:${a.id}`,
          label: a.name,
          children: arUnits.length > 0 ? arUnits.map(u => ({
            value: `unit:${u.id}`,
            label: u.name
          })) : undefined
        }
      }) : undefined
    }
  })
}

/**
 * 校验资产（具有 workshopId, areaId 和/或 unitId）是否匹配组织级联筛选范围
 * @param asset 包含组织属性的资产对象
 * @param orgScope 级联筛选值，格式为 "workshop:1" | "area:5" | "unit:12"
 * @param org 组织架构定义
 */
export function matchesOrgScope(
  asset: { workshopId?: number | null; areaId?: number | null; unitId?: number | null },
  orgScope: string | undefined | null,
  org: Organization
): boolean {
  if (!orgScope) return true
  const [type, idStr] = orgScope.split(':')
  const id = parseInt(idStr, 10)
  if (isNaN(id)) return true

  const assetWorkshopId = asset.workshopId ?? null
  const assetAreaId = asset.areaId ?? null
  const assetUnitId = asset.unitId ?? null

  if (type === 'unit') {
    return assetUnitId === id
  }

  if (type === 'area') {
    if (assetAreaId === id) return true
    if (assetUnitId && org.units) {
      const u = org.units.find(item => item.id === assetUnitId)
      if (u && u.parentId === id) return true
    }
    return false
  }

  if (type === 'workshop') {
    if (assetWorkshopId === id) return true
    // 检查 areaId 对应的上级车间
    if (assetAreaId && org.areas) {
      const a = org.areas.find(item => item.id === assetAreaId)
      if (a && a.parentId === id) return true
    }
    // 检查 unitId 对应的上级车间
    if (assetUnitId && org.units && org.areas) {
      const u = org.units.find(item => item.id === assetUnitId)
      if (u && u.parentId) {
        const a = org.areas.find(item => item.id === u.parentId)
        if (a && a.parentId === id) return true
      }
    }
    return false
  }

  return true
}
