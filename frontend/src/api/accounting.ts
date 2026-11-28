import request from './request'
import type { PagedResult } from './system'

export interface ReceiptDto {
  id: string
  version: string
  fileItemId: string | null
  fileName: string | null
  md5: string | null
  sizeBytes: number | null
  documentTypeDictId: string
  type: string
  siteCategoryDictId: string | null
  receiptDate: string | null
  amount: number | null
  currencyCode: string | null
  merchantName: string | null
  invoiceNumber: string | null
  remark: string | null
  ocrStatus: string
  ocrError: string | null
  /** 只有詳情接口有；列表回應不含這兩欄。 */
  ocrText?: string | null
  /** 已保存的識別建議；舊資料沒有欄位結構時為 null。只有詳情接口有。 */
  ocrResult?: OcrSuggestion | null
}

/** 列表一行：後端 ReceiptListResponse 的形狀，OCR 原文和識別建議不在裡面。 */
export type ReceiptListDto = Omit<ReceiptDto, 'ocrText' | 'ocrResult'>

/** 單一識別欄位：值 + Azure 回報的置信度（0–1）；未識別到時兩者皆為 null。 */
export interface OcrField<T> {
  value: T | null
  confidence: number | null
}

export interface OcrSuggestion {
  merchantName: OcrField<string>
  receiptDate: OcrField<string>
  amount: OcrField<number>
  currencyCode: OcrField<string>
  invoiceNumber: OcrField<string>
  categoryValue: string | null
  /** 置信度低於閾值的欄位名，例如 ['amount']。 */
  reviewFields: string[]
  needsReview: boolean
}

export interface RecognizeResult {
  receipt: ReceiptDto
  fields: OcrSuggestion
}

export interface ReceiptQuery {
  documentTypeDictId?: string
  type?: string
  siteCategoryDictId?: string
  start?: string
  end?: string
  /** 只在提取圖片（ZIP）用：false 時不做子樹擴張，只提取剛好落在所選分類的票據 */
  includeDescendants?: boolean
}

export type UpdateReceipt = Pick<ReceiptDto, 'version' | 'documentTypeDictId' | 'siteCategoryDictId'
  | 'receiptDate' | 'amount' | 'currencyCode' | 'merchantName' | 'invoiceNumber' | 'remark'> & { type: string }

export async function accountingStatus(): Promise<{ ocrConfigured: boolean }> {
  return await request.get('/accounting/status') as { ocrConfigured: boolean }
}

/** 單一幣種的金額合計；currencyCode 為 null 代表有金額但沒填幣種。 */
export interface ReceiptCurrencyTotal {
  currencyCode: string | null
  count: number
  amount: number
}

/** 整份篩選結果的金額合計；不同幣種分開，不混加。missingAmount 是未填金額的張數。 */
export interface ReceiptTotals {
  amounts: ReceiptCurrencyTotal[]
  missingAmount: number
}

export async function listReceipts(query: ReceiptQuery, page = 1, pageSize = 20): Promise<PagedResult<ReceiptListDto> & { totals: ReceiptTotals }> {
  return await request.get('/accounting/receipts', { params: { ...query, page, pageSize } }) as PagedResult<ReceiptListDto> & { totals: ReceiptTotals }
}

export async function getReceipt(id: string): Promise<ReceiptDto> {
  return await request.get(`/accounting/receipts/${id}`) as ReceiptDto
}

/** 建立草稿票據；原圖、地盤分類可稍後再補，不影響先填表。 */
export async function createReceipt(documentTypeDictId: string): Promise<ReceiptDto> {
  return await request.post('/accounting/receipts', { documentTypeDictId }) as ReceiptDto
}

/** 補上或更換票據原圖（JPEG / PNG，最多 4 MB）。 */
export async function attachReceiptImage(id: string, file: File): Promise<ReceiptDto> {
  const form = new FormData()
  form.append('file', file)
  return await request.post(`/accounting/receipts/${id}/image`, form, { timeout: 60000 }) as ReceiptDto
}

/** 辨識原圖，回傳建議欄位（不會改寫票據資料）。 */
export async function recognizeReceipt(id: string, documentTypeDictId?: string): Promise<RecognizeResult> {
  return await request.post(`/accounting/receipts/${id}/recognize`, null, { params: { documentTypeDictId }, timeout: 150000 }) as RecognizeResult
}

export async function updateReceipt(id: string, payload: UpdateReceipt): Promise<ReceiptDto> {
  return await request.put(`/accounting/receipts/${id}`, payload) as ReceiptDto
}

/** 刪除票據，連同原圖一併清理。 */
export async function deleteReceipt(id: string): Promise<void> {
  await request.delete(`/accounting/receipts/${id}`)
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