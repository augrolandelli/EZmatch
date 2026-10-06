import { useEffect, useRef, useState } from 'react'
import type { CSSProperties, ReactNode } from 'react'
import { Ban, CircleAlert, MessageCircle, Plus, Store, Umbrella } from 'lucide-react'
import { formatMoney, formatTime } from '../../shared/format'
import type { AgendaCourt, AgendaItem } from './types'
import { sportLabel } from './types'

/** Píxeles por minuto: un turno de 90 min mide ~100 px. */
const SCALE = 1.1

function minutesNow(): number {
  const d = new Date()
  return d.getHours() * 60 + d.getMinutes()
}

/**
 * Agenda tipo calendario: una columna por cancha y cada turno ubicado por su minuto de inicio.
 * Así conviven turnos de distinta duración (pádel 90, fútbol 60) y los que cruzan la medianoche.
 */
export function AgendaGrid({
  courts,
  isToday,
  onFreeClick,
  onBookingClick,
}: {
  courts: AgendaCourt[]
  isToday: boolean
  onFreeClick: (court: AgendaCourt, item: AgendaItem) => void
  onBookingClick: (court: AgendaCourt, item: AgendaItem) => void
}) {
  const scrollRef = useRef<HTMLDivElement>(null)
  const [now, setNow] = useState(minutesNow)

  const items = courts.flatMap((c) => c.items)
  const first = Math.floor(Math.min(...items.map((i) => i.startMinute)) / 60) * 60
  const last = Math.ceil(Math.max(...items.map((i) => i.startMinute + i.durationMinutes)) / 60) * 60
  const hours = Array.from({ length: (last - first) / 60 + 1 }, (_, i) => first + i * 60)
  const top = (minute: number) => (minute - first) * SCALE
  const showNow = isToday && now >= first && now <= last

  // Actualizar la línea de "ahora" cada minuto.
  useEffect(() => {
    if (!isToday) return
    const id = setInterval(() => setNow(minutesNow()), 60_000)
    return () => clearInterval(id)
  }, [isToday])

  // Al abrir el día de hoy, ir a la hora actual (con una hora de margen arriba).
  useEffect(() => {
    if (isToday && scrollRef.current) scrollRef.current.scrollTop = Math.max(0, top(now - 60))
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [isToday, first])

  if (items.length === 0) {
    return <p className="rounded-xl border border-line bg-surface p-8 text-center text-sm text-muted">No hay turnos cargados para este día.</p>
  }

  return (
    <div ref={scrollRef} className="max-h-[calc(100dvh-16rem)] overflow-auto rounded-xl border border-line bg-surface md:max-h-[calc(100dvh-14rem)]">
      <div className="flex min-w-max">
        {/* Eje de horas */}
        <div className="sticky left-0 z-20 w-14 shrink-0 border-r border-line bg-surface">
          <div className="sticky top-0 z-10 h-14 border-b border-line bg-surface" />
          <div className="relative" style={{ height: top(last) }}>
            {showNow ? <span className="absolute right-0 size-2 -translate-y-1/2 rounded-full bg-danger" style={{ top: top(now) }} aria-label="Ahora" /> : null}
            {hours.map((m) => (
              <span key={m} className="absolute right-2 -translate-y-1/2 text-xs tabular-nums text-muted" style={{ top: top(m) }}>
                {String(Math.floor(m / 60) % 24).padStart(2, '0')}:00
              </span>
            ))}
          </div>
        </div>

        {courts.map((court) => (
          <div key={court.id} className="w-44 shrink-0 border-r border-line last:border-r-0 sm:w-52 lg:min-w-52 lg:flex-1">
            <div className="sticky top-0 z-10 flex h-14 flex-col justify-center border-b border-line bg-surface px-3">
              <p className="flex items-center gap-1.5 truncate text-sm font-semibold text-ink">
                {court.name}
                {court.isCovered ? <Umbrella className="size-3.5 text-muted" aria-label="Techada" /> : null}
              </p>
              <p className="text-xs text-muted">{sportLabel[court.sport]}</p>
            </div>
            <div className="relative" style={{ height: top(last) }}>
              {hours.map((m) => (
                <div key={m} className="absolute inset-x-0 border-t border-line/70" style={{ top: top(m) }} />
              ))}
              {showNow ? <div className="pointer-events-none absolute inset-x-0 z-[5] h-0.5 bg-danger" style={{ top: top(now) }} aria-hidden /> : null}
              {court.items.map((item) => (
                <ItemCard
                  key={`${item.kind}-${item.startMinute}-${item.booking?.id ?? ''}`}
                  item={item}
                  style={{ top: top(item.startMinute) + 2, height: item.durationMinutes * SCALE - 4 }}
                  onClick={() => (item.kind === 'Free' ? onFreeClick(court, item) : item.kind === 'Booking' ? onBookingClick(court, item) : undefined)}
                />
              ))}
            </div>
          </div>
        ))}
      </div>

    </div>
  )
}

function ItemCard({ item, style, onClick }: { item: AgendaItem; style: CSSProperties; onClick: () => void }) {
  const time = `${formatTime(item.startTime)}–${formatTime(item.endTime)}`
  const base = 'absolute inset-x-1.5 overflow-hidden rounded-lg px-2.5 py-1.5 text-left text-xs transition'

  if (item.kind === 'Free') {
    return (
      <button
        onClick={onClick}
        style={style}
        className={`${base} group border border-dashed border-line text-muted hover:border-brand hover:bg-brand-soft hover:text-brand ${item.isPast ? 'opacity-50' : ''}`}
        aria-label={`Reservar ${time}`}
      >
        <Plus className="absolute right-2 top-1.5 size-3.5 opacity-0 transition group-hover:opacity-100" aria-hidden />
        <span className="block font-medium tabular-nums">{time}</span>
        {item.price != null ? <span className="block">{formatMoney(item.price)}</span> : null}
      </button>
    )
  }

  if (item.kind === 'Booking' && item.booking) {
    const b = item.booking
    const noShow = b.status === 'NoShow'
    const paid = b.paymentStatus === 'Paid'
    return (
      <button
        onClick={onClick}
        style={style}
        className={`${base} border-l-4 hover:brightness-95 ${
          noShow ? 'border-danger bg-danger-soft' : 'border-brand bg-brand-soft'
        } ${item.isPast && !noShow ? 'opacity-75' : ''}`}
      >
        <span className="flex items-center gap-1 truncate font-semibold text-ink">
          {b.customerIsBlocked ? <CircleAlert className="size-3.5 shrink-0 text-danger" aria-label="Cliente bloqueado" /> : null}
          <span className="truncate">{b.customerName}</span>
        </span>
        <span className="flex items-center gap-1 tabular-nums text-muted">
          {b.source === 'WhatsApp' ? (
            <MessageCircle className="size-3 shrink-0" aria-label="Reservó por WhatsApp" />
          ) : (
            <Store className="size-3 shrink-0" aria-label="Reserva del mostrador" />
          )}
          {time}
        </span>
        <span className="mt-1 flex flex-wrap gap-1">
          {noShow ? (
            <Badge tone="danger">No vino</Badge>
          ) : paid ? (
            <Badge tone="success">Pagado</Badge>
          ) : (
            <Badge tone="warn">Debe {formatMoney(b.price)}</Badge>
          )}
        </span>
      </button>
    )
  }

  if (item.kind === 'Block') {
    return (
      <div
        style={{
          ...style,
          backgroundImage: 'repeating-linear-gradient(135deg, var(--surface-2) 0 8px, transparent 8px 16px)',
        }}
        className={`${base} border border-line text-muted`}
      >
        <span className="flex items-center gap-1 font-medium">
          <Ban className="size-3.5" aria-hidden /> {item.blockReason}
        </span>
        <span className="tabular-nums">{time}</span>
      </div>
    )
  }

  return (
    <div style={style} className={`${base} border border-line bg-surface-2 text-muted`}>
      <span className="font-medium">Ocupado</span>
      <span className="block text-[11px]">Turno del día anterior</span>
    </div>
  )
}

export function Badge({ tone, children }: { tone: 'success' | 'warn' | 'danger' | 'neutral'; children: ReactNode }) {
  const styles = {
    success: 'bg-brand text-on-brand',
    warn: 'bg-warn-soft text-warn',
    danger: 'bg-danger text-white',
    neutral: 'bg-surface-2 text-muted',
  }[tone]
  return <span className={`inline-flex items-center rounded-full px-1.5 py-0.5 text-[11px] font-semibold leading-none ${styles}`}>{children}</span>
}
