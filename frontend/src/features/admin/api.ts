import { api } from '../../shared/api/client'

export interface ClubSummary {
  id: string
  name: string
  slug: string
  isActive: boolean
  chatwootAccountId: number | null
  chatwootInboxId: number | null
  activeCourts: number
  activeOwners: number
  customers: number
  createdAt: string
}

export interface NewClub {
  name: string
  chatwootAccountId: number | null
  chatwootInboxId: number | null
  ownerFullName: string
  ownerEmail: string
  ownerPassword: string
}

export interface ClubUpdate {
  name: string
  isActive: boolean
  chatwootAccountId: number | null
  chatwootInboxId: number | null
}

export interface ClearActivityResult {
  bookings: number
  customers: number
  blocks: number
}

export async function getClubs(): Promise<ClubSummary[]> {
  const { data } = await api.get<ClubSummary[]>('/admin/clubs')
  return data
}

export const createClub = async (club: NewClub) => (await api.post<ClubSummary>('/admin/clubs', club)).data
export const updateClub = async (id: string, club: ClubUpdate) => (await api.put<ClubSummary>(`/admin/clubs/${id}`, club)).data
export const clearActivity = async (id: string, confirmName: string) =>
  (await api.post<ClearActivityResult>(`/admin/clubs/${id}/clear-activity`, { confirmName })).data
