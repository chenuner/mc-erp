import request from './request'

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

/** 獲取文件二進制流（用於圖片預覽） */
export async function getFileBlob(id: string): Promise<Blob> {
  return (await request.get(`/storage/files/${id}/download`, { responseType: 'blob' })) as unknown as Blob
}
