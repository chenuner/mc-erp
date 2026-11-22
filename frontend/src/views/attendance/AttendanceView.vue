<script setup lang="ts">
import { computed, nextTick, onMounted, reactive, ref, watch } from 'vue'
import { ElLoading, ElMessage, ElMessageBox, type FormInstance, type FormRules } from 'element-plus'
import AppIcon from '@/components/AppIcon.vue'
import { useAuthStore } from '@/stores/auth'
import {
  confirmCardScan, createWorker, deleteCardScan, deleteWorker, getCardScan, listCardScans,
  listAttendanceSites, listPayroll, listPunches, listWorkerPositions, listWorkers, scanAttendanceCard, syncCardScan, updateCardScan,
  updatePayroll, updatePunch, updateWorker,
  type AttendanceCardScanDto, type AttendanceCardScanSummaryDto, type AttendanceRow, type PayrollDto,
  type PayrollFields, type PayrollTotals, type PunchDto, type WorkerDto,
} from '@/api/attendance'
import type { SysDictDto } from '@/api/system'
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
/** 香港時區的當前月份，也是打開頁面時的預設篩選。 */
const currentMonth = `${now.find(part => part.type === 'year')?.value}-${now.find(part => part.type === 'month')?.value}`
const month = ref(currentMonth)
/** 上次選過的月份：薪酬表／打卡明細的月份必填（後端按年月查），從「全部年月」切回來時用它恢復。 */
let lastMonth = month.value
const workerFilter = ref('')
/** 薪酬表專用：按當月職位快照篩選；打卡明細只顯示職位、不提供篩選，切到其他 tab 時不帶這個條件。 */
const positionFilter = ref('')
/** 薪酬表專用：按當月地盤快照篩選；打卡明細不提供地盤篩選，切到其他 tab 時不帶這個條件。 */
const siteFilter = ref('')
const page = ref(1)
const pageSize = ref(20)
const total = ref(0)
const loading = ref(false)
const payroll = ref<PayrollDto[]>([])
const punches = ref<PunchDto[]>([])
const cards = ref<AttendanceCardScanSummaryDto[]>([])
/** 合計按整份篩選結果算，跟當前頁無關；後端回傳，不由前端對本頁加總。 */
const payrollTotals = ref<PayrollTotals | null>(null)
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

/** 合計行只顯示這些欄位；日薪是每人單價，不加總。 */
const payrollTotalKeys: (keyof PayrollTotals)[] = [
  'workDays', 'laborLeaveDays', 'totalWorkDays', 'overtimeHours',
  'overtimeAmount', 'mpfEmployer', 'mpfEmployee', 'actualPay', 'totalAmount',
]

/** 薪酬表合計行；總數按篩選結果的人數，不是本頁行數。 */
function payrollSummary({ columns: tableColumns }: { columns: { property?: string }[] }): string[] {
  const totals = payrollTotals.value
  return tableColumns.map((column, index) => {
    if (index === 0) return totals ? `合計（${total.value} 人）` : ''
    const key = column.property as keyof PayrollTotals | undefined
    if (!key || !totals || !payrollTotalKeys.includes(key)) return ''
    const money = columns.find(item => item.key === key)?.money ?? false
    return (money ? moneyFormatter : numberFormatter).format(totals[key])
  })
}

/** 目前選的年月；考勤卡識別可以清空＝查全部年月，另外兩個分頁的月份必填。 */
function selectedMonth(): { year: number; month: number } | null {
  if (!month.value) return null
  const [year, monthNumber] = month.value.split('-').map(Number)
  return { year: year!, month: monthNumber! }
}

async function fetchRows() {
  const id = ++requestId
  loading.value = true
  try {
    if (activeTab.value === 'cards') {
      // 考勤卡按年月篩；年月可以留空＝全部（識別不出年月的記錄只有這樣才看得到），也不帶姓名條件
      const selected = selectedMonth()
      const result = await listCardScans({
        year: selected?.year, month: selected?.month, page: page.value, pageSize: pageSize.value,
      })
      if (id !== requestId) return
      cards.value = result.items
      total.value = result.total
    } else {
      const selected = selectedMonth()
      // 薪酬表／打卡明細的月份是必填；正常不會為空，真為空就恢復上次月份再查
      if (!selected) {
        month.value = lastMonth
        return
      }
      // 沒選的條件一律不帶：空字串會變成 ?siteDictId= 這種空值，後端 Guid? 綁定會 400
      const params = {
        ...selected,
        workerId: workerFilter.value || undefined,
        positionId: activeTab.value === 'payroll' ? positionFilter.value || undefined : undefined,
        siteDictId: activeTab.value === 'payroll' ? siteFilter.value || undefined : undefined,
        page: page.value,
        pageSize: pageSize.value,
      }
      if (activeTab.value === 'payroll') {
        const result = await listPayroll(params)
        if (id !== requestId) return
        payroll.value = result.items
        total.value = result.total
        payrollTotals.value = result.totals
      } else {
        const result = await listPunches(params)
        if (id !== requestId) return
        punches.value = result.items
        total.value = result.total
        payrollTotals.value = null
      }
    }
    if (page.value > 1 && (page.value - 1) * pageSize.value >= total.value) page.value = 1
  } catch {
    if (id === requestId) {
      payroll.value = []
      punches.value = []
      cards.value = []
      payrollTotals.value = null
      total.value = 0
    }
  } finally {
    if (id === requestId) loading.value = false
  }
}

/** 姓名下拉／查詢按鈕都走這裡；回到第一頁時由 page 的 watch 觸發查詢。 */
function search() {
  if (page.value !== 1) page.value = 1
  else fetchRows()
}

/** 薪酬表／打卡明細的重置：清空姓名、職位、地盤，月份回到當前月（月份必填，不能清空）。 */
function resetFilters() {
  workerFilter.value = ''
  positionFilter.value = ''
  siteFilter.value = ''
  // 月份有變就由 watch 重查；沒變就自己在原地重查一次
  if (month.value !== currentMonth) month.value = currentMonth
  else search()
}

watch([month, activeTab, pageSize], ([selected, tab]) => {
  if (selected) lastMonth = selected
  // 薪酬表／打卡明細的月份必填：從考勤卡的「全部年月」切回來時先補回上次月份，下一輪 watch 再查
  if (tab !== 'cards' && !selected) {
    month.value = lastMonth
    return
  }
  payroll.value = []
  punches.value = []
  cards.value = []
  payrollTotals.value = null
  total.value = 0
  if (page.value !== 1) page.value = 1
  else fetchRows()
})
watch(page, fetchRows)
onMounted(() => {
  // 姓名篩選和同步的員工下拉都要員工清單，先載入（失敗不影響列表查詢）
  void ensureOptions()
  return fetchRows()
})

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
const punchForm = reactive({
  clockInTime: '', clockOutTime: '', overtimeHours: 0, isLaborLeave: false,
  siteDictId: '', positionDictId: '', remark: '',
})
const punchRules: FormRules = {
  clockInTime: [{ required: true, message: '請選擇上班時間', trigger: 'change' }],
  overtimeHours: [{ required: true, type: 'number', min: 0, message: '請輸入有效的加班小時', trigger: 'change' }],
  siteDictId: [{ required: true, message: '請選擇地盤', trigger: 'change' }],
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
    overtimeHours: row.overtimeHours, isLaborLeave: row.isLaborLeave,
    siteDictId: row.siteDictId, positionDictId: row.positionDictId ?? '', remark: row.remark ?? '',
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
    const result = await updatePunch(editingId.value, {
      clockInTime: `${punchForm.clockInTime.replace(' ', 'T')}+08:00`,
      clockOutTime: punchForm.clockOutTime ? `${punchForm.clockOutTime.replace(' ', 'T')}+08:00` : null,
      overtimeHours: punchForm.overtimeHours,
      isLaborLeave: punchForm.isLaborLeave,
      siteDictId: punchForm.siteDictId,
      positionDictId: punchForm.positionDictId || null,
      remark: punchForm.remark,
    })
    punchDialog.value = false
    ElMessage.success(result.payrollRows > 0
      ? `打卡明細已保存，該月薪酬已按明細重算（${result.payrollRows} 行）`
      : '打卡明細已保存')
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
const sites = ref<SysDictDto[]>([])
const positions = ref<SysDictDto[]>([])
const syncWorkerId = ref('')
/** 第 2 步選員工的提示：卡上的姓名在員工清單裡找不到時，引導去「員工管理」維護。 */
const syncWorkerHint = ref('')
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

/** 和上次保存的內容比，決定「保存修改」是否可用；下拉選了不同姓名也算改動。 */
const draftChanged = computed(() => cardDraft.value != null && JSON.stringify(cardDraft.value) !== cardBaseline.value)
const cardDirty = computed(() => draftChanged.value || workerChanged.value)

/** 考勤卡各欄的中文名，用於複核提示。 */
const cardLabels: Record<string, string> = {
  year: '年份', month: '月份', name: '姓名', layout: '表格版面',
  amIn: '上午上班', amOut: '上午下班', pmIn: '下午上班', pmOut: '下午下班', otIn: '加班上班', otOut: '加班下班',
}

const cardReviewText = computed(() =>
  (cardDetail.value?.card.reviewFields ?? []).map(name => cardLabels[name] ?? name).join('、'))

/** 手寫欄位（年月姓名）本身不算需覆核，但置信度低仍值得提示——不影響卡片狀態。 */
const handwrittenLow = computed(() => {
  const card = cardDetail.value?.card
  if (!card) return []
  const fields: { key: 'year' | 'month' | 'name'; label: string; text: string }[] = [
    { key: 'year', label: '年份', text: card.year.value == null ? '未識別' : String(card.year.value) },
    { key: 'month', label: '月份', text: card.month.value == null ? '未識別' : String(card.month.value) },
    { key: 'name', label: '姓名', text: card.name.value || '未識別' },
  ]
  return fields
    .filter(field => lowConfidence(card[field.key]))
    .map(field => `${field.label}「${field.text}」${percent(card[field.key])}`)
})

const cardFlaggedRows = computed(() => (cardDetail.value?.card.records ?? []).filter(row => row.needsReview).length)

/** 被標記的行中，具體是哪幾欄（去重後的中文名），用在「標記為已核對」的確認文字裡。 */
const cardFlaggedLabels = computed(() => {
  const names = new Set<string>()
  for (const row of cardDetail.value?.card.records ?? []) {
    for (const field of row.reviewFields) names.add(cardLabels[field] ?? field)
  }
  return [...names].join('、')
})

/** 整張卡都乾淨（卡片層沒標記、每天的紅字也都處理過）。 */
const cardClean = computed(() => (cardDetail.value?.card.reviewFields.length ?? 0) === 0 && cardFlaggedRows.value === 0)

/** 第 1 步真正完成＝沒有紅字，而且已經按過「標記為已核對」；同步只看這個。 */
const cardChecked = computed(() => cardClean.value && !!cardDetail.value?.confirmedAt)

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

/** 這一欄是否已被人工改過（未保存）：改過就不再顯示原本的識別置信度（姓名由下拉選擇，不走這裡）。 */
function edited(key: 'year' | 'month'): boolean {
  const card = cardDetail.value?.card
  const draft = cardDraft.value
  if (!card || !draft) return false
  return draft[key] !== card[key].value
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

/** 重新載入職位分類；職位是選填，載入失敗不影響其他功能。 */
async function reloadPositions() {
  try {
    positions.value = await listWorkerPositions()
    if (positionFilter.value && !positions.value.some(item => item.id === positionFilter.value)) {
      positionFilter.value = ''
      if (activeTab.value === 'payroll') void fetchRows()
    }
  } catch {
    positions.value = []
  }
}

/** 重新載入地盤分類；和職位一樣是選填（可清空），載入失敗就當沒有選項。 */
async function reloadSites() {
  try {
    sites.value = await listAttendanceSites()
    if (siteFilter.value && !sites.value.some(item => item.id === siteFilter.value)) {
      siteFilter.value = ''
      if (activeTab.value === 'payroll') void fetchRows()
    }
  } catch {
    sites.value = []
  }
}

/**
 * 員工、地盤和職位分類清單只在第一次用到時載入；同步要選人、選地盤，篩選和員工表單要職位。
 * 每次進入頁面都重新抓一次地盤和職位：字典改了名之後，篩選和下拉不能留著舊標籤。
 */
async function ensureOptions() {
  await Promise.all([
    workers.value.length ? Promise.resolve() : listWorkers().then(rows => { workers.value = rows }).catch(() => {}),
    reloadSites(),
    reloadPositions(),
  ])
}

/**
 * 按目前的下拉選擇決定第 2 步那句提示，優先次序：
 * 1. 根本沒選員工（或卡上姓名不在清單裡）→ 去「員工管理」維護；
 * 2. 選了和卡上姓名不同的員工 → 這等於改內容，要走「保存修改」，不再提新建；
 * 3. 其餘（兩者一致或還沒有卡上姓名）→ 不提示。
 */
function refreshWorkerHint() {
  const worker = workers.value.find(item => item.id === syncWorkerId.value)
  const mismatch = !!worker && !!cardName.value && worker.name !== cardName.value
  if (!worker) {
    // 沒選員工：卡上姓名不在員工清單裡，引導去「員工管理」；卡上沒認出姓名就只提示去選
    syncWorkerHint.value = cardName.value
      ? `員工清單沒有「${cardName.value}」：請先按「員工管理」新增（日薪是計薪基礎），再回來選員工。`
      : '請先在「姓名」選擇對應的員工；員工清單沒有的話先到「員工管理」新增。'
    return
  }
  // 選了和卡上姓名不同的員工＝改內容：走「保存修改」這條路，不提新建
  syncWorkerHint.value = mismatch
    ? `卡上姓名「${cardName.value}」和已選的「${worker.name}」不同：按「保存修改」把姓名改成「${worker.name}」，之後才能標記為已核對並同步。`
    : ''
}

/** 卡上手寫姓名和員工姓名對得上就預選；對不上就不選，提示使用者先到「員工管理」維護。 */
function matchWorker(name: string | null) {
  syncWorkerId.value = workers.value.find(worker => worker.name === name)?.id ?? ''
  refreshWorkerHint()
}

/** 這次會寫入幾筆打卡明細＝有打卡的日子數。 */
const syncRowCount = computed(() => cardDraft.value?.rows.length ?? 0)

/** 已選員工的日薪；0 表示沒設定，計薪會變成負數。 */
const syncWorkerWage = computed(() => workers.value.find(worker => worker.id === syncWorkerId.value)?.dailyWage ?? null)

/** 年月、員工、地盤都齊、員工有日薪、沒有未保存修改才可以同步（第 2 步只在已核對後才出現）。 */
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

/** 卡上手寫的姓名，用來判斷是不是 OCR 認錯字（認錯就改成員工清單裡的名字）。 */
const cardName = computed(() => cardDraft.value?.name.trim() ?? '')

/** 下拉選中的員工姓名；沒選就是 null。 */
const syncWorkerName = computed(() => workers.value.find(worker => worker.id === syncWorkerId.value)?.name ?? null)

/** 選了和卡上姓名不同的員工＝這一格被改過，和年月、時間一樣要走「保存修改」。 */
const workerChanged = computed(() => !!syncWorkerName.value && syncWorkerName.value !== cardName.value)

/** 第 2 步：員工是下拉，只從「員工管理」已維護的員工裡選，這裡不新建；提示按選擇的員工和卡上姓名重算。 */
function pickSyncWorker() {
  refreshWorkerHint()
}

/** 員工管理：姓名、工號、職位分類、日薪、在職。日薪是計薪基礎，所以在這裡維護而不是散在薪酬表。 */
const workerDialog = ref(false)
const workerEditDialog = ref(false)
const workerRows = ref<WorkerDto[]>([])
const workerLoading = ref(false)
const workerSaving = ref(false)
const workerForm = reactive({ id: '', name: '', employeeId: '', dailyWage: 0, isActive: true, positionId: '' })

/** 後端約定：更新員工時 positionId 傳空 Guid 代表清空職位，不傳代表不改。 */
const EMPTY_GUID = '00000000-0000-0000-0000-000000000000'

/** 新員工預設選字典第一個啟用職位（通常是樓面員工）；職位是可選的，可以清空。 */
function firstEnabledPositionId() {
  return positions.value.find(item => item.enabled)?.id ?? ''
}

function resetWorkerForm() {
  workerForm.id = ''
  workerForm.name = ''
  workerForm.employeeId = ''
  workerForm.dailyWage = 0
  workerForm.isActive = true
  workerForm.positionId = firstEnabledPositionId()
}

async function openWorkers() {
  workerDialog.value = true
  await Promise.all([reloadWorkers(), reloadPositions()])
}

/** 新增：清空表單再開編輯框；職位下拉要先有系統字典的選項。 */
async function startCreateWorker() {
  // 字典可能還沒載入或剛好被清掉，先確保有選項再把表單打開
  if (!positions.value.length) await reloadPositions()
  resetWorkerForm()
  workerEditDialog.value = true
}

/** 重新載入員工清單；同步選單和姓名篩選用的 workers 也一起更新。 */
async function reloadWorkers() {
  workerLoading.value = true
  try {
    workerRows.value = await listWorkers()
    workers.value = workerRows.value
  } catch {
    workerRows.value = []
  } finally {
    workerLoading.value = false
  }
}

/** 編輯：帶出該員工的資料，改完按保存才寫回。 */
function editWorker(row: WorkerDto) {
  workerForm.id = row.id
  workerForm.name = row.name
  workerForm.employeeId = row.employeeId ?? ''
  workerForm.dailyWage = row.dailyWage
  workerForm.isActive = row.isActive
  workerForm.positionId = row.positionId ?? ''
  workerEditDialog.value = true
}

async function saveWorker() {
  const name = workerForm.name.trim()
  if (!name) {
    ElMessage.warning('請輸入姓名')
    return
  }
  workerSaving.value = true
  try {
    if (workerForm.id) {
      await updateWorker(workerForm.id, {
        name, employeeId: workerForm.employeeId.trim(), dailyWage: workerForm.dailyWage, isActive: workerForm.isActive,
        // 空 Guid 代表清空職位；不帶欄位則保持原值
        positionId: workerForm.positionId || EMPTY_GUID,
      })
      ElMessage.success('員工已更新')
    } else {
      const created = await createWorker({
        name, employeeId: workerForm.employeeId.trim() || null, dailyWage: workerForm.dailyWage,
        positionId: workerForm.positionId || null,
      })
      // 從考勤卡新建員工後直接選中，接著就能標記核對、同步
      if (!workerDialog.value) syncWorkerId.value = created.id
      ElMessage.success(`已新增員工 ${name}`)
    }
    workerEditDialog.value = false
    resetWorkerForm()
    await reloadWorkers()
  } catch { /* 已由攔截器提示 */ }
  finally { workerSaving.value = false }
}

/** 停用／啟用：只是不在同步下拉裡出現，資料和薪酬查詢都保留。 */
async function toggleWorker(row: WorkerDto) {
  try {
    await updateWorker(row.id, { isActive: !row.isActive })
    ElMessage.success(row.isActive ? `已停用 ${row.name}` : `已啟用 ${row.name}`)
    await reloadWorkers()
  } catch { /* 已由攔截器提示 */ }
}

/** 刪除：軟刪除，連薪酬表和打卡明細都不再顯示；記錄仍留在庫裡。 */
async function removeWorker(row: WorkerDto) {
  try {
    await ElMessageBox.confirm(
      `刪除員工「${row.name}」？刪除後他的薪酬表和打卡明細都不再顯示（資料仍在庫裡），也不能復原；只想暫時隱藏請用「停用」。`,
      '刪除員工', { confirmButtonText: '刪除', cancelButtonText: '取消', type: 'warning' })
  } catch { return }
  try {
    await deleteWorker(row.id)
    ElMessage.success(`已刪除 ${row.name}`)
    await reloadWorkers()
  } catch { /* 已由攔截器提示 */ }
}

/** 同步：導入打卡明細並重算該員工該月薪酬，完成後切到薪酬表看結果。 */
async function syncCard() {
  const detail = cardDetail.value
  const draft = cardDraft.value
  if (!detail || !draft) return
  if (!cardChecked.value) {
    ElMessage.warning('請先完成第 1 步「標記為已核對」再同步')
    return
  }
  if (!canSync.value) return
  const workerName = workers.value.find(worker => worker.id === syncWorkerId.value)?.name ?? '該員工'
  try {
    await ElMessageBox.confirm(
      `將為「${workerName}」寫入 ${draft.rows.length} 筆打卡明細，並重算 ${draft.year} 年 ${draft.month} 月薪酬。`
      + '該員工該月上次由考勤卡導入的明細會被覆蓋（模擬或人工的明細不受影響）。',
      '同步到打卡明細及薪酬表', { confirmButtonText: '同步', cancelButtonText: '取消', type: 'warning' })
  } catch { return }
  syncing.value = true
  try {
    const result = await syncCardScan(detail.id, { workerId: syncWorkerId.value, siteDictId: syncSiteId.value })
    const totals = result.rows.reduce(
      (sum, row) => ({
        workDays: sum.workDays + row.workDays,
        overtimeHours: sum.overtimeHours + row.overtimeHours,
        actualPay: sum.actualPay + row.actualPay,
      }),
      { workDays: 0, overtimeHours: 0, actualPay: 0 },
    )
    ElMessage.success(`已寫入 ${result.addedRecords} 筆打卡明細；${result.year} 年 ${result.month} 月薪酬已重算（${result.rows.length} 行）：工數 ${numberFormatter.format(totals.workDays)}、O.T. ${numberFormatter.format(totals.overtimeHours)} 小時、實付 HK$${moneyFormatter.format(totals.actualPay)}`)
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
  // 識別要等 Azure（可能幾十秒），用全螢幕遮罩讓使用者知道還在跑、不要重複點上傳
  const overlay = ElLoading.service({ fullscreen: true, lock: true, text: '考勤卡識別中，請稍候…' })
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
  finally {
    overlay.close()
    scanning.value = false
  }
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
  const wasConfirmed = !!detail.confirmedAt
  cardSaving.value = true
  try {
    const updated = await updateCardScan(detail.id, {
      year: draft.year,
      month: draft.month,
      // 姓名由第 2 步的員工下拉決定：選了就存成員工姓名（改名以員工為準），沒選就保留卡上原本識別的
      name: syncWorkerName.value ?? (draft.name.trim() || null),
      records: draft.rows.map(row => ({ day: row.day, ...row.values })),
    })
    loadCard(updated)
    if (!locateMonth(updated.card.year.value, updated.card.month.value)) await fetchRows()
    ElMessage.success('考勤卡識別結果已保存')
    // 改動過的內容原本的核對不再算數，後端已清掉核對時間
    if (wasConfirmed && !updated.confirmedAt) ElMessage.warning('內容已修改，請重新「標記為已核對」後再同步')
    if (!updated.card.year.value || !updated.card.month.value) {
      ElMessage.warning('沒有年月，這筆記錄不會出現在按月份篩選的列表；補上年月再保存就會出現')
    }
    // 存完名字就跟卡上一致了，提示隨之消失
    refreshWorkerHint()
  } catch { /* 已由攔截器提示 */ }
  finally { cardSaving.value = false }
}

/** 第 1 步：手動把整張卡標記為已核對——這是第 2 步同步的前提。 */
async function markChecked() {
  const detail = cardDetail.value
  if (!detail) return
  if (cardDirty.value) {
    ElMessage.warning('有未保存的修改，先按「保存修改」再標記為已核對')
    return
  }
  try {
    await ElMessageBox.confirm('確認已對照原卡核對整張卡？', '標記為已核對',
      { confirmButtonText: '標記為已核對', cancelButtonText: '取消', type: 'warning' })
  } catch { return }
  cardSaving.value = true
  try {
    const updated = await confirmCardScan(detail.id)
    loadCard(updated)
    ElMessage.success('已標記為已核對，可以按第 2 步「同步到打卡明細及薪酬表」')
    await fetchRows()
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
        <el-button v-if="isAdmin" @click="openWorkers">
          <AppIcon name="users" :size="16" class="button-icon" />員工管理
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
          :clearable="activeTab === 'cards'" :placeholder="activeTab === 'cards' ? '全部年月' : '請選擇月份'"
          :disabled-date="(date: Date) => date.getFullYear() < 2000 || date.getFullYear() > 2099" />
      </div>
      <div class="filter-field">
        <label for="attendance-name">姓名</label>
        <el-select id="attendance-name" v-model="workerFilter" filterable clearable :disabled="activeTab === 'cards'"
          :placeholder="activeTab === 'cards' ? '考勤卡按年月篩選' : '全部員工'" @change="search">
          <el-option v-for="worker in workers" :key="worker.id" :label="worker.name" :value="worker.id" />
        </el-select>
      </div>
      <div v-if="activeTab === 'payroll'" class="filter-field">
        <label for="attendance-position">職位分類</label>
        <el-select id="attendance-position" v-model="positionFilter" filterable clearable placeholder="全部職位" @change="search">
          <el-option v-for="position in positions" :key="position.id"
            :label="position.enabled ? position.label : `${position.label}（停用）`" :value="position.id" />
        </el-select>
      </div>
      <div v-if="activeTab === 'payroll'" class="filter-field">
        <label for="attendance-site">地盤</label>
        <el-select id="attendance-site" v-model="siteFilter" filterable clearable placeholder="全部地盤" @change="search">
          <el-option v-for="site in sites" :key="site.id"
            :label="site.enabled ? site.label : `${site.label}（停用）`" :value="site.id" />
        </el-select>
      </div>
      <el-button type="primary" native-type="submit"><AppIcon name="search" :size="16" class="button-icon" />查詢</el-button>
      <el-button v-if="activeTab !== 'cards'" :disabled="loading" @click="resetFilters">重置</el-button>
      <el-tooltip content="刷新" placement="top">
        <el-button :disabled="loading" circle aria-label="刷新" @click="fetchRows"><AppIcon name="refresh-cw" :size="16" /></el-button>
      </el-tooltip>
    </form>

    <div class="table-region">
      <el-table v-if="activeTab === 'payroll'" v-loading="loading" :data="payroll" stripe row-key="id" max-height="600"
        class="mc-table-summary" :show-summary="payroll.length > 0" :summary-method="payrollSummary">
        <el-table-column prop="name" label="姓名" width="110" fixed show-overflow-tooltip />
        <el-table-column label="職位分類" width="120" show-overflow-tooltip>
          <template #default="{ row }">{{ row.position || '—' }}</template>
        </el-table-column>
        <el-table-column label="地盤" width="140" show-overflow-tooltip>
          <template #default="{ row }">{{ row.site || '—' }}</template>
        </el-table-column>
        <el-table-column v-for="column in columns" :key="column.key" :prop="column.key" :label="column.label"
          :width="column.key === 'totalAmount' ? undefined : column.width"
          :min-width="column.key === 'totalAmount' ? 125 : undefined" align="right">
          <template #default="{ row }">
            <span class="num" :class="{ 'col-highlight': column.key === 'totalAmount' }">
              {{ (column.money ? moneyFormatter : numberFormatter).format(row[column.key]) }}
            </span>
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
        <el-table-column label="職位分類" width="120" show-overflow-tooltip>
          <template #default="{ row }">{{ row.positionName || '—' }}</template>
        </el-table-column>
        <el-table-column prop="siteName" label="地盤" min-width="140" show-overflow-tooltip />
        <el-table-column label="上班打卡（香港時間）" width="205">
          <template #default="{ row }"><span class="num">{{ localTime(row.clockInTime) }}</span></template>
        </el-table-column>
        <el-table-column label="下班打卡（香港時間）" width="205">
          <template #default="{ row }"><span class="num">{{ localTime(row.clockOutTime) || '—' }}</span></template>
        </el-table-column>
        <el-table-column prop="overtimeHours" label="O.T.(小時)" width="110" align="right">
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
        <el-table-column label="年月" width="140">
          <template #default="{ row }">
            {{ row.year == null || row.month == null ? '未識別' : `${row.year} 年 ${row.month} 月` }}
          </template>
        </el-table-column>
        <el-table-column label="姓名" width="130" show-overflow-tooltip>
          <template #default="{ row }">{{ row.name || '未識別' }}</template>
        </el-table-column>
        <el-table-column label="打卡天數" width="110" align="right">
          <template #default="{ row }"><span class="num">{{ row.punchCount }}</span></template>
        </el-table-column>
        <el-table-column label="狀態" width="110">
          <template #default="{ row }">
            <el-tag :type="row.needsReview ? 'warning' : row.confirmedAt ? 'success' : 'info'" effect="plain">
              {{ row.needsReview ? '需覆核' : row.confirmedAt ? '已核對' : '待核對' }}
            </el-tag>
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
          <el-empty :description="month
            ? '該月份暫無考勤卡掃描記錄，可按右上角「拍照識別考勤卡」上傳'
            : '暫無考勤卡掃描記錄，可按右上角「拍照識別考勤卡」上傳'" :image-size="72" />
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
            description="上傳時已自動保存。雙擊年月或任何時間即可修正，姓名從員工清單選，改完按「保存修改」；沒按保存就關閉不會影響已保存的內容。" />
          <el-alert v-if="cardDetail.card.reviewFields.length" :title="`需人工複核：${cardReviewText}（手寫內容識別率有限，請對照原卡核對）`"
            type="warning" :closable="false" show-icon />
          <div v-if="handwrittenLow.length" class="card-note">
            <span class="card-note-label">手寫欄位置信度偏低（不算需覆核）：</span>{{ handwrittenLow.join('、') }}
          </div>
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
            <el-descriptions-item label="姓名">
              <!-- 卡上是手寫姓名，員工只能從「員工管理」維護好的清單裡選；沒有這個人就先去員工管理新增 -->
              <!-- 下拉獨佔一行、固定寬度，卡上姓名和管理入口放到下面一行，避免擠到下拉只剩一條縫 -->
              <div class="card-worker">
                <el-select v-model="syncWorkerId" filterable clearable placeholder="從員工清單選擇" class="card-worker-select"
                  @change="pickSyncWorker">
                  <el-option v-for="worker in workers" :key="worker.id" :label="worker.name" :value="worker.id"
                    :disabled="!worker.isActive">
                    <span>{{ worker.name }}</span>
                    <span class="option-note">{{ worker.employeeId ?? '無工號' }} · 日薪 {{ moneyFormatter.format(worker.dailyWage) }}</span>
                  </el-option>
                  <template #empty>
                    <div class="card-pick-empty">員工清單是空的，請先到「員工管理」新增員工。</div>
                  </template>
                </el-select>
                <div class="card-worker-foot">
                  <span class="confidence-value">卡上：{{ cardDraft.name || '未識別' }} · {{ percent(cardDetail.card.name) }}</span>
                  <el-button v-if="isAdmin" link type="primary" @click="openWorkers">
                    <AppIcon name="users" :size="14" class="button-icon" />員工管理
                  </el-button>
                </div>
              </div>
            </el-descriptions-item>
          </el-descriptions>
          <p v-if="isAdmin" class="card-hint">
            卡上是手寫姓名，只在員工清單裡選。員工清單沒有這個人（例如手寫的「新人」），先到「員工管理」按「新增員工」補上姓名、日薪等資料，回來再選。
          </p>
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

          <div class="card-step">
            <div class="card-step-title">第 1 步：核對並標記為已核對</div>
            <div v-if="cardChecked" class="card-checked">
              <el-tag type="success" effect="plain" size="small">已核對</el-tag>
              <span class="card-note">已於 {{ localTime(cardDetail.confirmedAt) }} 標記為已核對。</span>
            </div>
            <div v-else class="card-checked">
              <el-button :loading="cardSaving" :disabled="cardDirty" @click="markChecked">標記為已核對</el-button>
              <span class="card-note">
                <template v-if="cardDirty">有未保存的修改，先按「保存修改」再標記為已核對。</template>
                <template v-else-if="cardClean">識別結果沒有需覆核的欄位；對照原卡看過內容無誤後按此。</template>
                <template v-else>
                  對照原卡核對整張卡後按此<template v-if="cardFlaggedRows">，會把 {{ cardFlaggedRows }} 天時間欄的紅字（{{ cardFlaggedLabels }}）記成人工確認</template>；時間和內容都不會被改動。
                </template>
              </span>
            </div>
          </div>

          <div v-if="cardChecked" class="card-step">
            <div class="card-step-title">第 2 步：同步到打卡明細及薪酬表</div>
            <div class="card-sync-fields">
              <el-select v-model="syncSiteId" filterable placeholder="選擇地盤" class="card-sync-field"
                no-data-text="尚未配置地盤，請先到「系統字典 → 地盤分類」新增">
                <el-option v-for="site in sites" :key="site.id"
                  :label="site.enabled ? site.label : `${site.label}（停用）`"
                  :value="site.id" :disabled="!site.enabled" />
              </el-select>
              <el-button type="primary" :loading="syncing" :disabled="!canSync" @click="syncCard">同步到打卡明細及薪酬表</el-button>
              <span v-if="cardDirty" class="card-sync-note">有未保存的修改，先按「保存修改」</span>
            </div>
            <p v-if="!sites.length" class="card-sync-note">
              還沒有地盤可選：
              <router-link to="/system/categories" @click="cardDialog = false">新增地盤分類字典</router-link>
            </p>
            <p v-else class="card-hint">地盤在「系統字典 → 地盤分類」維護，和會計票據共用同一份名單</p>
            <p v-if="syncWorkerHint" class="card-sync-note card-worker-hint">
              {{ syncWorkerHint }}
              <!-- 只有「員工清單沒有這個人」才給維護入口；選了不同員工時要走保存修改，不給這個按鈕 -->
              <el-button v-if="isAdmin && !syncWorkerId" link type="primary" @click="openWorkers">員工管理</el-button>
            </p>
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

        </template>
      </div>
      <template #footer>
        <el-button v-if="cardDirty" :disabled="cardSaving" @click="resetCard">還原</el-button>
        <el-button :type="cardDirty ? '' : 'primary'" :disabled="cardSaving" @click="cardDialog = false">{{ cardDirty ? '關閉' : '完成' }}</el-button>
        <el-button v-if="cardDirty" type="primary" :loading="cardSaving" @click="saveCard">保存修改</el-button>
      </template>
    </el-dialog>

    <el-dialog v-model="workerDialog" title="員工管理" width="min(820px, calc(100vw - 32px))" class="attendance-dialog">
      <div class="worker-toolbar">
        <span class="worker-toolbar-hint">姓名、工號、職位分類、日薪都在這裡維護；職位分類在「系統字典 → 職位分類」維護。日薪是計薪基礎（O.T. 時薪＝日薪÷8×1.5）。</span>
        <el-button type="primary" @click="startCreateWorker">
          <AppIcon name="plus" :size="16" class="button-icon" />新增員工
        </el-button>
      </div>
      <el-table v-loading="workerLoading" :data="workerRows" stripe size="small" max-height="380" row-key="id">
        <el-table-column prop="name" label="姓名" min-width="110" show-overflow-tooltip />
        <el-table-column label="工號" width="120">
          <template #default="{ row }">{{ row.employeeId || '—' }}</template>
        </el-table-column>
        <el-table-column label="職位分類" width="120" show-overflow-tooltip>
          <template #default="{ row }">{{ row.position || '—' }}</template>
        </el-table-column>
        <el-table-column label="日薪（HK$）" width="120" align="right">
          <template #default="{ row }"><span class="num">{{ moneyFormatter.format(row.dailyWage) }}</span></template>
        </el-table-column>
        <el-table-column label="狀態" width="90" align="center">
          <template #default="{ row }">
            <el-tag :type="row.isActive ? 'success' : 'info'" effect="plain">{{ row.isActive ? '在職' : '停用' }}</el-tag>
          </template>
        </el-table-column>
        <el-table-column label="操作" width="200" align="center">
          <template #default="{ row }">
            <el-button link type="primary" @click="editWorker(row)">編輯</el-button>
            <el-button link :type="row.isActive ? 'warning' : 'success'" @click="toggleWorker(row)">{{ row.isActive ? '停用' : '啟用' }}</el-button>
            <el-button link type="danger" @click="removeWorker(row)">刪除</el-button>
          </template>
        </el-table-column>
        <template #empty><el-empty description="還沒有員工，按右上角「新增員工」" :image-size="64" /></template>
      </el-table>
      <p class="card-hint">停用＝暫時不出現在同步的員工下拉，薪酬查詢照舊看得到；刪除＝軟刪除，薪酬表和打卡明細都不再顯示（記錄仍在庫裡）。</p>
    </el-dialog>

    <el-dialog v-model="workerEditDialog" :title="workerForm.id ? '編輯員工' : '新增員工'" append-to-body
      width="min(560px, calc(100vw - 32px))" class="attendance-dialog" :close-on-click-modal="false"
      @closed="resetWorkerForm">
      <el-form label-position="top" @submit.prevent>
        <div class="worker-form-row">
          <el-form-item label="姓名" required>
            <el-input v-model="workerForm.name" maxlength="50" placeholder="姓名" :disabled="workerSaving" />
          </el-form-item>
          <el-form-item label="工號">
            <el-input v-model="workerForm.employeeId" maxlength="50" placeholder="選填" :disabled="workerSaving" />
          </el-form-item>
          <el-form-item label="職位分類">
            <el-select v-model="workerForm.positionId" clearable filterable placeholder="請選擇" :disabled="workerSaving"
              no-data-text="尚未配置職位分類，請先到「系統字典 → 職位分類」新增">
              <el-option v-for="position in positions" :key="position.id"
                :label="position.enabled ? position.label : `${position.label}（停用）`"
                :value="position.id" :disabled="!position.enabled && position.id !== workerForm.positionId" />
            </el-select>
            <router-link v-if="!positions.length" to="/system/categories" @click="workerEditDialog = false">
              新增職位分類字典
            </router-link>
          </el-form-item>
          <el-form-item label="日薪（HK$）" required>
            <el-input-number v-model="workerForm.dailyWage" :precision="2" :step="50" :min="0" :max="999999.99"
              controls-position="right" :disabled="workerSaving" />
          </el-form-item>
          <el-form-item label="在職">
            <el-switch v-model="workerForm.isActive" :disabled="workerSaving" />
          </el-form-item>
        </div>
      </el-form>
      <template #footer>
        <el-button :disabled="workerSaving" @click="workerEditDialog = false">取消</el-button>
        <el-button type="primary" :loading="workerSaving" @click="saveWorker">{{ workerForm.id ? '保存修改' : '新增' }}</el-button>
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
        <el-form-item label="地盤" prop="siteDictId">
          <el-select v-model="punchForm.siteDictId" filterable :disabled="saving" placeholder="選擇地盤"
            no-data-text="尚未配置地盤，請先到「系統字典 → 地盤分類」新增">
            <el-option v-for="site in sites" :key="site.id"
              :label="site.enabled ? site.label : `${site.label}（停用）`" :value="site.id" />
          </el-select>
        </el-form-item>
        <el-form-item label="職位分類（可選）" prop="positionDictId">
          <el-select v-model="punchForm.positionDictId" filterable clearable :disabled="saving" placeholder="不指定職位">
            <el-option v-for="position in positions" :key="position.id"
              :label="position.enabled ? position.label : `${position.label}（停用）`" :value="position.id" />
          </el-select>
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
.card-step { margin-top: 16px; padding-top: 12px; border-top: 1px solid var(--el-border-color); }
.card-step-title { margin-bottom: 8px; font-size: 13px; font-weight: 500; color: var(--mc-text-secondary); }
.card-sync-fields { display: flex; align-items: center; gap: 8px; flex-wrap: wrap; }
.card-sync-field { width: 240px; }
.card-sync-note { font-size: 12px; color: var(--el-color-warning); }
.card-note { margin: 8px 0; font-size: 12px; line-height: 1.6; color: var(--el-text-color-secondary); }
.card-note-label { color: var(--el-color-warning); }
.card-checked { display: flex; align-items: center; gap: 8px; flex-wrap: wrap; margin: 8px 0; }
.card-checked .card-note { margin: 0; }
.worker-toolbar { display: flex; align-items: center; justify-content: space-between; gap: 12px; margin-bottom: 12px; flex-wrap: wrap; }
.worker-toolbar-hint { font-size: 12px; color: var(--el-text-color-secondary); }
.worker-form-row { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); column-gap: 16px; }
.worker-form-row :deep(.el-input-number) { width: 100%; }
.worker-form-row :deep(.el-select) { width: 100%; }
.option-note { margin-left: 16px; font-size: 12px; color: var(--el-text-color-secondary); }
.card-worker { display: flex; flex-direction: column; gap: 4px; min-width: 0; }
.card-worker-select { width: 160px; max-width: 100%; }
.card-worker-foot { display: flex; align-items: center; gap: 8px; flex-wrap: wrap; }
.card-pick-empty { padding: 8px 12px; font-size: 12px; color: var(--el-text-color-secondary); }
.card-worker-hint { margin-top: 8px; }
.card-editable { display: inline-block; min-width: 44px; padding: 1px 4px; border-radius: 4px; cursor: pointer; }
.card-editable:hover { background: var(--el-fill-color-light); box-shadow: inset 0 0 0 1px var(--el-border-color); }
.card-editor { width: 96px; }
.confidence-value { margin-left: 6px; font-size: 12px; color: var(--el-text-color-secondary); font-variant-numeric: tabular-nums; }
.low { color: var(--el-color-warning); }
.filters { display: flex; align-items: flex-end; gap: 16px; flex-wrap: wrap; padding: 0 0 16px; }
.filter-field { display: flex; flex-direction: column; gap: 8px; width: 220px; max-width: 100%; }
.filter-field label { color: var(--mc-text-secondary); font-size: 13px; }
.filter-hint { font-size: 12px; line-height: 1.5; color: var(--el-text-color-secondary); }
.filter-field :deep(.el-date-editor) { width: 100%; }
.filter-field :deep(.el-select) { width: 100%; }
/* 合計行樣式在 styles/index.css 的 .mc-table-summary（和會計收據共用） */
.table-region { min-width: 0; background: var(--mc-card-bg, var(--el-bg-color)); border-top: 1px solid var(--el-border-color); }
/* 明細的「總數」欄：這是主管最常看的一欄，加粗標出來 */
.col-highlight { font-weight: 700; }
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
