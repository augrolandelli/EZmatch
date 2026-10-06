import { useEffect, useState } from 'react'
import { Navigate, Outlet, useLocation } from 'react-router-dom'
import { LoaderCircle } from 'lucide-react'
import { apiErrorMessage, refreshSession } from '../../shared/api/client'
import { Button } from '../../shared/components/ui'
import { useAuthStore } from './authStore'

/**
 * Guard de rutas privadas.
 * - Sin refresh token → /login.
 * - Con refresh token pero sin access token (recarga de la página) → renueva primero.
 */
export function RequireAuth() {
  const accessToken = useAuthStore((s) => s.accessToken)
  const refreshToken = useAuthStore((s) => s.refreshToken)
  const location = useLocation()
  const [error, setError] = useState<unknown>(null)
  const [attempt, setAttempt] = useState(0)

  useEffect(() => {
    if (accessToken || !refreshToken) return
    let cancelled = false
    refreshSession().catch((e) => {
      if (!cancelled) setError(e)
    })
    return () => {
      cancelled = true
    }
  }, [accessToken, refreshToken, attempt])

  if (!refreshToken) return <Navigate to="/login" replace state={{ from: location }} />

  if (!accessToken) {
    return (
      <div className="flex min-h-dvh flex-col items-center justify-center gap-4 p-6 text-center" role={error ? 'alert' : 'status'}>
        {error ? (
          <>
            <p className="text-sm text-muted">{apiErrorMessage(error)}</p>
            <Button
              onClick={() => {
                setError(null)
                setAttempt((n) => n + 1)
              }}
            >
              Reintentar
            </Button>
          </>
        ) : (
          <LoaderCircle className="size-6 animate-spin text-muted" aria-label="Cargando sesión" />
        )}
      </div>
    )
  }

  return <Outlet />
}

/** Solo deja pasar a los roles indicados. */
export function RequireRole({ roles }: { roles: string[] }) {
  const user = useAuthStore((s) => s.user)
  if (!user || !roles.includes(user.role)) return <Navigate to="/" replace />
  return <Outlet />
}
