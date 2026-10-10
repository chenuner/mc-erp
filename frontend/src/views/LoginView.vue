<script setup lang="ts">
import { reactive, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { ElMessage, type FormInstance, type FormRules } from 'element-plus'
import AppIcon from '@/components/AppIcon.vue'
import { useAuthStore } from '@/stores/auth'

const router = useRouter()
const route = useRoute()
const auth = useAuthStore()

const formRef = ref<FormInstance>()
const loading = ref(false)
const showPassword = ref(false)

const form = reactive({
  username: '',
  password: '',
})

const rules: FormRules<typeof form> = {
  username: [{ required: true, message: '請輸入用戶名', trigger: 'blur' }],
  password: [{ required: true, message: '請輸入密碼', trigger: 'blur' }],
}

async function handleLogin() {
  const valid = await formRef.value?.validate().catch(() => false)
  if (!valid) return
  loading.value = true
  try {
    await auth.login(form.username, form.password)
    ElMessage.success('登錄成功')
    const redirect = (route.query.redirect as string) ?? '/'
    router.push(redirect)
  } catch {
    /* 錯誤信息由 request 攔截器統一提示 */
  } finally {
    loading.value = false
  }
}
</script>

<template>
  <div class="login">
    <!-- 左側品牌區 -->
    <div class="brand-panel">
      <div class="brand-mark">
        <AppIcon name="hard-hat" :size="36" />
      </div>
      <h1 class="brand-title">MC ERP</h1>
      <p class="brand-subtitle">建築管理一體化平台</p>
      <div class="brand-features">
        <div class="feature">
          <AppIcon name="calendar-clock" :size="20" />
          <span>考勤薪酬 · 自動月結</span>
        </div>
        <div class="feature">
          <AppIcon name="receipt" :size="20" />
          <span>收據識別 · 支出歸檔</span>
        </div>
        <div class="feature">
          <AppIcon name="hard-drive" :size="20" />
          <span>地盤文件 · 分類存儲</span>
        </div>
      </div>
      <div class="grid-pattern" />
    </div>

    <!-- 右側登錄表單 -->
    <div class="form-panel">
      <div class="login-card mc-card">
        <h2 class="login-title">歡迎回來</h2>
        <p class="login-desc">請使用公司賬號登錄系統</p>

        <el-form
          ref="formRef"
          :model="form"
          :rules="rules"
          size="large"
          label-position="top"
          @keyup.enter="handleLogin"
        >
          <el-form-item label="用戶名" prop="username">
            <el-input v-model="form.username" placeholder="請輸入用戶名" :prefix-icon="undefined" autofocus>
              <template #prefix><AppIcon name="user" :size="16" /></template>
            </el-input>
          </el-form-item>
          <el-form-item label="密碼" prop="password">
            <el-input
              v-model="form.password"
              :type="showPassword ? 'text' : 'password'"
              placeholder="請輸入密碼"
            >
              <template #prefix><AppIcon name="lock" :size="16" /></template>
              <template #suffix>
                <button class="eye-btn" type="button" @click="showPassword = !showPassword">
                  <AppIcon :name="showPassword ? 'eye-off' : 'eye'" :size="16" />
                </button>
              </template>
            </el-input>
          </el-form-item>
          <el-button type="primary" size="large" class="login-btn" :loading="loading" @click="handleLogin">
            登 錄
          </el-button>
        </el-form>
      </div>
      <p class="copyright">© 2026 MC Construction Management Ltd.</p>
    </div>
  </div>
</template>

<style scoped>
.login {
  display: flex;
  height: 100%;
}

.brand-panel {
  position: relative;
  display: flex;
  flex-direction: column;
  justify-content: center;
  flex: 1.2;
  padding: 0 8%;
  overflow: hidden;
  color: #fff;
  background: linear-gradient(135deg, #14203a 0%, #1B2432 45%, #1D5BBF 130%);
}

.brand-mark {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 64px;
  height: 64px;
  margin-bottom: 20px;
  border-radius: 16px;
  background: rgb(46 115 232 / 25%);
  color: #8fc0ff;
}

.brand-title {
  margin: 0;
  font-size: 40px;
  font-weight: 700;
  letter-spacing: 2px;
}

.brand-subtitle {
  margin: 8px 0 40px;
  font-size: 16px;
  color: var(--mc-sidebar-text);
}

.brand-features {
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.feature {
  display: inline-flex;
  align-items: center;
  gap: 12px;
  color: var(--mc-sidebar-text);
}

.feature svg {
  color: var(--mc-primary-light);
}

/* 工程網格底紋 */
.grid-pattern {
  position: absolute;
  inset: 0;
  pointer-events: none;
  background-image:
    linear-gradient(rgb(255 255 255 / 4%) 1px, transparent 1px),
    linear-gradient(90deg, rgb(255 255 255 / 4%) 1px, transparent 1px);
  background-size: 40px 40px;
  mask-image: radial-gradient(ellipse at center, transparent 40%, #000 100%);
}

.form-panel {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  flex: 1;
  padding: 24px;
  background: var(--mc-bg);
}

.login-card {
  width: 100%;
  max-width: 400px;
  padding: 40px 36px;
}

.login-title {
  margin: 0 0 4px;
  font-size: 24px;
  font-weight: 600;
}

.login-desc {
  margin: 0 0 28px;
  color: var(--mc-text-secondary);
}

.eye-btn {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  border: none;
  background: transparent;
  color: var(--mc-text-secondary);
  cursor: pointer;
}

.eye-btn:hover {
  color: var(--mc-primary);
}

.login-btn {
  width: 100%;
  margin-top: 8px;
  font-weight: 600;
  letter-spacing: 4px;
}

.copyright {
  margin-top: 32px;
  font-size: 12px;
  color: var(--mc-text-secondary);
}

/* 窄屏隱藏品牌區 */
@media (max-width: 900px) {
  .brand-panel {
    display: none;
  }
}
</style>
