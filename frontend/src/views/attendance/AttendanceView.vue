<script setup lang="ts">
import { computed, onMounted, reactive, ref, watch } from 'vue'
import { ElMessage, ElMessageBox, type FormInstance, type FormRules } from 'element-plus'
import AppIcon from '@/components/AppIcon.vue'
import { useAuthStore } from '@/stores/auth'
import {
  initializeAttendanceMock, listPayroll, listPunches, updatePayroll, updatePunch,
  type PayrollDto, type PayrollFields, type PunchDto,
} from '@/api/attendance'

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
const canMock = import.meta.env.DEV
const activeTab = ref<'payroll' | 'punches'>('payroll')
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
    if (page.value > 1 && (page.value - 1) * pageSize.value >= total.value) page.value = 1
  } catch {
    if (id === requestId) {
      payroll.value = []
      punches.value = []
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
</script>

<template>
  <div class="attendance-view">
    <div class="page-header">
      <h1 class="page-title">考勤薪酬</h1>
      <!-- <el-button v-if="canMock && isAdmin" :loading="initializing" :disabled="loading" @click="initializeMock"> -->
      <el-button @click="initializeMock">
        <AppIcon name="plus" :size="16" class="button-icon" />初始化模擬數據
      </el-button>
    </div>

    <el-tabs v-model="activeTab">
      <el-tab-pane label="員工薪酬表" name="payroll" />
      <el-tab-pane label="打卡明細" name="punches" />
    </el-tabs>

    <form class="filters" @submit.prevent="search">
      <div class="filter-field">
        <label for="attendance-month">月份</label>
        <el-date-picker id="attendance-month" v-model="month" type="month" value-format="YYYY-MM" format="YYYY年MM月"
          :clearable="false" :disabled="initializing" :disabled-date="(date: Date) => date.getFullYear() < 2000 || date.getFullYear() > 2099" />
      </div>
      <div class="filter-field">
        <label for="attendance-name">姓名</label>
        <el-input id="attendance-name" v-model="keyword" clearable maxlength="100" @clear="search" />
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

      <el-table v-else v-loading="loading" :data="punches" stripe row-key="id" max-height="600">
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
      <el-pagination v-model:current-page="page" v-model:page-size="pageSize" :total="total" :page-sizes="[20, 50, 100]"
        layout="total, sizes, prev, pager, next" :pager-count="5" class="pagination" />
    </div>

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
.page-title { margin: 0; font-size: 18px; font-weight: 500; }
.button-icon { margin-right: 6px; flex: none; }
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
