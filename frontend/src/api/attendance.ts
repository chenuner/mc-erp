import request from './request'
import type { PagedResult } from './system'
import type { OcrField } from './accounting'

export interface SiteDto {
  id: string
  name: string
  address: string | null
  isActive: boolean
}

export async function listSites(): Promise<SiteDto[]> {
  return await request.get('/attendance/sites') as SiteDto[]
}

export interface WorkerDto {
  id: string
  name: string
  employeeId: string | null
  dailyWage: number
  isActive: boolean
}

/** 考勤卡同步時選員工用；日薪就是計薪用的日薪。 */
export async function listWorkers(): Promise<WorkerDto[]> {
  return await request.get('/attendance/workers') as WorkerDto[]
}

/** 卡上手寫姓名對不上任何員工時，直接用該姓名建一個工人。 */
export async function createWorker(payload: { name: string; employeeId?: string | null; dailyWage: number }): Promise<WorkerDto> {
  return (await request.post('/attendance/workers', payload)) as WorkerDto
}

/** 改員工的姓名 / 工號 / 日薪 / 在職；只送有改的欄位。 */
export async function updateWorker(id: string, payload: { name?: string; employeeId?: string | null; dailyWage?: number; isActive?: boolean }): Promise<WorkerDto> {
  return (await request.put(`/attendance/workers/${id}`, payload)) as WorkerDto
}

/** 刪除員工（軟刪除）：已入庫的打卡明細和薪酬保留，只是不再顯示。 */
export async function deleteWorker(id: string): Promise<void> {
  await request.delete(`/attendance/workers/${id}`)
}

export interface PayrollFields {
  workDays: number
  laborLeaveDays: number
  totalWorkDays: number
  overtimeHours: number
  dailyWage: number
  overtimeAmount: number
  mpfEmployer: number
  mpfEmployee: number
  actualPay: number
  totalAmount: number
  remark: string | null
}

export interface PayrollDto extends PayrollFields {
  id: string
  workerId: string
  name: string
  employeeId: string | null
  year: number
  month: number
}

export interface PunchFields {
  clockInTime: string
  clockOutTime: string | null
  overtimeHours: number
  isLaborLeave: boolean
  remark: string | null
}

export interface PunchDto extends PunchFields {
  id: string
  workerId: string
  name: string
  employeeId: string | null
  siteName: string
}

export interface AttendanceQuery {
  year: number
  month: number
  /** 只看某個員工；不填就是全部（姓名下拉選的是員工 id，重名不會混）。 */
  workerId?: string
  page: number
  pageSize: number
}

export async function listPayroll(params: AttendanceQuery): Promise<PagedResult<PayrollDto>> {
  return (await request.get('/attendance/payroll', { params })) as PagedResult<PayrollDto>
}

export async function listPunches(params: AttendanceQuery): Promise<PagedResult<PunchDto>> {
  return (await request.get('/attendance/punches', { params })) as PagedResult<PunchDto>
}

export async function updatePayroll(id: string, payload: PayrollFields): Promise<PayrollDto> {
  return (await request.put(`/attendance/payroll/${id}`, payload)) as PayrollDto
}

export async function updatePunch(id: string, payload: PunchFields): Promise<PunchDto> {
  return (await request.put(`/attendance/punches/${id}`, payload)) as PunchDto
}

export async function initializeAttendanceMock(year: number, month: number): Promise<{ added: number }> {
  return (await request.post('/attendance/mock', null, { params: { year, month } })) as { added: number }
}

/** 考勤卡單日打卡；時間是 HH:mm，空白代表當日沒有該次打卡。 */
export interface AttendanceRow {
  day: number
  amIn: OcrField<string>
  amOut: OcrField<string>
  pmIn: OcrField<string>
  pmOut: OcrField<string>
  otIn: OcrField<string>
  otOut: OcrField<string>
  needsReview: boolean
  reviewFields: string[]
}

/** 一次掃描的識別結果，形狀與後端 AttendanceCard 一致。 */
export interface AttendanceCard {
  year: OcrField<number>
  month: OcrField<number>
  name: OcrField<string>
  records: AttendanceRow[]
  reviewFields: string[]
  needsReview: boolean
}

export interface AttendanceCardScanDto {
  id: string
  fileItemId: string | null
  fileName: string | null
  card: AttendanceCard
  createdAt: string
}

export interface AttendanceCardScanSummaryDto {
  id: string
  year: number | null
  month: number | null
  name: string | null
  punchCount: number
  needsReview: boolean
  createdAt: string
}

export interface CardScanQuery {
  year?: number
  month?: number
  page: number
  pageSize: number
}

/** 單日打卡的人工修正；時間為 HH:mm，留空即清除該欄。 */
export interface CardPunchEdit {
  day: number
  amIn: string
  amOut: string
  pmIn: string
  pmOut: string
  otIn: string
  otOut: string
}

/** 一次掃描的人工修正內容；只改動過的欄位會被覆寫並記為人工確認。 */
export interface CardScanEdit {
  year: number | null
  month: number | null
  name: string | null
  records: CardPunchEdit[]
}

/** 上傳圖片 → 識別 → 落庫，回傳已保存的掃描（JPEG / PNG，最多 4 MB）。 */
export async function scanAttendanceCard(file: File): Promise<AttendanceCardScanDto> {
  const form = new FormData()
  form.append('file', file)
  return (await request.post('/attendance/scan', form, { timeout: 150000 })) as AttendanceCardScanDto
}

export async function listCardScans(params: CardScanQuery): Promise<PagedResult<AttendanceCardScanSummaryDto>> {
  return (await request.get('/attendance/scans', { params })) as PagedResult<AttendanceCardScanSummaryDto>
}

export async function getCardScan(id: string): Promise<AttendanceCardScanDto> {
  return (await request.get(`/attendance/scans/${id}`)) as AttendanceCardScanDto
}

/** 人工修正識別結果（年月、姓名、每日時間），回傳修正後的完整掃描。 */
export async function updateCardScan(id: string, payload: CardScanEdit): Promise<AttendanceCardScanDto> {
  return (await request.put(`/attendance/scans/${id}`, payload)) as AttendanceCardScanDto
}

/**
 * 標記為已核對：清掉卡片層複核標記，狀態由「需覆核」變「已識別」，時間欄不改。
 * includeHandwritten 為 true 時連手寫的年月姓名一併判定（低置信度仍會保留需覆核）。
 */
export async function confirmCardScan(id: string, includeHandwritten = false): Promise<AttendanceCardScanDto> {
  return (await request.post(`/attendance/scans/${id}/confirm`, { includeHandwritten })) as AttendanceCardScanDto
}

export async function deleteCardScan(id: string): Promise<void> {
  await request.delete(`/attendance/scans/${id}`)
}

/** 同步結果：寫入幾筆打卡明細，以及重算後的薪酬表內容。 */
export interface CardSyncResult {
  scanId: string
  year: number
  month: number
  workerId: string
  workerName: string
  siteId: string
  siteName: string
  addedRecords: number
  workDays: number
  laborLeaveDays: number
  totalWorkDays: number
  overtimeHours: number
  dailyWage: number
  overtimeAmount: number
  mpfEmployer: number
  mpfEmployee: number
  actualPay: number
  totalAmount: number
}

/** 把識別結果導入打卡明細（員工、地盤由使用者選），並重算該員工該月的薪酬表。 */
export async function syncCardScan(id: string, payload: { workerId: string; siteId: string }): Promise<CardSyncResult> {
  return (await request.post(`/attendance/scans/${id}/sync`, payload)) as CardSyncResult
}