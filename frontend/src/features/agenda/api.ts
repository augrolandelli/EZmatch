import { api } from '../../shared/api/client'
import type { Agenda, Booking, CustomerSummary } from './types'

export async function getAgenda(date: string | null): Promise<Agenda> {
  const { data } = await api.get<Agenda>('/agenda', { params: date ? { date } : undefined })
  return data
}

export interface NewBooking {
  courtId: string
  date: string
  startTime: string
  phone: string
  customerName: string | null
}

export async function createBooking(booking: NewBooking): Promise<Booking> {
  const { data } = await api.post<Booking>('/bookings', booking)
  return data
}

export async function cancelBooking(id: string, reason: string | null): Promise<Booking> {
  const { data } = await api.post<Booking>(`/bookings/${id}/cancel`, { reason })
  return data
}

export async function setPayment(id: string, paid: boolean): Promise<Booking> {
  const { data } = await api.put<Booking>(`/bookings/${id}/payment`, { paid })
  return data
}

export async function setNoShow(id: string, noShow: boolean): Promise<Booking> {
  const { data } = await api.put<Booking>(`/bookings/${id}/no-show`, { noShow })
  return data
}

export async function searchCustomers(search: string): Promise<CustomerSummary[]> {
  const { data } = await api.get<CustomerSummary[]>('/customers', { params: { search } })
  return data
}
