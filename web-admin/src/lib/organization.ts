import type { Organization } from '../api'

export interface UnitCascaderOption {
  value: number | string
  label: string
  disabled?: boolean
  children?: UnitCascaderOption[]
}

/**
 * 构建车间 -> 区域 -> 单元的三级级联选择树。
 * 叶子节点为单元（value 为数字 ID），上级节点（车间、区域）value 带有前缀防止 ID 碰撞。
 * 仅叶子单元可被最终选中；无下级单元的组织节点被自动禁用以防误选。
 */
export function buildUnitCascaderOptions(org: Organization): UnitCascaderOption[] {
  const { workshops = [], areas = [], units = [] } = org

  // 按区域 ID 映射单元
  const areaUnitsMap = new Map<number, UnitCascaderOption[]>()
  for (const a of areas) {
    areaUnitsMap.set(a.id, [])
  }

  const orphanedUnits: UnitCascaderOption[] = []
  for (const u of units) {
    const unitOption: UnitCascaderOption = {
      value: u.id,
      label: u.name
    }
    if (u.parentId && areaUnitsMap.has(u.parentId)) {
      areaUnitsMap.get(u.parentId)!.push(unitOption)
    } else {
      orphanedUnits.push(unitOption)
    }
  }

  // 按车间 ID 映射区域
  const workshopAreasMap = new Map<number, UnitCascaderOption[]>()
  for (const w of workshops) {
    workshopAreasMap.set(w.id, [])
  }

  const orphanedAreas: UnitCascaderOption[] = []
  for (const a of areas) {
    const childUnits = areaUnitsMap.get(a.id) || []
    const hasUnits = childUnits.length > 0
    const areaOption: UnitCascaderOption = {
      value: `area-${a.id}`,
      label: a.name,
      disabled: !hasUnits,
      children: hasUnits ? childUnits : undefined
    }

    if (a.parentId && workshopAreasMap.has(a.parentId)) {
      workshopAreasMap.get(a.parentId)!.push(areaOption)
    } else {
      orphanedAreas.push(areaOption)
    }
  }

  const result: UnitCascaderOption[] = []

  // 车间节点
  for (const w of workshops) {
    const childAreas = workshopAreasMap.get(w.id) || []
    const hasAvailableUnits = childAreas.some(a => (a.children?.length ?? 0) > 0)
    result.push({
      value: `workshop-${w.id}`,
      label: w.name,
      disabled: !hasAvailableUnits,
      children: childAreas.length > 0 ? childAreas : undefined
    })
  }

  // 兜底：未归属车间的区域
  if (orphanedAreas.length > 0) {
    const hasUnits = orphanedAreas.some(a => (a.children?.length ?? 0) > 0)
    result.push({
      value: 'orphaned-areas',
      label: '未归属车间的区域',
      disabled: !hasUnits,
      children: orphanedAreas
    })
  }

  // 兜底：未归属区域的单元
  if (orphanedUnits.length > 0) {
    result.push({
      value: 'orphaned-units',
      label: '未归属区域的单元',
      children: orphanedUnits
    })
  }

  return result
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
