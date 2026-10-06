import { useQueryClient } from '@tanstack/react-query'
import { useNavigate } from 'react-router-dom'
import { useAuthStore } from './authStore'
import { logout } from './api'

/** Cierra la sesión: revoca el refresh token en la API, limpia el store y la caché y va al login. */
export function useLogout() {
  const queryClient = useQueryClient()
  const navigate = useNavigate()
  return async () => {
    const { refreshToken, clear } = useAuthStore.getState()
    if (refreshToken) await logout(refreshToken).catch(() => undefined)
    clear()
    queryClient.clear()
    navigate('/login', { replace: true })
  }
}
