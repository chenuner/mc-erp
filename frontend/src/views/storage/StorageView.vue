<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue'
import { ElMessage, ElMessageBox, type FormInstance, type FormRules, type UploadFile, type UploadFiles, type UploadRequestOptions } from 'element-plus'
import AppIcon from '@/components/AppIcon.vue'
import {
  createCategory,
  deleteCategory,
  deleteFile,
  downloadFile,
  listCategories,
  listFiles,
  updateCategory,
  uploadFile,
  type CategoryDto,
  type FileDto,
} from '@/api/storage'

// ---------- 分類字典 ----------
const categoryLoading = ref(false)
const categories = ref<CategoryDto[]>([])

const categoryDialog = ref(false)
const editingId = ref<string | null>(null)
const categorySubmitting = ref(false)
const categoryFormRef = ref<FormInstance>()

const categoryForm = reactive({
  siteName: '',
  eventDate: '',
  eventReason: '',
  remark: '',
})

const categoryRules: FormRules<typeof categoryForm> = {
  siteName: [{ required: true, message: '請輸入地盤名稱', trigger: 'blur' }],
  eventDate: [{ required: true, message: '請選擇時間', trigger: 'change' }],
  eventReason: [{ required: true, message: '請輸入事件原因', trigger: 'blur' }],
}

async function fetchCategories() {
  categoryLoading.value = true
  try {
    categories.value = await listCategories()
  } catch {
    /* 攔截器已提示 */
  } finally {
    categoryLoading.value = false
  }
}

function openCategoryCreate() {
  editingId.value = null
  categoryForm.siteName = ''
  categoryForm.eventDate = ''
  categoryForm.eventReason = ''
  categoryForm.remark = ''
  categoryDialog.value = true
}

function openCategoryEdit(row: CategoryDto) {
  editingId.value = row.id
  categoryForm.siteName = row.siteName
  categoryForm.eventDate = row.eventDate.slice(0, 10)
  categoryForm.eventReason = row.eventReason
  categoryForm.remark = row.remark ?? ''
  categoryDialog.value = true
}

async function submitCategory() {
  const valid = await categoryFormRef.value?.validate().catch(() => false)
  if (!valid) return
  categorySubmitting.value = true
  try {
    if (editingId.value) {
      await updateCategory(editingId.value, { ...categoryForm })
      ElMessage.success('分類已更新')
    } else {
      await createCategory({ ...categoryForm })
      ElMessage.success('分類已創建')
    }
    categoryDialog.value = false
    await Promise.all([fetchCategories(), fetchFiles()])
  } catch {
    /* 攔截器已提示 */
  } finally {
    categorySubmitting.value = false
  }
}

async function removeCategory(row: CategoryDto) {
  await ElMessageBox.confirm(
    `確定刪除分類「${row.siteName} / ${row.eventReason}」嗎？`,
    '提示',
    { confirmButtonText: '刪除', cancelButtonText: '取消', type: 'warning' },
  )
  try {
    await deleteCategory(row.id)
    ElMessage.success('已刪除')
    await fetchCategories()
  } catch {
    /* 攔截器已提示（有文件引用時後端會拒絕） */
  }
}

// ---------- 文件 ----------
const fileLoading = ref(false)
const files = ref<FileDto[]>([])
const filterCategoryId = ref<string>('')
const uploadCategoryId = ref('')
const uploadDialog = ref(false)
const uploading = ref(false)
const uploadFileList = ref<UploadFiles>([])

const filterCategories = computed(() => categories.value)

const uploadDisabled = computed(() => !uploadCategoryId.value || uploadFileList.value.length === 0)

function formatSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`
  return `${(bytes / 1024 / 1024).toFixed(1)} MB`
}

function formatDate(iso: string): string {
  return iso.slice(0, 10)
}

async function fetchFiles() {
  fileLoading.value = true
  try {
    files.value = await listFiles(filterCategoryId.value || undefined)
  } catch {
    /* 攔截器已提示 */
  } finally {
    fileLoading.value = false
  }
}

function openUpload() {
  if (categories.value.length === 0) {
    ElMessage.warning('請先創建分類（地盤 / 時間 / 事件原因）再上傳文件')
    return
  }
  uploadCategoryId.value = filterCategoryId.value
  uploadFileList.value = []
  uploadDialog.value = true
}

function onFileChange(_file: UploadFile, fileList: UploadFiles) {
  uploadFileList.value = fileList
}

async function doUpload(_options: UploadRequestOptions) {
  /* 自定義上傳在 confirmUpload 統一處理，不走 el-upload 默認行為 */
}

async function confirmUpload() {
  if (uploadDisabled.value) return
  uploading.value = true
  let ok = 0
  try {
    for (const f of uploadFileList.value) {
      try {
        await uploadFile(uploadCategoryId.value, f.raw!)
        ok++
      } catch {
        /* 單個失敗繼續後面的 */
      }
    }
    if (ok > 0) {
      ElMessage.success(`${ok} 個文件上傳成功`)
      uploadDialog.value = false
      await Promise.all([fetchCategories(), fetchFiles()])
    }
  } finally {
    uploading.value = false
  }
}

async function removeFile(row: FileDto) {
  await ElMessageBox.confirm(`確定刪除文件「${row.originalName}」嗎？物理文件將一併刪除。`, '危險操作', {
    confirmButtonText: '刪除',
    cancelButtonText: '取消',
    type: 'warning',
    confirmButtonClass: 'el-button--danger',
  })
  try {
    await deleteFile(row.id)
    ElMessage.success('已刪除')
    await Promise.all([fetchCategories(), fetchFiles()])
  } catch {
    /* 攔截器已提示 */
  }
}

async function handleDownload(row: FileDto) {
  try {
    await downloadFile(row.id, row.originalName)
  } catch {
    /* 攔截器已提示 */
  }
}

onMounted(async () => {
  await fetchCategories()
  await fetchFiles()
})
</script>

<template>
  <div class="storage-view">
    <!-- 分類字典 -->
    <div class="mc-card section">
      <div class="section-header">
        <div>
          <h2 class="section-title">分類字典</h2>
          <p class="section-desc">維護文件歸檔分類：哪個地盤、時間、事件原因</p>
        </div>
        <el-button type="primary" @click="openCategoryCreate">
          <el-icon style="margin-right: 6px"><AppIcon name="plus" :size="14" /></el-icon>
          新建分類
        </el-button>
      </div>

      <el-table v-loading="categoryLoading" :data="categories" stripe>
        <el-table-column label="地盤" prop="siteName" min-width="140" />
        <el-table-column label="時間" width="120">
          <template #default="{ row }">
            <span class="num">{{ formatDate(row.eventDate) }}</span>
          </template>
        </el-table-column>
        <el-table-column label="事件原因" prop="eventReason" min-width="180" />
        <el-table-column label="備註" min-width="140">
          <template #default="{ row }">{{ row.remark ?? '—' }}</template>
        </el-table-column>
        <el-table-column label="文件數" width="90" align="center">
          <template #default="{ row }">
            <el-tag effect="plain" round>{{ row.fileCount }}</el-tag>
          </template>
        </el-table-column>
        <el-table-column label="操作" width="130" align="center">
          <template #default="{ row }">
            <el-button link type="primary" @click="openCategoryEdit(row)">編輯</el-button>
            <el-button link type="danger" @click="removeCategory(row)">刪除</el-button>
          </template>
        </el-table-column>
        <template #empty>
          <el-empty description="暫無分類，請先創建" :image-size="72" />
        </template>
      </el-table>
    </div>

    <!-- 文件列表 -->
    <div class="mc-card section">
      <div class="section-header">
        <div>
          <h2 class="section-title">地盤文件</h2>
          <p class="section-desc">上傳照片 / 文件，按分類歸檔到對應地盤與事件</p>
        </div>
        <div class="file-actions">
          <el-select
            v-model="filterCategoryId"
            placeholder="按分類篩選"
            clearable
            style="width: 220px"
            @change="fetchFiles"
          >
            <el-option
              v-for="c in filterCategories"
              :key="c.id"
              :value="c.id"
              :label="`${c.siteName} · ${formatDate(c.eventDate)} · ${c.eventReason}`"
            />
          </el-select>
          <el-button type="primary" @click="openUpload">
            <el-icon style="margin-right: 6px"><AppIcon name="construction" :size="14" /></el-icon>
            上傳文件
          </el-button>
        </div>
      </div>

      <el-table v-loading="fileLoading" :data="files" stripe>
        <el-table-column label="文件名" prop="originalName" min-width="220" show-overflow-tooltip />
        <el-table-column label="地盤" prop="siteName" min-width="120" />
        <el-table-column label="事件原因" prop="eventReason" min-width="160" />
        <el-table-column label="大小" width="100">
          <template #default="{ row }">
            <span class="num">{{ formatSize(row.sizeBytes) }}</span>
          </template>
        </el-table-column>
        <el-table-column label="上傳人" width="110">
          <template #default="{ row }">{{ row.uploaderName ?? '—' }}</template>
        </el-table-column>
        <el-table-column label="上傳時間" width="120">
          <template #default="{ row }">
            <span class="num">{{ formatDate(row.createdAt) }}</span>
          </template>
        </el-table-column>
        <el-table-column label="操作" width="130" align="center">
          <template #default="{ row }">
            <el-button link type="primary" @click="handleDownload(row)">下載</el-button>
            <el-button link type="danger" @click="removeFile(row)">刪除</el-button>
          </template>
        </el-table-column>
        <template #empty>
          <el-empty description="暫無文件，點擊「上傳文件」歸檔地盤照片" :image-size="72" />
        </template>
      </el-table>
    </div>

    <!-- 分類編輯 -->
    <el-dialog v-model="categoryDialog" :title="editingId ? '編輯分類' : '新建分類'" width="460px" :close-on-click-modal="false">
      <el-form ref="categoryFormRef" :model="categoryForm" :rules="categoryRules" label-position="top">
        <el-form-item label="地盤" prop="siteName">
          <el-input v-model="categoryForm.siteName" placeholder="如：將軍澳地盤" />
        </el-form-item>
        <el-form-item label="時間" prop="eventDate">
          <el-date-picker v-model="categoryForm.eventDate" type="date" value-format="YYYY-MM-DD" placeholder="選擇日期" style="width: 100%" />
        </el-form-item>
        <el-form-item label="事件原因" prop="eventReason">
          <el-input v-model="categoryForm.eventReason" placeholder="如：混凝土澆築驗收" />
        </el-form-item>
        <el-form-item label="備註" prop="remark">
          <el-input v-model="categoryForm.remark" type="textarea" :rows="2" placeholder="可選" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="categoryDialog = false">取消</el-button>
        <el-button type="primary" :loading="categorySubmitting" @click="submitCategory">
          {{ editingId ? '保存' : '創建' }}
        </el-button>
      </template>
    </el-dialog>

    <!-- 上傳 -->
    <el-dialog v-model="uploadDialog" title="上傳文件" width="520px" :close-on-click-modal="false">
      <el-form label-position="top">
        <el-form-item label="歸檔分類" required>
          <el-select v-model="uploadCategoryId" placeholder="選擇分類（地盤 / 時間 / 事件原因）" style="width: 100%">
            <el-option
              v-for="c in categories"
              :key="c.id"
              :value="c.id"
              :label="`${c.siteName} · ${formatDate(c.eventDate)} · ${c.eventReason}`"
            />
          </el-select>
        </el-form-item>
        <el-form-item label="文件">
          <el-upload
            drag
            multiple
            :auto-upload="false"
            :http-request="doUpload"
            :on-change="onFileChange"
            accept="image/*,.pdf,.doc,.docx,.xls,.xlsx,.zip"
          >
            <div class="upload-dragger">
              <el-icon :size="28"><AppIcon name="hard-drive" :size="28" /></el-icon>
              <p class="upload-tip">拖拽文件到此處，或點擊選擇</p>
              <p class="upload-sub">支持圖片 / PDF / Office 文檔 / 壓縮包，可多選</p>
            </div>
          </el-upload>
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="uploadDialog = false">取消</el-button>
        <el-button type="primary" :disabled="uploadDisabled" :loading="uploading" @click="confirmUpload">
          上傳（{{ uploadFileList.length }} 個文件）
        </el-button>
      </template>
    </el-dialog>
  </div>
</template>

<style scoped>
.storage-view {
  display: flex;
  flex-direction: column;
  gap: 24px;
}

.section {
  padding: 20px;
}

.section-header {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 16px;
  margin-bottom: 16px;
}

.section-title {
  margin: 0 0 4px;
  font-size: 18px;
  font-weight: 500;
}

.section-desc {
  margin: 0;
  color: var(--mc-text-secondary);
  font-size: 13px;
}

.file-actions {
  display: flex;
  gap: 12px;
  flex: none;
}

.upload-dragger {
  display: flex;
  flex-direction: column;
  align-items: center;
  padding: 18px 0;
  color: var(--mc-text-secondary);
}

.upload-tip {
  margin: 10px 0 0;
  color: var(--mc-text);
  font-size: 14px;
}

.upload-sub {
  margin: 4px 0 0;
  font-size: 12px;
}
</style>
