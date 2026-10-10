import request from './request'

/** 歸檔分類字典（系統級共享：地盤 / 時間 / 事件原因） */
export interface CategoryDto {
  id: string
  siteName: string
  eventDate: string
  eventReason: string
  remark: string | null
  fileCount: number
}

export async function listCategories(): Promise<CategoryDto[]> {
  return (await request.get('/system/categories')) as CategoryDto[]
}

export async function createCategory(payload: {
  siteName: string
  eventDate: string
  eventReason: string
  remark?: string
}): Promise<CategoryDto> {
  return (await request.post('/system/categories', payload)) as CategoryDto
}

export async function updateCategory(
  id: string,
  payload: { siteName?: string; eventDate?: string; eventReason?: string; remark?: string },
): Promise<CategoryDto> {
  return (await request.put(`/system/categories/${id}`, payload)) as CategoryDto
}

export async function deleteCategory(id: string): Promise<void> {
  await request.delete(`/system/categories/${id}`)
}
