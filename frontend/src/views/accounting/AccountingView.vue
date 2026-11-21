<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, onMounted, reactive, ref } from 'vue'
import { ElMessage, ElMessageBox, type FormInstance, type FormRules } from 'element-plus'
import AppIcon from '@/components/AppIcon.vue'
import { useAuthStore } from '@/stores/auth'
import { listSysDictsByGroup, type SysDictDto } from '@/api/system'
import { accountingStatus, listReceipts, getReceipt, createReceipt, attachReceiptImage, recognizeReceipt,
  updateReceipt, deleteReceipt, receiptImage, exportReceiptImages, downloadBlob, type ReceiptDto,
  type ReceiptQuery, type UpdateReceipt, type OcrSuggestion } from '@/api/accounting'

const auth = useAuthStore()
const isAdmin = computed(() => auth.roles.includes('Admin'))
const loading = ref(false)
const busy = ref(false)
const exporting = ref(false)
const configured = ref(false)
const rows = ref<ReceiptDto[]>([])
const total = ref(0)
const page = ref(1)
const pageSize = ref(20)
const documentTypes = ref<SysDictDto[]>([])
const types = ref<SysDictDto[]>([])
const siteCategories = ref<SysDictDto[]>([])
const filter = reactive({ siteCategoryDictId: '', documentTypeDictId: '', type: '' })
const dates = ref<[string, string] | null>(null)
const selected = ref<ReceiptDto | null>(null)
const detailVisible = ref(false)
const exportVisible = ref(false)
const exportFormRef = ref<FormInstance>()
/** 提取圖片對話框的獨立條件，不跟主篩選連動。 */
const exportForm = reactive({
  siteCategoryDictId: '',
  documentTypeDictId: '',
  type: '',
  dates: null as [string, string] | null,
})
const exportRules: FormRules = {
  siteCategoryDictId: [{ required: true, message: '請選擇地盤分類', trigger: 'change' }],
}
const exportCount = ref<number | null>(null)
const exportCountLoading = ref(false)
const albumInput = ref<HTMLInputElement>()
const cameraInput = ref<HTMLInputElement>()
const imageUrl = ref('')
const imageLoading = ref(false)
const ocrPanel = ref<string[]>([])
/** 本次識別中置信度偏低、需要人工複核的欄位名。 */
const ocrReview = ref<string[]>([])
const formRef = ref<FormInstance>()
const form = reactive<UpdateReceipt>({ version: '', documentTypeDictId: '', type: '', siteCategoryDictId: null,
  receiptDate: null, amount: null, currencyCode: null, merchantName: null, invoiceNumber: null, remark: null })
const rules: FormRules = {
  documentTypeDictId: [{ required: true, message: '請選擇票據類型', trigger: 'change' },
    { validator: (_rule: unknown, value: string, callback: (error?: Error) => void) => {
        const dict = documentTypes.value.find(item => item.id === value)
        if (!dict || !dict.enabled) callback(new Error('此票據類型已停用'))
        else callback()
      }, trigger: 'change' }],
  type: [{ required: true, message: '請選擇支出分類', trigger: 'change' },
    { validator: (_rule: unknown, value: string, callback: (error?: Error) => void) => {
        const dict = types.value.find(item => item.id === value)
        if (!dict) callback(new Error('請選擇支出分類'))
        else if (!dict.enabled) callback(new Error('此支出分類已停用'))
        else if (dict.value === 'pending') callback(new Error('不能選「待分類」，請選擇實際分類'))
        else callback()
      }, trigger: 'change' }],
  siteCategoryDictId: [{ required: true, message: '請選擇地盤分類', trigger: 'change' },
    { validator: (_rule: unknown, value: string, callback: (error?: Error) => void) => {
        const dict = siteCategories.value.find(item => item.id === value)
        if (!dict || !dict.enabled) callback(new Error('此地盤分類已停用'))
        else callback()
      }, trigger: 'change' }],
  amount: [{ validator: (_rule: unknown, value: number | null, callback: (error?: Error) => void) => {
        if (value == null) callback()
        else if (value <= 0) callback(new Error('金額必須大於 0'))
        else if (value > 9999999999999999.99) callback(new Error('金額過大'))
        else callback()
      }, trigger: 'blur' }],
  currencyCode: [{ validator: (_rule: unknown, value: string | null, callback: (error?: Error) => void) => {
        const code = (value ?? '').trim()
        if (code && !/^[A-Za-z]{3}$/.test(code)) callback(new Error('幣種須為三位英文字母，例如 HKD'))
        else callback()
      }, trigger: 'change' }],
}
const statusNames: Record<string, string> = { NotStarted: '未識別', Processing: '識別中', Succeeded: '已識別', Failed: '識別失敗' }
const pendingTypeSelected = computed(() => types.value.find(dict => dict.id === form.type)?.value === 'pending')

function query(): ReceiptQuery {
  return { siteCategoryDictId: filter.siteCategoryDictId || undefined,
    documentTypeDictId: filter.documentTypeDictId || undefined,
    type: filter.type || undefined, start: dates.value?.[0], end: dates.value?.[1] }
}

async function load(reset = false) {
  if (reset) page.value = 1
  loading.value = true
  try {
    const result = await listReceipts(query(), page.value, pageSize.value)
    rows.value = result.items
    total.value = result.total
  } catch { /* 已由攔截器提示 */ }
  finally { loading.value = false }
}

function fill(receipt: ReceiptDto) {
  selected.value = receipt
  Object.assign(form, { version: receipt.version, documentTypeDictId: receipt.documentTypeDictId,
    type: receipt.type, siteCategoryDictId: receipt.siteCategoryDictId, receiptDate: receipt.receiptDate,
    amount: receipt.amount, currencyCode: receipt.currencyCode, merchantName: receipt.merchantName,
    invoiceNumber: receipt.invoiceNumber, remark: receipt.remark })
}

function revokeImage() {
  if (imageUrl.value) URL.revokeObjectURL(imageUrl.value)
  imageUrl.value = ''
}

function resetForm() {
  Object.assign(form, { version: '',
    documentTypeDictId: documentTypes.value.find(dict => dict.enabled && dict.value === 'receipt')?.id ?? '',
    type: '',
    siteCategoryDictId: filter.siteCategoryDictId || null, receiptDate: null, amount: null, currencyCode: null,
    merchantName: null, invoiceNumber: null, remark: null })
}

function openCreate() {
  selected.value = null
  resetForm()
  ocrPanel.value = []
  ocrReview.value = []
  revokeImage()
  imageLoading.value = false
  detailVisible.value = true
  nextTick(() => formRef.value?.clearValidate())
}

async function openDetail(receipt: ReceiptDto) {
  busy.value = true
  try {
    const detail = await getReceipt(receipt.id)
    fill(detail)
    ocrPanel.value = []
    ocrReview.value = detail.ocrResult?.reviewFields ?? []
    detailVisible.value = true
    revokeImage()
    imageLoading.value = true
    if (detail.fileItemId) imageUrl.value = URL.createObjectURL(await receiptImage(detail.id))
  } catch { /* 已由攔截器提示 */ }
  finally { busy.value = false; imageLoading.value = false }
}

async function chooseFile(event: Event) {
  const input = event.target as HTMLInputElement
  const file = input.files?.[0]
  input.value = ''
  if (!file) return
  if (!['image/jpeg', 'image/png'].includes(file.type) || file.size > 4 * 1024 * 1024 || file.size === 0) {
    ElMessage.warning('請選擇不超過 4 MB 的 JPEG / PNG 圖片')
    return
  }
  await attachImage(file)
}

/** 選擇圖片後立即保存原圖；新票據會先建立，再附上原圖。不會自動識別。 */
async function attachImage(file: File) {
  if (!form.documentTypeDictId) { ElMessage.warning('請先選擇票據類型'); return }
  busy.value = true
  try {
    let receipt = selected.value
    if (!receipt) {
      receipt = await createReceipt(form.documentTypeDictId)
      selected.value = receipt
      form.version = receipt.version
    }
    receipt = await attachReceiptImage(receipt.id, file)
    selected.value = receipt
    form.version = receipt.version
    revokeImage()
    imageUrl.value = URL.createObjectURL(file)
    imageLoading.value = false
    ElMessage.success(configured.value ? '原圖已保存，可按「OCR 識別」讀取圖中資料' : '原圖已保存')
    await load()
  } catch { /* 已由攔截器提示 */ }
  finally { busy.value = false }
}

/** 手動觸發 OCR：只回傳建議值，填入空白欄位，不覆蓋已輸入內容。 */
async function recognize() {
  if (!selected.value?.fileItemId) return
  busy.value = true
  try {
    const { receipt, fields } = await recognizeReceipt(selected.value.id, form.documentTypeDictId)
    selected.value = receipt
    form.version = receipt.version
    ocrPanel.value = ['ocr']
    if (receipt.ocrStatus === 'Succeeded') {
      const filled = fillFromOcr(fields)
      ocrReview.value = fields.reviewFields
      if (ocrReview.value.length) {
        const suffix = filled.length ? `（已填入：${filled.join('、')}）` : ''
        ElMessage.warning(`已識別，但${reviewText(ocrReview.value)}置信度偏低，請人工核對${suffix}`)
      } else if (filled.length) ElMessage.success(`已識別並填入：${filled.join('、')}`)
      else ElMessage.info('識別完成，欄位已有資料未覆蓋；可從「識別結果」查看')
    } else {
      ocrReview.value = []
      ElMessage.warning(receipt.ocrError ?? '未能識別，請手動填寫')
    }
    await load()
  } catch { /* 已由攔截器提示，原圖仍保存 */ }
  finally { busy.value = false }
}

const pendingTypeId = computed(() => types.value.find(dict => dict.value === 'pending')?.id)
const currencyOptions = computed(() => {
  const defaults = ['HKD', 'CNY', 'USD', 'EUR', 'GBP', 'MOP', 'JPY', 'TWD', 'SGD']
  const current = form.currencyCode?.trim().toUpperCase()
  return current && !defaults.includes(current) ? [...defaults, current] : defaults
})
const ocrLabels: Record<string, string> = { merchantName: '商戶', receiptDate: '票據日期', amount: '金額', currencyCode: '幣種', invoiceNumber: '票據號碼' }
const ocrFieldKeys = ['merchantName', 'receiptDate', 'amount', 'currencyCode', 'invoiceNumber'] as const

/** 已保存的識別結果，按票據欄位順序展示；只有票據表關心的欄位，不含 Azure 的內部欄位名。 */
const ocrSummary = computed(() => {
  const fields = selected.value?.ocrResult
  if (!fields) return []
  const review = new Set(fields.reviewFields ?? [])
  return ocrFieldKeys.map(key => {
    const field = fields[key]
    return {
      key,
      label: ocrLabels[key],
      text: field?.value == null || field.value === '' ? '未識別' : String(field.value),
      confidence: field?.confidence == null ? null : Math.round(field.confidence * 100),
      review: review.has(key),
    }
  })
})

/** 把低置信度欄位名轉成中文，供提示文案使用。 */
function reviewText(names: string[]) {
  return names.map(name => ocrLabels[name] ?? name).join('、')
}

/** 只填空白欄位；回傳實際填入的中文欄名。 */
function fillFromOcr(fields: OcrSuggestion) {
  const filled: string[] = []
  if (fields.merchantName.value && !form.merchantName) { form.merchantName = fields.merchantName.value; filled.push(ocrLabels.merchantName) }
  if (fields.receiptDate.value && !form.receiptDate) { form.receiptDate = fields.receiptDate.value; filled.push(ocrLabels.receiptDate) }
  if (fields.amount.value != null && form.amount == null) { form.amount = fields.amount.value; filled.push(ocrLabels.amount) }
  if (fields.currencyCode.value && !form.currencyCode) { form.currencyCode = fields.currencyCode.value; filled.push(ocrLabels.currencyCode) }
  if (fields.invoiceNumber.value && !form.invoiceNumber) { form.invoiceNumber = fields.invoiceNumber.value; filled.push(ocrLabels.invoiceNumber) }
  if (fields.categoryValue && (!form.type || form.type === pendingTypeId.value)) {
    const dict = types.value.find(item => item.enabled && item.value === fields.categoryValue)
    if (dict) { form.type = dict.id; filled.push('支出分類') }
  }
  return filled
}

/** 單一「保存」：必填齊全後存檔，成功即關閉對話框。 */
async function save() {
  if (!await formRef.value?.validate().catch(() => false)) return
  busy.value = true
  try {
    let receipt = selected.value
    if (!receipt) {
      receipt = await createReceipt(form.documentTypeDictId)
      selected.value = receipt
      form.version = receipt.version
    }
    const updated = await updateReceipt(receipt.id, {
      ...form,
      currencyCode: form.currencyCode?.trim().toUpperCase() ?? null,
      amount: form.amount ?? null,
    })
    fill(updated)
    ElMessage.success('票據已保存')
    detailVisible.value = false
    await load()
  } catch { /* 已由攔截器提示 */ }
  finally { busy.value = false }
}

/** 刪除票據（連同原圖）；確認後不可復原。 */
async function removeReceipt(receipt: ReceiptDto) {
  const name = receipt.merchantName || receipt.receiptDate || receipt.documentTypeLabel
  try {
    await ElMessageBox.confirm(`刪除票據「${name}」？原圖會一併刪除，無法復原。`, '刪除票據',
      { type: 'warning', confirmButtonText: '刪除', cancelButtonText: '取消', confirmButtonClass: 'el-button--danger' })
  } catch { return }
  busy.value = true
  try {
    await deleteReceipt(receipt.id)
    if (selected.value?.id === receipt.id) detailVisible.value = false
    ElMessage.success('票據已刪除')
    await load()
  } catch { /* 已由攔截器提示 */ }
  finally { busy.value = false }
}

function downloadOriginal() {
  if (!selected.value) return
  receiptImage(selected.value.id).then(blob => downloadBlob(blob, selected.value!.fileName ?? 'receipt')).catch(() => {})
}

/** 字典的第一個啟用項，用於對話框預設值；skipValue 用於略過「待分類」這類特殊值。 */
function firstEnabledId(list: SysDictDto[], skipValue?: string) {
  return (list.find(dict => dict.enabled && dict.value !== skipValue)
    ?? list.find(dict => dict.enabled))?.id ?? ''
}

/** 預設日期範圍：近三個月（含今日）。 */
function recentThreeMonths(): [string, string] {
  const end = new Date()
  const start = new Date(end.getFullYear(), end.getMonth() - 3, end.getDate())
  const format = (date: Date) => `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}`
  return [format(start), format(end)]
}

function exportQuery(): ReceiptQuery {
  return { siteCategoryDictId: exportForm.siteCategoryDictId || undefined,
    documentTypeDictId: exportForm.documentTypeDictId || undefined,
    type: exportForm.type || undefined,
    start: exportForm.dates?.[0], end: exportForm.dates?.[1] }
}

async function countExport() {
  if (!exportForm.siteCategoryDictId) { exportCount.value = null; return }
  exportCountLoading.value = true
  exportCount.value = null
  try { exportCount.value = (await listReceipts(exportQuery(), 1, 1)).total }
  catch { /* 已由攔截器提示 */ }
  finally { exportCountLoading.value = false }
}

async function openExport() {
  exportForm.siteCategoryDictId = firstEnabledId(siteCategories.value)
  exportForm.documentTypeDictId = firstEnabledId(documentTypes.value)
  exportForm.type = firstEnabledId(types.value, 'pending')
  exportForm.dates = recentThreeMonths()
  exportVisible.value = true
  nextTick(() => exportFormRef.value?.clearValidate())
  await countExport()
}

async function downloadArchive() {
  if (!await exportFormRef.value?.validate().catch(() => false)) return
  exporting.value = true
  try {
    downloadBlob(await exportReceiptImages(exportQuery()), 'accounting-images.zip')
    exportVisible.value = false
  } catch { /* 已由攔截器提示 */ }
  finally { exporting.value = false }
}

onMounted(async () => {
  if (!isAdmin.value) return
  loading.value = true
  try {
    const [documentList, typeList, categoryList, status] = await Promise.all([
      listSysDictsByGroup('AccountingDocumentType'), listSysDictsByGroup('Accounting'),
      listSysDictsByGroup('AccountingSiteCategory'), accountingStatus(),
    ])
    documentTypes.value = documentList
    types.value = typeList
    siteCategories.value = categoryList
    configured.value = status.ocrConfigured
    await load()
  } catch { /* 已由攔截器提示 */ }
  finally { loading.value = false }
})
onBeforeUnmount(revokeImage)
</script>

<template>
  <el-result v-if="!isAdmin" icon="warning" title="僅管理員可存取會計票據" />
  <div v-else class="accounting-view">
    <header class="page-header">
      <h1 class="page-title">會計票據</h1>
      <div class="actions">
        <el-button :disabled="loading || busy" @click="openExport"><AppIcon name="download" :size="16" />提取圖片</el-button>
        <el-button type="primary" :disabled="busy || loading" @click="openCreate"><AppIcon name="plus" :size="16" />新增票據</el-button>
      </div>
    </header>
    <el-alert v-if="!configured && !loading" title="OCR 尚未配置：票據可直接手動填寫及保存，原圖照常保存；配置後可上傳原圖再按「OCR 識別」讀取資料" type="warning" :closable="false" show-icon />
    <el-form class="filters" label-position="top" @submit.prevent="load(true)">
      <el-form-item label="地盤分類"><el-select v-model="filter.siteCategoryDictId" clearable filterable><el-option v-for="dict in siteCategories" :key="dict.id" :label="dict.label" :value="dict.id" /></el-select></el-form-item>
      <el-form-item label="票據類型"><el-select v-model="filter.documentTypeDictId" clearable><el-option v-for="dict in documentTypes" :key="dict.id" :label="dict.label" :value="dict.id" /></el-select></el-form-item>
      <el-form-item label="支出分類"><el-select v-model="filter.type" clearable><el-option v-for="dict in types" :key="dict.id" :label="dict.label" :value="dict.id" /></el-select></el-form-item>
      <el-form-item label="票據日期"><el-date-picker v-model="dates" type="daterange" value-format="YYYY-MM-DD" start-placeholder="開始日期" end-placeholder="結束日期" /></el-form-item>
      <el-button type="primary" native-type="submit" :loading="loading">查詢</el-button>
    </el-form>
    <section class="results">
      <div class="result-heading"><span>{{ total }} 張票據</span><el-button text :disabled="busy" @click="load()"><AppIcon name="refresh-cw" :size="16" />刷新</el-button></div>
      <el-table v-loading="loading" :data="rows" stripe max-height="600" empty-text="沒有符合條件的票據">
        <el-table-column prop="receiptDate" label="票據日期" width="120"><template #default="{ row }">{{ row.receiptDate || '未填寫' }}</template></el-table-column>
        <el-table-column prop="merchantName" label="商戶 / 供應商" min-width="160" show-overflow-tooltip />
        <el-table-column prop="siteCategoryLabel" label="地盤分類" min-width="145" show-overflow-tooltip />
        <el-table-column prop="documentTypeLabel" label="票據類型" width="100" />
        <el-table-column prop="typeLabel" label="支出分類" width="100" />
        <el-table-column label="金額" width="160" align="right"><template #default="{ row }"><span class="number">{{ row.amount == null ? '未填寫' : `${row.currencyCode || '?'} ${row.amount.toLocaleString('zh-HK', { minimumFractionDigits: 2 })}` }}</span></template></el-table-column>
        <el-table-column label="原圖" width="110"><template #default="{ row }"><el-tag :type="!row.fileItemId ? 'info' : row.ocrStatus === 'Failed' ? 'danger' : row.ocrStatus === 'Succeeded' ? 'success' : 'warning'">{{ !row.fileItemId ? '無原圖' : statusNames[row.ocrStatus] || row.ocrStatus }}</el-tag></template></el-table-column>
        <el-table-column label="操作" width="130" fixed="right"><template #default="{ row }"><el-button link type="primary" :disabled="busy" @click="openDetail(row)">查看</el-button><el-button link type="danger" :disabled="busy" @click="removeReceipt(row)">刪除</el-button></template></el-table-column>
      </el-table>
      <el-pagination v-model:current-page="page" v-model:page-size="pageSize" :total="total" :page-sizes="[20, 50, 100]" layout="prev, pager, next, sizes" @current-change="load()" @size-change="load(true)" />
    </section>

    <el-dialog v-model="detailVisible" :title="selected ? '票據資料' : '新增票據'" width="min(1000px, calc(100vw - 32px))" :close-on-click-modal="!busy" :show-close="!busy" :close-on-press-escape="!busy" @closed="revokeImage">
      <div v-if="selected" class="detail-status"><span>{{ selected.fileItemId ? statusNames[selected.ocrStatus] : '未上傳原圖' }}</span><el-button :disabled="busy || !configured || !selected.fileItemId" :loading="busy" @click="recognize">OCR 識別</el-button></div>
      <el-alert v-if="selected?.ocrError" :title="selected.ocrError" type="warning" :closable="false" show-icon />
      <el-alert v-if="ocrReview.length" :title="`需人工複核：${reviewText(ocrReview)}（識別置信度偏低或未讀到值，請對照原圖核對）`" type="warning" :closable="false" show-icon />
      <div class="detail-grid">
        <div class="detail-left">
          <div v-loading="imageLoading" class="image-panel">
            <img v-if="imageUrl" :src="imageUrl" alt="票據原圖" />
            <div v-else class="image-upload">
              <AppIcon name="image" :size="32" />
              <p class="image-upload-title">尚未上傳原圖</p>
              <p class="image-upload-hint">{{ configured ? '上傳後按「OCR 識別」讀取圖中資料，再由你核對。' : '先保存原圖；識別服務配置後可按「OCR 識別」讀取。' }}</p>
              <div class="actions">
                <el-button :disabled="busy" @click="cameraInput?.click()"><AppIcon name="image" :size="16" />拍照</el-button>
                <el-button :disabled="busy" @click="albumInput?.click()">選擇圖片</el-button>
              </div>
            </div>
            <div v-if="imageUrl" class="actions">
              <el-button text @click="downloadOriginal">下載原圖</el-button>
              <el-button text :disabled="busy" @click="albumInput?.click()">更換原圖</el-button>
            </div>
            <span v-if="selected?.fileItemId" class="md5">MD5: {{ selected.md5 || '未記錄' }}</span>
            <input ref="cameraInput" class="file-input" type="file" accept="image/jpeg,image/png" capture="environment" @change="chooseFile" />
            <input ref="albumInput" class="file-input" type="file" accept="image/jpeg,image/png" @change="chooseFile" />
          </div>
          <el-collapse v-if="selected" v-model="ocrPanel" class="ocr-panel">
            <el-collapse-item name="ocr">
              <template #title>
                <span class="ocr-panel-title">識別結果</span>
                <el-tag v-if="selected.ocrStatus === 'Succeeded'" size="small" type="success" effect="plain">已識別</el-tag>
                <el-tag v-else-if="selected.ocrStatus === 'Failed'" size="small" type="danger" effect="plain">識別失敗</el-tag>
              </template>
              <div v-if="selected.ocrResult || selected.ocrText" class="ocr-body">
                <div v-if="selected.ocrResult" class="confidence-list">
                  <div v-for="field in ocrSummary" :key="field.key" class="confidence-field">
                    <el-tag :type="field.review ? 'warning' : 'info'">{{ field.label }}</el-tag>
                    <span>{{ field.text }}</span>
                    <span v-if="field.confidence != null" class="confidence-value" :class="{ low: field.review }">{{ field.confidence }}%</span>
                  </div>
                </div>
                <pre v-if="selected.ocrText" class="ocr-text">{{ selected.ocrText }}</pre>
              </div>
              <p v-else class="ocr-empty">尚未識別。有原圖後按上方「OCR 識別」，原文會顯示在這裡，方便複製填寫。</p>
            </el-collapse-item>
          </el-collapse>
        </div>
        <el-form ref="formRef" :model="form" :rules="rules" label-position="top" :disabled="busy">
          <div class="form-pair">
            <el-form-item label="票據類型" prop="documentTypeDictId"><el-select v-model="form.documentTypeDictId"><el-option v-for="dict in documentTypes" :key="dict.id" :label="dict.label" :value="dict.id" :disabled="!dict.enabled" /></el-select></el-form-item>
            <el-form-item label="支出分類" prop="type"><el-select v-model="form.type" placeholder="請選擇"><el-option v-for="dict in types" :key="dict.id" :label="dict.label" :value="dict.id" :disabled="!dict.enabled || dict.value === 'pending'" /></el-select><span v-if="pendingTypeSelected" class="form-hint">「待分類」不能保存，請選擇實際分類</span></el-form-item>
          </div>
          <el-form-item label="地盤分類" prop="siteCategoryDictId"><el-select v-model="form.siteCategoryDictId" clearable filterable placeholder="請選擇"><el-option v-for="dict in siteCategories" :key="dict.id" :label="dict.label" :value="dict.id" :disabled="!dict.enabled" /></el-select><router-link v-if="!siteCategories.length" to="/system/categories" @click="detailVisible = false">新增地盤分類字典</router-link></el-form-item>
          <el-form-item label="票據日期" prop="receiptDate"><el-date-picker v-model="form.receiptDate" type="date" value-format="YYYY-MM-DD" placeholder="請選擇日期" /></el-form-item>
          <div class="form-pair">
            <el-form-item label="金額" prop="amount"><el-input-number v-model="form.amount" :min="0" :precision="2" :controls="false" placeholder="請輸入金額" /></el-form-item>
            <el-form-item label="幣種" prop="currencyCode"><el-select v-model="form.currencyCode" filterable allow-create placeholder="請選擇或輸入"><el-option v-for="code in currencyOptions" :key="code" :label="code" :value="code" /></el-select></el-form-item>
          </div>
          <el-form-item label="商戶 / 供應商"><el-input v-model="form.merchantName" maxlength="255" /></el-form-item>
          <el-form-item label="票據號碼"><el-input v-model="form.invoiceNumber" maxlength="100" /></el-form-item>
          <el-form-item label="備註"><el-input v-model="form.remark" type="textarea" maxlength="255" show-word-limit /></el-form-item>
        </el-form>
      </div>
      <template #footer><el-button v-if="selected" type="danger" link :disabled="busy" @click="removeReceipt(selected)">刪除</el-button><el-button :disabled="busy" @click="detailVisible = false">取消</el-button><el-button type="primary" :loading="busy" @click="save">保存</el-button></template>
    </el-dialog>

    <el-dialog v-model="exportVisible" title="提取票據圖片" width="min(560px, calc(100vw - 32px))" :close-on-click-modal="!exporting" :show-close="!exporting" :close-on-press-escape="!exporting">
      <el-form ref="exportFormRef" class="export-form" :model="exportForm" :rules="exportRules" label-position="top" :disabled="exporting">
        <el-form-item label="地盤分類" prop="siteCategoryDictId"><el-select v-model="exportForm.siteCategoryDictId" filterable placeholder="請選擇" @change="countExport"><el-option v-for="dict in siteCategories" :key="dict.id" :label="dict.label" :value="dict.id" :disabled="!dict.enabled" /></el-select><router-link v-if="!siteCategories.length" to="/system/categories" @click="exportVisible = false">新增地盤分類字典</router-link></el-form-item>
        <div class="form-pair">
          <el-form-item label="票據類型"><el-select v-model="exportForm.documentTypeDictId" clearable placeholder="全部" @change="countExport"><el-option v-for="dict in documentTypes" :key="dict.id" :label="dict.label" :value="dict.id" :disabled="!dict.enabled" /></el-select></el-form-item>
          <el-form-item label="支出分類"><el-select v-model="exportForm.type" clearable placeholder="全部" @change="countExport"><el-option v-for="dict in types" :key="dict.id" :label="dict.label" :value="dict.id" :disabled="!dict.enabled" /></el-select></el-form-item>
        </div>
        <el-form-item label="票據日期"><el-date-picker v-model="exportForm.dates" type="daterange" value-format="YYYY-MM-DD" start-placeholder="開始日期" end-placeholder="結束日期" @change="countExport" /></el-form-item>
      </el-form>
      <el-descriptions :column="1"><el-descriptions-item label="符合票據">{{ exportCountLoading ? '查詢中' : exportCount ?? '未取得' }}</el-descriptions-item></el-descriptions>
      <p class="export-hint">只包含已上傳原圖的票據。設定日期範圍時，未填票據日期的票據不會被包含；清空日期可包含它們。</p>
      <template #footer><el-button :disabled="exporting" @click="exportVisible = false">取消</el-button><el-button type="primary" :loading="exporting" :disabled="!exportCount || exportCountLoading" @click="downloadArchive"><AppIcon name="download" :size="16" />下載 ZIP</el-button></template>
    </el-dialog>
  </div>
</template>

<style scoped>
.accounting-view { display: grid; gap: 16px; min-width: 0; }
.page-header, .actions, .result-heading, .detail-status { display: flex; align-items: center; gap: 16px; flex-wrap: wrap; }
.page-header, .result-heading { justify-content: space-between; }
.page-title { margin: 0; font-size: 24px; font-weight: 600; }
.actions .el-button { margin-left: 0; gap: 6px; }
.filters { display: grid; grid-template-columns: repeat(3, minmax(140px, 1fr)); gap: 16px; align-items: end; }
.filters .el-form-item { margin-bottom: 0; min-width: 0; }
.filters :deep(.el-date-editor), .detail-grid :deep(.el-date-editor), .detail-grid :deep(.el-input-number) { width: 100%; min-width: 0; }
.export-form :deep(.el-select), .export-form :deep(.el-date-editor) { width: 100%; min-width: 0; }
.export-form .el-form-item { margin-bottom: 18px; }
.export-hint { margin: 0; font-size: 12px; line-height: 1.6; color: var(--el-text-color-secondary); }
.results { background: var(--el-bg-color); border-top: 1px solid var(--el-border-color); min-width: 0; }
.result-heading { padding: 12px 16px; }
.el-pagination { padding: 16px 0; justify-content: flex-end; flex-wrap: wrap; }
.number, .md5 { font-variant-numeric: tabular-nums; }
.file-input { display: none; }
.detail-status { margin-bottom: 16px; }
.detail-grid { display: grid; grid-template-columns: minmax(0, 1fr) minmax(0, 1fr); gap: 24px; margin-top: 16px; }
.detail-left { min-width: 0; display: flex; flex-direction: column; gap: 12px; }
.ocr-panel { width: 100%; }
.ocr-panel-title { margin-right: 8px; font-weight: 500; }
.ocr-body { display: flex; flex-direction: column; gap: 8px; }
.ocr-empty { margin: 0; font-size: 12px; line-height: 1.6; color: var(--el-text-color-secondary); }
.image-panel { min-width: 0; display: flex; flex-direction: column; align-items: center; gap: 16px; }
.image-panel img { width: 100%; height: 560px; object-fit: contain; background: var(--el-fill-color-light); }
.image-upload { width: 100%; min-height: 360px; display: flex; flex-direction: column; align-items: center; justify-content: center; gap: 8px;
  padding: 24px; border: 1px dashed var(--el-border-color); border-radius: var(--el-border-radius-base); background: var(--el-fill-color-lighter); text-align: center; }
.image-upload :deep(.app-icon) { color: var(--el-text-color-secondary); }
.image-upload-title { margin: 0; font-weight: 500; }
.image-upload-hint { margin: 0 0 8px; font-size: 12px; color: var(--el-text-color-secondary); max-width: 280px; }
.md5 { font-size: 12px; color: var(--el-text-color-secondary); overflow-wrap: anywhere; }
.form-pair { display: grid; grid-template-columns: minmax(0, 1fr) minmax(0, 1fr); gap: 16px; }
.form-hint { font-size: 12px; line-height: 1.5; color: var(--el-color-warning); }
.confidence-list { display: flex; flex-wrap: wrap; gap: 8px; }
.confidence-field { display: flex; gap: 8px; flex-wrap: wrap; overflow-wrap: anywhere; max-width: 100%; }
.confidence-value { font-variant-numeric: tabular-nums; font-size: 12px; color: var(--el-text-color-secondary); }
.confidence-value.low { color: var(--el-color-warning); }
.ocr-text { margin: 0; max-height: 220px; overflow: auto; white-space: pre-wrap; overflow-wrap: anywhere; font-family: inherit; font-size: 12px; background: var(--el-fill-color-lighter); padding: 8px; border-radius: var(--el-border-radius-base); }
@media (max-width: 700px) {
  .filters { grid-template-columns: minmax(0, 1fr); }
  .detail-grid { grid-template-columns: minmax(0, 1fr); }
  .image-panel img { height: 300px; }
  .image-upload { min-height: 220px; }
  .form-pair { grid-template-columns: minmax(0, 1fr); gap: 0; }
  .actions { gap: 8px; }
}
</style>
