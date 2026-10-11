<script setup lang="ts">
import { onMounted, reactive, ref } from 'vue'
import { ElMessage, ElMessageBox, type FormInstance, type FormRules } from 'element-plus'
import AppIcon from '@/components/AppIcon.vue'
import {
  createSysDict,
  deleteSysDict,
  listSysDicts,
  updateSysDict,
  type SysDictDto,
} from '@/api/system'

const loading = ref(false)
const dicts = ref<SysDictDto[]>([])
const page = ref(1)
const pageSize = ref(20)
const total = ref(0)

const dialogVisible = ref(false)
const editingId = ref<string | null>(null)
const submitting = ref(false)
const formRef = ref<FormInstance>()

const form = reactive({
  groupName: '',
  label: '',
  value: '',
  sort: 0,
  enabled: true,
  remark: '',
})

const rules: FormRules<typeof form> = {
  groupName: [{ required: true, message: '請輸入分組', trigger: 'blur' }],
  label: [{ required: true, message: '請輸入標籤', trigger: 'blur' }],
  value: [{ required: true, message: '請輸入值', trigger: 'blur' }],
}

async function fetchDicts() {
  loading.value = true
  try {
    const result = await listSysDicts(page.value, pageSize.value)
    dicts.value = result.items
    total.value = result.total
    if (page.value > 1 && dicts.value.length === 0) {
      page.value--
      await fetchDicts()
    }
  } catch {
    /* 攔截器已提示 */
  } finally {
    loading.value = false
  }
}

function openCreate() {
  editingId.value = null
  form.groupName = ''
  form.label = ''
  form.value = ''
  form.sort = 0
  form.enabled = true
  form.remark = ''
  dialogVisible.value = true
}

function openEdit(row: SysDictDto) {
  editingId.value = row.id
  form.groupName = row.groupName
  form.label = row.label
  form.value = row.value
  form.sort = row.sort
  form.enabled = row.enabled
  form.remark = row.remark ?? ''
  dialogVisible.value = true
}

async function handleSubmit() {
  const valid = await formRef.value?.validate().catch(() => false)
  if (!valid) return
  submitting.value = true
  try {
    if (editingId.value) {
      const { groupName: _groupName, ...payload } = form
      await updateSysDict(editingId.value, payload)
      ElMessage.success('字典項已更新')
    } else {
      await createSysDict({ ...form })
      ElMessage.success('字典項已創建')
    }
    dialogVisible.value = false
    await fetchDicts()
  } catch {
    /* 攔截器已提示 */
  } finally {
    submitting.value = false
  }
}

async function handleDelete(row: SysDictDto) {
  try {
    await ElMessageBox.confirm(
      `確定刪除字典項「${row.groupName} / ${row.label}」嗎？`,
      '提示',
      { confirmButtonText: '刪除', cancelButtonText: '取消', type: 'warning' },
    )
    await deleteSysDict(row.id)
    ElMessage.success('已刪除')
    await fetchDicts()
  } catch {
  }
}

onMounted(fetchDicts)
</script>

<template>
  <div class="categories-view">
    <!-- 頁頭 -->
    <div class="page-header">
      <div>
        <h1 class="page-title">系統字典</h1>
      </div>
      <el-button type="primary" @click="openCreate">
        <el-icon style="margin-right: 6px"><AppIcon name="plus" :size="14" /></el-icon>
        新建字典項
      </el-button>
    </div>

    <!-- 分類列表 -->
    <div class="mc-card table-card">
      <el-table v-loading="loading" :data="dicts" stripe>
        <el-table-column label="分組" prop="groupName" min-width="140" />
        <el-table-column label="標籤" prop="label" min-width="140" />
        <el-table-column label="值" prop="value" min-width="120" />
        <el-table-column label="排序" prop="sort" width="80" />
        <el-table-column label="備註" min-width="140">
          <template #default="{ row }">{{ row.remark ?? '—' }}</template>
        </el-table-column>
        <el-table-column label="狀態" width="100" align="center">
          <template #default="{ row }">
            <el-tag :type="row.enabled ? 'success' : 'info'" effect="plain" round>
              {{ row.enabled ? '啟用' : '停用' }}
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
          <el-empty description="暫無字典項" :image-size="72" />
        </template>
      </el-table>
      <el-pagination
        v-model:current-page="page"
        :page-size="pageSize"
        :total="total"
        layout="total, prev, pager, next"
        class="pagination"
        @current-change="fetchDicts"
      />
    </div>

    <!-- 新建 / 編輯分類 -->
    <el-dialog v-model="dialogVisible" :title="editingId ? '編輯字典項' : '新建字典項'" width="460px" :close-on-click-modal="false" @closed="formRef?.clearValidate()">
      <el-form ref="formRef" :model="form" :rules="rules" label-position="top">
        <el-form-item label="分組" prop="groupName">
          <el-input v-model="form.groupName" :disabled="!!editingId" maxlength="50" />
        </el-form-item>
        <el-form-item label="標籤" prop="label">
          <el-input v-model="form.label" maxlength="100" />
        </el-form-item>
        <el-form-item label="值" prop="value">
          <el-input v-model="form.value" maxlength="50" />
        </el-form-item>
        <el-form-item label="排序" prop="sort">
          <el-input-number v-model="form.sort" :precision="0" />
        </el-form-item>
        <el-form-item label="啟用" prop="enabled">
          <el-switch v-model="form.enabled" />
        </el-form-item>
        <el-form-item label="備註" prop="remark">
          <el-input v-model="form.remark" type="textarea" :rows="2" maxlength="255" placeholder="可選" />
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

.pagination {
  justify-content: flex-end;
  padding: 16px;
}
</style>
