import { api } from '../../shared/api/client'
import type { Booking } from '../agenda/types'

export type CustomerSort = 'Name' | 'Recent' | 'NoShows'

export interface CustomerListItem {
  id: string
  name: string
  phone: string
  isBlocked: boolean
  notes: string | null
  bookings: number
  noShows: number
  lastBookingAt: string | null
  nextBookingAt: string | null
  createdAt: string
}

export interface CustomerPage {
  items: CustomerListItem[]
  total: number
  page: number
  pageSize: number
}

export interface CustomerDetail {
  id: string
  name: string
  phone: string
  isBlocked: boolean
  notes: string | null
  createdAt: string
  bookings: number
  noShows: number
  cancellations: number
  paidAmount: number
  history: Booking[]
}

export const PAGE_SIZE = 50

export const getDirectory = async (search: string, sort: CustomerSort, page: number) =>
  (await api.get<CustomerPage>('/customers/directory', { params: { search: search || undefined, sort, page, pageSize: PAGE_SIZE } })).data

export const getCustomer = async (id: string) => (await api.get<CustomerDetail>(`/customers/${id}`)).data

export const updateCustomer = async (id: string, name: string, notes: string | null) =>
  (await api.put<CustomerDetail>(`/customers/${id}`, { name, notes })).data

export const setBlocked = async (id: string, blocked: boolean) =>
  (await api.put<CustomerDetail>(`/customers/${id}/blocked`, { blocked })).data
