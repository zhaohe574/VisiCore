import type { components } from './generated/client'
export type { paths, operations, components } from './generated/client'
// 服务端以 JSON 数字输出；OpenAPI 同时允许数字字符串的输入形式。
export type JsonNumbers<T> = number extends T ? Exclude<T, string> : T extends (infer U)[] ? JsonNumbers<U>[] : T extends object ? { [K in keyof T]: JsonNumbers<T[K]> } : T
export type SchemaDto<K extends keyof components['schemas']> = JsonNumbers<components['schemas'][K]>
export type Page<T> = Omit<SchemaDto<'PagedChannelResponse'>, 'items'> & { items: T[] }
export type User = SchemaDto<'UserDto'>
export type LoginResult = SchemaDto<'AuthResponse'>
export type Role = SchemaDto<'AdministrationRoleDto'>
export type Permission = SchemaDto<'PermissionDto'>
export type Device = SchemaDto<'DeviceDto'>
export type DeviceInput = Omit<SchemaDto<'DeviceRequest'>, 'password'> & { password?: string | null }
export type Channel = SchemaDto<'ChannelDto'> & { alias?: string | null }
export type OrganizationNode = SchemaDto<'OrganizationNodeDto'>
export type Organization = SchemaDto<'OrganizationTreeDto'>
export type OrganizationKind = keyof Organization
export type ScopeType = 'workshop' | 'area' | 'unit' | 'channel'
export type AccessScope = SchemaDto<'ScopeRequest'>
export type OnlineSession = SchemaDto<'AdministrationSessionDto'>
export type Audit = SchemaDto<'AdministrationAuditDto'>
export type Settings = SchemaDto<'PlatformSettings'>
export type Dashboard = SchemaDto<'DashboardDto'>
export type SystemStatus = SchemaDto<'SystemStatisticsDto'>
export type LiveSession = SchemaDto<'LiveSessionDto'>
export type Recording = SchemaDto<'RecordingListResponse'>[number]
export type TimeSegment = SchemaDto<'RecordingSegmentDto'>
export type PlaybackSession = SchemaDto<'PlaybackSessionDto'>
export type PlaybackControl = Omit<SchemaDto<'PlaybackControlRequest'>, 'action'> & { action: 'pause' | 'resume' | 'seek' | 'speed' }
export type PtzCommand = 'up' | 'down' | 'left' | 'right' | 'auto' | 'zoomIn' | 'zoomOut' | 'focusNear' | 'focusFar' | 'irisOpen' | 'irisClose'
export type Layout = Omit<SchemaDto<'AdministrationLayoutDto'>, 'kind' | 'layout'> & { kind: 'layout' | 'patrol'; layout: 1 | 4 | 9 | 16 }
export type Alarm = SchemaDto<'AlarmDto'>
export type AlarmDetail = SchemaDto<'AlarmDetailDto'>
export type ExportJob = SchemaDto<'ExportDto'>
export type Release = SchemaDto<'ReleaseDto'>
export type PublicRelease = Omit<SchemaDto<'LatestReleaseDto'>, 'packages'> & { packages?: Release[] }
export type EventKind = 'alarm.changed' | 'device.changed' | 'media.changed' | 'export.changed' | 'access.changed'
export interface ResourceEvent { id: string | number; version: number; kind: string }
