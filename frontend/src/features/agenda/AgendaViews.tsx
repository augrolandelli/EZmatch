import { NavLink } from 'react-router-dom'
import { CalendarDays, CalendarRange, Repeat } from 'lucide-react'

const views = [
  { to: '/agenda', label: 'Día', icon: CalendarDays },
  { to: '/agenda/semana', label: 'Semana', icon: CalendarRange },
  { to: '/agenda/fijos', label: 'Turnos fijos', icon: Repeat },
]

/** Selector de vista de la agenda. */
export function AgendaViews() {
  return (
    <nav aria-label="Vista de la agenda" className="mb-5 inline-flex rounded-lg border border-line bg-surface p-1">
      {views.map((v) => (
        <NavLink
          key={v.to}
          to={v.to}
          end
          className={({ isActive }) =>
            `inline-flex min-h-9 items-center gap-1.5 rounded-md px-3 text-sm font-medium transition-colors ${
              isActive ? 'bg-brand text-on-brand' : 'text-muted hover:bg-surface-2 hover:text-ink'
            }`
          }
        >
          <v.icon className="size-4" aria-hidden />
          {v.label}
        </NavLink>
      ))}
    </nav>
  )
}
