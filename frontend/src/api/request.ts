import axios from 'axios'
import { ElMessage } from 'element-plus'

const TOKEN_KEY = 'mc_token'

export const getToken = () => localStorage.getItem(TOKEN_KEY)
export const setToken = (token: string) => localStorage.setItem(TOKEN_KEY, token)
export const clearToken = () => localStorage.removeItem(TOKEN_KEY)

const request = axios.create({
  baseURL: '/api',
  timeout: 15000,
})

request.interceptors.request.use((config) => {
  const token = getToken()
  if (token) {
    config.headers.Authorization = `Bearer ${token}`
  }
  return config
})

request.interceptors.response.use(
  (response) => response.data,
  async (error) => {
    const status = error.response?.status
    if (status === 401) {
      clearToken()
      if (location.pathname !== '/login') {
        location.href = '/login'
      }
    } else {
      if (error.response?.data instanceof Blob && error.response.data.type.includes('json')) {
        try { error.response.data = JSON.parse(await error.response.data.text()) }
        catch { /* 非 JSON 響應沿用通用提示 */ }
      }
      const data = error.response?.data
      const fieldErrors: string[] = data?.errors
        ? Object.values(data.errors as Record<string, string[]>).flat()
        : []
      const detail = typeof data?.detail === 'string' ? data.detail : typeof data === 'string' ? data : ''
      const msg = fieldErrors.length > 0 ? fieldErrors.join('；') : detail || '請求失敗，請稍後再試'
      ElMessage.error(msg)
    }
    return Promise.reject(error)
  },
)

export default request
