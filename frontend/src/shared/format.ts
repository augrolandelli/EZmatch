const money = new Intl.NumberFormat('es-AR', { style: 'currency', currency: 'ARS', maximumFractionDigits: 0 })

/** $30.000 */
export function formatMoney(amount: number): string {
  return money.format(amount).replace(/\s/g, '')
}

/** "20:00:00" → "20:00" */
export function formatTime(time: string): string {
  return time.slice(0, 5)
}

/** Fecha ISO "2026-10-06" como fecha local (sin corrimiento por zona horaria). */
export function parseIsoDate(iso: string): Date {
  const [y, m, d] = iso.split('-').map(Number)
  return new Date(y, m - 1, d)
}

export function toIsoDate(date: Date): string {
  const pad = (n: number) => String(n).padStart(2, '0')
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`
}

export function addDays(iso: string, days: number): string {
  const date = parseIsoDate(iso)
  date.setDate(date.getDate() + days)
  return toIsoDate(date)
}

/** "martes 6 de octubre" */
export function formatLongDate(iso: string): string {
  return new Intl.DateTimeFormat('es-AR', { weekday: 'long', day: 'numeric', month: 'long' }).format(parseIsoDate(iso))
}

/** "+5493415550001" → "341 555-0001" (argentinos); otros, tal cual. */
export function formatPhone(phone: string): string {
  const ar = /^\+549(\d{10})$/.exec(phone)
  if (!ar) return phone
  const n = ar[1]
  // Áreas de 2 dígitos (AMBA) o 3; la mayoría de las consultas son de 3.
  const area = n.startsWith('11') ? 2 : 3
  const rest = n.slice(area)
  return `${n.slice(0, area)} ${rest.slice(0, rest.length - 4)}-${rest.slice(-4)}`
}

/** Link para abrir el chat de WhatsApp con ese número. */
export function whatsappLink(phone: string): string {
  return `https://wa.me/${phone.replace(/\D/g, '')}`
}
