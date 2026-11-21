<script setup lang="ts">
import { computed, nextTick, onMounted, reactive, ref, watch } from 'vue'
import { ElMessage, ElMessageBox, type FormInstance, type FormRules } from 'element-plus'
import AppIcon from '@/components/AppIcon.vue'
import { useAuthStore } from '@/stores/auth'
import {
  createWorker, deleteCardScan, getCardScan, initializeAttendanceMock, listCardScans, listPayroll, listPunches,
  listSites, listWorkers, scanAttendanceCard, syncCardScan, updateCardScan, updatePayroll, updatePunch, updateWorker,
  type AttendanceCardScanDto, type AttendanceCardScanSummaryDto, type AttendanceRow, type PayrollDto,
  type PayrollFields, type PunchDto, type SiteDto, type WorkerDto,
} from '@/api/attendance'
import type { OcrField } from '@/api/accounting'

type NumericField = Exclude<keyof PayrollFields, 'remark'>
const columns: { key: NumericField; label: string; money: boolean; width: number }[] = [
  { key: 'workDays', label: '工數', money: false, width: 85 },
  { key: 'laborLeaveDays', label: '勞工假', money: false, width: 85 },
  { key: 'totalWorkDays', label: '總工數', money: false, width: 90 },
  { key: 'overtimeHours', label: 'O.T.(小時)', money: false, width: 110 },
  { key: 'dailyWage', label: '日薪', money: true, width: 110 },
  { key: 'overtimeAmount', label: 'OT金額', money: true, width: 115 },
  { key: 'mpfEmployer', label: 'MPF（僱主）', money: true, width: 125 },
  { key: 'mpfEmployee', label: 'MPF（僱員）', money: true, width: 125 },
  { key: 'actualPay', label: '實付人工', money: true, width: 125 },
  { key: 'totalAmount', label: '總數', money: true, width: 125 },
]

const auth = useAuthStore()
const isAdmin = computed(() => auth.roles.includes('Admin'))
const activeTab = ref<'payroll' | 'punches' | 'cards'>('payroll')
const now = new Intl.DateTimeFormat('en-CA', { timeZone: 'Asia/Hong_Kong', year: 'numeric', month: '2-digit' }).formatToParts(new Date())
const month = ref(`${now.find(part => part.type === 'year')?.value}-${now.find(part => part.type === 'month')?.value}`)
const keyword = ref('')
const nameFilter = ref('')
const page = ref(1)
const pageSize = ref(20)
const total = ref(0)
const loading = ref(false)
const initializing = ref(false)
const payroll = ref<PayrollDto[]>([])
const punches = ref<PunchDto[]>([])
const cards = ref<AttendanceCardScanSummaryDto[]>([])
let requestId = 0

const numberFormatter = new Intl.NumberFormat('zh-HK', { maximumFractionDigits: 2 })
const moneyFormatter = new Intl.NumberFormat('zh-HK', { minimumFractionDigits: 2, maximumFractionDigits: 2 })
const timeFormatter = new Intl.DateTimeFormat('en-GB', {
  timeZone: 'Asia/Hong_Kong', year: 'numeric', month: '2-digit', day: '2-digit',
  hour: '2-digit', minute: '2-digit', second: '2-digit', hourCycle: 'h23',
})

function localTime(iso: string | null): string {
  if (!iso) return ''
  const parts = timeFormatter.formatToParts(new Date(iso))
  const part = (type: string) => parts.find(item => item.type === type)?.value ?? ''
  return `${part('year')}-${part('month')}-${part('day')} ${part('hour')}:${part('minute')}:${part('second')}`
}

function selectedMonth() {
  const [year, monthNumber] = month.value.split('-').map(Number)
  return { year: year!, month: monthNumber! }
}

async function fetchRows() {
  const id = ++requestId
  loading.value = true
  try {
    if (activeTab.value === 'cards') {
      // 考勤卡列表按年月篩，但年份月份可能識別不出來，所以不帶姓名條件
      const selected = selectedMonth()
      const result = await listCardScans({ year: selected.year, month: selected.month, page: page.value, pageSize: pageSize.value })
      if (id !== requestId) return
      cards.value = result.items
      total.value = result.total
    } else {
      const params = { ...selectedMonth(), name: nameFilter.value || undefined, page: page.value, pageSize: pageSize.value }
      if (activeTab.value === 'payroll') {
        const result = await listPayroll(params)
        if (id !== requestId) return
        payroll.value = result.items
        total.value = result.total
      } else {
        const result = await listPunches(params)
        if (id !== requestId) return
        punches.value = result.items
        total.value = result.total
      }
    }
    if (page.value > 1 && (page.value - 1) * pageSize.value >= total.value) page.value = 1
  } catch {
    if (id === requestId) {
      payroll.value = []
      punches.value = []
      cards.value = []
      total.value = 0
    }
  } finally {
    if (id === requestId) loading.value = false
  }
}

function search() {
  nameFilter.value = keyword.value.trim()
  if (page.value !== 1) page.value = 1
  else fetchRows()
}

watch([month, activeTab, pageSize], () => {
  payroll.value = []
  punches.value = []
  cards.value = []
  total.value = 0
  if (page.value !== 1) page.value = 1
  else fetchRows()
})
watch(page, fetchRows)
onMounted(fetchRows)

async function initializeMock() {
  try {
    await ElMessageBox.confirm(`初始化 ${month.value} 的模擬打卡及薪酬？已有月結不會被覆蓋。`, '初始化模擬數據', {
      confirmButtonText: '初始化', cancelButtonText: '取消', type: 'warning',
    })
  } catch { return }
  initializing.value = true
  try {
    const selected = selectedMonth()
    const result = await initializeAttendanceMock(selected.year, selected.month)
    ElMessage.success(result.added > 0 ? `已初始化 ${result.added} 名員工的模擬數據` : '該月份已有模擬數據，未覆蓋')
    await fetchRows()
  } catch {
  } finally { initializing.value = false }
}

const payrollDialog = ref(false)
const punchDialog = ref(false)
const saving = ref(false)
const editingId = ref('')
const editingName = ref('')
const payrollFormRef = ref<FormInstance>()
const punchFormRef = ref<FormInstance>()
const payrollForm = reactive<PayrollFields>({
  workDays: 0, laborLeaveDays: 0, totalWorkDays: 0, overtimeHours: 0, dailyWage: 0,
  overtimeAmount: 0, mpfEmployer: 0, mpfEmployee: 0, actualPay: 0, totalAmount: 0, remark: '',
})
const payrollRules: FormRules = Object.fromEntries(columns.map(column => [column.key, [
  { required: true, type: 'number', min: 0, message: `請輸入有效的${column.label}`, trigger: 'change' },
]]))
const punchForm = reactive({ clockInTime: '', clockOutTime: '', overtimeHours: 0, isLaborLeave: false, remark: '' })
const punchRules: FormRules = {
  clockInTime: [{ required: true, message: '請選擇上班時間', trigger: 'change' }],
  overtimeHours: [{ required: true, type: 'number', min: 0, message: '請輸入有效的加班小時', trigger: 'change' }],
  clockOutTime: [{
    validator: (_rule, value, callback) => {
      if (value && value <= punchForm.clockInTime) callback(new Error('下班時間須晚於上班時間'))
      else callback()
    }, trigger: 'change',
  }],
}

function editPayroll(row: PayrollDto) {
  editingId.value = row.id
  editingName.value = row.name
  columns.forEach(column => { payrollForm[column.key] = row[column.key] })
  payrollForm.remark = row.remark ?? ''
  payrollDialog.value = true
}

function editPunch(row: PunchDto) {
  editingId.value = row.id
  editingName.value = row.name
  Object.assign(punchForm, {
    clockInTime: localTime(row.clockInTime), clockOutTime: localTime(row.clockOutTime),
    overtimeHours: row.overtimeHours, isLaborLeave: row.isLaborLeave, remark: row.remark ?? '',
  })
  punchDialog.value = true
}

async function savePayroll() {
  if (!await payrollFormRef.value?.validate().catch(() => false)) return
  saving.value = true
  try {
    await updatePayroll(editingId.value, { ...payrollForm })
    payrollDialog.value = false
    ElMessage.success('薪酬已保存')
    await fetchRows()
  } catch {
  } finally { saving.value = false }
}

async function savePunch() {
  if (!await punchFormRef.value?.validate().catch(() => false)) return
  saving.value = true
  try {
    await updatePunch(editingId.value, {
      ...punchForm, clockInTime: `${punchForm.clockInTime.replace(' ', 'T')}+08:00`,
      clockOutTime: punchForm.clockOutTime ? `${punchForm.clockOutTime.replace(' ', 'T')}+08:00` : null,
    })
    punchDialog.value = false
    ElMessage.success('打卡明細已保存')
    await fetchRows()
  } catch {
  } finally { saving.value = false }
}

// --- 考勤卡識別 ---
const cameraInput = ref<HTMLInputElement>()
const albumInput = ref<HTMLInputElement>()
const scanning = ref(false)
const cardDetail = ref<AttendanceCardScanDto | null>(null)
const cardDialog = ref(false)
const cardLoading = ref(false)
const cardSaving = ref(false)
const workers = ref<WorkerDto[]>([])
const sites = ref<SiteDto[]>([])
const syncWorkerId = ref('')
const syncSiteId = ref('')
const syncing = ref(false)

type CardTimeKey = 'amIn' | 'amOut' | 'pmIn' | 'pmOut' | 'otIn' | 'otOut'
const cardTimeColumns: { key: CardTimeKey; label: string }[] = [
  { key: 'amIn', label: '上午上班' }, { key: 'amOut', label: '上午下班' },
  { key: 'pmIn', label: '下午上班' }, { key: 'pmOut', label: '下午下班' },
  { key: 'otIn', label: '加班上班' }, { key: 'otOut', label: '加班下班' },
]

/** 可編輯的一列：值、識別置信度（人工改過就清空）和待複核欄位。 */
interface CardRowDraft {
  day: number
  values: Record<CardTimeKey, string>
  confidence: Record<CardTimeKey, number | null>
  review: string[]
}
interface CardDraft {
  year: number | null
  month: number | null
  name: string
  rows: CardRowDraft[]
}

const cardDraft = ref<CardDraft | null>(null)
const cardBaseline = ref('')
const editingKey = ref('')
const editingText = ref('')
let editorRef: { focus?: () => void } | null = null

/** 和上次保存的內容比，決定「保存修改」是否可用。 */
const cardDirty = computed(() => cardDraft.value != null && JSON.stringify(cardDraft.value) !== cardBaseline.value)

/** 考勤卡各欄的中文名，用於複核提示。 */
const cardLabels: Record<string, string> = {
  year: '年份', month: '月份', name: '姓名', layout: '表格版面',
  amIn: '上午上班', amOut: '上午下班', pmIn: '下午上班', pmOut: '下午下班', otIn: '加班上班', otOut: '加班下班',
}

const cardReviewText = computed(() =>
  (cardDetail.value?.card.reviewFields ?? []).map(name => cardLabels[name] ?? name).join('、'))

const hasTime = (row: AttendanceRow) => cardTimeColumns.some(column => row[column.key].value)

function draftRow(row: AttendanceRow): CardRowDraft {
  return {
    day: row.day,
    values: {
      amIn: row.amIn.value ?? '', amOut: row.amOut.value ?? '', pmIn: row.pmIn.value ?? '',
      pmOut: row.pmOut.value ?? '', otIn: row.otIn.value ?? '', otOut: row.otOut.value ?? '',
    },
    confidence: {
      amIn: row.amIn.confidence, amOut: row.amOut.confidence, pmIn: row.pmIn.confidence,
      pmOut: row.pmOut.confidence, otIn: row.otIn.confidence, otOut: row.otOut.confidence,
    },
    review: [...row.reviewFields],
  }
}

/** 載入一份識別結果；只列出有打卡的日子，識別置信度留在 cardDetail。 */
function loadCard(scan: AttendanceCardScanDto) {
  cardDetail.value = scan
  cardDraft.value = {
    year: scan.card.year.value,
    month: scan.card.month.value,
    name: scan.card.name.value ?? '',
    rows: scan.card.records.filter(hasTime).map(draftRow),
  }
  cardBaseline.value = JSON.stringify(cardDraft.value)
  editingKey.value = ''
}

function resetCard() {
  if (cardDetail.value) loadCard(cardDetail.value)
}

/** 這一欄是否已被人工改過（未保存）：改過就不再顯示原本的識別置信度。 */
function edited(key: 'year' | 'month' | 'name'): boolean {
  const card = cardDetail.value?.card
  const draft = cardDraft.value
  if (!card || !draft) return false
  return key === 'name' ? (draft.name.trim() || null) !== card.name.value : draft[key] !== card[key].value
}

/** 輸入框由 v-if 現身，所以要自己聚焦（Element Plus 的 el-input 有 focus()）。 */
function bindEditor(target: unknown) {
  editorRef = target as { focus?: () => void } | null
}

async function startEdit(key: string, value: string | number | null) {
  editingText.value = value == null ? '' : String(value)
  editingKey.value = key
  await nextTick()
  editorRef?.focus?.()
}

function cancelEdit() {
  editingKey.value = ''
}

function commitMeta(key: 'year' | 'month', text: string) {
  const draft = cardDraft.value
  if (!draft) return
  if (text === '') { draft[key] = null; return }
  const value = Number(text)
  const [min, max] = key === 'year' ? [2000, 2099] : [1, 12]
  if (!Number.isInteger(value) || value < min || value > max) {
    ElMessage.warning(key === 'year' ? '年份須為 2000 至 2099' : '月份須為 1 至 12')
    return
  }
  draft[key] = value
}

function commitTime(day: number, field: CardTimeKey, text: string) {
  const row = cardDraft.value?.rows.find(item => item.day === day)
  if (!row) return
  if (text !== '' && !/^([01]?\d|2[0-3]):[0-5]\d$/.test(text)) {
    ElMessage.warning('時間格式須為 HH:mm，例如 09:07')
    return
  }
  const value = text.padStart(5, '0')
  // 值沒改就當沒編輯過，原本的識別置信度照舊
  if (value === row.values[field]) return
  row.values[field] = value
  // 真的改過才不算「識別置信度低」，由人工負責
  row.confidence[field] = null
  row.review = row.review.filter(name => name !== field)
}

/** 提交雙擊的修改；key 為 year / month / name / `day:5:amIn`。 */
function commitEdit() {
  const key = editingKey.value
  const text = editingText.value.trim()
  editingKey.value = ''
  if (!key || !cardDraft.value) return
  if (key === 'year' || key === 'month') { commitMeta(key, text); return }
  if (key === 'name') { cardDraft.value.name = text; return }
  const parts = key.split(':')
  commitTime(Number(parts[1]), parts[2] as CardTimeKey, text)
}

/** 把月份篩選移到識別到的年月；改了月份 watch 就會自動重查。 */
function locateMonth(year: number | null, monthNumber: number | null): boolean {
  if (!year || !monthNumber) return false
  const value = `${year}-${String(monthNumber).padStart(2, '0')}`
  if (month.value === value) return false
  month.value = value
  return true
}

/** 員工和地盤清單只在第一次用到時載入；同步要選人、選地盤。 */
async function ensureOptions() {
  await Promise.all([
    workers.value.length ? Promise.resolve() : listWorkers().then(rows => { workers.value = rows }).catch(() => {}),
    sites.value.length ? Promise.resolve() : listSites().then(rows => { sites.value = rows }).catch(() => {}),
  ])
}

/** 卡上手寫姓名和員工姓名對得上就預選，對不上留空由使用者自己選。 */
function matchWorker(name: string | null) {
  syncWorkerId.value = workers.value.find(worker => worker.name === name)?.id ?? ''
}

/** 這次會寫入幾筆打卡明細＝有打卡的日子數。 */
const syncRowCount = computed(() => cardDraft.value?.rows.length ?? 0)

/** 已選員工的日薪；0 表示沒設定，計薪會變成負數。 */
const syncWorkerWage = computed(() => workers.value.find(worker => worker.id === syncWorkerId.value)?.dailyWage ?? null)

/** 年月、員工、地盤都齊、員工有日薪、沒有未保存修改才可以同步。 */
const canSync = computed(() => !!cardDraft.value?.year && !!cardDraft.value?.month
  && !!syncWorkerId.value && !!syncSiteId.value && (syncWorkerWage.value ?? 0) > 0 && !cardDirty.value)

/** 日薪是計薪基礎，選到沒日薪的工人時在這裡補上（存回工人本身，下次不用再填）。 */
async function setWorkerWage() {
  const worker = workers.value.find(item => item.id === syncWorkerId.value)
  if (!worker) return
  let text: string
  try {
    const result = await ElMessageBox.prompt('日薪（HK$）', `設定「${worker.name}」的日薪`, {
      confirmButtonText: '保存', cancelButtonText: '取消', inputValue: worker.dailyWage > 0 ? String(worker.dailyWage) : '',
      inputPattern: /^\d+(\.\d{1,2})?$/, inputErrorMessage: '請輸入 0 或正數，最多兩位小數',
    })
    text = result.value
  } catch { return }
  try {
    const updated = await updateWorker(worker.id, { dailyWage: Number(text) })
    workers.value = workers.value.map(item => item.id === updated.id ? updated : item)
    ElMessage.success(`已設定日薪 HK$${moneyFormatter.format(updated.dailyWage)}，可以同步了`)
  } catch { /* 已由攔截器提示 */ }
}

/** 卡上手寫姓名在員工清單裡找不到時，允許直接用該姓名建工人（否則新名字永遠同步不了）。 */
const unmatchedName = computed(() => {
  const name = cardDraft.value?.name.trim()
  return name && !workers.value.some(worker => worker.name === name) ? name : ''
})

/** 用卡上的姓名建工人，日薪由使用者補；建好即選中。 */
async function createWorkerFromCard() {
  const name = unmatchedName.value
  if (!name) return
  let wage: string
  try {
    const result = await ElMessageBox.prompt('日薪（HK$），之後可在薪酬表修改', `新建工人：${name}`, {
      confirmButtonText: '新建', cancelButtonText: '取消', inputValue: '0',
      inputPattern: /^\d+(\.\d{1,2})?$/, inputErrorMessage: '請輸入 0 或正數，最多兩位小數',
    })
    wage = result.value
  } catch { return }
  try {
    const worker = await createWorker({ name, dailyWage: Number(wage) })
    workers.value = [...workers.value, worker]
    syncWorkerId.value = worker.id
    ElMessage.success(`已新建工人 ${worker.name}`)
  } catch { /* 已由攔截器提示 */ }
}

/** 同步：導入打卡明細並重算該員工該月薪酬，完成後切到薪酬表看結果。 */
async function syncCard() {
  const detail = cardDetail.value
  const draft = cardDraft.value
  if (!detail || !draft || !canSync.value) return
  const workerName = workers.value.find(worker => worker.id === syncWorkerId.value)?.name ?? '該員工'
  try {
    await ElMessageBox.confirm(
      `將為「${workerName}」寫入 ${draft.rows.length} 筆打卡明細，並重算 ${draft.year} 年 ${draft.month} 月薪酬。該員工該月上次由考勤卡導入的明細會被覆蓋（模擬或人工的明細不受影響）。`,
      '同步到打卡明細及薪酬表', { confirmButtonText: '同步', cancelButtonText: '取消', type: 'warning' })
  } catch { return }
  syncing.value = true
  try {
    const result = await syncCardScan(detail.id, { workerId: syncWorkerId.value, siteId: syncSiteId.value })
    ElMessage.success(`已寫入 ${result.addedRecords} 筆打卡明細；${result.year} 年 ${result.month} 月薪酬已重算：工數 ${numberFormatter.format(result.workDays)}、O.T. ${numberFormatter.format(result.overtimeHours)} 小時、實付 HK$${moneyFormatter.format(result.actualPay)}`)
    cardDialog.value = false
    // 切到薪酬表才看得到重算結果；篩選也跟著卡上的年月走
    activeTab.value = 'payroll'
    if (!locateMonth(result.year, result.month)) await fetchRows()
  } catch { /* 已由攔截器提示 */ }
  finally { syncing.value = false }
}

function pickCard(event: Event) {
  const input = event.target as HTMLInputElement
  const file = input.files?.[0]
  input.value = ''
  if (!file) return
  if (!['image/jpeg', 'image/png'].includes(file.type) || file.size > 4 * 1024 * 1024 || file.size === 0) {
    ElMessage.warning('請選擇不超過 4 MB 的 JPEG / PNG 圖片')
    return
  }
  void scanCard(file)
}

/** 上傳即識別並保存，完成後跳到識別到的年月並打開明細讓使用者對照原圖核對。 */
async function scanCard(file: File) {
  scanning.value = true
  try {
    const [scan] = await Promise.all([scanAttendanceCard(file), ensureOptions()])
    const rows = scan.card.records.filter(hasTime)
    if (scan.card.needsReview) {
      const names = scan.card.reviewFields.map(name => cardLabels[name] ?? name).join('、')
      ElMessage.warning(names ? `已識別 ${rows.length} 天，但${names}置信度偏低，請核對` : `已識別 ${rows.length} 天，有時間欄位置信度偏低，請核對`)
    } else {
      ElMessage.success(`已識別並保存 ${rows.length} 天的打卡記錄`)
    }
    loadCard(scan)
    matchWorker(scan.card.name.value)
    // 篩選跟著識別到的年月走，否則新記錄會因為月份不符而看不到
    const moved = locateMonth(scan.card.year.value, scan.card.month.value)
    const switched = activeTab.value !== 'cards'
    if (switched) activeTab.value = 'cards'
    if (!moved && !switched) await fetchRows()
    cardDialog.value = true
    if (!scan.card.year.value || !scan.card.month.value) {
      ElMessage.warning('沒有認出年月，這筆記錄不會出現在按月份篩選的列表；請在明細裡雙擊年月補上再保存')
    }
  } catch { /* 已由攔截器提示 */ }
  finally { scanning.value = false }
}

async function openCard(row: AttendanceCardScanSummaryDto) {
  cardDialog.value = true
  cardLoading.value = true
  cardDetail.value = null
  cardDraft.value = null
  try {
    const [scan] = await Promise.all([getCardScan(row.id), ensureOptions()])
    loadCard(scan)
    matchWorker(scan.card.name.value)
  } catch {
    cardDialog.value = false
  } finally { cardLoading.value = false }
}

/** 保存人工修正；年月一併反映到列表篩選，改完就看得到。 */
async function saveCard() {
  const draft = cardDraft.value
  const detail = cardDetail.value
  if (!draft || !detail) return
  cardSaving.value = true
  try {
    const updated = await updateCardScan(detail.id, {
      year: draft.year,
      month: draft.month,
      name: draft.name.trim() || null,
      records: draft.rows.map(row => ({ day: row.day, ...row.values })),
    })
    loadCard(updated)
    if (!locateMonth(updated.card.year.value, updated.card.month.value)) await fetchRows()
    ElMessage.success('考勤卡識別結果已保存')
    if (!updated.card.year.value || !updated.card.month.value) {
      ElMessage.warning('沒有年月，這筆記錄不會出現在按月份篩選的列表；補上年月再保存就會出現')
    }
  } catch { /* 已由攔截器提示 */ }
  finally { cardSaving.value = false }
}

function closeCard() {
  cardDetail.value = null
  cardDraft.value = null
  cardBaseline.value = ''
  editingKey.value = ''
}

async function removeCard(row: AttendanceCardScanSummaryDto) {
  try {
    await ElMessageBox.confirm('刪除該掃描記錄及其原圖？已保存的打卡明細不會受影響。', '刪除掃描記錄', {
      confirmButtonText: '刪除', cancelButtonText: '取消', type: 'warning',
    })
  } catch { return }
  try {
    await deleteCardScan(row.id)
    ElMessage.success('已刪除')
    if (cardDetail.value?.id === row.id) cardDialog.value = false
    await fetchRows()
  } catch { /* 已由攔截器提示 */ }
}

/** 置信度低於 80% 的欄位標警告色，和後端閾值一致。 */
const lowConfidence = (field: OcrField<unknown>) => field.confidence == null || field.confidence < 0.8
const percent = (field: OcrField<unknown>) => field.confidence == null ? '—' : `${Math.round(field.confidence * 100)}%`
</script>

<template>
  <div class="attendance-view">
    <div class="page-header">
      <h1 class="page-title">考勤薪酬</h1>
      <div class="header-actions">
        <el-button :loading="scanning" @click="cameraInput?.click()">
          <AppIcon name="image" :size="16" class="button-icon" />拍照識別考勤卡
        </el-button>
        <el-button :loading="scanning" @click="albumInput?.click()">選擇考勤卡圖片</el-button>
        <el-button @click="initializeMock">
          <AppIcon name="plus" :size="16" class="button-icon" />初始化模擬數據
        </el-button>
      </div>
      <input ref="cameraInput" class="file-input" type="file" accept="image/jpeg,image/png" capture="environment" @change="pickCard" />
      <input ref="albumInput" class="file-input" type="file" accept="image/jpeg,image/png" @change="pickCard" />
    </div>

    <el-tabs v-model="activeTab">
      <el-tab-pane label="員工薪酬表" name="payroll" />
      <el-tab-pane label="打卡明細" name="punches" />
      <el-tab-pane label="考勤卡識別" name="cards" />
    </el-tabs>

    <form class="filters" @submit.prevent="search">
      <div class="filter-field">
        <label for="attendance-month">月份</label>
        <el-date-picker id="attendance-month" v-model="month" type="month" value-format="YYYY-MM" format="YYYY年MM月"
          :clearable="false" :disabled="initializing" :disabled-date="(date: Date) => date.getFullYear() < 2000 || date.getFullYear() > 2099" />
      </div>
      <div class="filter-field">
        <label for="attendance-name">姓名</label>
        <el-input id="attendance-name" v-model="keyword" clearable maxlength="100" :disabled="activeTab === 'cards'"
          :placeholder="activeTab === 'cards' ? '考勤卡按年月篩選' : ''" @clear="search" />
      </div>
      <el-button type="primary" native-type="submit"><AppIcon name="search" :size="16" class="button-icon" />查詢</el-button>
      <el-tooltip content="刷新" placement="top">
        <el-button :disabled="loading" circle aria-label="刷新" @click="fetchRows"><AppIcon name="refresh-cw" :size="16" /></el-button>
      </el-tooltip>
    </form>

    <div class="table-region">
      <el-table v-if="activeTab === 'payroll'" v-loading="loading" :data="payroll" stripe row-key="id" max-height="600">
        <el-table-column prop="name" label="姓名" width="110" fixed show-overflow-tooltip />
        <el-table-column v-for="column in columns" :key="column.key" :prop="column.key" :label="column.label" :width="column.width" align="right">
          <template #default="{ row }">
            <span class="num">{{ (column.money ? moneyFormatter : numberFormatter).format(row[column.key]) }}</span>
          </template>
        </el-table-column>
        <el-table-column prop="remark" label="注意！" min-width="230" show-overflow-tooltip>
          <template #default="{ row }">{{ row.remark || '—' }}</template>
        </el-table-column>
        <el-table-column v-if="isAdmin" label="操作" width="70" fixed="right" align="center">
          <template #default="{ row }">
            <el-tooltip content="編輯薪酬" placement="top">
              <el-button link type="primary" :aria-label="`編輯${row.name}的薪酬`" @click="editPayroll(row)"><AppIcon name="file-text" :size="16" /></el-button>
            </el-tooltip>
          </template>
        </el-table-column>
        <template #empty><el-empty description="該月份暫無薪酬資料" :image-size="72" /></template>
      </el-table>

      <el-table v-else-if="activeTab === 'punches'" v-loading="loading" :data="punches" stripe row-key="id" max-height="600">
        <el-table-column prop="name" label="姓名" width="110" fixed show-overflow-tooltip />
        <el-table-column prop="employeeId" label="工號" width="125" />
        <el-table-column prop="siteName" label="地盤" min-width="140" show-overflow-tooltip />
        <el-table-column label="上班打卡（香港時間）" width="205">
          <template #default="{ row }"><span class="num">{{ localTime(row.clockInTime) }}</span></template>
        </el-table-column>
        <el-table-column label="下班打卡（香港時間）" width="205">
          <template #default="{ row }"><span class="num">{{ localTime(row.clockOutTime) || '—' }}</span></template>
        </el-table-column>
        <el-table-column label="O.T.(小時)" width="110" align="right">
          <template #default="{ row }"><span class="num">{{ numberFormatter.format(row.overtimeHours) }}</span></template>
        </el-table-column>
        <el-table-column label="勞工假" width="90" align="center">
          <template #default="{ row }"><el-tag v-if="row.isLaborLeave" type="warning" effect="plain">勞工假</el-tag><span v-else>否</span></template>
        </el-table-column>
        <el-table-column label="注意！" prop="remark" min-width="180" show-overflow-tooltip />
        <el-table-column v-if="isAdmin" label="操作" width="70" fixed="right" align="center">
          <template #default="{ row }">
            <el-tooltip content="編輯打卡" placement="top">
              <el-button link type="primary" :aria-label="`編輯${row.name}的打卡`" @click="editPunch(row)"><AppIcon name="clock" :size="16" /></el-button>
            </el-tooltip>
          </template>
        </el-table-column>
        <template #empty><el-empty description="該月份暫無打卡資料" :image-size="72" /></template>
      </el-table>

      <el-table v-else v-loading="loading" :data="cards" stripe row-key="id" max-height="600">
        <el-table-column label="年月" width="110">
          <template #default="{ row }">
            {{ row.year == null || row.month == null ? '未識別' : `${row.year} 年 ${row.month} 月` }}
          </template>
        </el-table-column>
        <el-table-column label="姓名（手寫）" width="130" show-overflow-tooltip>
          <template #default="{ row }">{{ row.name || '未識別' }}</template>
        </el-table-column>
        <el-table-column label="打卡天數" width="110" align="right">
          <template #default="{ row }"><span class="num">{{ row.punchCount }}</span></template>
        </el-table-column>
        <el-table-column label="狀態" width="110">
          <template #default="{ row }">
            <el-tag :type="row.needsReview ? 'warning' : 'success'" effect="plain">{{ row.needsReview ? '需覆核' : '已識別' }}</el-tag>
          </template>
        </el-table-column>
        <el-table-column label="掃描時間（香港時間）" min-width="185">
          <template #default="{ row }"><span class="num">{{ localTime(row.createdAt) }}</span></template>
        </el-table-column>
        <el-table-column v-if="isAdmin" label="操作" width="130" fixed="right" align="center">
          <template #default="{ row }">
            <el-button link type="primary" @click="openCard(row)">查看</el-button>
            <el-button link type="danger" @click="removeCard(row)">刪除</el-button>
          </template>
        </el-table-column>
        <template #empty>
          <el-empty description="該月份暫無考勤卡掃描記錄，可按右上角「拍照識別考勤卡」上傳" :image-size="72" />
        </template>
      </el-table>
      <el-pagination v-model:current-page="page" v-model:page-size="pageSize" :total="total" :page-sizes="[20, 50, 100]"
        layout="total, sizes, prev, pager, next" :pager-count="5" class="pagination" />
    </div>

    <el-dialog v-model="cardDialog" title="考勤卡識別結果" width="min(940px, calc(100vw - 32px))" @closed="closeCard">
      <div v-loading="cardLoading">
        <template v-if="cardDetail && cardDraft">
          <el-alert class="card-saved" type="success" :closable="false" show-icon
            :title="`已保存到「考勤卡識別」記錄（${localTime(cardDetail.createdAt)}）`"
            description="上傳時已自動保存。雙擊年月、姓名或任何時間即可修正，改完按「保存修改」；沒按保存就關閉不會影響已保存的內容。" />
          <el-alert v-if="cardDetail.card.reviewFields.length" :title="`需人工複核：${cardReviewText}（手寫內容識別率有限，請對照原卡核對）`"
            type="warning" :closable="false" show-icon />
          <el-descriptions class="card-meta" :column="3" border size="small">
            <el-descriptions-item label="年份（雙擊修改）">
              <el-input v-if="editingKey === 'year'" :ref="bindEditor" v-model="editingText" size="small" class="card-editor"
                maxlength="4" placeholder="2026" @keyup.enter="commitEdit" @keyup.esc="cancelEdit" @blur="commitEdit" />
              <span v-else class="card-editable" @dblclick="startEdit('year', cardDraft.year)">
                <span :class="{ low: !edited('year') && lowConfidence(cardDetail.card.year) }">{{ cardDraft.year ?? '未識別' }}</span>
                <span v-if="!edited('year')" class="confidence-value">{{ percent(cardDetail.card.year) }}</span>
              </span>
            </el-descriptions-item>
            <el-descriptions-item label="月份（雙擊修改）">
              <el-input v-if="editingKey === 'month'" :ref="bindEditor" v-model="editingText" size="small" class="card-editor"
                maxlength="2" placeholder="11" @keyup.enter="commitEdit" @keyup.esc="cancelEdit" @blur="commitEdit" />
              <span v-else class="card-editable" @dblclick="startEdit('month', cardDraft.month)">
                <span :class="{ low: !edited('month') && lowConfidence(cardDetail.card.month) }">{{ cardDraft.month ?? '未識別' }}</span>
                <span v-if="!edited('month')" class="confidence-value">{{ percent(cardDetail.card.month) }}</span>
              </span>
            </el-descriptions-item>
            <el-descriptions-item label="姓名（手寫，雙擊修改）">
              <el-input v-if="editingKey === 'name'" :ref="bindEditor" v-model="editingText" size="small" class="card-editor"
                maxlength="50" @keyup.enter="commitEdit" @keyup.esc="cancelEdit" @blur="commitEdit" />
              <span v-else class="card-editable" @dblclick="startEdit('name', cardDraft.name)">
                <span :class="{ low: !edited('name') && lowConfidence(cardDetail.card.name) }">{{ cardDraft.name || '未識別' }}</span>
                <span v-if="!edited('name')" class="confidence-value">{{ percent(cardDetail.card.name) }}</span>
              </span>
            </el-descriptions-item>
          </el-descriptions>
          <div class="card-sync">
            <div class="card-sync-title">同步到打卡明細及薪酬表</div>
            <div class="card-sync-fields">
              <el-select v-model="syncWorkerId" filterable placeholder="選擇員工" class="card-sync-field">
                <el-option v-for="worker in workers" :key="worker.id" :label="worker.name" :value="worker.id"
                  :disabled="!worker.isActive">
                  <span>{{ worker.name }}</span>
                  <span class="option-note">{{ worker.employeeId ?? '無工號' }} · 日薪 {{ moneyFormatter.format(worker.dailyWage) }}</span>
                </el-option>
              </el-select>
              <el-button v-if="unmatchedName" link type="primary" @click="createWorkerFromCard">新建「{{ unmatchedName }}」為工人</el-button>
              <el-select v-model="syncSiteId" filterable placeholder="選擇地盤" class="card-sync-field">
                <el-option v-for="site in sites" :key="site.id" :label="site.name" :value="site.id" :disabled="!site.isActive" />
              </el-select>
              <el-button type="primary" :loading="syncing" :disabled="!canSync" @click="syncCard">同步到打卡明細及薪酬表</el-button>
              <span v-if="cardDirty" class="card-sync-note">有未保存的修改，先按「保存修改」</span>
            </div>
            <p class="card-hint">
              會寫入 {{ syncRowCount }} 筆打卡明細（有打卡的日子各一筆），並重算 {{ cardDraft.year ?? '—' }} 年 {{ cardDraft.month ?? '—' }} 月薪酬：
              O.T.＝加班小時 ×（日薪÷8×1.5），MPF＝總工數×50，勞工假沿用薪酬表已填的值。
              同一員工同一月再同步會覆蓋上次導入的明細，不會重複計工數。
            </p>
            <p v-if="syncWorkerWage === 0" class="card-sync-note">
              該員工日薪是 0，實付人工會是負數；
              <el-button link type="primary" @click="setWorkerWage">設定日薪</el-button>
            </p>
          </div>
          <el-table :data="cardDraft.rows" stripe row-key="day" max-height="420" size="small" class="card-rows"
            empty-text="沒有讀到任何打卡時間">
            <el-table-column prop="day" label="日" width="60" align="center" />
            <el-table-column v-for="column in cardTimeColumns" :key="column.key" :label="column.label" min-width="96" align="right">
              <template #default="{ row }">
                <el-input v-if="editingKey === `day:${row.day}:${column.key}`" :ref="bindEditor" v-model="editingText"
                  size="small" class="card-editor" maxlength="5" placeholder="--:--"
                  @keyup.enter="commitEdit" @keyup.esc="cancelEdit" @blur="commitEdit" />
                <span v-else class="num card-editable" :class="{ low: row.review.includes(column.key) }"
                  @dblclick="startEdit(`day:${row.day}:${column.key}`, row.values[column.key])">
                  {{ row.values[column.key] || '—' }}
                  <span v-if="row.confidence[column.key] != null" class="confidence-value">{{ Math.round(row.confidence[column.key]! * 100) }}%</span>
                </span>
              </template>
            </el-table-column>
          </el-table>
          <p class="card-hint">雙擊任何時間可直接改（HH:mm，留空代表該次沒有打卡）；只顯示有打卡的日子。</p>
        </template>
      </div>
      <template #footer>
        <el-button v-if="cardDirty" :disabled="cardSaving" @click="resetCard">還原</el-button>
        <el-button :type="cardDirty ? '' : 'primary'" :disabled="cardSaving" @click="cardDialog = false">{{ cardDirty ? '關閉' : '完成' }}</el-button>
        <el-button v-if="cardDirty" type="primary" :loading="cardSaving" @click="saveCard">保存修改</el-button>
      </template>
    </el-dialog>

    <el-dialog v-model="payrollDialog" :title="`編輯薪酬 · ${editingName}`" width="640px" class="attendance-dialog"
      :close-on-click-modal="false" :close-on-press-escape="!saving" :show-close="!saving" @closed="payrollFormRef?.clearValidate()">
      <el-form ref="payrollFormRef" :model="payrollForm" :rules="payrollRules" label-position="top">
        <div class="form-grid">
          <el-form-item v-for="column in columns" :key="column.key" :label="column.money ? `${column.label}（HK$）` : column.label" :prop="column.key">
            <el-input-number v-model="payrollForm[column.key]" :precision="2" :step="column.money ? 50 : 0.5" :min="0"
              :max="column.money ? 999999999999.99 : 999999.99" :disabled="saving" controls-position="right" />
          </el-form-item>
        </div>
        <el-form-item label="注意！" prop="remark"><el-input v-model="payrollForm.remark" type="textarea" :rows="3" maxlength="255" show-word-limit :disabled="saving" /></el-form-item>
      </el-form>
      <template #footer><el-button :disabled="saving" @click="payrollDialog = false">取消</el-button><el-button type="primary" :loading="saving" @click="savePayroll">保存</el-button></template>
    </el-dialog>

    <el-dialog v-model="punchDialog" :title="`編輯打卡 · ${editingName}`" width="520px" class="attendance-dialog"
      :close-on-click-modal="false" :close-on-press-escape="!saving" :show-close="!saving" @closed="punchFormRef?.clearValidate()">
      <el-form ref="punchFormRef" :model="punchForm" :rules="punchRules" label-position="top">
        <el-form-item label="上班打卡（香港時間）" prop="clockInTime">
          <el-date-picker v-model="punchForm.clockInTime" type="datetime" value-format="YYYY-MM-DD HH:mm:ss" format="YYYY-MM-DD HH:mm:ss" :disabled="saving" />
        </el-form-item>
        <el-form-item label="下班打卡（香港時間）" prop="clockOutTime">
          <el-date-picker v-model="punchForm.clockOutTime" type="datetime" value-format="YYYY-MM-DD HH:mm:ss" format="YYYY-MM-DD HH:mm:ss" :disabled="saving" />
        </el-form-item>
        <el-form-item label="O.T.(小時)" prop="overtimeHours"><el-input-number v-model="punchForm.overtimeHours" :precision="2" :min="0" :max="999999.99" :step="0.5" :disabled="saving" /></el-form-item>
        <el-form-item label="勞工假" prop="isLaborLeave"><el-switch v-model="punchForm.isLaborLeave" :disabled="saving" /></el-form-item>
        <el-form-item label="注意！" prop="remark"><el-input v-model="punchForm.remark" type="textarea" :rows="3" maxlength="255" show-word-limit :disabled="saving" /></el-form-item>
      </el-form>
      <template #footer><el-button :disabled="saving" @click="punchDialog = false">取消</el-button><el-button type="primary" :loading="saving" @click="savePunch">保存</el-button></template>
    </el-dialog>
  </div>
</template>

<style scoped>
.attendance-view { min-width: 0; }
.page-header { display: flex; align-items: center; justify-content: space-between; gap: 16px; margin-bottom: 16px; flex-wrap: wrap; }
.header-actions { display: flex; gap: 8px; flex-wrap: wrap; }
.header-actions .el-button { margin-left: 0; }
.page-title { margin: 0; font-size: 18px; font-weight: 500; }
.button-icon { margin-right: 6px; flex: none; }
.file-input { display: none; }
.card-meta { margin: 12px 0; }
.card-rows { margin-top: 4px; }
.card-hint { margin: 12px 0 0; font-size: 12px; line-height: 1.6; color: var(--el-text-color-secondary); }
.card-sync { margin-top: 16px; padding-top: 12px; border-top: 1px solid var(--el-border-color); }
.card-sync-title { margin-bottom: 8px; font-size: 13px; font-weight: 500; color: var(--mc-text-secondary); }
.card-sync-fields { display: flex; align-items: center; gap: 8px; flex-wrap: wrap; }
.card-sync-field { width: 240px; }
.card-sync-note { font-size: 12px; color: var(--el-color-warning); }
.option-note { margin-left: 16px; font-size: 12px; color: var(--el-text-color-secondary); }
.card-editable { display: inline-block; min-width: 44px; padding: 1px 4px; border-radius: 4px; cursor: pointer; }
.card-editable:hover { background: var(--el-fill-color-light); box-shadow: inset 0 0 0 1px var(--el-border-color); }
.card-editor { width: 96px; }
.confidence-value { margin-left: 6px; font-size: 12px; color: var(--el-text-color-secondary); font-variant-numeric: tabular-nums; }
.low { color: var(--el-color-warning); }
.filters { display: flex; align-items: flex-end; gap: 16px; flex-wrap: wrap; padding: 0 0 16px; }
.filter-field { display: flex; flex-direction: column; gap: 8px; width: 220px; max-width: 100%; }
.filter-field label { color: var(--mc-text-secondary); font-size: 13px; }
.filter-field :deep(.el-date-editor) { width: 100%; }
.table-region { min-width: 0; background: var(--mc-card-bg, var(--el-bg-color)); border-top: 1px solid var(--el-border-color); }
.num { font-variant-numeric: tabular-nums; }
.pagination { display: flex; justify-content: flex-end; flex-wrap: wrap; gap: 8px; padding: 16px 0; }
.form-grid { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); column-gap: 24px; }
.form-grid :deep(.el-input-number) { width: 100%; }
:global(.attendance-dialog) { max-width: calc(100vw - 32px); }
@media (max-width: 600px) {
  .filter-field { width: 100%; }
  .pagination { justify-content: flex-start; }
  .form-grid { grid-template-columns: minmax(0, 1fr); }
}
</style>
