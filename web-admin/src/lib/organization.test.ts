import { describe, expect, it } from 'vitest'
import type { Channel, Organization } from '../api'
import {
  buildAreaCascaderOptions,
  buildOrganizationTree,
  buildUnitCascaderOptions,
  calculateOrgChannelCounts,
  getAreaHierarchy,
  getNodeBreadcrumb,
  getUnitHierarchy,
  getUnitParentPath,
  getUnitArea,
  getUnitWorkshop,
  getUnitName,
  groupUnitsByArea,
  matchesOrgScope
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

  it('正确构建二级级联选项 (buildAreaCascaderOptions)，叶子节点为区域数字 ID', () => {
    const options = buildAreaCascaderOptions(mockOrg)
    expect(options).toHaveLength(3)

    // 一车间 (包含冲压区、喷涂区)
    const ws1 = options.find(o => o.value === 'workshop-1')
    expect(ws1).toBeDefined()
    expect(ws1?.disabled).toBe(false)
    expect(ws1?.children).toHaveLength(2)

    // 冲压区作为叶子节点，value 为数字 11
    const ar11 = ws1?.children?.find(c => c.value === 11)
    expect(ar11).toBeDefined()
    expect(ar11?.label).toBe('冲压区')
    expect(ar11?.children).toBeUndefined()

    // 喷涂区虽然没有单元，但在区域级联中仍是合法区域，value 为数字 12
    const ar12 = ws1?.children?.find(c => c.value === 12)
    expect(ar12).toBeDefined()
    expect(ar12?.label).toBe('喷涂区')

    // 空车间没有区域，应被禁用
    const ws3 = options.find(o => o.value === 'workshop-3')
    expect(ws3).toBeDefined()
    expect(ws3?.disabled).toBe(true)
  })

  it('获取区域完整层级 (getAreaHierarchy)', () => {
    expect(getAreaHierarchy(mockOrg, 11)).toBe('一车间 / 冲压区')
    expect(getAreaHierarchy(mockOrg, 21)).toBe('二车间 / 装配区')
    expect(getAreaHierarchy(mockOrg, 999)).toBe('区域 999')
    expect(getAreaHierarchy(mockOrg, null)).toBe('未划拨区域')
    expect(getAreaHierarchy(mockOrg, undefined)).toBe('未划拨区域')
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

  it('构建完整组织拓扑树并自底向上汇总通道数 (buildOrganizationTree)', () => {
    const unitCounts = new Map<number, number>([
      [101, 3], // 冲压区 - 包装单元: 3
      [102, 2], // 冲压区 - 质检单元: 2
      [201, 5]  // 装配区 - 包装单元: 5
    ])
    const tree = buildOrganizationTree(mockOrg, unitCounts)
    expect(tree).toHaveLength(3)

    // 一车间 (包含冲压区 5 + 喷涂区 0 = 5 路通道)
    const ws1 = tree.find(t => t.id === 1)
    expect(ws1).toBeDefined()
    expect(ws1?.name).toBe('一车间')
    expect(ws1?.kind).toBe('workshops')
    expect(ws1?.channelCount).toBe(5)
    expect(ws1?.children).toHaveLength(2)

    // 冲压区 (包含 101: 3 + 102: 2 = 5)
    const ar11 = ws1?.children?.find(c => c.id === 11)
    expect(ar11).toBeDefined()
    expect(ar11?.kind).toBe('areas')
    expect(ar11?.channelCount).toBe(5)
    expect(ar11?.children).toHaveLength(2)

    // 包装单元 (101: 3)
    const u101 = ar11?.children?.find(c => c.id === 101)
    expect(u101).toBeDefined()
    expect(u101?.kind).toBe('units')
    expect(u101?.channelCount).toBe(3)

    // 喷涂区 (0 路通道)
    const ar12 = ws1?.children?.find(c => c.id === 12)
    expect(ar12).toBeDefined()
    expect(ar12?.channelCount).toBe(0)
    expect(ar12?.children).toBeUndefined()

    // 二车间 (包含装配区 5 路通道)
    const ws2 = tree.find(t => t.id === 2)
    expect(ws2?.channelCount).toBe(5)

    // 空车间 (0 路通道)
    const ws3 = tree.find(t => t.id === 3)
    expect(ws3?.channelCount).toBe(0)
  })

  it('构建拓扑树并自底向上汇总四类资产 (buildOrganizationTree with multi-assets)', () => {
    const mockChannels: Partial<Channel>[] = [
      { id: 1, unitId: 101 },
      { id: 2, unitId: 101 },
      { id: 3, unitId: 102 }
    ]
    const mockComputers = [
      { id: 10, unitId: 101, name: '电脑1' },
      { id: 11, areaId: 11, unitId: null, name: '区域电脑' },
      { id: 12, workshopId: 1, areaId: null, unitId: null, name: '车间电脑' }
    ] as any
    const mockBoxes = [
      { id: 20, unitId: 101, name: '终端箱1' },
      { id: 21, areaId: 11, unitId: null, name: '区域终端箱' }
    ] as any
    const mockNetDevs = [
      { id: 30, areaId: 11, name: '接入交换机' },
      { id: 31, workshopId: 1, name: '汇聚交换机' }
    ] as any

    const tree = buildOrganizationTree(mockOrg, {
      channels: mockChannels as Channel[],
      computers: mockComputers,
      terminalBoxes: mockBoxes,
      networkDevices: mockNetDevs
    })

    const ws1 = tree.find(t => t.id === 1)
    expect(ws1).toBeDefined()
    // 通道：101 有 2 个，102 有 1 个，共 3 路
    expect(ws1?.assetCounts.channels).toBe(3)
    // 电脑：101 有 1 台 + 区域 1 台 + 车间 1 台 = 3 台
    expect(ws1?.assetCounts.computers).toBe(3)
    // 终端箱：101 有 1 个 + 区域 1 个 = 2 个
    expect(ws1?.assetCounts.terminalBoxes).toBe(2)
    // 网络设备：区域 1 台 + 车间 1 台 = 2 台
    expect(ws1?.assetCounts.networkDevices).toBe(2)

    // 冲压区
    const ar11 = ws1?.children?.find(c => c.id === 11)
    expect(ar11).toBeDefined()
    expect(ar11?.assetCounts.channels).toBe(3)
    expect(ar11?.assetCounts.computers).toBe(2) // 单元 1 + 区域 1
    expect(ar11?.assetCounts.terminalBoxes).toBe(2) // 单元 1 + 区域 1
    expect(ar11?.assetCounts.networkDevices).toBe(1) // 区域 1

    // 包装单元
    const u101 = ar11?.children?.find(c => c.id === 101)
    expect(u101).toBeDefined()
    expect(u101?.assetCounts.channels).toBe(2)
    expect(u101?.assetCounts.computers).toBe(1)
    expect(u101?.assetCounts.terminalBoxes).toBe(1)
    expect(u101?.assetCounts.networkDevices).toBe(0)
  })

  it('统计通道单元分布及在线状态 (calculateOrgChannelCounts)', () => {
    const mockChannels: Partial<Channel>[] = [
      { id: 1, unitId: 101, status: 'online' },
      { id: 2, unitId: 101, status: 'online' },
      { id: 3, unitId: 102, status: 'offline' },
      { id: 4, unitId: null, status: 'online' },
      { id: 5, unitId: undefined, status: 'offline' }
    ]
    const counts = calculateOrgChannelCounts(mockChannels as Channel[])
    expect(counts.totalCount).toBe(5)
    expect(counts.onlineCount).toBe(3)
    expect(counts.offlineCount).toBe(2)
    expect(counts.unassignedCount).toBe(2)
    expect(counts.unitCounts.get(101)).toBe(2)
    expect(counts.unitCounts.get(102)).toBe(1)
  })

  it('解析组织节点面包屑导航 (getNodeBreadcrumb)', () => {
    // 根节点
    expect(getNodeBreadcrumb(mockOrg, 'all', 0)).toEqual([
      { kind: 'all', name: '全部组织' }
    ])

    // 未划拨通道
    expect(getNodeBreadcrumb(mockOrg, 'unassigned', 0)).toEqual([
      { kind: 'all', name: '全部组织' },
      { kind: 'unassigned', name: '未划拨通道' }
    ])

    // 车间
    expect(getNodeBreadcrumb(mockOrg, 'workshops', 1)).toEqual([
      { kind: 'all', name: '全部组织' },
      { kind: 'workshops', id: 1, name: '一车间' }
    ])

    // 区域
    expect(getNodeBreadcrumb(mockOrg, 'areas', 11)).toEqual([
      { kind: 'all', name: '全部组织' },
      { kind: 'workshops', id: 1, name: '一车间' },
      { kind: 'areas', id: 11, name: '冲压区' }
    ])

    // 单元
    expect(getNodeBreadcrumb(mockOrg, 'units', 101)).toEqual([
      { kind: 'all', name: '全部组织' },
      { kind: 'workshops', id: 1, name: '一车间' },
      { kind: 'areas', id: 11, name: '冲压区' },
      { kind: 'units', id: 101, name: '包装单元' }
    ])
  })

  it('多级级联范围匹配 (matchesOrgScope)', () => {
    // 资产在区域 11 (属于一车间)
    const boxInArea11 = { areaId: 11, unitId: null }
    // 资产在单元 101 (属于区域 11，进而属于一车间)
    const boxInUnit101 = { areaId: null, unitId: 101 }
    // 资产在区域 21 (属于二车间)
    const boxInArea21 = { areaId: 21, unitId: null }
    // 未划拨资产
    const unassignedBox = { areaId: null, unitId: null }

    // 空筛选匹配全部
    expect(matchesOrgScope(boxInArea11, undefined, mockOrg)).toBe(true)
    expect(matchesOrgScope(boxInArea11, '', mockOrg)).toBe(true)
    expect(matchesOrgScope(unassignedBox, undefined, mockOrg)).toBe(true)

    // 车间级别筛选
    expect(matchesOrgScope(boxInArea11, 'workshop:1', mockOrg)).toBe(true)
    expect(matchesOrgScope(boxInUnit101, 'workshop:1', mockOrg)).toBe(true)
    expect(matchesOrgScope(boxInArea21, 'workshop:1', mockOrg)).toBe(false)
    expect(matchesOrgScope(unassignedBox, 'workshop:1', mockOrg)).toBe(false)

    // 区域级别筛选
    expect(matchesOrgScope(boxInArea11, 'area:11', mockOrg)).toBe(true)
    expect(matchesOrgScope(boxInUnit101, 'area:11', mockOrg)).toBe(true)
    expect(matchesOrgScope(boxInArea21, 'area:11', mockOrg)).toBe(false)

    // 单元级别筛选
    expect(matchesOrgScope(boxInUnit101, 'unit:101', mockOrg)).toBe(true)
    expect(matchesOrgScope(boxInUnit101, 'unit:102', mockOrg)).toBe(false)
    expect(matchesOrgScope(boxInArea11, 'unit:101', mockOrg)).toBe(false)
  })

  it('组织架构拓扑与级联严格按编码标识 (code) 排序，不按名称排序', () => {
    // 故意打乱顺序、且名称拼音顺序与编码相反的数据
    const unorderedOrg: Organization = {
      workshops: [
        { id: 4, name: '乙车间 (拼音靠前)', code: 'WS-02', status: 'active', parentId: null },
        { id: 1, name: '甲车间 (拼音靠后)', code: 'WS-01', status: 'active', parentId: null },
        { id: 9, name: '未编码车间', code: '', status: 'active', parentId: null }
      ],
      areas: [
        { id: 12, name: '综合区 (拼音Z)', code: 'AR-01', status: 'active', parentId: 1 },
        { id: 11, name: '保卫区 (拼音B)', code: 'AR-02', status: 'active', parentId: 1 }
      ],
      units: [
        { id: 103, name: '包装十单元', code: '03', status: 'active', parentId: 12 },
        { id: 101, name: '包装一单元', code: '01', status: 'active', parentId: 12 },
        { id: 102, name: '包装二单元', code: '02', status: 'active', parentId: 12 }
      ]
    }

    // 1. 验证 buildOrganizationTree 拓扑树
    const tree = buildOrganizationTree(unorderedOrg)
    // 顶层车间顺序必须是 WS-01 -> WS-02 -> 无编码
    expect(tree[0].code).toBe('WS-01')
    expect(tree[0].name).toBe('甲车间 (拼音靠后)')
    expect(tree[1].code).toBe('WS-02')
    expect(tree[1].name).toBe('乙车间 (拼音靠前)')
    expect(tree[2].code).toBe('')

    // 二级区域顺序必须是 AR-01 -> AR-02（即使 AR-01 名称是"综合区"拼音Z，AR-02 是"保卫区"拼音B）
    const childAreas = tree[0].children!
    expect(childAreas).toHaveLength(2)
    expect(childAreas[0].code).toBe('AR-01')
    expect(childAreas[0].name).toBe('综合区 (拼音Z)')
    expect(childAreas[1].code).toBe('AR-02')
    expect(childAreas[1].name).toBe('保卫区 (拼音B)')

    // 三级工位单元顺序必须是 01 -> 02 -> 03
    const childUnits = childAreas[0].children!
    expect(childUnits).toHaveLength(3)
    expect(childUnits[0].code).toBe('01')
    expect(childUnits[1].code).toBe('02')
    expect(childUnits[2].code).toBe('03')

    // 2. 验证 buildUnitCascaderOptions 下拉级联
    const cascader = buildUnitCascaderOptions(unorderedOrg)
    expect(cascader[0].value).toBe('workshop-1') // WS-01
    expect(cascader[1].value).toBe('workshop-4') // WS-02
    const cascaderAreas = cascader[0].children!
    expect(cascaderAreas[0].value).toBe('area-12') // AR-01
    expect(cascaderAreas[1].value).toBe('area-11') // AR-02
    const cascaderUnits = cascaderAreas[0].children!
    expect(cascaderUnits[0].value).toBe(101) // 01
    expect(cascaderUnits[1].value).toBe(102) // 02
    expect(cascaderUnits[2].value).toBe(103) // 03
  })
})

