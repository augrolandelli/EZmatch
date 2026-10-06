import { api } from '../../shared/api/client'

export type Period = 7 | 30 | 90

export interface Kpi {
  value: number
  previous: number
}

export interface DashboardKpis {
  occupancy: Kpi
  bookings: Kpi
  paidRevenue: Kpi
  whatsAppShare: Kpi
  noShowRate: Kpi
  newCustomers: Kpi
}

export interface DashboardToday {
  bookings: number
  occupancy: number
  paidAmount: number
  pendingAmount: number
  next: { startTime: string; courtName: string; customerName: string } | null
}

export interface DailyBookings {
  date: string
  whatsApp: number
  panel: number
}

export type WeekDay = 'Monday' | 'Tuesday' | 'Wednesday' | 'Thursday' | 'Friday' | 'Saturday' | 'Sunday'

export interface HeatCell {
  dayOfWeek: WeekDay
  hour: number
  slots: number
  booked: number
}

export interface CourtStats {
  id: string
  name: string
  slots: number
  booked: number
  occupancy: number
}

export interface TopCustomer {
  id: string
  name: string
  phone: string
  bookings: number
  noShows: number
}

export interface Dashboard {
  days: number
  from: string
  to: string
  today: DashboardToday
  kpis: DashboardKpis
  daily: DailyBookings[]
  heatmap: HeatCell[]
  courts: CourtStats[]
  topCustomers: TopCustomer[]
}

export const getDashboard = async (days: Period) => (await api.get<Dashboard>('/dashboard', { params: { days } })).data
