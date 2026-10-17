import request from './request'
import type { PagedResult } from './system'

export interface ReceiptDto {
  id: string
  version: string
  fileItemId: string
  fileName: string
  md5: string | null
  sizeBytes: number
  siteId: string
  siteName: string
  documentTypeDictId: string
  documentTypeLabel: string
  type: string | null
  typeLabel: string | null
  siteCategoryDictId: string | null
  siteCategoryLabel: string | null
  receiptDate: string | null
  amount: number | null
  currencyCode: string | null
  merchantName: string | null
  invoiceNumber: string | null
  remark: string | null
  ocrStatus: string
  ocrError: string | null
  ocrText: string | null
  ocrResult: { documents?: { fields?: Record<string, { confidence?: number; content?: string }> }[] } | null
  manuallyEdited: boolean
  isConfirmed: boolean
}

export interface ReceiptQuery {
  siteId?: string
  documentTypeDictId?: string
  type?: string
  siteCategoryDictId?: string
  start?: string
  end?: string
  isConfirmed?: boolean
}

export type UpdateReceipt = Pick<ReceiptDto, 'version' | 'siteId' | 'documentTypeDictId' | 'siteCategoryDictId'
  | 'receiptDate' | 'amount' | 'currencyCode' | 'merchantName' | 'invoiceNumber' | 'remark'> & { type: string }

export async function accountingStatus(): Promise<{ ocrConfigured: boolean }> {
  return await request.get('/accounting/status') as { ocrConfigured: boolean }
}

export async function listReceipts(query: ReceiptQuery, page = 1, pageSize = 20): Promise<PagedResult<ReceiptDto>> {
  return await request.get('/accounting/receipts', { params: { ...query, page, pageSize } }) as PagedResult<ReceiptDto>
}

export async function getReceipt(id: string): Promise<ReceiptDto> {
  return await request.get(`/accounting/receipts/${id}`) as ReceiptDto
}

export async function uploadReceipt(siteId: string, documentTypeDictId: string, file: File): Promise<ReceiptDto> {
  const form = new FormData()
  form.append('file', file)
  return await request.post('/accounting/receipts', form, { params: { siteId, documentTypeDictId }, timeout: 60000 }) as ReceiptDto
}

export async function recognizeReceipt(id: string, documentTypeDictId?: string): Promise<ReceiptDto> {
  return await request.post(`/accounting/receipts/${id}/recognize`, null, { params: { documentTypeDictId }, timeout: 150000 }) as ReceiptDto
}

export async function updateReceipt(id: string, payload: UpdateReceipt): Promise<ReceiptDto> {
  return await request.put(`/accounting/receipts/${id}`, payload) as ReceiptDto
}

export async function confirmReceipt(id: string, version: string): Promise<ReceiptDto> {
  return await request.post(`/accounting/receipts/${id}/confirm`, { version }) as ReceiptDto
}

export async function receiptImage(id: string): Promise<Blob> {
  return await request.get(`/accounting/receipts/${id}/image`, { responseType: 'blob' }) as unknown as Blob
}

export async function exportReceiptImages(query: ReceiptQuery): Promise<Blob> {
  return await request.get('/accounting/receipts/export', { params: query, responseType: 'blob', timeout: 300000 }) as unknown as Blob
}

export function downloadBlob(blob: Blob, name: string) {
  const url = URL.createObjectURL(blob)
  const anchor = document.createElement('a')
  anchor.href = url
  anchor.download = name
  anchor.click()
  window.setTimeout(() => URL.revokeObjectURL(url), 1000)
}