import { useState } from 'react'
import type { ReactNode } from 'react'
import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { Link } from 'react-router-dom'
import { ArrowDownRight, ArrowRight, ArrowUpRight, CalendarClock, LoaderCircle, Minus, RefreshCw } from 'lucide-react'
import { Alert, Button, Card } from '../../shared/components/ui'
import { apiErrorMessage } from '../../shared/api/client'
import { formatMoney, formatMoneyShort, formatPercent as percent, formatPhone, formatTime, parseIsoDate } from '../../shared/format'
import { useActiveClub, useAuthStore } from '../auth/authStore'
import { getDashboard } from './api'
import type { Dashboard, Kpi, Period } from './api'
import { CourtBars, DailyBookingsChart, OccupancyHeatmap } from './charts'

const periods: { value: Period; label: string }[] = [
  { value: 7, label: '7 días' },
  { value: 30, label: '30 días' },
  { value: 90, label: '90 días' },
]

const rangeDate = new Intl.DateTimeFormat('es-AR', { day: 'numeric', month: 'short' })

/** Inicio del panel: cómo viene hoy y cómo viene el club en el período elegido. */
export default function DashboardPage() {
  const club = useActiveClub()
  const user = useAuthStore((s) => s.user)
  const isSuperAdmin = user?.role === 'SuperAdmin'
  const [days, setDays] = useState<Period>(30)

  const dashboard = useQuery({
    queryKey: ['dashboard', club?.id, days],
    queryFn: () => getDashboard(days),
    enabled: club !== null,
    placeholderData: keepPreviousData,
    refetchInterval: 60_000,
  })

  if (!club) {
    return (
      <Card className="mx-auto mt-10 max-w-md text-center">
        <p className="font-semibold text-ink">Elegí un club</p>
        <p className="mt-1 text-sm text-muted">
          {isSuperAdmin ? 'Usá el selector de club para ver sus métricas.' : 'Tu usuario no tiene un club asignado.'}
        </p>
      </Card>
    )
  }

  const data = dashboard.data
  const firstName = user?.fullName.split(' ')[0]

  return (
    <div className="mx-auto max-w-6xl">
      <header className="mb-6 flex flex-wrap items-end justify-between gap-3">
        <div>
          <h1 className="text-2xl font-bold tracking-tight text-ink">Hola{firstName ? `, ${firstName}` : ''}</h1>
          <p className="mt-1 flex items-center gap-2 text-sm text-muted">
            {club.name}
            {data ? ` · ${rangeDate.format(parseIsoDate(data.from))} al ${rangeDate.format(parseIsoDate(data.to))}` : ''}
            {dashboard.isFetching && !dashboard.isPending ? <RefreshCw className="size-3.5 animate-spin" aria-label="Actualizando" /> : null}
          </p>
        </div>
        <div role="group" aria-label="Período" className="inline-flex rounded-lg border border-line bg-surface p-1">
          {periods.map((p) => (
            <button
              key={p.value}
              aria-pressed={p.value === days}
              onClick={() => setDays(p.value)}
              className={`min-h-9 cursor-pointer rounded-md px-3 text-sm font-medium transition-colors ${
                p.value === days ? 'bg-brand text-on-brand' : 'text-muted hover:bg-surface-2 hover:text-ink'
              }`}
            >
              {p.label}
            </button>
          ))}
        </div>
      </header>

      {dashboard.isPending ? (
        <div className="flex justify-center py-20 text-muted">
          <LoaderCircle className="size-6 animate-spin" aria-label="Cargando" />
        </div>
      ) : dashboard.isError ? (
        <Alert>
          {apiErrorMessage(dashboard.error)}{' '}
          <Button variant="ghost" className="ml-2" onClick={() => dashboard.refetch()}>
            Reintentar
          </Button>
        </Alert>
      ) : data ? (
        <Content data={data} />
      ) : null}
    </div>
  )
}

function Content({ data }: { data: Dashboard }) {
  const { today, kpis } = data
  const totalBookings = data.daily.reduce((n, d) => n + d.whatsApp + d.panel, 0)
  const vs = `vs. ${data.days} días anteriores`
  // Sin reservas en el período anterior (club nuevo) los porcentajes no tienen contra qué compararse.
  const hasPrevious = kpis.bookings.previous > 0

  return (
    <div className="flex flex-col gap-5">
      {/* Hoy */}
      <section aria-labelledby="hoy" className="rounded-xl border border-line bg-surface">
        <div className="flex items-center justify-between gap-3 border-b border-line px-5 py-3">
          <h2 id="hoy" className="font-semibold text-ink">Hoy</h2>
          <Link
            to="/agenda"
            className="inline-flex min-h-9 items-center gap-1.5 rounded-lg px-2 text-sm font-medium text-brand hover:bg-brand-soft"
          >
            Ver agenda <ArrowRight className="size-4" aria-hidden />
          </Link>
        </div>
        <dl className="grid grid-cols-2 divide-line lg:grid-cols-4 lg:divide-x [&>div]:px-5 [&>div]:py-4">
          <TodayStat label="Reservas" value={String(today.bookings)} hint={`${percent(today.occupancy)} de los turnos`} />
          <TodayStat
            label="Próximo turno"
            value={today.next ? formatTime(today.next.startTime) : '—'}
            hint={today.next ? `${today.next.customerName} · ${today.next.courtName}` : 'No quedan turnos hoy'}
          />
          <TodayStat label="Cobrado" value={formatMoney(today.paidAmount)} hint="Reservas de hoy ya pagadas" />
          <TodayStat
            label="Por cobrar"
            value={formatMoney(today.pendingAmount)}
            hint="Reservas de hoy sin pagar"
            emphasis={today.pendingAmount > 0 ? 'warn' : undefined}
          />
        </dl>
      </section>

      {/* Indicadores del período */}
      <section aria-label="Indicadores del período" className="grid grid-cols-2 gap-3 md:grid-cols-3 xl:grid-cols-6">
        <KpiCard label="Ocupación" kpi={kpis.occupancy} format={percent} delta={hasPrevious ? 'points' : 'none'} note={vs} />
        <KpiCard label="Reservas" kpi={kpis.bookings} format={(v) => String(v)} delta="relative" note={vs} />
        <KpiCard label="Cobrado" kpi={kpis.paidRevenue} format={formatMoneyShort} delta="relative" note={vs} />
        <KpiCard label="Por WhatsApp" kpi={kpis.whatsAppShare} format={percent} delta={hasPrevious ? 'points' : 'none'} note="de las reservas las tomó el bot" />
        <KpiCard label="Ausencias" kpi={kpis.noShowRate} format={percent} delta={hasPrevious ? 'points' : 'none'} lowerIsBetter note="no vinieron" />
        <KpiCard label="Clientes nuevos" kpi={kpis.newCustomers} format={(v) => String(v)} delta="relative" note={vs} />
      </section>

      <div className="grid gap-5 lg:grid-cols-5">
        <Panel title="Reservas por día" subtitle={`${totalBookings} en el período`} className="lg:col-span-3">
          {totalBookings > 0 ? <DailyBookingsChart data={data.daily} /> : <Empty>Todavía no hay reservas en este período.</Empty>}
        </Panel>
        <Panel title="Ocupación por cancha" subtitle="Turnos reservados sobre los de la grilla" className="lg:col-span-2">
          {data.courts.length > 0 ? <CourtBars courts={data.courts} /> : <Empty>No hay canchas activas.</Empty>}
        </Panel>
      </div>

      <div className="grid gap-5 lg:grid-cols-5">
        <Panel
          title="¿Cuándo se llenan las canchas?"
          subtitle="% de turnos reservados por día y hora de inicio. Útil para ajustar precios pico y promociones."
          className="lg:col-span-3"
        >
          {data.heatmap.length > 0 ? <OccupancyHeatmap cells={data.heatmap} /> : <Empty>No hay turnos en la grilla.</Empty>}
        </Panel>
        <Panel title="Mejores clientes" subtitle="Los que más reservaron en el período" className="lg:col-span-2">
          {data.topCustomers.length > 0 ? (
            <ol className="flex flex-col divide-y divide-line">
              {data.topCustomers.map((c, i) => (
                <li key={c.id} className="flex items-center gap-3 py-2.5 first:pt-0 last:pb-0">
                  <span className="flex size-7 shrink-0 items-center justify-center rounded-full bg-surface-2 text-xs font-semibold text-muted">
                    {i + 1}
                  </span>
                  <div className="min-w-0 flex-1">
                    <p className="truncate text-sm font-medium text-ink">{c.name}</p>
                    <p className="text-xs text-muted">{formatPhone(c.phone)}</p>
                  </div>
                  <div className="text-right">
                    <p className="text-sm font-semibold tabular-nums text-ink">{c.bookings}</p>
                    <p className={`text-xs ${c.noShows > 0 ? 'text-danger' : 'text-muted'}`}>
                      {c.noShows > 0 ? `${c.noShows} ausencia${c.noShows > 1 ? 's' : ''}` : 'reservas'}
                    </p>
                  </div>
                </li>
              ))}
            </ol>
          ) : (
            <Empty>Todavía no hay reservas en este período.</Empty>
          )}
        </Panel>
      </div>
    </div>
  )
}

function TodayStat({ label, value, hint, emphasis }: { label: string; value: string; hint: string; emphasis?: 'warn' }) {
  return (
    <div className="min-w-0">
      <dt className="text-xs font-medium uppercase tracking-wide text-muted">{label}</dt>
      <dd className={`mt-1 text-2xl font-bold tabular-nums tracking-tight ${emphasis === 'warn' ? 'text-warn' : 'text-ink'}`}>{value}</dd>
      <dd className="mt-0.5 line-clamp-2 text-xs text-muted">{hint}</dd>
    </div>
  )
}

function KpiCard({
  label,
  kpi,
  format,
  delta,
  lowerIsBetter = false,
  note,
}: {
  label: string
  kpi: Kpi
  format: (v: number) => string
  /** "points": diferencia en puntos porcentuales; "relative": variación % contra el período anterior; "none": sin comparación. */
  delta: 'points' | 'relative' | 'none'
  lowerIsBetter?: boolean
  note: string
}) {
  const diff = kpi.value - kpi.previous
  let text: string | null
  if (delta === 'none') text = null
  else if (delta === 'points') text = `${Math.abs(Math.round(diff * 100))} pts`
  else text = kpi.previous > 0 ? `${Math.abs(Math.round((diff / kpi.previous) * 100))}%` : null

  const flat = text === null || text === '0%' || text === '0 pts'
  const good = lowerIsBetter ? diff < 0 : diff > 0
  const Icon = flat ? Minus : diff > 0 ? ArrowUpRight : ArrowDownRight
  const tone = flat ? 'text-muted bg-surface-2' : good ? 'text-brand bg-brand-soft' : 'text-danger bg-danger-soft'
  const status = flat ? 'sin cambios' : `${diff > 0 ? 'subió' : 'bajó'} ${text}`

  return (
    <div className="flex flex-col rounded-xl border border-line bg-surface p-4">
      <p className="text-xs font-medium uppercase tracking-wide text-muted">{label}</p>
      <p className="mt-1.5 truncate text-2xl font-bold tabular-nums tracking-tight text-ink">{format(kpi.value)}</p>
      <div className="mt-auto flex flex-wrap items-center gap-x-1.5 gap-y-1 pt-2 text-xs">
        {text === null && kpi.value > 0 ? (
          <span className="text-muted">sin datos previos</span>
        ) : (
          <span className={`inline-flex items-center gap-0.5 rounded-full px-1.5 py-0.5 font-semibold ${tone}`} title={status}>
            <Icon className="size-3.5" aria-hidden />
            <span className="sr-only">{status}</span>
            <span aria-hidden>{flat ? 'igual' : text}</span>
          </span>
        )}
        <span className="text-muted">{note}</span>
      </div>
    </div>
  )
}

function Panel({ title, subtitle, className = '', children }: { title: string; subtitle?: string; className?: string; children: ReactNode }) {
  return (
    <Card className={`min-w-0 ${className}`}>
      <h2 className="font-semibold text-ink">{title}</h2>
      {subtitle ? <p className="mb-4 mt-0.5 text-xs text-muted">{subtitle}</p> : <div className="mb-4" />}
      {children}
    </Card>
  )
}

function Empty({ children }: { children: ReactNode }) {
  return (
    <div className="flex flex-col items-center gap-2 py-10 text-center text-sm text-muted">
      <CalendarClock className="size-6" aria-hidden />
      {children}
    </div>
  )
}
