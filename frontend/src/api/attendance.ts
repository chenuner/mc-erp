import request from './request'
import { listSysDictsByGroup, type PagedResult, type SysDictDto } from './system'
import type { OcrField } from './accounting'

export interface WorkerDto {
  id: string
  name: string
  employeeId: string | null
  dailyWage: number
  isActive: boolean
  /** 職位分類（AttendancePosition 字典）的字典項 id，可為空；名稱由前端按 id 映射。 */
  positionId: string | null
}

/** 考勤卡同步時選員工用；日薪就是計薪用的日薪。 */
export async function listWorkers(): Promise<WorkerDto[]> {
  return await request.get('/attendance/workers') as WorkerDto[]
}

/** 職位分類選項從系統字典 AttendancePosition 讀取，由用戶在「系統字典」自行維護。 */
export async function listWorkerPositions(): Promise<SysDictDto[]> {
  return await listSysDictsByGroup('AttendancePosition')
}

/** 地盤選項和會計票據共用同一份系統字典（AccountingSiteCategory，「系統字典 → 地盤分類」）。 */
export async function listAttendanceSites(): Promise<SysDictDto[]> {
  return await listSysDictsByGroup('AccountingSiteCategory')
}

/** 卡上手寫姓名對不上任何員工時，直接用該姓名建一個工人；職位分類可選。 */
export async function createWorker(payload: { name: string; employeeId?: string | null; dailyWage: number; positionId?: string | null }): Promise<WorkerDto> {
  return (await request.post('/attendance/workers', payload)) as WorkerDto
}

/** 改員工的姓名 / 工號 / 日薪 / 職位 / 在職；只送有改的欄位。 */
export async function updateWorker(id: string, payload: { name?: string; employeeId?: string | null; dailyWage?: number; isActive?: boolean; positionId?: string | null }): Promise<WorkerDto> {
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
  /** 當月薪酬生成時的職位分類快照；之後改員工職位不會影響已生成的月份。 */
  positionId: string | null
  position: string | null
  /** 當月薪酬生成時的地盤快照；薪酬按「員工 × 職位 × 地盤」分組，所以同一人同一月可能有多行。 */
  siteDictId: string | null
  site: string | null
  year: number
  month: number
}

export interface PunchFields {
  clockInTime: string
  clockOutTime: string | null
  overtimeHours: number
  isLaborLeave: boolean
  /** 地盤決定這筆明細在薪酬表歸到哪一行（必填）。 */
  siteDictId: string
  /** 職位分類可為空；和地盤一起決定薪酬表的分組行。 */
  positionDictId: string | null
  remark: string | null
}

export interface PunchDto extends PunchFields {
  id: string
  workerId: string
  name: string
  employeeId: string | null
}

export interface AttendanceQuery {
  year: number
  month: number
  /** 只看某個員工；不填就是全部（姓名下拉選的是員工 id，重名不會混）。 */
  workerId?: string
  /** 只看薪酬表當月職位快照符合的員工；不填就是全部。 */
  positionId?: string
  /** 只看薪酬表當月地盤快照符合的行（一個人在兩個地盤做過就是兩行）；不填就是全部。 */
  siteDictId?: string
  page: number
  pageSize: number
}

/** 薪酬表合計，按整份篩選結果算（不是當前頁）；日薪是每人單價，不參與合計。 */
export interface PayrollTotals {
  workDays: number
  laborLeaveDays: number
  totalWorkDays: number
  overtimeHours: number
  overtimeAmount: number
  mpfEmployer: number
  mpfEmployee: number
  actualPay: number
  totalAmount: number
}

/** 打卡明細合計：打卡筆數及 O.T. 小時（明細不存工時）。 */
export interface PunchTotals {
  records: number
  overtimeHours: number
}

export async function listPayroll(params: AttendanceQuery): Promise<PagedResult<PayrollDto> & { totals: PayrollTotals }> {
  return (await request.get('/attendance/payroll', { params })) as PagedResult<PayrollDto> & { totals: PayrollTotals }
}

export async function listPunches(params: AttendanceQuery): Promise<PagedResult<PunchDto> & { totals: PunchTotals }> {
  return (await request.get('/attendance/punches', { params })) as PagedResult<PunchDto> & { totals: PunchTotals }
}

export async function updatePayroll(id: string, payload: PayrollFields): Promise<PayrollDto> {
  return (await request.put(`/attendance/payroll/${id}`, payload)) as PayrollDto
}

/** 改打卡明細的結果：payrollRows 是這次重算出來的薪酬行數，0 表示沒有涉及計薪的改動。 */
export interface PunchUpdateResult {
  punch: PunchDto
  payrollRows: number
}

export async function updatePunch(id: string, payload: PunchFields): Promise<PunchUpdateResult> {
  return (await request.put(`/attendance/punches/${id}`, payload)) as PunchUpdateResult
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
  /** 人工按「標記為已核對」的時間；null 表示還沒核對，同步會被後端拒絕。 */
  confirmedAt: string | null
  createdAt: string
}

export interface AttendanceCardScanSummaryDto {
  id: string
  year: number | null
  month: number | null
  name: string | null
  punchCount: number
  needsReview: boolean
  /** 三種狀態：需覆核（needsReview）、待核對（乾淨但沒標記）、已核對（confirmedAt 有值）。 */
  confirmedAt: string | null
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
 * 標記為已核對：清掉卡片層複核標記並記下核對時間，狀態由「需覆核」變「已核對」，時間欄不改。
 * 這是同步的前提；核對後再修改識別結果，核對時間會被清空，要重新標記。
 * includeHandwritten 為 true 時連手寫的年月姓名一併判定（低置信度仍會保留需覆核）。
 */
export async function confirmCardScan(id: string, includeHandwritten = false): Promise<AttendanceCardScanDto> {
  return (await request.post(`/attendance/scans/${id}/confirm`, { includeHandwritten })) as AttendanceCardScanDto
}

export async function deleteCardScan(id: string): Promise<void> {
  await request.delete(`/attendance/scans/${id}`)
}

/** 同步後該員工該月薪酬表的一行（按職位 × 地盤分組）；員工資訊在結果頂層。 */
export interface CardPayrollRow {
  id: string
  positionDictId: string | null
  position: string | null
  siteDictId: string | null
  site: string | null
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

/** 同步結果：寫入幾筆打卡明細，以及重算後該員工該月薪酬表的行（按職位 × 地盤分組，可能多行）。 */
export interface CardSyncResult {
  scanId: string
  year: number
  month: number
  workerId: string
  workerName: string
  /** 地盤是系統字典 AccountingSiteCategory 的字典項 id。 */
  siteDictId: string
  addedRecords: number
  rows: CardPayrollRow[]
}

/** 把識別結果導入打卡明細（員工、地盤由使用者選），並重算該員工該月的薪酬表；前提是已標記為已核對。 */
export async function syncCardScan(id: string, payload: { workerId: string; siteDictId: string }): Promise<CardSyncResult> {
  return (await request.post(`/attendance/scans/${id}/sync`, payload)) as CardSyncResult
}