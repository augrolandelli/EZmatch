import { useState } from 'react'
import { formatPercent as percent, parseIsoDate } from '../../shared/format'
import type { CourtStats, DailyBookings, HeatCell, WeekDay } from './api'

const shortDate = new Intl.DateTimeFormat('es-AR', { day: 'numeric', month: 'short' })
const longDate = new Intl.DateTimeFormat('es-AR', { weekday: 'short', day: 'numeric', month: 'short' })


function LegendDot({ className, label }: { className: string; label: string }) {
  return (
    <span className="inline-flex items-center gap-1.5 text-xs text-muted">
      <span className={`size-2.5 rounded-sm ${className}`} aria-hidden />
      {label}
    </span>
  )
}

/** Escala "linda" para el eje: 1, 2, 5, 10, 20, 50… */
function niceMax(value: number): number {
  if (value <= 4) return Math.max(value, 1)
  const pow = 10 ** Math.floor(Math.log10(value))
  return [1, 2, 5, 10].map((m) => m * pow).find((n) => n >= value) ?? value
}

/** Reservas por día, apiladas por origen (WhatsApp abajo, mostrador arriba). */
export function DailyBookingsChart({ data }: { data: DailyBookings[] }) {
  const [hover, setHover] = useState<number | null>(null)
  const max = niceMax(Math.max(0, ...data.map((d) => d.whatsApp + d.panel)))
  const gap = data.length > 40 ? 'gap-px' : 'gap-[2px] sm:gap-1'
  const ticks = [0, Math.round(data.length / 2), data.length - 1].filter((v, i, a) => a.indexOf(v) === i)
  const h = hover !== null ? data[hover] : null

  return (
    <div>
      <div className="mb-3 flex flex-wrap gap-4">
        <LegendDot className="bg-series-whatsapp" label="WhatsApp (bot)" />
        <LegendDot className="bg-series-panel" label="Mostrador" />
      </div>
      <div className="relative flex gap-2">
        {/* Eje Y: máximo y mitad */}
        <div className="flex h-40 w-6 shrink-0 flex-col justify-between text-right text-[11px] tabular-nums text-muted" aria-hidden>
          <span className="-translate-y-1/2">{max}</span>
          <span>{max / 2 === Math.round(max / 2) ? max / 2 : ''}</span>
          <span className="translate-y-1/2">0</span>
        </div>
        <div className="relative h-40 min-w-0 flex-1">
          <div className="pointer-events-none absolute inset-0 flex flex-col justify-between" aria-hidden>
            <div className="border-t border-dashed border-line" />
            <div className="border-t border-dashed border-line" />
            <div className="border-t border-line" />
          </div>
          <div className={`absolute inset-0 flex items-end ${gap}`} onMouseLeave={() => setHover(null)} aria-hidden>
            {data.map((d, i) => {
              const total = d.whatsApp + d.panel
              return (
                <div
                  key={d.date}
                  className={`flex h-full min-w-0 flex-1 flex-col justify-end rounded-t ${hover === i ? 'bg-surface-2' : ''}`}
                  onMouseEnter={() => setHover(i)}
                >
                  <div className="flex flex-col gap-[2px]" style={{ height: `${(total / max) * 100}%` }}>
                    {d.panel > 0 ? <div className="rounded-t bg-series-panel" style={{ flexGrow: d.panel }} /> : null}
                    {d.whatsApp > 0 ? (
                      <div className={`bg-series-whatsapp ${d.panel === 0 ? 'rounded-t' : ''}`} style={{ flexGrow: d.whatsApp }} />
                    ) : null}
                  </div>
                </div>
              )
            })}
          </div>
          {h && hover !== null ? (
            <div
              role="status"
              className="pointer-events-none absolute -top-2 z-10 w-40 -translate-y-full rounded-lg border border-line bg-surface px-3 py-2 text-xs shadow-lg"
              style={{ left: `clamp(0px, calc(${((hover + 0.5) / data.length) * 100}% - 5rem), calc(100% - 10rem))` }}
            >
              <p className="mb-1 font-semibold text-ink first-letter:uppercase">{longDate.format(parseIsoDate(h.date))}</p>
              <p className="flex justify-between text-muted">
                <LegendDot className="bg-series-whatsapp" label="WhatsApp" />
                <span className="tabular-nums text-ink">{h.whatsApp}</span>
              </p>
              <p className="flex justify-between text-muted">
                <LegendDot className="bg-series-panel" label="Mostrador" />
                <span className="tabular-nums text-ink">{h.panel}</span>
              </p>
              <p className="mt-1 flex justify-between border-t border-line pt-1 font-medium text-ink">
                Total <span className="tabular-nums">{h.whatsApp + h.panel}</span>
              </p>
            </div>
          ) : null}
        </div>
      </div>
      <div className="relative ml-8 mt-1.5 h-4 text-[11px] text-muted" aria-hidden>
        {ticks.map((i) => (
          <span
            key={i}
            className="absolute whitespace-nowrap"
            style={{
              left: `${((i + 0.5) / data.length) * 100}%`,
              transform: i === 0 ? 'translateX(-25%)' : i === data.length - 1 ? 'translateX(-75%)' : 'translateX(-50%)',
            }}
          >
            {shortDate.format(parseIsoDate(data[i].date))}
          </span>
        ))}
      </div>
      <table className="sr-only">
        <caption>Reservas por día y origen</caption>
        <thead>
          <tr>
            <th>Día</th>
            <th>WhatsApp</th>
            <th>Mostrador</th>
          </tr>
        </thead>
        <tbody>
          {data.map((d) => (
            <tr key={d.date}>
              <td>{shortDate.format(parseIsoDate(d.date))}</td>
              <td>{d.whatsApp}</td>
              <td>{d.panel}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}

const weekDays: { key: WeekDay; label: string }[] = [
  { key: 'Monday', label: 'Lun' },
  { key: 'Tuesday', label: 'Mar' },
  { key: 'Wednesday', label: 'Mié' },
  { key: 'Thursday', label: 'Jue' },
  { key: 'Friday', label: 'Vie' },
  { key: 'Saturday', label: 'Sáb' },
  { key: 'Sunday', label: 'Dom' },
]

const fullDay: Record<WeekDay, string> = {
  Monday: 'lunes',
  Tuesday: 'martes',
  Wednesday: 'miércoles',
  Thursday: 'jueves',
  Friday: 'viernes',
  Saturday: 'sábado',
  Sunday: 'domingo',
}

/** Rangos discretos de la rampa azul (más fácil de leer que un degradé continuo). */
const heatBins = [
  { upTo: 0, className: 'bg-surface-2 text-muted', label: '0%' },
  { upTo: 0.25, className: 'bg-heat-1 text-ink', label: 'hasta 25%' },
  { upTo: 0.5, className: 'bg-heat-2 text-ink', label: '25–50%' },
  { upTo: 0.75, className: 'bg-heat-3 text-[var(--heat-ink-strong)]', label: '50–75%' },
  { upTo: 1, className: 'bg-heat-4 text-[var(--heat-ink-strong)]', label: 'más de 75%' },
]

const binFor = (ratio: number) => heatBins.find((b) => ratio <= b.upTo) ?? heatBins[heatBins.length - 1]

/** Mapa de calor día de la semana × hora de inicio: qué % de los turnos de la grilla se reservó. */
export function OccupancyHeatmap({ cells }: { cells: HeatCell[] }) {
  const byKey = new Map(cells.map((c) => [`${c.dayOfWeek}-${c.hour}`, c]))
  const hours = [...new Set(cells.map((c) => c.hour))].sort((a, b) => a - b)
  const days = weekDays.filter((d) => cells.some((c) => c.dayOfWeek === d.key))

  return (
    <div>
      <div className="overflow-x-auto pb-1">
        <table className="w-full border-separate border-spacing-[2px] text-center text-[11px]">
          <caption className="sr-only">Porcentaje de turnos reservados por día y hora</caption>
          <thead>
            <tr>
              <th className="w-8" />
              {hours.map((h) => (
                <th key={h} scope="col" className="min-w-7 font-normal tabular-nums text-muted">
                  {h}
                </th>
              ))}
            </tr>
          </thead>
          <tbody>
            {days.map((d) => (
              <tr key={d.key}>
                <th scope="row" className="pr-1 text-left font-medium text-muted">
                  {d.label}
                </th>
                {hours.map((h) => {
                  const c = byKey.get(`${d.key}-${h}`)
                  if (!c || c.slots === 0) {
                    return <td key={h} className="h-8 rounded-md text-muted/50" aria-label="Sin turnos">·</td>
                  }
                  const ratio = c.booked / c.slots
                  return (
                    <td
                      key={h}
                      title={`${fullDay[d.key]} ${h} h: ${c.booked} de ${c.slots} turnos (${percent(ratio)})`}
                      className={`h-8 rounded-md font-semibold tabular-nums ${binFor(ratio).className}`}
                    >
                      {Math.round(ratio * 100)}
                    </td>
                  )
                })}
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      <div className="mt-3 flex flex-wrap items-center gap-x-3 gap-y-1.5 text-xs text-muted">
        <span>% reservado:</span>
        {heatBins.map((b) => (
          <span key={b.label} className="inline-flex items-center gap-1.5">
            <span className={`size-3 rounded-sm ${b.className.split(' ')[0]}`} aria-hidden />
            {b.label}
          </span>
        ))}
      </div>
    </div>
  )
}

/** Ocupación de cada cancha en el período (barras horizontales, de mayor a menor). */
export function CourtBars({ courts }: { courts: CourtStats[] }) {
  const sorted = [...courts].sort((a, b) => b.occupancy - a.occupancy)
  return (
    <ul className="flex flex-col gap-3.5">
      {sorted.map((c) => (
        <li key={c.id}>
          <div className="mb-1 flex items-baseline justify-between gap-2 text-sm">
            <span className="truncate font-medium text-ink">{c.name}</span>
            <span className="shrink-0 tabular-nums text-muted">
              <span className="font-semibold text-ink">{percent(c.occupancy)}</span> · {c.booked}/{c.slots}
            </span>
          </div>
          <div className="h-2 overflow-hidden rounded-full bg-surface-2" aria-hidden>
            <div className="h-full rounded-full bg-heat-3" style={{ width: `${Math.min(c.occupancy, 1) * 100}%` }} />
          </div>
        </li>
      ))}
    </ul>
  )
}
