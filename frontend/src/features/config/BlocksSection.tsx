import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import axios from 'axios'
import { Ban, Plus, Trash2 } from 'lucide-react'
import { Alert, Button, Card, Checkbox, TextField } from '../../shared/components/ui'
import { Modal } from '../../shared/components/Modal'
import { apiErrorMessage } from '../../shared/api/client'
import { formatLongDate, formatTime, toIsoDate } from '../../shared/format'
import { useActiveClub } from '../auth/authStore'
import { createBlock, deleteBlock, getBlocks, getCourts } from './api'
import type { Block, BlockConflicts, Court } from './api'

/** Bloqueos próximos (torneos, mantenimiento, lluvia) y alta de nuevos. */
export function BlocksSection() {
  const club = useActiveClub()
  const queryClient = useQueryClient()
  const blocks = useQuery({ queryKey: ['blocks', club?.id], queryFn: getBlocks })
  const courts = useQuery({ queryKey: ['courts', club?.id], queryFn: getCourts })
  const [creating, setCreating] = useState(false)

  const remove = useMutation({
    mutationFn: deleteBlock,
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['blocks', club?.id] })
      void queryClient.invalidateQueries({ queryKey: ['agenda', club?.id] })
    },
  })

  return (
    <div className="flex max-w-3xl flex-col gap-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <p className="text-sm text-muted">Mientras dure un bloqueo, ni el bot ni el mostrador pueden reservar esa cancha.</p>
        <Button onClick={() => setCreating(true)} disabled={!courts.data?.length}>
          <Plus className="size-4" aria-hidden /> Nuevo bloqueo
        </Button>
      </div>

      {remove.isError ? <Alert>{apiErrorMessage(remove.error)}</Alert> : null}

      {blocks.isPending ? (
        <p className="text-sm text-muted">Cargando…</p>
      ) : blocks.isError ? (
        <Alert>{apiErrorMessage(blocks.error)}</Alert>
      ) : blocks.data.length === 0 ? (
        <Card className="text-center text-sm text-muted">No hay bloqueos en los próximos 60 días.</Card>
      ) : (
        <ul className="flex flex-col gap-2">
          {blocks.data.map((b) => (
            <li key={b.id} className="flex items-center gap-3 rounded-xl border border-line bg-surface px-4 py-3">
              <Ban className="size-5 shrink-0 text-muted" aria-hidden />
              <div className="min-w-0 flex-1">
                <p className="text-sm font-semibold text-ink">
                  {b.courtName} · {b.reason}
                </p>
                <p className="text-xs text-muted first-letter:uppercase">{rangeLabel(b)}</p>
              </div>
              <button
                aria-label={`Quitar bloqueo de ${b.courtName}`}
                onClick={() => remove.mutate(b.id)}
                className="rounded-lg p-2 text-muted hover:bg-danger-soft hover:text-danger"
              >
                <Trash2 className="size-4" />
              </button>
            </li>
          ))}
        </ul>
      )}

      <Modal open={creating} onClose={() => setCreating(false)} title="Nuevo bloqueo">
        {creating && courts.data ? <BlockForm courts={courts.data} onClose={() => setCreating(false)} /> : null}
      </Modal>
    </div>
  )
}

function rangeLabel(b: Block): string {
  const start = `${formatLongDate(b.startDate)} ${formatTime(b.startTime)}`
  return b.startDate === b.endDate ? `${start} a ${formatTime(b.endTime)}` : `${start} al ${formatLongDate(b.endDate)} ${formatTime(b.endTime)}`
}

const reasons = ['Torneo', 'Mantenimiento', 'Lluvia', 'Evento privado', 'Clases']

function BlockForm({ courts, onClose }: { courts: Court[]; onClose: () => void }) {
  const club = useActiveClub()
  const queryClient = useQueryClient()
  const active = courts.filter((c) => c.isActive)
  const [today] = useState(() => toIsoDate(new Date()))
  const [courtIds, setCourtIds] = useState<string[]>([])
  const [startDate, setStartDate] = useState(today)
  const [startTime, setStartTime] = useState('08:00')
  const [endDate, setEndDate] = useState(today)
  const [endTime, setEndTime] = useState('12:00')
  const [reason, setReason] = useState('')

  const create = useMutation({
    mutationFn: () => createBlock({ courtIds, startDate, startTime, endDate, endTime, reason }),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['blocks', club?.id] })
      void queryClient.invalidateQueries({ queryKey: ['agenda', club?.id] })
      onClose()
    },
  })
  const conflicts =
    axios.isAxiosError(create.error) && create.error.response?.status === 409
      ? ((create.error.response.data as { details?: BlockConflicts }).details?.bookings ?? [])
      : []

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
      {conflicts.length > 0 ? (
        <ul className="-mt-2 flex flex-col gap-1 rounded-lg bg-surface-2 p-3 text-sm">
          {conflicts.map((b) => (
            <li key={b.id}>
              <span className="font-medium text-ink">{b.customerName}</span>
              <span className="text-muted">
                {' '}
                · {b.courtName} · {formatTime(b.startTime)}
              </span>
            </li>
          ))}
        </ul>
      ) : null}

      <fieldset className="flex flex-col gap-2">
        <legend className="mb-1 text-sm font-medium text-ink">Canchas</legend>
        <Checkbox
          label="Todas"
          checked={courtIds.length === active.length}
          onChange={(e) => setCourtIds(e.target.checked ? active.map((c) => c.id) : [])}
        />
        <div className="flex flex-wrap gap-x-4 gap-y-2">
          {active.map((c) => (
            <Checkbox
              key={c.id}
              label={c.name}
              checked={courtIds.includes(c.id)}
              onChange={(e) => setCourtIds(e.target.checked ? [...courtIds, c.id] : courtIds.filter((id) => id !== c.id))}
            />
          ))}
        </div>
      </fieldset>

      <div className="grid grid-cols-2 gap-4">
        <TextField label="Desde" name="startDate" type="date" value={startDate} onChange={(e) => {
          setStartDate(e.target.value)
          if (e.target.value > endDate) setEndDate(e.target.value)
        }} />
        <TextField label="Hora" name="startTime" type="time" value={startTime} onChange={(e) => setStartTime(e.target.value)} />
        <TextField label="Hasta" name="endDate" type="date" value={endDate} min={startDate} onChange={(e) => setEndDate(e.target.value)} />
        <TextField label="Hora" name="endTime" type="time" value={endTime} onChange={(e) => setEndTime(e.target.value)} />
      </div>

      <div className="flex flex-col gap-2">
        <TextField label="Motivo" name="reason" value={reason} onChange={(e) => setReason(e.target.value)} placeholder="Ej: Torneo de verano" />
        <div className="flex flex-wrap gap-1.5">
          {reasons.map((r) => (
            <button key={r} type="button" onClick={() => setReason(r)} className="rounded-full border border-line px-2.5 py-1 text-xs text-muted hover:border-brand hover:text-brand">
              {r}
            </button>
          ))}
        </div>
      </div>

      <div className="flex justify-end gap-2 pt-2">
        <Button type="button" variant="ghost" onClick={onClose}>
          Cancelar
        </Button>
        <Button type="submit" loading={create.isPending} disabled={courtIds.length === 0 || !reason.trim()}>
          Bloquear
        </Button>
      </div>
    </form>
  )
}
