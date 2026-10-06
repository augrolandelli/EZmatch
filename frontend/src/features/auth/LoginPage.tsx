import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { Navigate, useLocation, useNavigate } from 'react-router-dom'
import { Alert, Button, Logo, TextField } from '../../shared/components/ui'
import { apiErrorMessage } from '../../shared/api/client'
import { useAuthStore } from './authStore'
import { login } from './api'

const schema = z.object({
  email: z.string().trim().min(1, 'Ingresá tu email.').email('El email no es válido.'),
  password: z.string().min(1, 'Ingresá tu contraseña.'),
})
type FormValues = z.infer<typeof schema>

export default function LoginPage() {
  const { refreshToken, setSession } = useAuthStore()
  const navigate = useNavigate()
  const location = useLocation()
  const [error, setError] = useState<string | null>(null)
  const {
    register,
    handleSubmit,
    formState: { errors, isSubmitting },
  } = useForm<FormValues>({ resolver: zodResolver(schema) })

  if (refreshToken) return <Navigate to="/" replace />

  const onSubmit = handleSubmit(async ({ email, password }) => {
    setError(null)
    try {
      setSession(await login(email, password))
      const from = (location.state as { from?: { pathname?: string } } | null)?.from?.pathname
      navigate(from ?? '/', { replace: true })
    } catch (e) {
      setError(apiErrorMessage(e))
    }
  })

  return (
    <main className="flex min-h-dvh items-center justify-center px-4 py-10">
      <div className="w-full max-w-sm">
        <div className="mb-8 flex flex-col items-center gap-3 text-center">
          <Logo />
          <p className="text-sm text-muted">Panel de reservas del club</p>
        </div>

        <form onSubmit={onSubmit} noValidate className="flex flex-col gap-4 rounded-xl border border-line bg-surface p-6 shadow-sm">
          <h1 className="text-lg font-semibold text-ink">Ingresar</h1>
          {error ? <Alert>{error}</Alert> : null}
          <TextField label="Email" type="email" autoComplete="email" autoFocus {...register('email')} error={errors.email?.message} />
          <TextField
            label="Contraseña"
            type="password"
            autoComplete="current-password"
            {...register('password')}
            error={errors.password?.message}
          />
          <Button type="submit" loading={isSubmitting} className="mt-2 w-full">
            Ingresar
          </Button>
        </form>

        <p className="mt-6 text-center text-xs text-muted">¿No tenés usuario? Pedíselo al dueño del club.</p>
      </div>
    </main>
  )
}
