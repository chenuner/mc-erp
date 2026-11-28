import request from './request'
import type { PagedResult, SysDictDto, SysDictGroupDto } from './system'

export interface FileDto {
  id: string
  /** 文件類別是系統字典的字典項 id；名稱由前端按 id 映射。 */
  type: string | null
  originalName: string
  contentType: string
  sizeBytes: number
  uploaderName: string | null
  createdAt: string
  md5: string | null
}

/** 可以當文件類別的字典（只含後端白名單分組）：篩選與上傳都用這份建樹。 */
export interface StorageTypesDto {
  groups: SysDictGroupDto[]
  dicts: SysDictDto[]
}

export async function listStorageTypes(): Promise<StorageTypesDto> {
  return (await request.get('/storage/types')) as StorageTypesDto
}

/**
 * 文件列表：按上傳時間倒序分頁，一頁預設 20 筆。
 * `type` 可以是字典項 id（含下級子樹）或 `group:<分組代碼>`（整個分組）。
 */
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
