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
