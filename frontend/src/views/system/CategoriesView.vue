<script setup lang="ts">
import { onMounted, reactive, ref } from 'vue'
import { ElMessage, ElMessageBox, type FormInstance, type FormRules } from 'element-plus'
import AppIcon from '@/components/AppIcon.vue'
import {
  createCategory,
  deleteCategory,
  listCategories,
  updateCategory,
  type CategoryDto,
} from '@/api/system'

const loading = ref(false)
const categories = ref<CategoryDto[]>([])

const dialogVisible = ref(false)
const editingId = ref<string | null>(null)
const submitting = ref(false)
const formRef = ref<FormInstance>()

const form = reactive({
  siteName: '',
  eventDate: '',
  eventReason: '',
  remark: '',
})

const rules: FormRules<typeof form> = {
  siteName: [{ required: true, message: '請輸入地盤名稱', trigger: 'blur' }],
  eventDate: [{ required: true, message: '請選擇時間', trigger: 'change' }],
  eventReason: [{ required: true, message: '請輸入事件原因', trigger: 'blur' }],
}

function formatDate(iso: string): string {
  return iso.slice(0, 10)
}

async function fetchCategories() {
  loading.value = true
  try {
    categories.value = await listCategories()
  } catch {
    /* 攔截器已提示 */
  } finally {
    loading.value = false
  }
}

function openCreate() {
  editingId.value = null
  form.siteName = ''
  form.eventDate = ''
  form.eventReason = ''
  form.remark = ''
  dialogVisible.value = true
}

function openEdit(row: CategoryDto) {
  editingId.value = row.id
  form.siteName = row.siteName
  form.eventDate = row.eventDate.slice(0, 10)
  form.eventReason = row.eventReason
  form.remark = row.remark ?? ''
  dialogVisible.value = true
}

async function handleSubmit() {
  const valid = await formRef.value?.validate().catch(() => false)
  if (!valid) return
  submitting.value = true
  try {
    if (editingId.value) {
      await updateCategory(editingId.value, { ...form })
      ElMessage.success('分類已更新')
    } else {
      await createCategory({ ...form })
      ElMessage.success('分類已創建')
    }
    dialogVisible.value = false
    await fetchCategories()
  } catch {
    /* 攔截器已提示 */
  } finally {
    submitting.value = false
  }
}

async function handleDelete(row: CategoryDto) {
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

onMounted(fetchCategories)
</script>

<template>
  <div class="categories-view">
    <!-- 頁頭 -->
    <div class="page-header">
      <div>
        <h1 class="page-title">分類字典</h1>
        <p class="page-desc">系統級歸檔分類（地盤 / 時間 / 事件原因），地盤文件、會計收據等模組共用</p>
      </div>
      <el-button type="primary" @click="openCreate">
        <el-icon style="margin-right: 6px"><AppIcon name="plus" :size="14" /></el-icon>
        新建分類
      </el-button>
    </div>

    <!-- 分類列表 -->
    <div class="mc-card table-card">
      <el-table v-loading="loading" :data="categories" stripe>
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
        <el-table-column label="引用文件" width="100" align="center">
          <template #default="{ row }">
            <el-tag :type="row.fileCount > 0 ? 'info' : 'success'" effect="plain" round>
              {{ row.fileCount }}
            </el-tag>
          </template>
        </el-table-column>
        <el-table-column label="操作" width="130" align="center">
          <template #default="{ row }">
            <el-button link type="primary" @click="openEdit(row)">編輯</el-button>
            <el-button link type="danger" @click="handleDelete(row)">刪除</el-button>
          </template>
        </el-table-column>
        <template #empty>
          <el-empty description="暫無分類，點擊「新建分類」創建" :image-size="72" />
        </template>
      </el-table>
    </div>

    <!-- 新建 / 編輯分類 -->
    <el-dialog v-model="dialogVisible" :title="editingId ? '編輯分類' : '新建分類'" width="460px" :close-on-click-modal="false">
      <el-form ref="formRef" :model="form" :rules="rules" label-position="top">
        <el-form-item label="地盤" prop="siteName">
          <el-input v-model="form.siteName" placeholder="如：將軍澳地盤" />
        </el-form-item>
        <el-form-item label="時間" prop="eventDate">
          <el-date-picker v-model="form.eventDate" type="date" value-format="YYYY-MM-DD" placeholder="選擇日期" style="width: 100%" />
        </el-form-item>
        <el-form-item label="事件原因" prop="eventReason">
          <el-input v-model="form.eventReason" placeholder="如：混凝土澆築驗收" />
        </el-form-item>
        <el-form-item label="備註" prop="remark">
          <el-input v-model="form.remark" type="textarea" :rows="2" placeholder="可選" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="dialogVisible = false">取消</el-button>
        <el-button type="primary" :loading="submitting" @click="handleSubmit">
          {{ editingId ? '保存' : '創建' }}
        </el-button>
      </template>
    </el-dialog>
  </div>
</template>

<style scoped>
.page-header {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  margin-bottom: 16px;
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

.table-card {
  padding: 8px 0 0;
}
</style>
