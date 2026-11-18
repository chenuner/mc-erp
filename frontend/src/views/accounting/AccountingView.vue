<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, onMounted, reactive, ref } from 'vue'
import { ElMessage, ElMessageBox, type FormInstance, type FormRules } from 'element-plus'
import AppIcon from '@/components/AppIcon.vue'
import { useAuthStore } from '@/stores/auth'
import { listSites, type SiteDto } from '@/api/attendance'
import { listSysDictsByGroup, type SysDictDto } from '@/api/system'
import { accountingStatus, listReceipts, getReceipt, createReceipt, attachReceiptImage, recognizeReceipt,
  updateReceipt, confirmReceipt, deleteReceipt, receiptImage, exportReceiptImages, downloadBlob, type ReceiptDto,
  type ReceiptQuery, type UpdateReceipt } from '@/api/accounting'

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
const sites = ref<SiteDto[]>([])
const documentTypes = ref<SysDictDto[]>([])
const types = ref<SysDictDto[]>([])
const siteCategories = ref<SysDictDto[]>([])
const filter = reactive({ siteId: '', documentTypeDictId: '', type: '', status: '' })
const dates = ref<[string, string] | null>(null)
const selected = ref<ReceiptDto | null>(null)
const detailVisible = ref(false)
const exportVisible = ref(false)
const exportSiteCategory = ref('')
const exportCount = ref<number | null>(null)
const exportCountLoading = ref(false)
const albumInput = ref<HTMLInputElement>()
const cameraInput = ref<HTMLInputElement>()
const imageUrl = ref('')
const imageLoading = ref(false)
const formRef = ref<FormInstance>()
const createBaseline = ref('')
const form = reactive<UpdateReceipt>({ version: '', siteId: '', documentTypeDictId: '', type: '', siteCategoryDictId: null,
  receiptDate: null, amount: null, currencyCode: null, merchantName: null, invoiceNumber: null, remark: null })
const rules: FormRules = {
  siteId: [{ required: true, message: '請選擇實際地盤', trigger: 'change' },
    { validator: (_rule: unknown, value: string, callback: (error?: Error) => void) => {
        const site = sites.value.find(item => item.id === value)
        if (!site) callback(new Error('請選擇實際地盤'))
        else if (!site.isActive) callback(new Error('此實際地盤已停用'))
        else callback()
      }, trigger: 'change' }],
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
  siteCategoryDictId: [{ required: true, message: '請選擇導出地盤分類', trigger: 'change' },
    { validator: (_rule: unknown, value: string, callback: (error?: Error) => void) => {
        const dict = siteCategories.value.find(item => item.id === value)
        if (!dict || !dict.enabled) callback(new Error('此導出地盤分類已停用'))
        else callback()
      }, trigger: 'change' }],
  receiptDate: [{ required: true, message: '請選擇票據日期', trigger: 'change' }],
  amount: [{ required: true, message: '請填寫金額', trigger: 'blur' },
    { validator: (_rule: unknown, value: number | null, callback: (error?: Error) => void) => {
        if (value == null || value <= 0) callback(new Error('金額必須大於 0'))
        else if (value > 9999999999999999.99) callback(new Error('金額過大'))
        else callback()
      }, trigger: 'blur' }],
  currencyCode: [{ required: true, message: '請選擇幣種', trigger: 'change' },
    { validator: (_rule: unknown, value: string | null, callback: (error?: Error) => void) => {
        if (!/^[A-Za-z]{3}$/.test((value ?? '').trim())) callback(new Error('幣種須為三位英文字母，例如 HKD'))
        else callback()
      }, trigger: 'change' }],
}
const statusNames: Record<string, string> = { NotStarted: '未識別', Processing: '識別中', Succeeded: '已識別', Failed: '識別失敗' }
const confidenceFields = computed(() => Object.entries(selected.value?.ocrResult?.documents?.[0]?.fields ?? {})
  .filter(([, field]) => field.confidence != null)
  .map(([name, field]) => ({ name, text: field.content, confidence: Math.round((field.confidence ?? 0) * 100) })))
const pendingTypeSelected = computed(() => types.value.find(dict => dict.id === form.type)?.value === 'pending')

/** 表單快照：新票據只有完全未改過表單時才自動 OCR，避免覆蓋手動輸入。 */
function formSnapshot() {
  return JSON.stringify([form.siteId, form.documentTypeDictId, form.type, form.siteCategoryDictId, form.receiptDate,
    form.amount, form.currencyCode, form.merchantName, form.invoiceNumber, form.remark])
}

function query(): ReceiptQuery {
  return { siteId: filter.siteId || undefined, documentTypeDictId: filter.documentTypeDictId || undefined,
    type: filter.type || undefined, start: dates.value?.[0], end: dates.value?.[1],
    isConfirmed: filter.status ? filter.status === 'confirmed' : undefined }
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
  Object.assign(form, { version: receipt.version, siteId: receipt.siteId, documentTypeDictId: receipt.documentTypeDictId,
    type: receipt.type, siteCategoryDictId: receipt.siteCategoryDictId, receiptDate: receipt.receiptDate,
    amount: receipt.amount, currencyCode: receipt.currencyCode, merchantName: receipt.merchantName,
    invoiceNumber: receipt.invoiceNumber, remark: receipt.remark })
}

function revokeImage() {
  if (imageUrl.value) URL.revokeObjectURL(imageUrl.value)
  imageUrl.value = ''
}

function resetForm() {
  Object.assign(form, { version: '', siteId: filter.siteId,
    documentTypeDictId: documentTypes.value.find(dict => dict.enabled && dict.value === 'receipt')?.id ?? '',
    type: '',
    siteCategoryDictId: null, receiptDate: null, amount: null, currencyCode: null,
    merchantName: null, invoiceNumber: null, remark: null })
}

function openCreate() {
  selected.value = null
  resetForm()
  createBaseline.value = formSnapshot()
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

/** 選擇圖片後立即保存原圖；新票據會先建立，再附上原圖。 */
async function attachImage(file: File) {
  if (!form.siteId) { ElMessage.warning('請先選擇實際支出地盤'); return }
  if (!form.documentTypeDictId) { ElMessage.warning('請先選擇票據類型'); return }
  const autoRecognize = !selected.value && formSnapshot() === createBaseline.value
  busy.value = true
  try {
    let receipt = selected.value
    if (!receipt) {
      receipt = await createReceipt(form.siteId, form.documentTypeDictId)
      selected.value = receipt
      form.version = receipt.version
    }
    receipt = await attachReceiptImage(receipt.id, file)
    selected.value = receipt
    form.version = receipt.version
    revokeImage()
    imageUrl.value = URL.createObjectURL(file)
    imageLoading.value = false
    ElMessage.success('原圖已保存')
    if (configured.value && autoRecognize) await recognize()
    else if (configured.value) ElMessage.info('表單已有手動輸入，可按「重新識別」以 OCR 結果覆蓋')
    await load()
  } catch { /* 已由攔截器提示 */ }
  finally { busy.value = false }
}

async function recognize() {
  if (!selected.value || !selected.value.fileItemId) return
  const current = selected.value
  const fields = ['siteId', 'type', 'siteCategoryDictId', 'receiptDate', 'amount', 'currencyCode', 'merchantName', 'invoiceNumber', 'remark'] as const
  if (fields.some(field => (form[field] ?? '') !== (current[field] ?? ''))) {
    ElMessage.warning('請先保存修改，再重新識別')
    return
  }
  busy.value = true
  try {
    fill(await recognizeReceipt(selected.value.id, form.documentTypeDictId))
    if (selected.value.ocrStatus === 'Succeeded') ElMessage.success('識別完成')
    else ElMessage.warning(selected.value.ocrError ?? '未能識別，請手動填寫')
    await load()
  } catch { /* 已由攔截器提示，原圖仍保存 */ }
  finally { busy.value = false }
}

async function save(confirm = false) {
  if (!await formRef.value?.validate().catch(() => false)) return
  busy.value = true
  try {
    let receipt = selected.value
    if (!receipt) {
      receipt = await createReceipt(form.siteId, form.documentTypeDictId)
      selected.value = receipt
      form.version = receipt.version
    }
    const updated = await updateReceipt(receipt.id, {
      ...form,
      currencyCode: form.currencyCode?.trim().toUpperCase() ?? null,
      amount: form.amount ?? null,
    })
    if (confirm) {
      fill(await confirmReceipt(updated.id, updated.version))
      ElMessage.success('票據已確認')
      detailVisible.value = false
    } else {
      fill(updated)
      ElMessage.success('資料已保存，待確認')
    }
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

async function countExport() {
  exportCountLoading.value = true
  exportCount.value = null
  try { exportCount.value = (await listReceipts({ ...query(), siteCategoryDictId: exportSiteCategory.value || undefined }, 1, 1)).total }
  catch { /* 已由攔截器提示 */ }
  finally { exportCountLoading.value = false }
}

async function openExport() {
  exportSiteCategory.value = ''
  exportVisible.value = true
  await countExport()
}

async function downloadArchive() {
  exporting.value = true
  try {
    downloadBlob(await exportReceiptImages({ ...query(), siteCategoryDictId: exportSiteCategory.value || undefined }), 'accounting-images.zip')
    exportVisible.value = false
  } catch { /* 已由攔截器提示 */ }
  finally { exporting.value = false }
}

onMounted(async () => {
  if (!isAdmin.value) return
  loading.value = true
  try {
    const [siteList, documentList, typeList, categoryList, status] = await Promise.all([
      listSites(), listSysDictsByGroup('AccountingDocumentType'), listSysDictsByGroup('Accounting'),
      listSysDictsByGroup('AccountingSiteCategory'), accountingStatus(),
    ])
    sites.value = siteList
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
    <el-alert v-if="!configured && !loading" title="Azure 識別服務尚未配置，票據可直接手動填寫，原圖亦會保存" type="warning" :closable="false" show-icon />
    <el-form class="filters" label-position="top" @submit.prevent="load(true)">
      <el-form-item label="實際支出地盤"><el-select v-model="filter.siteId" clearable filterable><el-option v-for="site in sites" :key="site.id" :label="site.name" :value="site.id" /></el-select></el-form-item>
      <el-form-item label="票據類型"><el-select v-model="filter.documentTypeDictId" clearable><el-option v-for="dict in documentTypes" :key="dict.id" :label="dict.label" :value="dict.id" /></el-select></el-form-item>
      <el-form-item label="支出分類"><el-select v-model="filter.type" clearable><el-option v-for="dict in types" :key="dict.id" :label="dict.label" :value="dict.id" /></el-select></el-form-item>
      <el-form-item label="票據日期"><el-date-picker v-model="dates" type="daterange" value-format="YYYY-MM-DD" start-placeholder="開始日期" end-placeholder="結束日期" /></el-form-item>
      <el-form-item label="確認狀態"><el-select v-model="filter.status" clearable><el-option label="待確認" value="pending" /><el-option label="已確認" value="confirmed" /></el-select></el-form-item>
      <el-button type="primary" native-type="submit" :loading="loading">查詢</el-button>
    </el-form>
    <section class="results">
      <div class="result-heading"><span>{{ total }} 張票據</span><el-button text :disabled="busy" @click="load()"><AppIcon name="refresh-cw" :size="16" />刷新</el-button></div>
      <el-table v-loading="loading" :data="rows" stripe max-height="600" empty-text="沒有符合條件的票據">
        <el-table-column prop="receiptDate" label="票據日期" width="120"><template #default="{ row }">{{ row.receiptDate || '未填寫' }}</template></el-table-column>
        <el-table-column prop="merchantName" label="商戶 / 供應商" min-width="160" show-overflow-tooltip />
        <el-table-column prop="siteName" label="實際地盤" min-width="145" show-overflow-tooltip />
        <el-table-column prop="documentTypeLabel" label="票據類型" width="100" />
        <el-table-column prop="typeLabel" label="支出分類" width="100" />
        <el-table-column label="金額" width="160" align="right"><template #default="{ row }"><span class="number">{{ row.amount == null ? '未填寫' : `${row.currencyCode || '?'} ${row.amount.toLocaleString('zh-HK', { minimumFractionDigits: 2 })}` }}</span></template></el-table-column>
        <el-table-column label="狀態" width="125"><template #default="{ row }"><el-tag :type="row.isConfirmed ? 'success' : !row.fileItemId ? 'info' : row.ocrStatus === 'Failed' ? 'danger' : 'warning'">{{ row.isConfirmed ? '已確認' : !row.fileItemId ? '無原圖' : statusNames[row.ocrStatus] || row.ocrStatus }}</el-tag></template></el-table-column>
        <el-table-column label="操作" width="130" fixed="right"><template #default="{ row }"><el-button link type="primary" :disabled="busy" @click="openDetail(row)">查看</el-button><el-button link type="danger" :disabled="busy" @click="removeReceipt(row)">刪除</el-button></template></el-table-column>
      </el-table>
      <el-pagination v-model:current-page="page" v-model:page-size="pageSize" :total="total" :page-sizes="[20, 50, 100]" layout="prev, pager, next, sizes" @current-change="load()" @size-change="load(true)" />
    </section>

    <el-dialog v-model="detailVisible" :title="selected ? '票據資料' : '新增票據'" width="min(1000px, calc(100vw - 32px))" :close-on-click-modal="!busy" :show-close="!busy" :close-on-press-escape="!busy" @closed="revokeImage">
      <div v-if="selected" class="detail-status"><el-tag :type="selected.isConfirmed ? 'success' : 'warning'">{{ selected.isConfirmed ? '已確認' : '待確認' }}</el-tag><span>{{ selected.fileItemId ? statusNames[selected.ocrStatus] : '未上傳原圖' }}</span><el-button :disabled="busy || !configured || selected.isConfirmed || !selected.fileItemId" :loading="busy" @click="recognize">重新識別</el-button></div>
      <el-alert v-if="selected?.ocrError" :title="selected.ocrError" type="warning" :closable="false" show-icon />
      <div class="detail-grid">
        <div v-loading="imageLoading" class="image-panel">
          <img v-if="imageUrl" :src="imageUrl" alt="票據原圖" />
          <div v-else class="image-upload">
            <AppIcon name="image" :size="32" />
            <p class="image-upload-title">尚未上傳原圖</p>
            <p class="image-upload-hint">{{ configured ? '上傳圖片後會自動識別填表，再由你核對確認。' : '可以先保存原圖作核對，識別服務配置後即可自動填表。' }}</p>
            <div class="actions">
              <el-button :disabled="busy" @click="cameraInput?.click()"><AppIcon name="image" :size="16" />拍照</el-button>
              <el-button :disabled="busy" @click="albumInput?.click()">選擇圖片</el-button>
            </div>
          </div>
          <div v-if="imageUrl" class="actions">
            <el-button text @click="downloadOriginal">下載原圖</el-button>
            <el-button text :disabled="busy || selected?.isConfirmed" @click="albumInput?.click()">更換原圖</el-button>
          </div>
          <span v-if="selected?.fileItemId" class="md5">MD5: {{ selected.md5 || '未記錄' }}</span>
          <input ref="cameraInput" class="file-input" type="file" accept="image/jpeg,image/png" capture="environment" @change="chooseFile" />
          <input ref="albumInput" class="file-input" type="file" accept="image/jpeg,image/png" @change="chooseFile" />
        </div>
        <el-form ref="formRef" :model="form" :rules="rules" label-position="top" :disabled="busy">
          <el-form-item label="實際支出地盤" prop="siteId"><el-select v-model="form.siteId" filterable><el-option v-for="site in sites" :key="site.id" :label="site.name" :value="site.id" :disabled="!site.isActive" /></el-select></el-form-item>
          <div class="form-pair">
            <el-form-item label="票據類型" prop="documentTypeDictId"><el-select v-model="form.documentTypeDictId"><el-option v-for="dict in documentTypes" :key="dict.id" :label="dict.label" :value="dict.id" :disabled="!dict.enabled" /></el-select></el-form-item>
            <el-form-item label="支出分類" prop="type"><el-select v-model="form.type" placeholder="請選擇"><el-option v-for="dict in types" :key="dict.id" :label="dict.label" :value="dict.id" :disabled="!dict.enabled || dict.value === 'pending'" /></el-select><span v-if="pendingTypeSelected" class="form-hint">「待分類」不能用於確認，請選擇實際分類</span></el-form-item>
          </div>
          <el-form-item label="導出地盤分類" prop="siteCategoryDictId"><el-select v-model="form.siteCategoryDictId" clearable filterable placeholder="請選擇"><el-option v-for="dict in siteCategories" :key="dict.id" :label="dict.label" :value="dict.id" :disabled="!dict.enabled" /></el-select><router-link v-if="!siteCategories.length" to="/system/categories" @click="detailVisible = false">新增 AccountingSiteCategory 字典</router-link></el-form-item>
          <el-form-item label="票據日期" prop="receiptDate"><el-date-picker v-model="form.receiptDate" type="date" value-format="YYYY-MM-DD" placeholder="請選擇日期" /></el-form-item>
          <div class="form-pair">
            <el-form-item label="金額" prop="amount"><el-input-number v-model="form.amount" :min="0" :precision="2" :controls="false" placeholder="請輸入金額" /></el-form-item>
            <el-form-item label="幣種" prop="currencyCode"><el-select v-model="form.currencyCode" filterable allow-create placeholder="請選擇或輸入"><el-option v-for="code in ['HKD', 'CNY', 'USD', 'EUR', 'GBP']" :key="code" :label="code" :value="code" /></el-select></el-form-item>
          </div>
          <el-form-item label="商戶 / 供應商"><el-input v-model="form.merchantName" maxlength="255" /></el-form-item>
          <el-form-item label="票據號碼"><el-input v-model="form.invoiceNumber" maxlength="100" /></el-form-item>
          <el-form-item label="備註"><el-input v-model="form.remark" type="textarea" maxlength="255" show-word-limit /></el-form-item>
        </el-form>
      </div>
      <el-collapse v-if="selected"><el-collapse-item title="原始識別結果" name="ocr"><div class="confidence-list"><div v-for="field in confidenceFields" :key="field.name" class="confidence-field"><el-tag :type="field.confidence < 80 ? 'warning' : 'info'">{{ field.name }}: {{ field.confidence }}%</el-tag><span>{{ field.text }}</span></div></div><pre class="ocr-text">{{ selected.ocrText || '尚無識別結果' }}</pre></el-collapse-item></el-collapse>
      <template #footer><el-button v-if="selected" type="danger" link :disabled="busy" @click="removeReceipt(selected)">刪除</el-button><el-button :disabled="busy" @click="detailVisible = false">取消</el-button><el-button v-if="selected" :loading="busy" @click="save()">保存</el-button><el-button type="primary" :loading="busy" @click="save(true)">確認票據</el-button></template>
    </el-dialog>

    <el-dialog v-model="exportVisible" title="提取票據圖片" width="min(480px, calc(100vw - 32px))" :close-on-click-modal="!exporting" :show-close="!exporting" :close-on-press-escape="!exporting">
      <el-form label-position="top"><el-form-item label="導出地盤分類"><el-select v-model="exportSiteCategory" clearable filterable @change="countExport"><el-option v-for="dict in siteCategories" :key="dict.id" :label="dict.label" :value="dict.id" /></el-select></el-form-item></el-form>
      <el-descriptions :column="1"><el-descriptions-item label="日期">{{ dates?.join(' 至 ') || '全部日期' }}</el-descriptions-item><el-descriptions-item label="票據類型">{{ documentTypes.find(dict => dict.id === filter.documentTypeDictId)?.label || '全部' }}</el-descriptions-item><el-descriptions-item label="支出分類">{{ types.find(dict => dict.id === filter.type)?.label || '全部' }}</el-descriptions-item><el-descriptions-item label="圖片數量">{{ exportCountLoading ? '查詢中' : exportCount ?? '未取得' }}</el-descriptions-item></el-descriptions>
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
.results { background: var(--el-bg-color); border-top: 1px solid var(--el-border-color); min-width: 0; }
.result-heading { padding: 12px 16px; }
.el-pagination { padding: 16px 0; justify-content: flex-end; flex-wrap: wrap; }
.number, .md5 { font-variant-numeric: tabular-nums; }
.file-input { display: none; }
.detail-status { margin-bottom: 16px; }
.detail-grid { display: grid; grid-template-columns: minmax(0, 1fr) minmax(0, 1fr); gap: 24px; margin-top: 16px; }
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
.ocr-text { white-space: pre-wrap; overflow-wrap: anywhere; font-family: inherit; }
@media (max-width: 700px) {
  .filters { grid-template-columns: minmax(0, 1fr); }
  .detail-grid { grid-template-columns: minmax(0, 1fr); }
  .image-panel img { height: 300px; }
  .image-upload { min-height: 220px; }
  .form-pair { grid-template-columns: minmax(0, 1fr); gap: 0; }
  .actions { gap: 8px; }
}
</style>
