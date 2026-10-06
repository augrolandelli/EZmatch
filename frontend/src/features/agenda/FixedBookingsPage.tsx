import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { CircleAlert, LoaderCircle, Plus, Repeat } from 'lucide-react'
import { Modal } from '../../shared/components/Modal'
import { Alert, Button, Card, SelectField, TextField } from '../../shared/components/ui'
import { apiErrorMessage } from '../../shared/api/client'
import { formatPhone, formatShortDay as fmt, formatTime, toIsoDate } from '../../shared/format'
import { useActiveClub } from '../auth/authStore'
import { AgendaViews } from './AgendaViews'
import { createFixedBooking, endFixedBooking, getAgenda, getFixedBookings } from './api'
import type { FixedBooking, WeekDay } from './types'
import { nextDateOf, weekDays } from './weekDays'

const dayLabel = (d: WeekDay) => weekDays.find((w) => w.value === d)?.label ?? d

/** Turnos fijos: "todos los martes a las 20". Se reservan solos las próximas semanas. */
export default function FixedBookingsPage() {
  const club = useActiveClub()
  const [creating, setCreating] = useState(false)
  const [ending, setEnding] = useState<FixedBooking | null>(null)
  const list = useQuery({ queryKey: ['fixed-bookings', club?.id], queryFn: getFixedBookings, enabled: club !== null })

  if (!club) {
    return (
      <Card className="mx-auto mt-10 max-w-md text-center">
        <p className="font-semibold text-ink">Elegí un club</p>
      </Card>
    )
  }

  const active = list.data?.filter((f) => f.isActive) ?? []
  const ended = list.data?.filter((f) => !f.isActive) ?? []

  return (
    <>
      <AgendaViews />
      <header className="mb-5 flex flex-wrap items-end justify-between gap-3">
        <div>
          <h1 className="text-2xl font-bold tracking-tight text-ink">Turnos fijos</h1>
          <p className="mt-1 max-w-xl text-sm text-muted">
            Los que juegan siempre el mismo día y hora. Se reservan solos las próximas 5 semanas; cancelar un día suelto no
            afecta a los demás.
          </p>
        </div>
        <Button onClick={() => setCreating(true)}>
          <Plus className="size-4" aria-hidden /> Nuevo turno fijo
        </Button>
      </header>

      {list.isPending ? (
        <div className="flex justify-center py-20">
          <LoaderCircle className="size-6 animate-spin text-muted" aria-label="Cargando" />
        </div>
      ) : list.isError ? (
        <Alert>{apiErrorMessage(list.error)}</Alert>
      ) : active.length === 0 ? (
        <Card className="flex flex-col items-center gap-2 py-10 text-center">
          <Repeat className="size-6 text-muted" aria-hidden />
          <p className="font-semibold text-ink">Todavía no hay turnos fijos</p>
          <p className="max-w-sm text-sm text-muted">
            Cargalos acá, o desde la agenda: abrí una reserva y tocá "Repetir todas las semanas".
          </p>
        </Card>
      ) : (
        <div className="flex flex-col gap-5">
          {weekDays
            .filter((d) => active.some((f) => f.dayOfWeek === d.value))
            .map((d) => (
              <section key={d.value}>
                <h2 className="mb-2 text-sm font-semibold uppercase tracking-wide text-muted">{d.label}</h2>
                <ul className="grid gap-2 md:grid-cols-2 xl:grid-cols-3">
                  {active
                    .filter((f) => f.dayOfWeek === d.value)
                    .map((f) => (
                      <FixedCard key={f.id} item={f} onEnd={() => setEnding(f)} />
                    ))}
                </ul>
              </section>
            ))}
        </div>
      )}

      {ended.length > 0 ? (
        <details className="mt-8">
          <summary className="cursor-pointer text-sm font-medium text-muted hover:text-ink">Dados de baja ({ended.length})</summary>
          <ul className="mt-3 flex flex-col divide-y divide-line rounded-xl border border-line bg-surface">
            {ended.map((f) => (
              <li key={f.id} className="flex flex-wrap items-center justify-between gap-2 px-4 py-2.5 text-sm">
                <span className="text-ink">
                  {dayLabel(f.dayOfWeek)} {formatTime(f.startTime)} · {f.courtName} · {f.customerName}
                </span>
                <span className="text-xs text-muted">{f.endsOn ? `Hasta el ${fmt(f.endsOn)}` : ''}</span>
              </li>
            ))}
          </ul>
        </details>
      ) : null}

      <NewFixedBookingDialog open={creating} onClose={() => setCreating(false)} />
      <EndDialog item={ending} onClose={() => setEnding(null)} />
    </>
  )
}

function FixedCard({ item, onEnd }: { item: FixedBooking; onEnd: () => void }) {
  return (
    <li className="flex flex-col gap-2 rounded-xl border border-line bg-surface p-4">
      <div className="flex items-start justify-between gap-2">
        <div className="min-w-0">
          <p className="font-semibold text-ink">
            {formatTime(item.startTime)}
            {item.endTime ? ` a ${formatTime(item.endTime)}` : ''} · {item.courtName}
          </p>
          <p className="truncate text-sm text-ink">{item.customerName}</p>
          <p className="text-xs text-muted">{formatPhone(item.customerPhone)}</p>
        </div>
        <Button variant="dangerGhost" className="shrink-0 px-2.5" onClick={onEnd}>
          Dar de baja
        </Button>
      </div>
      {item.notes ? <p className="text-xs text-muted">{item.notes}</p> : null}
      <p className="text-xs text-muted">{item.nextDate ? `Próximo: ${fmt(item.nextDate)}` : 'Sin próximas fechas reservadas'}</p>
      {item.missingDates.length > 0 ? (
        <p className="flex items-start gap-1.5 rounded-lg bg-warn-soft px-2.5 py-1.5 text-xs text-warn">
          <CircleAlert className="mt-px size-3.5 shrink-0" aria-hidden />
          <span>
            Sin lugar el {item.missingDates.map(fmt).join(', ')}: la cancha está ocupada o bloqueada. Se reserva solo si se libera.
          </span>
        </p>
      ) : null}
    </li>
  )
}

function NewFixedBookingDialog({ open, onClose }: { open: boolean; onClose: () => void }) {
  return (
    <Modal open={open} onClose={onClose} title="Nuevo turno fijo">
      {open ? <NewFixedBookingForm onClose={onClose} /> : null}
    </Modal>
  )
}

function NewFixedBookingForm({ onClose }: { onClose: () => void }) {
  const club = useActiveClub()
  const queryClient = useQueryClient()
  const [day, setDay] = useState<WeekDay>('Monday')
  const [courtId, setCourtId] = useState('')
  const [startTime, setStartTime] = useState('')
  const [phone, setPhone] = useState('')
  const [name, setName] = useState('')
  const [notes, setNotes] = useState('')
  const [today] = useState(() => toIsoDate(new Date()))

  // Los horarios salen de la agenda del próximo día elegido: la grilla real de cada cancha.
  const sampleDate = nextDateOf(day, today)
  const agenda = useQuery({ queryKey: ['agenda', club?.id, sampleDate], queryFn: () => getAgenda(sampleDate) })
  const courts = agenda.data?.courts ?? []
  const court = courts.find((c) => c.id === courtId) ?? courts[0]
  const times = court
    ? [...new Set(court.items.filter((i) => i.kind !== 'Block').map((i) => i.startTime))].sort()
    : []
  const time = times.includes(startTime) ? startTime : (times[0] ?? '')

  const save = useMutation({
    mutationFn: () =>
      createFixedBooking({
        courtId: court!.id,
        dayOfWeek: day,
        startTime: time,
        phone,
        customerName: name.trim() || null,
        notes: notes.trim() || null,
      }),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['fixed-bookings', club?.id] })
      void queryClient.invalidateQueries({ queryKey: ['agenda', club?.id] })
      onClose()
    },
  })

  return (
    <form
      noValidate
      onSubmit={(e) => {
        e.preventDefault()
        save.mutate()
      }}
      className="flex flex-col gap-4"
    >
      {save.isError ? <Alert>{apiErrorMessage(save.error)}</Alert> : null}
      <div className="grid grid-cols-2 gap-3">
        <SelectField label="Día" name="day" value={day} onChange={(e) => setDay(e.target.value as WeekDay)}>
          {weekDays.map((d) => (
            <option key={d.value} value={d.value}>
              {d.label}
            </option>
          ))}
        </SelectField>
        <SelectField label="Hora" name="startTime" value={time} onChange={(e) => setStartTime(e.target.value)} disabled={times.length === 0}>
          {times.length === 0 ? <option value="">{agenda.isPending ? 'Cargando…' : 'Sin turnos'}</option> : null}
          {times.map((t) => (
            <option key={t} value={t}>
              {formatTime(t)}
            </option>
          ))}
        </SelectField>
      </div>
      <SelectField label="Cancha" name="courtId" value={court?.id ?? ''} onChange={(e) => setCourtId(e.target.value)}>
        {courts.map((c) => (
          <option key={c.id} value={c.id}>
            {c.name}
          </option>
        ))}
      </SelectField>
      <TextField
        label="Teléfono"
        name="phone"
        type="tel"
        inputMode="tel"
        autoComplete="off"
        hint="Código de área + número, sin 0 ni 15. Ej: 341 555 0001"
        value={phone}
        onChange={(e) => setPhone(e.target.value)}
      />
      <TextField
        label="Nombre"
        name="customerName"
        autoComplete="off"
        hint="Si ya es cliente del club, alcanza con el teléfono."
        value={name}
        onChange={(e) => setName(e.target.value)}
      />
      <TextField label="Notas (opcional)" name="notes" placeholder="Ej: grupo de los martes" value={notes} onChange={(e) => setNotes(e.target.value)} />
      <p className="text-xs text-muted">Se reservan desde esta semana. Si algún día la cancha ya está ocupada, te avisamos.</p>
      <div className="flex justify-end gap-2 pt-2">
        <Button type="button" variant="ghost" onClick={onClose}>
          Cancelar
        </Button>
        <Button type="submit" loading={save.isPending} disabled={!court || !time || phone.trim().length === 0}>
          Crear turno fijo
        </Button>
      </div>
    </form>
  )
}

function EndDialog({ item, onClose }: { item: FixedBooking | null; onClose: () => void }) {
  const club = useActiveClub()
  const queryClient = useQueryClient()
  const end = useMutation({
    mutationFn: () => endFixedBooking(item!.id),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['fixed-bookings', club?.id] })
      void queryClient.invalidateQueries({ queryKey: ['agenda', club?.id] })
      onClose()
    },
  })
  return (
    <Modal open={item !== null} onClose={onClose} title="Dar de baja el turno fijo">
      {item ? (
        <div className="flex flex-col gap-4">
          <p className="text-sm text-ink">
            {item.customerName} deja de tener el {dayLabel(item.dayOfWeek).toLowerCase()} a las {formatTime(item.startTime)} en{' '}
            {item.courtName}. Se cancelan las reservas que ya estaban hechas para las próximas semanas.
          </p>
          {end.isError ? <Alert>{apiErrorMessage(end.error)}</Alert> : null}
          <div className="flex justify-end gap-2">
            <Button variant="ghost" onClick={onClose}>
              Volver
            </Button>
            <Button variant="danger" loading={end.isPending} onClick={() => end.mutate()}>
              Dar de baja
            </Button>
          </div>
        </div>
      ) : null}
    </Modal>
  )
}
