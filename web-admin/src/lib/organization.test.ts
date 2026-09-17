import { describe, expect, it } from 'vitest'
import type { Organization } from '../api'
import {
  buildUnitCascaderOptions,
  getUnitHierarchy,
  getUnitParentPath,
  getUnitArea,
  getUnitWorkshop,
  getUnitName,
  groupUnitsByArea
} from './organization'

const mockOrg: Organization = {
  workshops: [
    { id: 1, name: '一车间', code: 'WS-01', status: 'active', parentId: null },
    { id: 2, name: '二车间', code: 'WS-02', status: 'active', parentId: null },
    { id: 3, name: '空车间', code: 'WS-03', status: 'active', parentId: null }
  ],
  areas: [
    { id: 11, name: '冲压区', code: 'AR-11', status: 'active', parentId: 1 },
    { id: 12, name: '喷涂区', code: 'AR-12', status: 'active', parentId: 1 },
    { id: 21, name: '装配区', code: 'AR-21', status: 'active', parentId: 2 }
  ],
  units: [
    { id: 101, name: '包装单元', code: 'UN-101', status: 'active', parentId: 11 },
    { id: 102, name: '质检单元', code: 'UN-102', status: 'active', parentId: 11 },
    { id: 201, name: '包装单元', code: 'UN-201', status: 'active', parentId: 21 } // 同名单元
  ]
}

describe('组织级联与层级工具 (organization.ts)', () => {
  it('正确构建三级级联选项，叶子节点为单元数字 ID', () => {
    const options = buildUnitCascaderOptions(mockOrg)
    expect(options).toHaveLength(3)

    // 一车间
    const ws1 = options.find(o => o.value === 'workshop-1')
    expect(ws1).toBeDefined()
    expect(ws1?.disabled).toBe(false)
    expect(ws1?.children).toHaveLength(2)

    // 冲压区
    const ar11 = ws1?.children?.find(c => c.value === 'area-11')
    expect(ar11).toBeDefined()
    expect(ar11?.disabled).toBe(false)
    expect(ar11?.children).toHaveLength(2)

    // 包装单元 (ID: 101)
    const u101 = ar11?.children?.find(c => c.value === 101)
    expect(u101).toBeDefined()
    expect(u101?.label).toBe('包装单元')
    expect(u101?.children).toBeUndefined()

    // 喷涂区没有单元，应该被禁用
    const ar12 = ws1?.children?.find(c => c.value === 'area-12')
    expect(ar12).toBeDefined()
    expect(ar12?.disabled).toBe(true)

    // 空车间没有可用单元，应该被禁用
    const ws3 = options.find(o => o.value === 'workshop-3')
    expect(ws3).toBeDefined()
    expect(ws3?.disabled).toBe(true)

    // 二车间的包装单元 (ID: 201)
    const ws2 = options.find(o => o.value === 'workshop-2')
    const ar21 = ws2?.children?.find(c => c.value === 'area-21')
    const u201 = ar21?.children?.find(c => c.value === 201)
    expect(u201).toBeDefined()
    expect(u201?.label).toBe('包装单元')
  })

  it('正确处理孤儿节点（未归属的区域或单元）', () => {
    const orgWithOrphans: Organization = {
      workshops: [],
      areas: [
        { id: 99, name: '流浪区域', code: 'AR-99', status: 'active', parentId: null }
      ],
      units: [
        { id: 999, name: '流浪单元', code: 'UN-999', status: 'active', parentId: null },
        { id: 998, name: '流浪区域下的单元', code: 'UN-998', status: 'active', parentId: 99 }
      ]
    }
    const options = buildUnitCascaderOptions(orgWithOrphans)
    expect(options.some(o => o.value === 'orphaned-areas')).toBe(true)
    expect(options.some(o => o.value === 'orphaned-units')).toBe(true)
  })

  it('获取单元完整层级 (getUnitHierarchy)', () => {
    expect(getUnitHierarchy(mockOrg, 101)).toBe('一车间 / 冲压区 / 包装单元')
    expect(getUnitHierarchy(mockOrg, 201)).toBe('二车间 / 装配区 / 包装单元')
    expect(getUnitHierarchy(mockOrg, 999)).toBe('单元 999')
    expect(getUnitHierarchy(mockOrg, null)).toBe('未划拨单元')
    expect(getUnitHierarchy(mockOrg, undefined)).toBe('未划拨单元')
  })

  it('获取单元父级路径 (getUnitParentPath)', () => {
    expect(getUnitParentPath(mockOrg, 101)).toBe('一车间 / 冲压区')
    expect(getUnitParentPath(mockOrg, 201)).toBe('二车间 / 装配区')
    expect(getUnitParentPath(mockOrg, 999)).toBe('')
    expect(getUnitParentPath(mockOrg, null)).toBe('')
  })

  it('获取单元所属区域与车间节点 (getUnitArea, getUnitWorkshop)', () => {
    const area101 = getUnitArea(mockOrg, 101)
    expect(area101).toBeDefined()
    expect(area101?.name).toBe('冲压区')

    const ws101 = getUnitWorkshop(mockOrg, 101)
    expect(ws101).toBeDefined()
    expect(ws101?.name).toBe('一车间')

    expect(getUnitArea(mockOrg, 999)).toBeUndefined()
    expect(getUnitWorkshop(mockOrg, 999)).toBeUndefined()
    expect(getUnitArea(mockOrg, null)).toBeUndefined()
    expect(getUnitWorkshop(mockOrg, null)).toBeUndefined()
  })

  it('获取单元名称 (getUnitName)', () => {
    expect(getUnitName(mockOrg, 101)).toBe('包装单元')
    expect(getUnitName(mockOrg, 999)).toBe('单元 999')
    expect(getUnitName(mockOrg, null)).toBe('未分配')
    expect(getUnitName(mockOrg, undefined)).toBe('未分配')
  })

  it('按所属区域分组单元 (groupUnitsByArea)', () => {
    const groups = groupUnitsByArea(mockOrg)
    expect(groups).toHaveLength(3)

    // 冲压区 (2 units)
    const ar11Group = groups.find(g => g.areaId === 11)
    expect(ar11Group).toBeDefined()
    expect(ar11Group?.areaName).toBe('冲压区')
    expect(ar11Group?.workshopName).toBe('一车间')
    expect(ar11Group?.units).toHaveLength(2)
    expect(ar11Group?.units.map(u => u.id)).toEqual([101, 102])

    // 喷涂区 (0 units)
    const ar12Group = groups.find(g => g.areaId === 12)
    expect(ar12Group).toBeDefined()
    expect(ar12Group?.units).toHaveLength(0)

    // 装配区 (1 unit)
    const ar21Group = groups.find(g => g.areaId === 21)
    expect(ar21Group).toBeDefined()
    expect(ar21Group?.units).toHaveLength(1)
    expect(ar21Group?.units[0].id).toBe(201)
  })
})
