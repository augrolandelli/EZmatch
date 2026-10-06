import { parseIsoDate, toIsoDate } from '../../shared/format'
import type { WeekDay } from './types'

/** Lunes primero, como se piensa la semana en el club. */
export const weekDays: { value: WeekDay; label: string; short: string }[] = [
  { value: 'Monday', label: 'Lunes', short: 'Lun' },
  { value: 'Tuesday', label: 'Martes', short: 'Mar' },
  { value: 'Wednesday', label: 'Miércoles', short: 'Mié' },
  { value: 'Thursday', label: 'Jueves', short: 'Jue' },
  { value: 'Friday', label: 'Viernes', short: 'Vie' },
  { value: 'Saturday', label: 'Sábado', short: 'Sáb' },
  { value: 'Sunday', label: 'Domingo', short: 'Dom' },
]

const byJsDay: WeekDay[] = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday']

/** "2026-10-06" → "Tuesday" */
export function weekDayOf(iso: string): WeekDay {
  return byJsDay[parseIsoDate(iso).getDay()]
}

/** Próxima fecha (desde hoy inclusive) que cae en ese día de la semana. */
export function nextDateOf(day: WeekDay, today: string): string {
  const from = parseIsoDate(today)
  const diff = (byJsDay.indexOf(day) - from.getDay() + 7) % 7
  from.setDate(from.getDate() + diff)
  return toIsoDate(from)
}
