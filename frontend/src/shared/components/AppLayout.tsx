import { NavLink, Outlet } from 'react-router-dom'
import { CalendarDays, LogOut, Settings, UserRound, Users } from 'lucide-react'
import type { LucideIcon } from 'lucide-react'
import { useActiveClub, useAuthStore } from '../../features/auth/authStore'
import { roleLabel } from '../../features/auth/types'
import { useLogout } from '../../features/auth/useLogout'
import { ClubSwitcher } from '../../features/admin/ClubSwitcher'
import { Logo } from './ui'

interface NavItem {
  to: string
  label: string
  icon: LucideIcon
  /** Roles que ven el ítem (todos si no se indica). */
  roles?: string[]
}

const allNavItems: NavItem[] = [
  { to: '/', label: 'Agenda', icon: CalendarDays },
  { to: '/clientes', label: 'Clientes', icon: Users },
  { to: '/configuracion', label: 'Configuración', icon: Settings, roles: ['Owner', 'SuperAdmin'] },
  { to: '/cuenta', label: 'Mi cuenta', icon: UserRound },
]

export function AppLayout() {
  const user = useAuthStore((s) => s.user)
  const club = useActiveClub()
  const onLogout = useLogout()
  const isSuperAdmin = user?.role === 'SuperAdmin'
  const navItems = allNavItems.filter((i) => !i.roles || (user && i.roles.includes(user.role)))

  return (
    <div className="min-h-dvh md:flex">
      {/* Barra lateral (computadora) */}
      <aside className="hidden w-64 shrink-0 flex-col border-r border-line bg-surface md:flex">
        <div className="flex flex-col gap-4 border-b border-line p-5">
          <Logo />
          {isSuperAdmin ? <ClubSwitcher /> : <p className="truncate text-sm font-semibold text-ink">{club?.name}</p>}
        </div>
        <nav className="flex flex-1 flex-col gap-1 p-3" aria-label="Principal">
          {navItems.map((item) => (
            <NavLink
              key={item.to}
              to={item.to}
              end={item.to === '/'}
              className={({ isActive }) =>
                `flex items-center gap-3 rounded-lg px-3 py-2.5 text-sm font-medium transition-colors ${
                  isActive ? 'bg-brand-soft text-brand' : 'text-muted hover:bg-surface-2 hover:text-ink'
                }`
              }
            >
              <item.icon className="size-4" aria-hidden />
              {item.label}
            </NavLink>
          ))}
        </nav>
        <div className="border-t border-line p-4">
          <p className="truncate text-sm font-medium text-ink">{user?.fullName}</p>
          <p className="truncate text-xs text-muted">{user ? roleLabel[user.role] : ''}</p>
          <button
            onClick={onLogout}
            className="mt-3 inline-flex items-center gap-2 text-sm text-muted hover:text-ink"
          >
            <LogOut className="size-4" aria-hidden /> Salir
          </button>
        </div>
      </aside>

      {/* Barra superior (celular) */}
      <header className="sticky top-0 z-10 flex items-center justify-between gap-3 border-b border-line bg-surface/95 px-4 py-3 backdrop-blur md:hidden">
        <Logo withText={false} />
        <div className="min-w-0 flex-1">{isSuperAdmin ? <ClubSwitcher /> : <p className="truncate text-sm font-semibold">{club?.name}</p>}</div>
      </header>

      <main className="min-w-0 flex-1 px-4 pb-24 pt-6 md:px-8 md:pb-10 md:pt-8">
        <Outlet />
      </main>

      {/* Navegación inferior (celular) */}
      <nav
        className="fixed inset-x-0 bottom-0 z-10 flex border-t border-line bg-surface pb-[env(safe-area-inset-bottom)] md:hidden"
        aria-label="Principal"
      >
        {navItems.map((item) => (
          <NavLink
            key={item.to}
            to={item.to}
            end={item.to === '/'}
            className={({ isActive }) =>
              `flex flex-1 flex-col items-center gap-1 py-2.5 text-xs font-medium ${isActive ? 'text-brand' : 'text-muted'}`
            }
          >
            <item.icon className="size-5" aria-hidden />
            {item.label}
          </NavLink>
        ))}
      </nav>
    </div>
  )
}
