import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useNavigate } from 'react-router-dom'
import { Eraser, LogIn, MessageCircle, Pencil, Plus } from 'lucide-react'
import { Modal } from '../../shared/components/Modal'
import { Alert, Button, Card, Checkbox, PageHeader, TextField } from '../../shared/components/ui'
import { apiErrorMessage } from '../../shared/api/client'
import { useAuthStore } from '../auth/authStore'
import { Badge } from '../agenda/AgendaGrid'
import { clearActivity, createClub, getClubs, updateClub } from './api'
import type { ClubSummary } from './api'

type Dialog = { kind: 'new' } | { kind: 'edit' | 'clear'; club: ClubSummary } | null

const toId = (value: string): number | null => (value.trim() ? Number(value) : null)

/** Clubes de EZmatch (solo SuperAdmin): alta con dueño, vínculo con Chatwoot y limpieza de prueba. */
export default function ClubsPage() {
  const clubs = useQuery({ queryKey: ['admin', 'clubs'], queryFn: getClubs })
  const selectClub = useAuthStore((s) => s.selectClub)
  const queryClient = useQueryClient()
  const navigate = useNavigate()
  const [dialog, setDialog] = useState<Dialog>(null)

  const operate = (c: ClubSummary) => {
    selectClub({ id: c.id, name: c.name })
    queryClient.removeQueries({ predicate: (q) => q.queryKey[0] !== 'admin' })
    navigate('/')
  }

  return (
    <>
      <PageHeader
        title="Clubes"
        subtitle="Alta de clubes, su dueño y el WhatsApp (inbox de Chatwoot) que atiende el bot."
        actions={
          <Button onClick={() => setDialog({ kind: 'new' })}>
            <Plus className="size-4" aria-hidden /> Nuevo club
          </Button>
        }
      />

      {clubs.isPending ? (
        <p className="text-sm text-muted">Cargando…</p>
      ) : clubs.isError ? (
        <Alert>{apiErrorMessage(clubs.error)}</Alert>
      ) : clubs.data.length === 0 ? (
        <Card className="text-center text-sm text-muted">Todavía no hay clubes.</Card>
      ) : (
        <ul className="grid gap-3 lg:grid-cols-2">
          {clubs.data.map((c) => (
            <li key={c.id} className={`flex flex-col gap-3 rounded-xl border border-line bg-surface p-4 ${c.isActive ? '' : 'opacity-70'}`}>
              <div className="flex items-start justify-between gap-3">
                <div className="min-w-0">
                  <p className="flex flex-wrap items-center gap-2 font-semibold text-ink">
                    {c.name}
                    {c.isActive ? null : <Badge tone="danger">Inactivo</Badge>}
                  </p>
                  <p className="text-xs text-muted">{c.slug}</p>
                </div>
                <Button variant="secondary" onClick={() => operate(c)}>
                  <LogIn className="size-4" aria-hidden /> Operar
                </Button>
              </div>
              <p className={`flex items-center gap-1.5 text-sm ${c.chatwootInboxId ? 'text-ink' : 'text-warn'}`}>
                <MessageCircle className="size-4 shrink-0" aria-hidden />
                {c.chatwootInboxId
                  ? `WhatsApp: inbox ${c.chatwootInboxId}${c.chatwootAccountId ? ` · cuenta ${c.chatwootAccountId}` : ''}`
                  : 'Sin WhatsApp vinculado: el bot no lo atiende'}
              </p>
              <p className="text-xs text-muted">
                {c.activeCourts} canchas activas · {c.activeOwners} {c.activeOwners === 1 ? 'dueño' : 'dueños'} · {c.customers} clientes
              </p>
              <div className="flex flex-wrap gap-2 border-t border-line pt-3">
                <Button variant="ghost" onClick={() => setDialog({ kind: 'edit', club: c })}>
                  <Pencil className="size-4" aria-hidden /> Editar
                </Button>
                <Button variant="dangerGhost" onClick={() => setDialog({ kind: 'clear', club: c })}>
                  <Eraser className="size-4" aria-hidden /> Limpiar actividad de prueba
                </Button>
              </div>
            </li>
          ))}
        </ul>
      )}

      <Modal
        open={dialog !== null}
        onClose={() => setDialog(null)}
        title={dialog?.kind === 'new' ? 'Nuevo club' : dialog?.kind === 'edit' ? `Editar ${dialog.club.name}` : 'Limpiar actividad de prueba'}
      >
        {dialog?.kind === 'new' ? <NewClubForm onClose={() => setDialog(null)} /> : null}
        {dialog?.kind === 'edit' ? <EditClubForm key={dialog.club.id} club={dialog.club} onClose={() => setDialog(null)} /> : null}
        {dialog?.kind === 'clear' ? <ClearForm key={dialog.club.id} club={dialog.club} onClose={() => setDialog(null)} /> : null}
      </Modal>
    </>
  )
}

function useClubsSaved(onClose: () => void) {
  const queryClient = useQueryClient()
  return () => {
    void queryClient.invalidateQueries({ queryKey: ['admin', 'clubs'] })
    onClose()
  }
}

function ChatwootFields({ account, inbox, onAccount, onInbox }: { account: string; inbox: string; onAccount: (v: string) => void; onInbox: (v: string) => void }) {
  return (
    <fieldset className="flex flex-col gap-2">
      <legend className="mb-1 text-sm font-medium text-ink">WhatsApp del club (Chatwoot)</legend>
      <div className="grid grid-cols-2 gap-4">
        <TextField label="Id de cuenta" name="account" inputMode="numeric" value={account} onChange={(e) => onAccount(e.target.value.replace(/\D/g, ''))} />
        <TextField label="Id de inbox" name="inbox" inputMode="numeric" value={inbox} onChange={(e) => onInbox(e.target.value.replace(/\D/g, ''))} />
      </div>
      <p className="text-xs text-muted">Salen de la URL del inbox en Chatwoot: …/accounts/<b>cuenta</b>/settings/inboxes/<b>inbox</b>. Se pueden cargar después.</p>
    </fieldset>
  )
}

function NewClubForm({ onClose }: { onClose: () => void }) {
  const saved = useClubsSaved(onClose)
  const [form, setForm] = useState({ name: '', account: '', inbox: '', ownerFullName: '', ownerEmail: '', ownerPassword: '' })
  const create = useMutation({
    mutationFn: () =>
      createClub({
        name: form.name,
        chatwootAccountId: toId(form.account),
        chatwootInboxId: toId(form.inbox),
        ownerFullName: form.ownerFullName,
        ownerEmail: form.ownerEmail,
        ownerPassword: form.ownerPassword,
      }),
    onSuccess: saved,
  })
  const set = (k: keyof typeof form) => (v: string) => setForm({ ...form, [k]: v })
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
      <TextField label="Nombre del club" name="name" autoFocus value={form.name} onChange={(e) => set('name')(e.target.value)} />
      <ChatwootFields account={form.account} inbox={form.inbox} onAccount={set('account')} onInbox={set('inbox')} />
      <fieldset className="flex flex-col gap-3">
        <legend className="mb-1 text-sm font-medium text-ink">Dueño (primer usuario del panel)</legend>
        <TextField label="Nombre" name="ownerFullName" value={form.ownerFullName} onChange={(e) => set('ownerFullName')(e.target.value)} />
        <TextField label="Email" name="ownerEmail" type="email" autoComplete="off" value={form.ownerEmail} onChange={(e) => set('ownerEmail')(e.target.value)} />
        <TextField
          label="Contraseña inicial"
          name="ownerPassword"
          type="text"
          autoComplete="off"
          value={form.ownerPassword}
          onChange={(e) => set('ownerPassword')(e.target.value)}
          hint="Mínimo 8 caracteres. El dueño la cambia desde Mi cuenta."
        />
      </fieldset>
      <p className="text-xs text-muted">Después, desde "Operar", cargá sus canchas y horarios en Configuración.</p>
      <div className="flex justify-end gap-2">
        <Button type="button" variant="ghost" onClick={onClose}>
          Cancelar
        </Button>
        <Button
          type="submit"
          loading={create.isPending}
          disabled={!form.name.trim() || !form.ownerFullName.trim() || !form.ownerEmail.trim() || form.ownerPassword.length < 8}
        >
          Crear club
        </Button>
      </div>
    </form>
  )
}

function EditClubForm({ club, onClose }: { club: ClubSummary; onClose: () => void }) {
  const saved = useClubsSaved(onClose)
  const [form, setForm] = useState({
    name: club.name,
    isActive: club.isActive,
    account: club.chatwootAccountId?.toString() ?? '',
    inbox: club.chatwootInboxId?.toString() ?? '',
  })
  const update = useMutation({
    mutationFn: () =>
      updateClub(club.id, { name: form.name, isActive: form.isActive, chatwootAccountId: toId(form.account), chatwootInboxId: toId(form.inbox) }),
    onSuccess: saved,
  })
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
      <TextField label="Nombre del club" name="name" value={form.name} onChange={(e) => setForm({ ...form, name: e.target.value })} />
      <ChatwootFields
        account={form.account}
        inbox={form.inbox}
        onAccount={(account) => setForm({ ...form, account })}
        onInbox={(inbox) => setForm({ ...form, inbox })}
      />
      <Checkbox label="Activo" checked={form.isActive} onChange={(e) => setForm({ ...form, isActive: e.target.checked })} />
      {!form.isActive ? <p className="text-xs text-danger">Inactivo: el bot deja de atenderlo y sus usuarios no pueden entrar al panel.</p> : null}
      <div className="flex justify-end gap-2">
        <Button type="button" variant="ghost" onClick={onClose}>
          Cancelar
        </Button>
        <Button type="submit" loading={update.isPending} disabled={!form.name.trim()}>
          Guardar
        </Button>
      </div>
    </form>
  )
}

function ClearForm({ club, onClose }: { club: ClubSummary; onClose: () => void }) {
  const queryClient = useQueryClient()
  const [confirm, setConfirm] = useState('')
  const clear = useMutation({
    mutationFn: () => clearActivity(club.id, confirm),
    onSuccess: () => {
      void queryClient.invalidateQueries()
    },
  })
  if (clear.isSuccess) {
    return (
      <div className="flex flex-col gap-4">
        <Alert tone="success">
          Listo: se borraron {clear.data.bookings} reservas, {clear.data.customers} clientes y {clear.data.blocks} bloqueos.
        </Alert>
        <Button onClick={onClose} className="self-end">
          Cerrar
        </Button>
      </div>
    )
  }
  return (
    <div className="flex flex-col gap-4">
      <p className="text-sm text-ink">
        Borra <strong>todas las reservas, clientes y bloqueos</strong> de {club.name}. Quedan las canchas, los horarios, la configuración y los usuarios.
        Sirve para dejar en limpio un club que se usó para pruebas. <strong>No se puede deshacer.</strong>
      </p>
      {clear.isError ? <Alert>{apiErrorMessage(clear.error)}</Alert> : null}
      <TextField label={`Escribí "${club.name}" para confirmar`} name="confirm" autoComplete="off" value={confirm} onChange={(e) => setConfirm(e.target.value)} />
      <div className="flex justify-end gap-2">
        <Button variant="ghost" onClick={onClose}>
          Cancelar
        </Button>
        <Button variant="danger" loading={clear.isPending} disabled={confirm.trim() !== club.name} onClick={() => clear.mutate()}>
          Borrar actividad
        </Button>
      </div>
    </div>
  )
}
