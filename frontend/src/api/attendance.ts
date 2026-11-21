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
  name?: string
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

export async function deleteCardScan(id: string): Promise<void> {
  await request.delete(`/attendance/scans/${id}`)
}