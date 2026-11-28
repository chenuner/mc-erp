<script setup lang="ts">
import { onMounted, reactive, ref } from 'vue'
import { ElMessage, ElMessageBox, type FormInstance, type FormRules } from 'element-plus'
import AppIcon from '@/components/AppIcon.vue'
import { createUser, deleteUser, listUsers, updateUser, type UserDto } from '@/api/identity'

const loading = ref(false)
const users = ref<UserDto[]>([])
const total = ref(0)
const query = reactive({ page: 1, pageSize: 20 })

const formRef = ref<FormInstance>()
const dialogVisible = ref(false)
const submitting = ref(false)

const form = reactive({
  username: '',
  password: '',
  displayName: '',
  email: '',
})

const rules: FormRules<typeof form> = {
  username: [
    { required: true, message: '請輸入用戶名', trigger: 'blur' },
    { min: 3, max: 32, message: '長度 3-32 個字符', trigger: 'blur' },
  ],
  password: [
    { required: true, message: '請輸入密碼', trigger: 'blur' },
    { min: 8, message: '密碼至少 8 位', trigger: 'blur' },
  ],
  email: [{ type: 'email', message: '郵箱格式不正確', trigger: 'blur' }],
}

async function fetchUsers() {
  loading.value = true
  try {
    const res = await listUsers(query.page, query.pageSize)
    users.value = res.items
    total.value = res.total
  } catch {
    /* 攔截器已提示 */
  } finally {
    loading.value = false
  }
}

function openCreate() {
  form.username = ''
  form.password = ''
  form.displayName = ''
  form.email = ''
  dialogVisible.value = true
}

async function handleCreate() {
  const valid = await formRef.value?.validate().catch(() => false)
  if (!valid) return
  submitting.value = true
  try {
    await createUser({
      username: form.username,
      password: form.password,
      displayName: form.displayName || undefined,
      email: form.email || undefined,
    })
    ElMessage.success('用戶創建成功')
    dialogVisible.value = false
    await fetchUsers()
  } catch {
    /* 攔截器已提示 */
  } finally {
    submitting.value = false
  }
}

async function toggleActive(user: UserDto) {
  if (user.isProtected) {
    ElMessage.warning('主管理員帳號不可停用')
    return
  }
  try {
    await updateUser(user.id, { isActive: !user.isActive })
    user.isActive = !user.isActive
    ElMessage.success(user.isActive ? '已啟用' : '已停用')
  } catch {
    /* 攔截器已提示 */
  }
}

async function handleDelete(user: UserDto) {
  if (user.isProtected) {
    ElMessage.warning('主管理員帳號不可刪除')
    return
  }
  await ElMessageBox.confirm(`確定刪除用戶「${user.displayName ?? user.username}」嗎？此操作不可恢復。`, '危險操作', {
    confirmButtonText: '刪除',
    cancelButtonText: '取消',
    type: 'warning',
    confirmButtonClass: 'el-button--danger',
  })
  try {
    await deleteUser(user.id)
    ElMessage.success('已刪除')
    await fetchUsers()
  } catch {
    /* 攔截器已提示 */
  }
}

onMounted(fetchUsers)
</script>

<template>
  <div class="users-view">
    <!-- 頁頭 -->
    <div class="page-header">
      <div>
        <h1 class="page-title">用戶管理</h1>
        <p class="page-desc">管理系統賬號、啟用狀態與角色分配</p>
      </div>
      <el-button type="primary" @click="openCreate">
        <el-icon style="margin-right: 6px"><AppIcon name="plus" :size="14" /></el-icon>
        新建用戶
      </el-button>
    </div>

    <!-- 用戶列表 -->
    <div class="mc-card table-card">
      <el-table v-loading="loading" :data="users" stripe>
        <el-table-column label="用戶名" prop="username" min-width="120">
          <template #default="{ row }">
            <span>{{ row.username }}</span>
            <el-tooltip v-if="row.isProtected" content="主管理員：不可刪除、不可停用，運維帳號永遠存在" placement="top">
              <el-tag size="small" type="warning" effect="plain" class="protected-tag">主管理員</el-tag>
            </el-tooltip>
          </template>
        </el-table-column>
        <el-table-column label="顯示名" min-width="120">
          <template #default="{ row }">{{ row.displayName ?? '—' }}</template>
        </el-table-column>
        <el-table-column label="郵箱" min-width="180">
          <template #default="{ row }">{{ row.email ?? '—' }}</template>
        </el-table-column>
        <el-table-column label="狀態" width="90" align="center">
          <template #default="{ row }">
            <el-tag :type="row.isActive ? 'success' : 'danger'" effect="light" round>
              {{ row.isActive ? '啟用' : '停用' }}
            </el-tag>
          </template>
        </el-table-column>
        <el-table-column label="操作" width="160" align="center">
          <template #default="{ row }">
            <el-tooltip v-if="row.isProtected" content="主管理員帳號不可停用" placement="top">
              <span class="action-locked"><el-button link type="primary" disabled>停用</el-button></span>
            </el-tooltip>
            <el-button v-else link type="primary" @click="toggleActive(row)">
              {{ row.isActive ? '停用' : '啟用' }}
            </el-button>
            <el-tooltip v-if="row.isProtected" content="主管理員帳號不可刪除" placement="top">
              <span class="action-locked"><el-button link type="danger" disabled>刪除</el-button></span>
            </el-tooltip>
            <el-button v-else link type="danger" @click="handleDelete(row)">刪除</el-button>
          </template>
        </el-table-column>
        <template #empty>
          <el-empty description="暫無用戶數據" />
        </template>
      </el-table>

      <div class="pager">
        <el-pagination
          v-model:current-page="query.page"
          v-model:page-size="query.pageSize"
          :total="total"
          :page-sizes="[10, 20, 50]"
          layout="total, sizes, prev, pager, next"
          @current-change="fetchUsers"
          @size-change="fetchUsers"
        />
      </div>
    </div>

    <!-- 新建用戶 -->
    <el-dialog v-model="dialogVisible" title="新建用戶" width="440px" :close-on-click-modal="false">
      <el-form ref="formRef" :model="form" :rules="rules" label-position="top">
        <el-form-item label="用戶名" prop="username">
          <el-input v-model="form.username" placeholder="登錄用戶名" />
        </el-form-item>
        <el-form-item label="密碼" prop="password">
          <el-input v-model="form.password" type="password" placeholder="至少 8 位" show-password />
        </el-form-item>
        <el-form-item label="顯示名" prop="displayName">
          <el-input v-model="form.displayName" placeholder="中文姓名，可選" />
        </el-form-item>
        <el-form-item label="郵箱" prop="email">
          <el-input v-model="form.email" placeholder="可選" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="dialogVisible = false">取消</el-button>
        <el-button type="primary" :loading="submitting" @click="handleCreate">創建</el-button>
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

.pager {
  display: flex;
  justify-content: flex-end;
  padding: 12px 16px;
}

/* 工具提示要包一層，禁用的按鈕本身不接收滑鼠事件 */
.action-locked {
  display: inline-block;
}

.protected-tag {
  margin-left: 6px;
}
</style>
