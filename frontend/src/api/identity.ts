import request from './request'

/** 與後端 PagedResult<T> 對齊 */
export interface PagedResult<T> {
  items: T[]
  total: number
  page: number
  pageSize: number
}

export interface UserDto {
  id: string
  username: string
  email: string | null
  displayName: string | null
  isActive: boolean
  /** 受保護的主管理員：不可刪除、不可停用 */
  isProtected: boolean
  /** 角色名清單，例如 ['Admin'] */
  roles: string[]
}

export async function login(username: string, password: string): Promise<string> {
  const res = (await request.post('/identity/login', { username, password })) as { token: string }
  return res.token
}

export async function listUsers(page = 1, pageSize = 20): Promise<PagedResult<UserDto>> {
  return (await request.get('/identity/users', { params: { page, pageSize } })) as PagedResult<UserDto>
}

export async function createUser(payload: {
  username: string
  password: string
  email?: string
  displayName?: string
}): Promise<UserDto> {
  return (await request.post('/identity/users', payload)) as UserDto
}

export async function updateUser(
  id: string,
  payload: { email?: string; displayName?: string; isActive?: boolean },
): Promise<UserDto> {
  return (await request.put(`/identity/users/${id}`, payload)) as UserDto
}

export async function deleteUser(id: string): Promise<void> {
  await request.delete(`/identity/users/${id}`)
}
