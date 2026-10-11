import request from './request'
import type { PagedResult } from './system'

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