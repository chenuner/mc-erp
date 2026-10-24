<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, reactive, ref, watch } from 'vue'
import { ElMessage, ElMessageBox, type UploadFile, type UploadFiles, type UploadRequestOptions } from 'element-plus'
import AppIcon from '@/components/AppIcon.vue'
import { listSysDicts, type SysDictDto } from '@/api/system'
import {
  deleteFile,
  downloadFile,
  getFileBlob,
  listFiles,
  uploadFile,
  type FileDto,
} from '@/api/storage'

type ViewMode = 'list' | 'grid'

const dictLoading = ref(false)
const dicts = ref<SysDictDto[]>([])
const filterType = ref('')
const enabledDicts = computed(() => dicts.value.filter((dict) => dict.enabled))

async function fetchDicts() {
  dictLoading.value = true
  try {
    const items: SysDictDto[] = []
    let page = 1
    let total = 0
    do {
      const result = await listSysDicts(page++, 100)
      items.push(...result.items)
      total = result.total
      if (result.items.length === 0) break
    } while (items.length < total)
    dicts.value = items
  } catch {
    /* 攔截器已提示 */
  } finally {
    dictLoading.value = false
  }
}

// ---------- 文件 ----------
const fileLoading = ref(false)
const files = ref<FileDto[]>([])
const viewMode = ref<ViewMode>('list')

function formatSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`
  return `${(bytes / 1024 / 1024).toFixed(1)} MB`
}

function formatDate(iso: string): string {
  return iso.slice(0, 10)
}

function dictLabel(dict: SysDictDto): string {
  return `${dict.groupName} / ${dict.label}`
}

interface FileTypeInfo {
  type: 'image' | 'pdf' | 'doc' | 'sheet' | 'archive' | 'other'
  label: string
  icon: string
}

function fileTypeInfo(contentType: string, name: string): FileTypeInfo {
  if (contentType.startsWith('image/')) return { type: 'image', label: '圖片', icon: 'image' }
  const ext = name.split('.').pop()?.toLowerCase() ?? ''
  if (ext === 'pdf') return { type: 'pdf', label: 'PDF', icon: 'file-text' }
  if (['doc', 'docx'].includes(ext)) return { type: 'doc', label: '文檔', icon: 'file-text' }
  if (['xls', 'xlsx'].includes(ext)) return { type: 'sheet', label: '表格', icon: 'file-text' }
  if (['zip', 'rar', '7z'].includes(ext)) return { type: 'archive', label: '壓縮包', icon: 'hard-drive' }
  return { type: 'other', label: '文件', icon: 'file-text' }
}

async function fetchFiles() {
  fileLoading.value = true
  try {
    files.value = await listFiles(filterType.value || undefined)
  } catch {
    /* 攔截器已提示 */
  } finally {
    fileLoading.value = false
  }
}

// ---------- 上傳 ----------
const uploadType = ref('')
const uploadDialog = ref(false)
const uploading = ref(false)
const uploadFileList = ref<UploadFiles>([])

const uploadDisabled = computed(() => !uploadType.value || uploadFileList.value.length === 0)

function openUpload() {
  if (enabledDicts.value.length === 0) {
    ElMessage.warning('請先到「系統設置 / 系統字典」新增並啟用字典項')
    return
  }
  uploadType.value = enabledDicts.value.find((dict) => dict.id === filterType.value)?.id ?? ''
  uploadFileList.value = []
  uploadDialog.value = true
}

function onFileChange(_file: UploadFile, fileList: UploadFiles) {
  uploadFileList.value = fileList
}

function doUpload(_options: UploadRequestOptions) {
  /* 自定義上傳在 confirmUpload 統一處理，不走 el-upload 默認行為 */
}

async function confirmUpload() {
  if (uploadDisabled.value) return
  uploading.value = true
  let ok = 0
  try {
    for (const f of uploadFileList.value) {
      try {
        await uploadFile(uploadType.value, f.raw!)
        ok++
      } catch {
        /* 單個失敗繼續後面的 */
      }
    }
    if (ok > 0) {
      ElMessage.success(`${ok} 個文件上傳成功`)
      uploadDialog.value = false
      await fetchFiles()
    }
  } finally {
    uploading.value = false
  }
}

// ---------- 刪除 / 下載 ----------
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
    await fetchFiles()
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

// ---------- 圖片預覽 ----------
const previewVisible = ref(false)
const previewUrls = ref<string[]>([])
const previewIndex = ref(0)
const previewLoading = ref(false)
const previewFiles = ref<FileDto[]>([])
const previewPlaceholder = 'data:image/gif;base64,R0lGODlhAQABAAD/ACwAAAAAAQABAAACADs='
const previewUrlCache = new Map<string, string>()
const previewRequests = new Map<string, Promise<string | null>>()
let previewSession = 0

const imageFiles = computed(() => files.value.filter((f) => fileTypeInfo(f.contentType, f.originalName).type === 'image'))

async function openPreview(target: FileDto) {
  const images = imageFiles.value
  const targetIndex = images.findIndex((file) => file.id === target.id)
  if (targetIndex < 0) return

  const session = ++previewSession
  previewFiles.value = images
  previewUrls.value = images.map(() => previewPlaceholder)
  previewLoading.value = true
  previewIndex.value = targetIndex
  previewVisible.value = true
  await loadPreviewImage(targetIndex, session, true)
  if (session === previewSession && previewVisible.value) preloadPreviewNeighbors(targetIndex, session)
}

function onPreviewSwitch(index: number) {
  previewIndex.value = index
  void loadPreviewImage(index, previewSession, true).then(() => {
    if (previewVisible.value) preloadPreviewNeighbors(index, previewSession)
  })
}

function previewCacheKey(file: FileDto): string {
  return file.md5?.toLowerCase() ?? `id:${file.id}`
}

function preloadPreviewNeighbors(index: number, session: number) {
  for (const adjacentIndex of [index - 1, index + 1]) {
    if (adjacentIndex >= 0 && adjacentIndex < previewFiles.value.length) {
      void loadPreviewImage(adjacentIndex, session)
    }
  }
}

async function loadPreviewImage(index: number, session = previewSession, showLoading = false): Promise<void> {
  const file = previewFiles.value[index]
  if (session !== previewSession || !file || previewUrls.value[index] !== previewPlaceholder) return

  const cacheKey = previewCacheKey(file)
  const cachedUrl = previewUrlCache.get(cacheKey)
  if (cachedUrl) {
    previewUrls.value[index] = cachedUrl
    return
  }

  if (showLoading) previewLoading.value = true
  let request = previewRequests.get(cacheKey)
  if (!request) {
    const pending = getFileBlob(file.id, file.md5)
      .then((blob) => {
        const url = URL.createObjectURL(blob)
        if (session !== previewSession || !previewVisible.value) {
          URL.revokeObjectURL(url)
          return null
        }
        previewUrlCache.set(cacheKey, url)
        return url
      })
      .catch(() => null)
      .finally(() => {
        if (previewRequests.get(cacheKey) === pending) previewRequests.delete(cacheKey)
      })
    previewRequests.set(cacheKey, pending)
    request = pending
  }

  const url = await request
  if (session === previewSession && previewVisible.value) {
    if (url) previewUrls.value[index] = url
    else if (showLoading) ElMessage.error('圖片加載失敗')
    if (showLoading && previewIndex.value === index) previewLoading.value = false
  }
}

function closePreview() {
  previewVisible.value = false
  previewSession++
  previewRequests.clear()
  previewLoading.value = false
  previewUrlCache.forEach((url) => URL.revokeObjectURL(url))
  previewUrlCache.clear()
  previewUrls.value = []
  previewFiles.value = []
}

// ---------- 網格視圖縮略圖 ----------
const thumbUrls = reactive<Record<string, string>>({})

watch(
  [viewMode, files],
  async ([mode]) => {
    if (mode !== 'grid') return
    // 僅加載圖片縮略圖
    const pending = files.value.filter(
      (f) => fileTypeInfo(f.contentType, f.originalName).type === 'image' && !thumbUrls[f.id],
    )
    for (const f of pending) {
      try {
        const blob = await getFileBlob(f.id, f.md5)
        thumbUrls[f.id] = URL.createObjectURL(blob)
      } catch {
        /* 忽略單個失敗 */
      }
    }
  },
  { immediate: true },
)

function clearThumbs() {
  Object.values(thumbUrls).forEach((u) => URL.revokeObjectURL(u))
  Object.keys(thumbUrls).forEach((k) => delete thumbUrls[k])
}

watch(filterType, () => {
  clearThumbs()
  fetchFiles()
})

onBeforeUnmount(() => {
  clearThumbs()
  closePreview()
})

onMounted(async () => {
  await fetchDicts()
  await fetchFiles()
})
</script>

<template>
  <div class="storage-view">
    <!-- 頁頭 -->
    <div class="page-header">
      <div>
        <h1 class="page-title">地盤文件</h1>
      </div>
      <el-button type="primary" :disabled="dictLoading" @click="openUpload">
        <el-icon style="margin-right: 6px"><AppIcon name="construction" :size="14" /></el-icon>
        上傳文件
      </el-button>
    </div>

    <!-- 工具欄 -->
    <div class="toolbar mc-card">
      <el-select
        v-model="filterType"
        placeholder="按文件類別篩選"
        aria-label="文件類別"
        :loading="dictLoading"
        filterable
        clearable
        style="width: 280px"
      >
        <el-option
          v-for="dict in dicts"
          :key="dict.id"
          :value="dict.id"
          :label="dictLabel(dict)"
        />
      </el-select>

      <div class="toolbar-spacer" />

      <el-radio-group v-model="viewMode" size="default">
        <el-radio-button value="list">列表</el-radio-button>
        <el-radio-button value="grid">網格</el-radio-button>
      </el-radio-group>
    </div>

    <!-- 列表視圖 -->
    <div v-show="viewMode === 'list'" class="mc-card table-card">
      <el-table v-loading="fileLoading" :data="files" stripe>
        <el-table-column label="文件名" min-width="240" show-overflow-tooltip>
          <template #default="{ row }">
            <div class="file-name-cell">
              <AppIcon :name="fileTypeInfo(row.contentType, row.originalName).icon" :size="18" class="file-type-icon" />
              <span>{{ row.originalName }}</span>
            </div>
          </template>
        </el-table-column>
        <el-table-column label="文件類別" min-width="140">
          <template #default="{ row }">{{ row.typeLabel ?? '—' }}</template>
        </el-table-column>
        <el-table-column label="格式" width="90" align="center">
          <template #default="{ row }">
            <el-tag effect="plain" round>{{ fileTypeInfo(row.contentType, row.originalName).label }}</el-tag>
          </template>
        </el-table-column>
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
        <el-table-column label="操作" width="160" align="center">
          <template #default="{ row }">
            <el-button
              v-if="fileTypeInfo(row.contentType, row.originalName).type === 'image'"
              link
              type="primary"
              @click="openPreview(row)"
            >預覽</el-button>
            <el-button link type="primary" @click="handleDownload(row)">下載</el-button>
            <el-button link type="danger" @click="removeFile(row)">刪除</el-button>
          </template>
        </el-table-column>
        <template #empty>
          <el-empty description="暫無文件，點擊「上傳文件」歸檔地盤照片" :image-size="72" />
        </template>
      </el-table>
    </div>

    <!-- 網格視圖 -->
    <div v-show="viewMode === 'grid'" v-loading="fileLoading" class="grid-view">
      <template v-if="files.length > 0">
        <div v-for="f in files" :key="f.id" class="file-card mc-card" :class="{ 'is-image': fileTypeInfo(f.contentType, f.originalName).type === 'image' }">
          <div class="card-thumb" @click="fileTypeInfo(f.contentType, f.originalName).type === 'image' && openPreview(f)">
            <template v-if="fileTypeInfo(f.contentType, f.originalName).type === 'image'">
              <img v-if="thumbUrls[f.id]" :src="thumbUrls[f.id]" :alt="f.originalName" />
              <div v-else class="thumb-loading">
                <el-icon class="is-loading"><AppIcon name="refresh-cw" :size="22" /></el-icon>
              </div>
            </template>
            <template v-else>
              <AppIcon :name="fileTypeInfo(f.contentType, f.originalName).icon" :size="40" class="file-big-icon" />
            </template>
          </div>
          <div class="card-body">
            <div class="card-name" :title="f.originalName">{{ f.originalName }}</div>
            <div class="card-meta">
              <span>{{ f.typeLabel ?? '—' }}</span>
              <span class="dot">·</span>
              <span class="num">{{ formatSize(f.sizeBytes) }}</span>
            </div>
            <div class="card-actions">
              <el-button link type="primary" @click="handleDownload(f)">下載</el-button>
              <el-button link type="danger" @click="removeFile(f)">刪除</el-button>
            </div>
          </div>
        </div>
      </template>
      <el-empty v-else description="暫無文件，點擊「上傳文件」歸檔地盤照片" :image-size="72" />
    </div>

    <!-- 上傳 -->
    <el-dialog v-model="uploadDialog" title="上傳文件" width="520px" :close-on-click-modal="false">
      <el-form label-position="top">
        <el-form-item label="文件類別" required>
          <el-select v-model="uploadType" placeholder="選擇系統字典項" filterable style="width: 100%">
            <el-option
              v-for="dict in enabledDicts"
              :key="dict.id"
              :value="dict.id"
              :label="dictLabel(dict)"
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

    <!-- 圖片預覽 -->
    <el-dialog v-model="previewVisible" title="圖片預覽" width="80vw" top="6vh" :close-on-click-modal="false" @close="closePreview">
      <div v-loading="previewLoading" class="preview-wrap">
        <el-image-viewer
          v-if="previewUrls.length > 0"
          :url-list="previewUrls"
          :initial-index="previewIndex"
          hide-on-click-modal
          @switch="onPreviewSwitch"
          @close="closePreview"
        />
      </div>
    </el-dialog>
  </div>
</template>

<style scoped>
.storage-view {
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.page-header {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
}

.page-title {
  margin: 0 0 4px;
  font-size: 18px;
  font-weight: 500;
}

.page-desc {
  margin: 0;
  color: var(--mc-text-secondary);
}

.toolbar {
  display: flex;
  align-items: center;
  gap: 12px;
  padding: 12px 16px;
}

.toolbar-spacer {
  flex: 1;
}

.table-card {
  padding: 8px 0 0;
}

.file-name-cell {
  display: flex;
  align-items: center;
  gap: 8px;
}

.file-type-icon {
  color: var(--mc-primary);
  flex: none;
}

/* 網格視圖 */
.grid-view {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(200px, 1fr));
  gap: 16px;
}

.file-card {
  display: flex;
  flex-direction: column;
  overflow: hidden;
  cursor: default;
}

.file-card.is-image .card-thumb {
  cursor: pointer;
}

.card-thumb {
  height: 150px;
  background: var(--mc-bg);
  display: flex;
  align-items: center;
  justify-content: center;
  overflow: hidden;
  position: relative;
}

.card-thumb img {
  width: 100%;
  height: 100%;
  object-fit: cover;
  transition: transform 0.2s ease;
}

.file-card.is-image:hover .card-thumb img {
  transform: scale(1.04);
}

.thumb-loading {
  color: var(--mc-text-secondary);
}

.file-big-icon {
  color: var(--mc-text-secondary);
  opacity: 0.5;
}

.card-body {
  padding: 10px 12px;
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.card-name {
  font-size: 13px;
  font-weight: 500;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.card-meta {
  font-size: 12px;
  color: var(--mc-text-secondary);
  display: flex;
  align-items: center;
  gap: 4px;
}

.card-meta .dot {
  opacity: 0.5;
}

.card-actions {
  display: flex;
  gap: 4px;
  margin-top: 2px;
}

/* 上傳 */
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

/* 預覽 */
.preview-wrap {
  min-height: 300px;
  display: flex;
  align-items: center;
  justify-content: center;
}

.preview-wrap :deep(.el-image-viewer__img) {
  max-height: 78vh;
}
</style>
