import vue from '@vitejs/plugin-vue'
import { fileURLToPath, URL } from 'node:url'
import { defineConfig } from 'vite'

// 開發環境統一走 Vite 代理轉發後端（http://localhost:5209），生產走 Nginx
export default defineConfig({
  plugins: [vue()],
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url)),
    },
  },
  server: {
    port: 5173,
    proxy: {
      '/api': {
        target: process.env.MC_ERP_API_TARGET || 'http://localhost:5209',
        changeOrigin: true,
      },
    },
  },
})
