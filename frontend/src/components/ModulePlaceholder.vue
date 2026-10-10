<script setup lang="ts">
import AppIcon from '@/components/AppIcon.vue'

defineProps<{
  /** 模組名稱 */
  title: string
  /** 待接入說明 */
  description: string
  /** Lucide 圖標名 */
  icon: string
}>()
</script>

<template>
  <div class="placeholder mc-card">
    <div class="placeholder-icon">
      <AppIcon :name="icon" :size="32" />
    </div>
    <h2 class="placeholder-title">{{ title }}</h2>
    <p class="placeholder-desc">{{ description }}</p>
    <el-tag type="info" effect="plain" round>業務模組開發中 · 待需求接入</el-tag>
  </div>
</template>

<style scoped>
.placeholder {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  min-height: 420px;
  padding: 48px 24px;
  text-align: center;
}

.placeholder-icon {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 72px;
  height: 72px;
  margin-bottom: 20px;
  border-radius: 18px;
  background: var(--mc-primary-bg);
  color: var(--mc-primary);
}

.placeholder-title {
  margin: 0 0 8px;
  font-size: 18px;
  font-weight: 500;
}

.placeholder-desc {
  max-width: 460px;
  margin: 0 0 20px;
  color: var(--mc-text-secondary);
  line-height: 22px;
}
</style>
