import request from './request'

export interface SysDictDto {
  id: string
  groupName: string
  label: string
  value: string
  sort: number
  enabled: boolean
  remark: string | null
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
  value: string
  sort: number
  enabled: boolean
  remark?: string
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

export async function deleteSysDict(id: string): Promise<void> {
  await request.delete(`/system/dicts/${id}`)
}
