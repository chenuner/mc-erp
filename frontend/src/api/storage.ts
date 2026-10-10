import request from './request'

/** 分類字典（地盤 / 時間 / 事件原因） */
export interface CategoryDto {
  id: string
  siteName: string
  eventDate: string
  eventReason: string
  remark: string | null
  fileCount: number
}

export interface FileDto {
  id: string
  categoryId: string
  siteName: string
  eventDate: string
  eventReason: string
  originalName: string
  contentType: string
  sizeBytes: number
  uploaderName: string | null
  createdAt: string
}

export async function listCategories(): Promise<CategoryDto[]> {
  return (await request.get('/storage/categories')) as CategoryDto[]
}

export async function createCategory(payload: {
  siteName: string
  eventDate: string
  eventReason: string
  remark?: string
}): Promise<CategoryDto> {
  return (await request.post('/storage/categories', payload)) as CategoryDto
}

export async function updateCategory(
  id: string,
  payload: { siteName?: string; eventDate?: string; eventReason?: string; remark?: string },
): Promise<CategoryDto> {
  return (await request.put(`/storage/categories/${id}`, payload)) as CategoryDto
}

export async function deleteCategory(id: string): Promise<void> {
  await request.delete(`/storage/categories/${id}`)
}

export async function listFiles(categoryId?: string): Promise<FileDto[]> {
  return (await request.get('/storage/files', { params: categoryId ? { categoryId } : {} })) as FileDto[]
}

export async function uploadFile(categoryId: string, file: File): Promise<FileDto> {
  const form = new FormData()
  form.append('file', file)
  return (await request.post('/storage/files', form, {
    params: { categoryId },
    headers: { 'Content-Type': 'multipart/form-data' },
  })) as FileDto
}

export async function downloadFile(id: string, fileName: string): Promise<void> {
  const res = await request.get(`/storage/files/${id}/download`, { responseType: 'blob' })
  const url = URL.createObjectURL(res as unknown as Blob)
  const a = document.createElement('a')
  a.href = url
  a.download = fileName
  a.click()
  URL.revokeObjectURL(url)
}

export async function deleteFile(id: string): Promise<void> {
  await request.delete(`/storage/files/${id}`)
}
