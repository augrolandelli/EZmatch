import { create } from 'zustand'
import { persist } from 'zustand/middleware'
import type { AuthResponse, UserDto } from './types'

export interface SelectedClub {
  id: string
  name: string
}

interface AuthState {
  user: UserDto | null
  /** Solo en memoria: al recargar se renueva con el refresh token. */
  accessToken: string | null
  /** Persistido para poder renovar la sesión al recargar. */
  refreshToken: string | null
  /** Club que está operando el SuperAdmin (se manda en X-Club-Id). Owner/Staff usan el suyo. */
  selectedClub: SelectedClub | null
  setSession: (session: AuthResponse) => void
  selectClub: (club: SelectedClub | null) => void
  clear: () => void
}

export const useAuthStore = create<AuthState>()(
  persist(
    (set) => ({
      user: null,
      accessToken: null,
      refreshToken: null,
      selectedClub: null,
      setSession: (session) =>
        set({ user: session.user, accessToken: session.accessToken, refreshToken: session.refreshToken }),
      selectClub: (club) => set({ selectedClub: club }),
      clear: () => set({ user: null, accessToken: null, refreshToken: null, selectedClub: null }),
    }),
    {
      name: 'ezmatch-auth',
      partialize: (state) => ({ user: state.user, refreshToken: state.refreshToken, selectedClub: state.selectedClub }),
    },
  ),
)

/** Club sobre el que trabaja el panel: el propio, o el elegido por el SuperAdmin. */
export function useActiveClub(): SelectedClub | null {
  const user = useAuthStore((s) => s.user)
  const selected = useAuthStore((s) => s.selectedClub)
  if (!user) return null
  if (user.role === 'SuperAdmin') return selected
  return user.clubId ? { id: user.clubId, name: user.clubName ?? '' } : null
}
