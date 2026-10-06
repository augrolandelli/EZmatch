import { CalendarDays } from 'lucide-react'
import { useActiveClub, useAuthStore } from '../auth/authStore'
import { Card, PageHeader } from '../../shared/components/ui'

// Placeholder de la etapa 3.1: la grilla del día llega en la etapa 3.2.
export default function AgendaPage() {
  const club = useActiveClub()
  const isSuperAdmin = useAuthStore((s) => s.user?.role === 'SuperAdmin')

  if (!club) {
    return (
      <Card className="mx-auto mt-10 max-w-md text-center">
        <p className="font-semibold text-ink">Elegí un club</p>
        <p className="mt-1 text-sm text-muted">
          {isSuperAdmin ? 'Usá el selector de club para operar sobre uno.' : 'Tu usuario no tiene un club asignado.'}
        </p>
      </Card>
    )
  }

  return (
    <>
      <PageHeader title="Agenda" subtitle={club.name} />
      <Card className="flex flex-col items-center gap-3 py-12 text-center">
        <CalendarDays className="size-10 text-brand" aria-hidden />
        <p className="font-semibold text-ink">Acá va a estar la grilla de turnos del día</p>
        <p className="max-w-sm text-sm text-muted">
          Canchas, reservas del bot y del mostrador, pagos y ausencias. Es lo próximo que se construye.
        </p>
      </Card>
    </>
  )
}
