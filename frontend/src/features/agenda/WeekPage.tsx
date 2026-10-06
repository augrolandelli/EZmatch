import { useQuery } from '@tanstack/react-query'
import { Link, useSearchParams } from 'react-router-dom'
import { ChevronLeft, ChevronRight, LoaderCircle, MessageCircle, Repeat } from 'lucide-react'
import { Alert, Button, Card } from '../../shared/components/ui'
import { apiErrorMessage } from '../../shared/api/client'
import { addDays, formatTime, parseIsoDate } from '../../shared/format'
import { useActiveClub } from '../auth/authStore'
import { AgendaViews } from './AgendaViews'
import { getWeek } from './api'
import type { WeekAgendaDay, WeekBooking } from './types'

const dayName = new Intl.DateTimeFormat('es-AR', { weekday: 'long' })
const dayMonth = new Intl.DateTimeFormat('es-AR', { day: 'numeric', month: 'short' })

/** Semana de un vistazo: por día, las reservas en orden y cuántos turnos quedan libres. */
export default function WeekPage() {
  const club = useActiveClub()
  const [params, setParams] = useSearchParams()
  const requested = params.get('semana')

  const week = useQuery({
    queryKey: ['agenda', club?.id, 'week', requested],
    queryFn: () => getWeek(requested),
    enabled: club !== null,
    refetchInterval: 30_000,
  })

  if (!club) {
    return (
      <Card className="mx-auto mt-10 max-w-md text-center">
        <p className="font-semibold text-ink">Elegí un club</p>
      </Card>
    )
  }

  const data = week.data
  const goTo = (iso: string | null) => setParams(iso ? { semana: iso } : {}, { replace: true })
  const isCurrent = data !== undefined && data.today >= data.start && data.today <= addDays(data.start, 6)

  return (
    <>
      <AgendaViews />
      <header className="mb-5 flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="text-2xl font-bold tracking-tight text-ink">
            {data ? `Semana del ${dayMonth.format(parseIsoDate(data.start))} al ${dayMonth.format(parseIsoDate(addDays(data.start, 6)))}` : 'Semana'}
          </h1>
          <p className="mt-0.5 text-sm text-muted">{club.name} · tocá un día para ver su agenda</p>
        </div>
        <div className="flex items-center gap-2">
          <Button variant="secondary" className="px-2.5" aria-label="Semana anterior" disabled={!data} onClick={() => data && goTo(addDays(data.start, -7))}>
            <ChevronLeft className="size-4" />
          </Button>
          <Button variant="secondary" disabled={isCurrent} onClick={() => goTo(null)}>
            Esta semana
          </Button>
          <Button variant="secondary" className="px-2.5" aria-label="Semana siguiente" disabled={!data} onClick={() => data && goTo(addDays(data.start, 7))}>
            <ChevronRight className="size-4" />
          </Button>
        </div>
      </header>

      {week.isPending ? (
        <div className="flex justify-center py-20">
          <LoaderCircle className="size-6 animate-spin text-muted" aria-label="Cargando semana" />
        </div>
      ) : week.isError ? (
        <Alert>{apiErrorMessage(week.error)}</Alert>
      ) : data ? (
        <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-7">
          {data.days.map((d) => (
            <DayColumn key={d.date} day={d} isToday={d.date === data.today} isPast={d.date < data.today} />
          ))}
        </div>
      ) : null}
    </>
  )
}

function DayColumn({ day, isToday, isPast }: { day: WeekAgendaDay; isToday: boolean; isPast: boolean }) {
  const active = day.bookings.filter((b) => b.status !== 'Cancelled')
  const occupancy = day.slots > 0 ? Math.round((active.length / day.slots) * 100) : 0
  return (
    <section
      className={`flex min-w-0 flex-col rounded-xl border bg-surface ${isToday ? 'border-brand ring-1 ring-brand' : 'border-line'} ${isPast ? 'opacity-70' : ''}`}
    >
      <Link
        to={`/agenda?fecha=${day.date}`}
        className="flex items-baseline justify-between gap-2 rounded-t-xl border-b border-line px-3 py-2.5 hover:bg-surface-2"
      >
        <span className="font-semibold capitalize text-ink">
          {dayName.format(parseIsoDate(day.date))}
          {isToday ? <span className="ml-1.5 text-xs font-medium normal-case text-brand">hoy</span> : null}
        </span>
        <span className="text-xs text-muted">{dayMonth.format(parseIsoDate(day.date))}</span>
      </Link>
      <div className="px-3 pt-2.5">
        <div className="flex justify-between text-xs text-muted">
          <span>
            <span className="font-semibold text-ink">{active.length}</span> reservas
          </span>
          <span>{isPast ? `${occupancy}%` : `${day.freeSlots} libres`}</span>
        </div>
        <div className="mt-1.5 h-1.5 overflow-hidden rounded-full bg-surface-2" aria-hidden>
          <div className="h-full rounded-full bg-brand" style={{ width: `${Math.min(occupancy, 100)}%` }} />
        </div>
      </div>
      <ul className="flex flex-col gap-1.5 p-3">
        {active.length === 0 ? <li className="py-3 text-center text-xs text-muted">Sin reservas</li> : null}
        {active.map((b) => (
          <BookingRow key={b.id} booking={b} />
        ))}
      </ul>
    </section>
  )
}

function BookingRow({ booking }: { booking: WeekBooking }) {
  const noShow = booking.status === 'NoShow'
  return (
    <li className={`rounded-lg px-2 py-1.5 text-xs ${noShow ? 'bg-danger-soft' : 'bg-surface-2'}`}>
      <div className="flex items-center gap-1.5">
        <span className="font-semibold tabular-nums text-ink">{formatTime(booking.startTime)}</span>
        <span className="ml-auto flex shrink-0 items-center gap-1 text-muted">
          {booking.isFixed ? <Repeat className="size-3.5" aria-label="Turno fijo" /> : null}
          {booking.source === 'WhatsApp' ? <MessageCircle className="size-3.5" aria-label="Reservó por WhatsApp" /> : null}
        </span>
      </div>
      <p className={`truncate ${noShow ? 'text-danger line-through' : 'text-ink'}`}>{booking.customerName}</p>
      <p className="truncate text-muted">{booking.courtName}</p>
    </li>
  )
}
