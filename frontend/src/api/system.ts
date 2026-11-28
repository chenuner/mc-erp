import request from './request'

export interface SysDictDto {
  id: string
  groupName: string
  label: string
  value: string
  sort: number
  enabled: boolean
  remark: string | null
  /** 上級字典項 ID；null 表示頂層（例如地盤「牛房」底下的「一樓」） */
  parentId: string | null
}

export interface SysDictGroupDto {
  id: string
  code: string
  name: string
  sort: number
  remark: string | null
  isSystem: boolean
  itemCount: number
  enabledCount: number
}

export interface PagedResult<T> {
  items: T[]
  total: number
  page: number
  pageSize: number
}

export interface CreateSysDictRequest {
  groupName: string
  label: string
  /** 留空時後端會自動生成 1、2、3… 的識別碼（有上級時為 1.1、1.2…） */
  value?: string
  sort?: number
  enabled?: boolean
  remark?: string | null
  /** 上級字典項 ID，省略為頂層 */
  parentId?: string | null
}

export interface CreateSysDictGroupRequest {
  name: string
  code?: string
  sort?: number
  remark?: string | null
}

export interface UpdateSysDictGroupRequest {
  name?: string
  sort?: number
  remark?: string | null
}

export async function listSysDictGroups(): Promise<SysDictGroupDto[]> {
  return (await request.get('/system/dict-groups')) as SysDictGroupDto[]
}

export async function createSysDictGroup(payload: CreateSysDictGroupRequest): Promise<SysDictGroupDto> {
  return (await request.post('/system/dict-groups', payload)) as SysDictGroupDto
}

export async function updateSysDictGroup(id: string, payload: UpdateSysDictGroupRequest): Promise<void> {
  await request.put(`/system/dict-groups/${id}`, payload)
}

export async function deleteSysDictGroup(id: string): Promise<void> {
  await request.delete(`/system/dict-groups/${id}`)
}

export async function listSysDicts(page = 1, pageSize = 20): Promise<PagedResult<SysDictDto>> {
  return (await request.get('/system/dicts', { params: { page, pageSize } })) as PagedResult<SysDictDto>
}

export async function listSysDictsByGroup(groupName: string): Promise<SysDictDto[]> {
  return (await request.get(`/system/dicts/group/${encodeURIComponent(groupName)}`)) as SysDictDto[]
}

export async function createSysDict(payload: CreateSysDictRequest): Promise<SysDictDto> {
  return (await request.post('/system/dicts', payload)) as SysDictDto
}

export async function updateSysDict(
  id: string,
  payload: Partial<Omit<CreateSysDictRequest, 'groupName'>>,
): Promise<SysDictDto> {
  return (await request.put(`/system/dicts/${id}`, payload)) as SysDictDto
}

export async function reorderSysDicts(groupName: string, ids: string[]): Promise<SysDictDto[]> {
  return (await request.put('/system/dicts/reorder', { groupName, ids })) as SysDictDto[]
}

export async function deleteSysDict(id: string): Promise<void> {
  await request.delete(`/system/dicts/${id}`)
}
