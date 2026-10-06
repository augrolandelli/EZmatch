import { api } from '../../shared/api/client'

export interface ClubSummary {
  id: string
  name: string
  slug: string
  isActive: boolean
  chatwootInboxId: number | null
}

export async function getClubs(): Promise<ClubSummary[]> {
  const { data } = await api.get<ClubSummary[]>('/admin/clubs')
  return data
}
