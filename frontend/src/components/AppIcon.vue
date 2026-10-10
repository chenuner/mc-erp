<script setup lang="ts">
import { computed } from 'vue'

/**
 * 通用 Lucide SVG 圖標渲染組件。
 * 圖標源文件在 src/assets/icons/（Lucide 線性圖標，24x24，currentColor）。
 */
const props = withDefaults(
  defineProps<{
    /** 圖標名，對應 assets/icons/{name}.svg */
    name: string
    size?: number
  }>(),
  { size: 24 },
)

const icons = import.meta.glob<string>('../assets/icons/*.svg', {
  query: '?raw',
  import: 'default',
  eager: true,
})

const svg = computed(() => {
  const raw = icons[`../assets/icons/${props.name}.svg`] ?? ''
  return raw
    .replace(/width="24"/, `width="${props.size}"`)
    .replace(/height="24"/, `height="${props.size}"`)
})
</script>

<template>
  <span class="app-icon" aria-hidden="true" v-html="svg" />
</template>

<style scoped>
.app-icon {
  display: inline-flex;
  flex: none;
  align-items: center;
  justify-content: center;
  line-height: 0;
}

.app-icon :deep(svg) {
  display: block;
}
</style>
