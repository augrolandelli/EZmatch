import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { LogOut } from 'lucide-react'
import { Alert, Button, Card, PageHeader, TextField } from '../../shared/components/ui'
import { useLogout } from '../auth/useLogout'
import { apiErrorMessage } from '../../shared/api/client'
import { useAuthStore } from '../auth/authStore'
import { roleLabel } from '../auth/types'
import { changePassword } from '../auth/api'

const schema = z
  .object({
    currentPassword: z.string().min(1, 'Ingresá tu contraseña actual.'),
    newPassword: z.string().min(8, 'Mínimo 8 caracteres.').max(100, 'Demasiado larga.'),
    confirm: z.string(),
  })
  .refine((v) => v.newPassword === v.confirm, { path: ['confirm'], message: 'Las contraseñas no coinciden.' })
type FormValues = z.infer<typeof schema>

export default function AccountPage() {
  const user = useAuthStore((s) => s.user)
  const setSession = useAuthStore((s) => s.setSession)
  const onLogout = useLogout()
  const [result, setResult] = useState<{ ok: boolean; message: string } | null>(null)
  const {
    register,
    handleSubmit,
    reset,
    formState: { errors, isSubmitting },
  } = useForm<FormValues>({ resolver: zodResolver(schema) })

  const onSubmit = handleSubmit(async ({ currentPassword, newPassword }) => {
    setResult(null)
    try {
      setSession(await changePassword(currentPassword, newPassword))
      reset()
      setResult({ ok: true, message: 'Contraseña actualizada. Se cerraron tus otras sesiones.' })
    } catch (e) {
      setResult({ ok: false, message: apiErrorMessage(e) })
    }
  })

  if (!user) return null

  return (
    <div className="max-w-xl">
      <PageHeader title="Mi cuenta" />

      <Card className="mb-6">
        <dl className="grid grid-cols-[auto_1fr] gap-x-6 gap-y-2 text-sm">
          <dt className="text-muted">Nombre</dt>
          <dd className="font-medium text-ink">{user.fullName}</dd>
          <dt className="text-muted">Email</dt>
          <dd className="font-medium text-ink">{user.email}</dd>
          <dt className="text-muted">Rol</dt>
          <dd className="font-medium text-ink">{roleLabel[user.role]}</dd>
          {user.clubName ? (
            <>
              <dt className="text-muted">Club</dt>
              <dd className="font-medium text-ink">{user.clubName}</dd>
            </>
          ) : null}
        </dl>
      </Card>

      <Card className="mb-6">
        <h2 className="mb-4 font-semibold text-ink">Cambiar contraseña</h2>
        <form onSubmit={onSubmit} noValidate className="flex flex-col gap-4">
          {result ? <Alert tone={result.ok ? 'success' : 'danger'}>{result.message}</Alert> : null}
          <TextField label="Contraseña actual" type="password" autoComplete="current-password" {...register('currentPassword')} error={errors.currentPassword?.message} />
          <TextField label="Nueva contraseña" type="password" autoComplete="new-password" hint="Mínimo 8 caracteres." {...register('newPassword')} error={errors.newPassword?.message} />
          <TextField label="Repetir nueva contraseña" type="password" autoComplete="new-password" {...register('confirm')} error={errors.confirm?.message} />
          <Button type="submit" loading={isSubmitting} className="self-start">
            Guardar
          </Button>
        </form>
      </Card>

      <Button variant="secondary" onClick={onLogout}>
        <LogOut className="size-4" aria-hidden /> Cerrar sesión
      </Button>
    </div>
  )
}
