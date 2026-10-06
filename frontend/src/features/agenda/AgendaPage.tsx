import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { useSearchParams } from 'react-router-dom'
import { ChevronLeft, ChevronRight, LoaderCircle, RefreshCw } from 'lucide-react'
import { useActiveClub, useAuthStore } from '../auth/authStore'
import { Alert, Button, Card } from '../../shared/components/ui'
import { apiErrorMessage } from '../../shared/api/client'
import { addDays, formatLongDate, formatMoney } from '../../shared/format'
import { getAgenda } from './api'
import { AgendaGrid } from './AgendaGrid'
import { NewBookingDialog } from './NewBookingDialog'
import { BookingDialog } from './BookingDialog'
import type { AgendaCourt, AgendaItem } from './types'

type Selection = { court: AgendaCourt; item: AgendaItem } | null

/** Agenda del día: grilla por cancha, resumen y acciones del mostrador. Se actualiza sola (reservas del bot). */
export default function AgendaPage() {
  const club = useActiveClub()
  const isSuperAdmin = useAuthStore((s) => s.user?.role === 'SuperAdmin')
  const [params, setParams] = useSearchParams()
  const requestedDate = params.get('fecha')
  const [newSlot, setNewSlot] = useState<Selection>(null)
  const [selected, setSelected] = useState<Selection>(null)

  const agenda = useQuery({
    queryKey: ['agenda', club?.id, requestedDate],
    queryFn: () => getAgenda(requestedDate),
    enabled: club !== null,
    // Las reservas del bot entran solas: refrescar seguido.
    refetchInterval: 20_000,
  })

  if (!club) {
    return (
      <Card className="mx-auto mt-10 max-w-md text-center">
        <p className="font-semibold text-ink">Elegí un club</p>
        <p className="mt-1 text-sm text-muted">
          {isSuperAdmin ? 'Usá el selector de club para operar sobre uno.' : 'Tu usuario no tiene un club asignado.'}
        </p>
      </Card>
    )
  }

  const data = agenda.data
  const date = data?.date ?? requestedDate
  const isToday = data !== undefined && data.date === data.today
  const goTo = (iso: string | null) => setParams(iso && iso !== data?.today ? { fecha: iso } : {}, { replace: true })

  return (
    <>
      <header className="mb-5 flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="text-2xl font-bold tracking-tight text-ink first-letter:uppercase">
            {date ? formatLongDate(date) : 'Agenda'}
          </h1>
          <p className="mt-0.5 flex items-center gap-2 text-sm text-muted">
            {isToday ? 'Hoy · ' : ''}
            {club.name}
            {agenda.isFetching && !agenda.isPending ? <RefreshCw className="size-3.5 animate-spin" aria-label="Actualizando" /> : null}
          </p>
        </div>

        <div className="flex items-center gap-2">
          <Button variant="secondary" className="px-2.5" aria-label="Día anterior" disabled={!date} onClick={() => date && goTo(addDays(date, -1))}>
            <ChevronLeft className="size-4" />
          </Button>
          <Button variant="secondary" disabled={isToday} onClick={() => goTo(null)}>
            Hoy
          </Button>
          <Button variant="secondary" className="px-2.5" aria-label="Día siguiente" disabled={!date} onClick={() => date && goTo(addDays(date, 1))}>
            <ChevronRight className="size-4" />
          </Button>
          <input
            type="date"
            aria-label="Elegir fecha"
            value={date ?? ''}
            onChange={(e) => e.target.value && goTo(e.target.value)}
            className="min-h-10 rounded-lg border border-line bg-surface px-2 text-sm text-ink"
          />
        </div>
      </header>

      {data ? (
        <dl className="mb-5 grid grid-cols-2 gap-3 sm:grid-cols-4">
          <Stat label="Reservas" value={String(data.summary.bookings)} />
          <Stat label={isToday ? 'Libres (resto del día)' : 'Turnos libres'} value={String(data.summary.freeSlots)} />
          <Stat label="Cobrado" value={formatMoney(data.summary.paidAmount)} tone="brand" />
          <Stat
            label={data.summary.noShows > 0 ? `Pendiente · ${data.summary.noShows} no vino` : 'Pendiente de cobro'}
            value={formatMoney(data.summary.pendingAmount)}
            tone={data.summary.pendingAmount > 0 ? 'warn' : undefined}
          />
        </dl>
      ) : null}

      {agenda.isPending ? (
        <div className="flex justify-center py-20">
          <LoaderCircle className="size-6 animate-spin text-muted" aria-label="Cargando agenda" />
        </div>
      ) : agenda.isError ? (
        <Alert>{apiErrorMessage(agenda.error)}</Alert>
      ) : data && data.courts.length === 0 ? (
        <Card className="text-center text-sm text-muted">Este club todavía no tiene canchas cargadas.</Card>
      ) : data ? (
        <AgendaGrid
          courts={data.courts}
          isToday={isToday}
          onFreeClick={(court, item) => setNewSlot({ court, item })}
          onBookingClick={(court, item) => setSelected({ court, item })}
        />
      ) : null}

      {data ? (
        <>
          <NewBookingDialog slot={newSlot} date={data.date} onClose={() => setNewSlot(null)} />
          <BookingDialog selected={selected} date={data.date} now={agenda.dataUpdatedAt} onClose={() => setSelected(null)} />
        </>
      ) : null}
    </>
  )
}

function Stat({ label, value, tone }: { label: string; value: string; tone?: 'brand' | 'warn' }) {
  const color = tone === 'brand' ? 'text-brand' : tone === 'warn' ? 'text-warn' : 'text-ink'
  return (
    <div className="rounded-xl border border-line bg-surface px-4 py-3">
      <dt className="truncate text-xs font-medium text-muted">{label}</dt>
      <dd className={`mt-1 text-xl font-bold tabular-nums ${color}`}>{value}</dd>
    </div>
  )
}
