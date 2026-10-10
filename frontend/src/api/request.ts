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
  (error) => {
    const status = error.response?.status
    if (status === 401) {
      clearToken()
      if (location.pathname !== '/login') {
        location.href = '/login'
      }
    } else {
      const detail = error.response?.data?.detail ?? error.response?.data
      const msg = typeof detail === 'string' && detail ? detail : '請求失敗，請稍後再試'
      ElMessage.error(msg)
    }
    return Promise.reject(error)
  },
)

export default request
