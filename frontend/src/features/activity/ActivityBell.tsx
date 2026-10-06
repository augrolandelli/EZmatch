import { useEffect, useRef, useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { Link } from 'react-router-dom'
import { Bell, CalendarPlus, CalendarX } from 'lucide-react'
import { api } from '../../shared/api/client'
import { formatShortDay, formatTime } from '../../shared/format'
import { useActiveClub } from '../auth/authStore'

interface Activity {
  id: string
  kind: 'Booked' | 'Cancelled'
  at: string
  bookingId: string
  customerName: string
  courtName: string
  date: string
  startTime: string
}

const getActivity = async () => (await api.get<Activity[]>('/activity')).data

const ago = new Intl.RelativeTimeFormat('es-AR', { numeric: 'auto' })

function timeAgo(iso: string): string {
  const minutes = Math.round((Date.parse(iso) - Date.now()) / 60_000)
  if (minutes > -60) return ago.format(Math.min(minutes, 0), 'minute')
  if (minutes > -60 * 24) return ago.format(Math.round(minutes / 60), 'hour')
  return ago.format(Math.round(minutes / 1440), 'day')
}

const seenKey = (clubId: string) => `ezmatch-activity-seen-${clubId}`

function readSeen(clubId: string): number {
  try {
    return Number(localStorage.getItem(seenKey(clubId))) || 0
  } catch {
    return 0
  }
}

/**
 * Avisos de lo que hizo el bot (reservas y cancelaciones por WhatsApp). Se actualiza solo; el número indica
 * cuántos llegaron desde la última vez que se abrió. Al llegar algo nuevo refresca la agenda.
 */
export function ActivityBell({ align = 'left' }: { align?: 'left' | 'right' }) {
  const club = useActiveClub()
  const queryClient = useQueryClient()
  const [open, setOpen] = useState(false)
  // Última apertura del panel, por club (se persiste en localStorage).
  const [seenByClub, setSeenByClub] = useState<Record<string, number>>({})
  const ref = useRef<HTMLDivElement>(null)

  const activity = useQuery({
    queryKey: ['activity', club?.id],
    queryFn: getActivity,
    enabled: club !== null,
    refetchInterval: 30_000,
  })

  // Algo nuevo del bot: la agenda y el inicio tienen que reflejarlo.
  const newest = activity.data?.[0]?.id
  useEffect(() => {
    if (newest) {
      void queryClient.invalidateQueries({ queryKey: ['agenda', club?.id] })
      void queryClient.invalidateQueries({ queryKey: ['dashboard', club?.id] })
    }
  }, [newest, club?.id, queryClient])

  useEffect(() => {
    if (!open) return
    const onClick = (e: MouseEvent) => {
      if (ref.current && !ref.current.contains(e.target as Node)) setOpen(false)
    }
    const onKey = (e: KeyboardEvent) => e.key === 'Escape' && setOpen(false)
    document.addEventListener('mousedown', onClick)
    document.addEventListener('keydown', onKey)
    return () => {
      document.removeEventListener('mousedown', onClick)
      document.removeEventListener('keydown', onKey)
    }
  }, [open])

  if (!club) return null
  const items = activity.data ?? []
  const seen = seenByClub[club.id] ?? readSeen(club.id)
  const unread = items.filter((a) => Date.parse(a.at) > seen).length

  const toggle = () => {
    if (!open) {
      const now = Date.now()
      try {
        localStorage.setItem(seenKey(club.id), String(now))
      } catch {
        // Sin almacenamiento: el contador se reinicia al recargar.
      }
      setSeenByClub((s) => ({ ...s, [club.id]: now }))
      setOpen(true)
    } else {
      setOpen(false)
    }
  }

  return (
    <div ref={ref} className="relative">
      <button
        type="button"
        onClick={toggle}
        aria-expanded={open}
        aria-label={unread > 0 ? `Avisos del bot: ${unread} nuevos` : 'Avisos del bot'}
        className="relative inline-flex size-10 cursor-pointer items-center justify-center rounded-lg text-muted hover:bg-surface-2 hover:text-ink"
      >
        <Bell className="size-5" aria-hidden />
        {unread > 0 ? (
          <span className="absolute right-1 top-1 inline-flex min-w-4 items-center justify-center rounded-full bg-danger px-1 text-[10px] font-bold leading-4 text-white">
            {unread > 9 ? '9+' : unread}
          </span>
        ) : null}
      </button>
      {open ? (
        <div
          className={`absolute top-12 z-30 w-80 max-w-[calc(100vw-2rem)] overflow-hidden rounded-xl border border-line bg-surface shadow-xl ${
            align === 'right' ? 'right-0' : 'left-0'
          }`}
        >
          <p className="border-b border-line px-4 py-2.5 text-sm font-semibold text-ink">Lo que hizo el bot</p>
          {items.length === 0 ? (
            <p className="px-4 py-6 text-center text-sm text-muted">Sin novedades en los últimos 7 días.</p>
          ) : (
            <ul className="max-h-96 divide-y divide-line overflow-y-auto">
              {items.map((a) => {
                const booked = a.kind === 'Booked'
                const Icon = booked ? CalendarPlus : CalendarX
                return (
                  <li key={a.id}>
                    <Link
                      to={`/agenda?fecha=${a.date}`}
                      onClick={() => setOpen(false)}
                      className="flex gap-3 px-4 py-2.5 hover:bg-surface-2"
                    >
                      <Icon className={`mt-0.5 size-4 shrink-0 ${booked ? 'text-brand' : 'text-danger'}`} aria-hidden />
                      <div className="min-w-0 text-sm">
                        <p className="text-ink">
                          <span className="font-semibold">{a.customerName}</span> {booked ? 'reservó' : 'canceló'}
                        </p>
                        <p className="text-xs text-muted first-letter:uppercase">
                          {formatShortDay(a.date)} {formatTime(a.startTime)} · {a.courtName} · {timeAgo(a.at)}
                        </p>
                      </div>
                    </Link>
                  </li>
                )
              })}
            </ul>
          )}
        </div>
      ) : null}
    </div>
  )
}
