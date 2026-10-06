import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { KeyRound, Pencil, Plus } from 'lucide-react'
import { api, apiErrorMessage } from '../../shared/api/client'
import { Modal } from '../../shared/components/Modal'
import { Alert, Button, Card, Checkbox, SelectField, TextField } from '../../shared/components/ui'
import { useActiveClub } from '../auth/authStore'
import { roleLabel } from '../auth/types'
import { Badge } from '../agenda/AgendaGrid'

type ClubRole = 'Owner' | 'Staff'

interface ClubUser {
  id: string
  email: string
  fullName: string
  role: ClubRole
  isActive: boolean
  createdAt: string
  lastLoginAt: string | null
  isMe: boolean
}

const getUsers = async () => (await api.get<ClubUser[]>('/users')).data
const lastLogin = new Intl.DateTimeFormat('es-AR', { day: 'numeric', month: 'short', hour: '2-digit', minute: '2-digit' })

/** Usuarios del panel del club: dueños y recepción. Solo el dueño (o el SuperAdmin) los administra. */
export function UsersSection() {
  const club = useActiveClub()
  const users = useQuery({ queryKey: ['users', club?.id], queryFn: getUsers })
  const [dialog, setDialog] = useState<{ kind: 'new' } | { kind: 'edit' | 'password'; user: ClubUser } | null>(null)

  return (
    <div className="flex max-w-3xl flex-col gap-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <p className="text-sm text-muted">
          <strong className="font-medium text-ink">Dueño</strong>: todo, incluida la configuración. <strong className="font-medium text-ink">Recepción</strong>: agenda y clientes.
        </p>
        <Button onClick={() => setDialog({ kind: 'new' })}>
          <Plus className="size-4" aria-hidden /> Nuevo usuario
        </Button>
      </div>

      {users.isPending ? (
        <p className="text-sm text-muted">Cargando…</p>
      ) : users.isError ? (
        <Alert>{apiErrorMessage(users.error)}</Alert>
      ) : users.data.length === 0 ? (
        <Card className="text-center text-sm text-muted">Este club todavía no tiene usuarios.</Card>
      ) : (
        <ul className="flex flex-col gap-2">
          {users.data.map((u) => (
            <li key={u.id} className={`flex items-center gap-3 rounded-xl border border-line bg-surface px-4 py-3 ${u.isActive ? '' : 'opacity-60'}`}>
              <div className="min-w-0 flex-1">
                <p className="flex flex-wrap items-center gap-2 text-sm font-semibold text-ink">
                  {u.fullName}
                  {u.isMe ? <Badge tone="neutral">Vos</Badge> : null}
                  <Badge tone={u.role === 'Owner' ? 'success' : 'neutral'}>{roleLabel[u.role]}</Badge>
                  {u.isActive ? null : <Badge tone="danger">Desactivado</Badge>}
                </p>
                <p className="truncate text-xs text-muted">
                  {u.email} · {u.lastLoginAt ? `último ingreso ${lastLogin.format(new Date(u.lastLoginAt))}` : 'nunca ingresó'}
                </p>
              </div>
              <button aria-label={`Blanquear contraseña de ${u.fullName}`} onClick={() => setDialog({ kind: 'password', user: u })} className="rounded-lg p-2 text-muted hover:bg-surface-2 hover:text-ink">
                <KeyRound className="size-4" />
              </button>
              <button aria-label={`Editar ${u.fullName}`} onClick={() => setDialog({ kind: 'edit', user: u })} className="rounded-lg p-2 text-muted hover:bg-surface-2 hover:text-ink">
                <Pencil className="size-4" />
              </button>
            </li>
          ))}
        </ul>
      )}

      <Modal
        open={dialog !== null}
        onClose={() => setDialog(null)}
        title={dialog?.kind === 'new' ? 'Nuevo usuario' : dialog?.kind === 'password' ? `Nueva contraseña · ${dialog.user.fullName}` : 'Editar usuario'}
      >
        {dialog?.kind === 'new' ? <NewUserForm onClose={() => setDialog(null)} /> : null}
        {dialog?.kind === 'edit' ? <EditUserForm key={dialog.user.id} user={dialog.user} onClose={() => setDialog(null)} /> : null}
        {dialog?.kind === 'password' ? <PasswordForm key={dialog.user.id} user={dialog.user} onClose={() => setDialog(null)} /> : null}
      </Modal>
    </div>
  )
}

function useUsersSaved(onClose: () => void) {
  const club = useActiveClub()
  const queryClient = useQueryClient()
  return () => {
    void queryClient.invalidateQueries({ queryKey: ['users', club?.id] })
    onClose()
  }
}

function RoleSelect({ value, onChange, disabled }: { value: ClubRole; onChange: (r: ClubRole) => void; disabled?: boolean }) {
  return (
    <SelectField label="Rol" name="role" value={value} disabled={disabled} onChange={(e) => onChange(e.target.value as ClubRole)}>
      <option value="Staff">Recepción (agenda y clientes)</option>
      <option value="Owner">Dueño (todo, incluida la configuración)</option>
    </SelectField>
  )
}

function NewUserForm({ onClose }: { onClose: () => void }) {
  const saved = useUsersSaved(onClose)
  const [form, setForm] = useState({ fullName: '', email: '', role: 'Staff' as ClubRole, password: '' })
  const create = useMutation({ mutationFn: () => api.post('/users', form), onSuccess: saved })
  return (
    <form
      noValidate
      onSubmit={(e) => {
        e.preventDefault()
        create.mutate()
      }}
      className="flex flex-col gap-4"
    >
      {create.isError ? <Alert>{apiErrorMessage(create.error)}</Alert> : null}
      <TextField label="Nombre" name="fullName" autoFocus value={form.fullName} onChange={(e) => setForm({ ...form, fullName: e.target.value })} />
      <TextField label="Email" name="email" type="email" autoComplete="off" value={form.email} onChange={(e) => setForm({ ...form, email: e.target.value })} />
      <RoleSelect value={form.role} onChange={(role) => setForm({ ...form, role })} />
      <TextField
        label="Contraseña inicial"
        name="password"
        type="text"
        autoComplete="off"
        value={form.password}
        onChange={(e) => setForm({ ...form, password: e.target.value })}
        hint="Mínimo 8 caracteres. Pasásela a la persona; después la cambia desde Mi cuenta."
      />
      <div className="flex justify-end gap-2 pt-2">
        <Button type="button" variant="ghost" onClick={onClose}>
          Cancelar
        </Button>
        <Button type="submit" loading={create.isPending} disabled={!form.fullName.trim() || !form.email.trim() || form.password.length < 8}>
          Crear usuario
        </Button>
      </div>
    </form>
  )
}

function EditUserForm({ user, onClose }: { user: ClubUser; onClose: () => void }) {
  const saved = useUsersSaved(onClose)
  const [form, setForm] = useState({ fullName: user.fullName, role: user.role, isActive: user.isActive })
  const update = useMutation({ mutationFn: () => api.put(`/users/${user.id}`, form), onSuccess: saved })
  return (
    <form
      noValidate
      onSubmit={(e) => {
        e.preventDefault()
        update.mutate()
      }}
      className="flex flex-col gap-4"
    >
      {update.isError ? <Alert>{apiErrorMessage(update.error)}</Alert> : null}
      <p className="text-sm text-muted">{user.email}</p>
      <TextField label="Nombre" name="fullName" value={form.fullName} onChange={(e) => setForm({ ...form, fullName: e.target.value })} />
      <RoleSelect value={form.role} disabled={user.isMe} onChange={(role) => setForm({ ...form, role })} />
      <Checkbox
        label="Activo (puede ingresar al panel)"
        disabled={user.isMe}
        checked={form.isActive}
        onChange={(e) => setForm({ ...form, isActive: e.target.checked })}
      />
      {user.isMe ? <p className="text-xs text-muted">No podés cambiarte el rol ni desactivarte a vos mismo.</p> : null}
      <div className="flex justify-end gap-2 pt-2">
        <Button type="button" variant="ghost" onClick={onClose}>
          Cancelar
        </Button>
        <Button type="submit" loading={update.isPending} disabled={!form.fullName.trim()}>
          Guardar
        </Button>
      </div>
    </form>
  )
}

function PasswordForm({ user, onClose }: { user: ClubUser; onClose: () => void }) {
  const saved = useUsersSaved(onClose)
  const [password, setPassword] = useState('')
  const reset = useMutation({ mutationFn: () => api.post(`/users/${user.id}/reset-password`, { password }), onSuccess: saved })
  return (
    <form
      noValidate
      onSubmit={(e) => {
        e.preventDefault()
        reset.mutate()
      }}
      className="flex flex-col gap-4"
    >
      {reset.isError ? <Alert>{apiErrorMessage(reset.error)}</Alert> : null}
      <TextField
        label="Nueva contraseña"
        name="password"
        type="text"
        autoComplete="off"
        autoFocus
        value={password}
        onChange={(e) => setPassword(e.target.value)}
        hint="Mínimo 8 caracteres. Se cierran las sesiones abiertas de esta persona."
      />
      <div className="flex justify-end gap-2 pt-2">
        <Button type="button" variant="ghost" onClick={onClose}>
          Cancelar
        </Button>
        <Button type="submit" loading={reset.isPending} disabled={password.length < 8}>
          Cambiar contraseña
        </Button>
      </div>
    </form>
  )
}
