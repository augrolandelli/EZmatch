import { api } from '../../shared/api/client'
import type { Booking, Sport } from '../agenda/types'

export type DayOfWeek = 'Monday' | 'Tuesday' | 'Wednesday' | 'Thursday' | 'Friday' | 'Saturday' | 'Sunday'

/** Lunes primero, como se piensa la semana en un club. */
export const weekDays: { value: DayOfWeek; short: string; long: string }[] = [
  { value: 'Monday', short: 'Lun', long: 'Lunes' },
  { value: 'Tuesday', short: 'Mar', long: 'Martes' },
  { value: 'Wednesday', short: 'Mié', long: 'Miércoles' },
  { value: 'Thursday', short: 'Jue', long: 'Jueves' },
  { value: 'Friday', short: 'Vie', long: 'Viernes' },
  { value: 'Saturday', short: 'Sáb', long: 'Sábado' },
  { value: 'Sunday', short: 'Dom', long: 'Domingo' },
]

export interface ClubSettings {
  id: string
  name: string
  address: string | null
  phone: string | null
  timeZone: string
  assistantName: string | null
  botInstructions: string | null
  botShowsPrices: boolean
  cancellationMinHours: number
  minLeadMinutes: number
  bookingHorizonDays: number
  maxActiveBookingsPerCustomer: number
  chatwootAccountId: number | null
  chatwootInboxId: number | null
}

export type ClubSettingsInput = Omit<ClubSettings, 'id' | 'timeZone' | 'chatwootAccountId' | 'chatwootInboxId'>

export interface Court {
  id: string
  name: string
  sport: Sport
  isCovered: boolean
  sortOrder: number
  isActive: boolean
  slotCount: number
}

export interface CourtInput {
  name: string
  sport: Sport
  isCovered: boolean
  isActive: boolean
}

export interface SlotTemplate {
  id: string
  dayOfWeek: DayOfWeek
  startTime: string
  durationMinutes: number
  price: number
}

export type SlotInput = Omit<SlotTemplate, 'id'>

export interface GenerateInput {
  days: DayOfWeek[]
  firstStart: string
  lastStart: string
  durationMinutes: number
  everyMinutes: number | null
  price: number
  peakFrom: string | null
  peakPrice: number | null
}

export interface Block {
  id: string
  courtId: string
  courtName: string
  startsAt: string
  endsAt: string
  startDate: string
  startTime: string
  endDate: string
  endTime: string
  reason: string
}

export interface BlockInput {
  courtIds: string[]
  startDate: string
  startTime: string
  endDate: string
  endTime: string
  reason: string
}

export interface BlockConflicts {
  bookings: Booking[]
}

export const getSettings = async () => (await api.get<ClubSettings>('/club')).data
export const updateSettings = async (input: ClubSettingsInput) => (await api.put<ClubSettings>('/club', input)).data

export const getCourts = async () => (await api.get<Court[]>('/courts')).data
export const createCourt = async (input: CourtInput) => (await api.post<Court>('/courts', input)).data
export const updateCourt = async (id: string, input: CourtInput) => (await api.put<Court>(`/courts/${id}`, input)).data
export const reorderCourts = async (courtIds: string[]) => (await api.put<Court[]>('/courts/order', { courtIds })).data

export const getSlots = async (courtId: string) => (await api.get<SlotTemplate[]>(`/courts/${courtId}/slots`)).data
export const replaceSlots = async (courtId: string, slots: SlotInput[]) =>
  (await api.put<SlotTemplate[]>(`/courts/${courtId}/slots`, { slots })).data
export const generateSlots = async (courtId: string, input: GenerateInput) =>
  (await api.post<SlotTemplate[]>(`/courts/${courtId}/slots/generate`, input)).data
export const copySlots = async (courtId: string, courtIds: string[]) => {
  await api.post(`/courts/${courtId}/slots/copy`, { courtIds })
}

export const getBlocks = async () => (await api.get<Block[]>('/blocks')).data
export const createBlock = async (input: BlockInput) => (await api.post<Block[]>('/blocks', input)).data
export const deleteBlock = async (id: string) => {
  await api.delete(`/blocks/${id}`)
}
