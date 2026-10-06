import axios, { AxiosError, type InternalAxiosRequestConfig } from 'axios'
import { useAuthStore } from '../../features/auth/authStore'
import type { AuthResponse } from '../../features/auth/types'

/**
 * Cliente HTTP del panel.
 * - baseURL '/api': en dev lo proxea Vite a la API local; en producción lo proxea nginx (mismo dominio).
 * - Adjunta el access token y, si es SuperAdmin, el club elegido (X-Club-Id).
 * - Ante un 401 renueva la sesión una sola vez (compartida entre requests concurrentes) y reintenta.
 */
const API_BASE = import.meta.env.VITE_API_URL ?? '/api'

export const api = axios.create({
  baseURL: API_BASE,
  headers: { 'Content-Type': 'application/json' },
  timeout: 15000,
})

api.interceptors.request.use((config) => {
  const { accessToken, user, selectedClub } = useAuthStore.getState()
  if (accessToken) config.headers.Authorization = `Bearer ${accessToken}`
  if (user?.role === 'SuperAdmin' && selectedClub) config.headers['X-Club-Id'] = selectedClub.id
  return config
})

let refreshPromise: Promise<string> | null = null

async function refreshAccessToken(): Promise<string> {
  const { refreshToken, clear } = useAuthStore.getState()
  if (!refreshToken) {
    clear()
    throw new Error('No hay sesión.')
  }
  try {
    const { data } = await axios.post<AuthResponse>(`${API_BASE}/auth/refresh`, { refreshToken }, { timeout: 15000 })
    // Si la sesión cambió mientras tanto (logout, otro login), no pisarla.
    if (useAuthStore.getState().refreshToken !== refreshToken) throw new Error('La sesión cambió.')
    useAuthStore.getState().setSession(data)
    return data.accessToken
  } catch (error) {
    if (useAuthStore.getState().refreshToken === refreshToken && axios.isAxiosError(error) && error.response?.status === 401) {
      clear()
    }
    throw error
  }
}

/** Renovación única compartida por el arranque y los reintentos (incluido React StrictMode). */
export function refreshSession(): Promise<string> {
  refreshPromise ??= refreshAccessToken().finally(() => {
    refreshPromise = null
  })
  return refreshPromise
}

api.interceptors.response.use(
  (response) => response,
  async (error: AxiosError) => {
    const original = error.config as (InternalAxiosRequestConfig & { _retried?: boolean }) | undefined
    const isAuthEndpoint = original?.url?.includes('/auth/')
    if (error.response?.status === 401 && original && !original._retried && !isAuthEndpoint) {
      original._retried = true
      try {
        const token = await refreshSession()
        original.headers.Authorization = `Bearer ${token}`
        return api(original)
      } catch {
        // Sesión vencida: RequireAuth redirige a /login.
      }
    }
    return Promise.reject(error)
  },
)

/** Mensaje legible desde la API ({ message, code }) o uno genérico. */
export function apiErrorMessage(error: unknown): string {
  if (axios.isAxiosError(error)) {
    const data = error.response?.data as { message?: string } | undefined
    if (data?.message) return data.message
    if (error.response?.status === 429) return 'Demasiados intentos. Esperá un minuto y probá de nuevo.'
    if (error.code === 'ERR_NETWORK') return 'Sin conexión con el servidor.'
    if (error.code === 'ECONNABORTED') return 'El servidor tardó demasiado en responder.'
  }
  return 'Ocurrió un error inesperado.'
}
