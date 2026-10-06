import { useQuery, useQueryClient } from '@tanstack/react-query'
import { useAuthStore } from '../auth/authStore'
import { getClubs } from './api'

/** Selector de club para el SuperAdmin: define sobre qué club opera el panel (header X-Club-Id). */
export function ClubSwitcher() {
  const selected = useAuthStore((s) => s.selectedClub)
  const selectClub = useAuthStore((s) => s.selectClub)
  const queryClient = useQueryClient()
  const { data: clubs, isPending } = useQuery({ queryKey: ['admin', 'clubs'], queryFn: getClubs })

  return (
    <label className="flex flex-col gap-1">
      <span className="text-xs font-medium uppercase tracking-wide text-muted">Club</span>
      <select
        value={selected?.id ?? ''}
        disabled={isPending}
        onChange={(e) => {
          const club = clubs?.find((c) => c.id === e.target.value)
          selectClub(club ? { id: club.id, name: club.name } : null)
          // Todo lo cacheado era del club anterior.
          queryClient.removeQueries({ predicate: (q) => q.queryKey[0] !== 'admin' })
        }}
        className="min-h-9 w-full rounded-lg border border-line bg-surface px-2 text-sm text-ink"
      >
        <option value="">{isPending ? 'Cargando…' : 'Elegí un club'}</option>
        {clubs?.map((c) => (
          <option key={c.id} value={c.id}>
            {c.name}
            {c.isActive ? '' : ' (inactivo)'}
          </option>
        ))}
      </select>
    </label>
  )
}
