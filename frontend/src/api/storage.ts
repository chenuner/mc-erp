import request from './request'
import type { PagedResult } from './system'

export interface FileDto {
  id: string
  type: string | null
  typeLabel: string | null
  originalName: string
  contentType: string
  sizeBytes: number
  uploaderName: string | null
  createdAt: string
  md5: string | null
}

/** 文件列表：按上傳時間倒序分頁，一頁預設 20 筆。 */
export async function listFiles(type?: string, page = 1, pageSize = 20): Promise<PagedResult<FileDto>> {
  return (await request.get('/storage/files', {
    params: { ...(type ? { type } : {}), page, pageSize },
  })) as PagedResult<FileDto>
}

export async function uploadFile(type: string, file: File): Promise<FileDto> {
  const form = new FormData()
  form.append('file', file)
  return (await request.post('/storage/files', form, {
    params: { type },
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

/**
 * 獲取文件二進制流（用於圖片預覽）。一律按 id 取圖：檔名和 ContentType 一定屬於這筆記錄。
 * 去重與快取由呼叫端按 MD5 做——同內容只下載、只快取一份。
 */
export async function getFileBlob(id: string): Promise<Blob> {
  return (await request.get(`/storage/files/${id}/download`, { responseType: 'blob' })) as unknown as Blob
}

/** 按內容（MD5）取文件；上傳已按 MD5 去重，同一個摘要的物理文件唯一。 */
export async function getFileBlobByMd5(md5: string): Promise<Blob> {
  return (await request.get(`/storage/files/by-md5/${encodeURIComponent(md5)}/download`, { responseType: 'blob' })) as unknown as Blob
}
