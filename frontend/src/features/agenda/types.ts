export type Sport = 'Padel' | 'Futbol5' | 'Futbol7' | 'Futbol11' | 'Tenis'
export type BookingStatus = 'Confirmed' | 'Completed' | 'Cancelled' | 'NoShow'
export type PaymentStatus = 'Unpaid' | 'Paid'
export type BookingSource = 'WhatsApp' | 'Panel'
export type AgendaItemKind = 'Free' | 'Booking' | 'Block' | 'Busy'

export interface AgendaBooking {
  id: string
  customerId: string
  customerName: string
  customerPhone: string
  customerIsBlocked: boolean
  price: number
  status: BookingStatus
  paymentStatus: PaymentStatus
  source: BookingSource
  createdAt: string
  /** Turno fijo que generó la reserva (null = reserva suelta). */
  fixedBookingId: string | null
}

export interface AgendaItem {
  kind: AgendaItemKind
  startTime: string
  endTime: string
  /** Minutos desde las 00:00 del día (puede pasar de 1440 si cruza la medianoche). */
  startMinute: number
  durationMinutes: number
  startsAt: string
  endsAt: string
  price: number | null
  isPast: boolean
  booking: AgendaBooking | null
  blockReason: string | null
}

export interface AgendaCourt {
  id: string
  name: string
  sport: Sport
  isCovered: boolean
  items: AgendaItem[]
}

export interface AgendaSummary {
  bookings: number
  freeSlots: number
  noShows: number
  paidAmount: number
  pendingAmount: number
}

export interface Agenda {
  date: string
  today: string
  courts: AgendaCourt[]
  summary: AgendaSummary
}

export interface Booking {
  id: string
  courtId: string
  courtName: string
  sport: Sport
  date: string
  startTime: string
  endTime: string
  price: number
  status: BookingStatus
  paymentStatus: PaymentStatus
  source: BookingSource
  customerName: string
  customerPhone: string
}

export interface CustomerSummary {
  id: string
  name: string
  phone: string
  isBlocked: boolean
}

export const sportLabel: Record<Sport, string> = {
  Padel: 'Pádel',
  Futbol5: 'Fútbol 5',
  Futbol7: 'Fútbol 7',
  Futbol11: 'Fútbol 11',
  Tenis: 'Tenis',
}

export type WeekDay = 'Monday' | 'Tuesday' | 'Wednesday' | 'Thursday' | 'Friday' | 'Saturday' | 'Sunday'

export interface WeekBooking {
  id: string
  courtName: string
  startTime: string
  endTime: string
  customerName: string
  status: BookingStatus
  paymentStatus: PaymentStatus
  source: BookingSource
  isFixed: boolean
}

export interface WeekAgendaDay {
  date: string
  bookings: WeekBooking[]
  freeSlots: number
  slots: number
}

export interface WeekAgenda {
  start: string
  today: string
  days: WeekAgendaDay[]
}

export interface FixedBooking {
  id: string
  courtId: string
  courtName: string
  sport: Sport
  dayOfWeek: WeekDay
  startTime: string
  endTime: string | null
  customerId: string
  customerName: string
  customerPhone: string
  startsOn: string
  endsOn: string | null
  notes: string | null
  isActive: boolean
  nextDate: string | null
  /** Próximas fechas que no se pudieron reservar (cancha ocupada o bloqueada). */
  missingDates: string[]
}

export interface NewFixedBooking {
  courtId: string
  dayOfWeek: WeekDay
  startTime: string
  phone: string
  customerName: string | null
  startsOn?: string | null
  notes?: string | null
}
