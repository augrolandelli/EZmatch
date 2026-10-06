import { useEffect, useState } from 'react'
import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { ChevronLeft, ChevronRight, CircleAlert, Search } from 'lucide-react'
import { Alert, Button, Card, PageHeader } from '../../shared/components/ui'
import { apiErrorMessage } from '../../shared/api/client'
import { formatPhone } from '../../shared/format'
import { useActiveClub } from '../auth/authStore'
import { Badge } from '../agenda/AgendaGrid'
import { PAGE_SIZE, getDirectory } from './api'
import type { CustomerListItem, CustomerSort } from './api'
import { CustomerDialog } from './CustomerDialog'

const shortDate = new Intl.DateTimeFormat('es-AR', { day: 'numeric', month: 'short', timeZone: 'America/Argentina/Buenos_Aires' })

function visit(iso: string | null): string {
  return iso ? shortDate.format(new Date(iso)) : '—'
}

/** Directorio de clientes del club: búsqueda, orden, ausencias y acceso al detalle. */
export default function CustomersPage() {
  const club = useActiveClub()
  const [search, setSearch] = useState('')
  const [debounced, setDebounced] = useState('')
  const [sort, setSort] = useState<CustomerSort>('Name')
  const [page, setPage] = useState(1)
  const [selected, setSelected] = useState<string | null>(null)

  useEffect(() => {
    const id = setTimeout(() => {
      setDebounced(search.trim())
      setPage(1)
    }, 300)
    return () => clearTimeout(id)
  }, [search])

  const directory = useQuery({
    queryKey: ['customers', 'directory', club?.id, debounced, sort, page],
    queryFn: () => getDirectory(debounced, sort, page),
    enabled: club !== null,
    placeholderData: keepPreviousData,
  })

  if (!club) {
    return (
      <Card className="mx-auto mt-10 max-w-md text-center">
        <p className="font-semibold text-ink">Elegí un club</p>
      </Card>
    )
  }

  const data = directory.data
  const pages = data ? Math.max(1, Math.ceil(data.total / PAGE_SIZE)) : 1

  return (
    <>
      <PageHeader title="Clientes" subtitle={data ? `${data.total} ${data.total === 1 ? 'cliente' : 'clientes'} · ${club.name}` : club.name} />

      <div className="mb-4 flex flex-wrap gap-3">
        <label className="relative min-w-60 flex-1">
          <span className="sr-only">Buscar</span>
          <Search className="pointer-events-none absolute left-3 top-1/2 size-4 -translate-y-1/2 text-muted" aria-hidden />
          <input
            type="search"
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            placeholder="Buscar por nombre o teléfono"
            className="min-h-10 w-full rounded-lg border border-line bg-surface pl-9 pr-3 text-sm text-ink placeholder:text-muted focus:border-brand focus:outline-none focus:ring-2 focus:ring-brand/25"
          />
        </label>
        <select
          aria-label="Ordenar"
          value={sort}
          onChange={(e) => {
            setSort(e.target.value as CustomerSort)
            setPage(1)
          }}
          className="min-h-10 rounded-lg border border-line bg-surface px-2 text-sm text-ink"
        >
          <option value="Name">Por nombre</option>
          <option value="Recent">Vinieron hace poco</option>
          <option value="NoShows">Más ausencias</option>
        </select>
      </div>

      {directory.isPending ? (
        <p className="text-sm text-muted">Cargando…</p>
      ) : directory.isError ? (
        <Alert>{apiErrorMessage(directory.error)}</Alert>
      ) : data && data.items.length === 0 ? (
        <Card className="text-center text-sm text-muted">
          {debounced ? 'No hay clientes que coincidan con la búsqueda.' : 'Todavía no hay clientes: se crean solos con la primera reserva.'}
        </Card>
      ) : data ? (
        <>
          {/* Tabla en pantallas anchas */}
          <div className="hidden overflow-hidden rounded-xl border border-line bg-surface md:block">
            <table className="w-full text-sm">
              <thead className="border-b border-line bg-surface-2 text-left text-xs uppercase tracking-wide text-muted">
                <tr>
                  <th className="px-4 py-2.5 font-medium">Cliente</th>
                  <th className="px-4 py-2.5 font-medium">Teléfono</th>
                  <th className="px-4 py-2.5 text-right font-medium">Reservas</th>
                  <th className="px-4 py-2.5 text-right font-medium">No vino</th>
                  <th className="px-4 py-2.5 font-medium">Última</th>
                  <th className="px-4 py-2.5 font-medium">Próxima</th>
                </tr>
              </thead>
              <tbody>
                {data.items.map((c) => (
                  <tr key={c.id} onClick={() => setSelected(c.id)} className="cursor-pointer border-b border-line last:border-0 hover:bg-surface-2">
                    <td className="px-4 py-3">
                      <button onClick={() => setSelected(c.id)} className="flex items-center gap-2 text-left font-medium text-ink">
                        {c.name}
                        {c.isBlocked ? <Badge tone="danger">Bloqueado</Badge> : null}
                      </button>
                      {c.notes ? <p className="max-w-xs truncate text-xs text-muted">{c.notes}</p> : null}
                    </td>
                    <td className="px-4 py-3 tabular-nums text-muted">{formatPhone(c.phone)}</td>
                    <td className="px-4 py-3 text-right tabular-nums">{c.bookings}</td>
                    <td className={`px-4 py-3 text-right tabular-nums ${c.noShows > 0 ? 'font-semibold text-danger' : 'text-muted'}`}>{c.noShows}</td>
                    <td className="px-4 py-3 text-muted">{visit(c.lastBookingAt)}</td>
                    <td className="px-4 py-3 text-muted">{visit(c.nextBookingAt)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>

          {/* Tarjetas en el celular */}
          <ul className="flex flex-col gap-2 md:hidden">
            {data.items.map((c) => (
              <li key={c.id}>
                <MobileCard customer={c} onClick={() => setSelected(c.id)} />
              </li>
            ))}
          </ul>

          {pages > 1 ? (
            <div className="mt-4 flex items-center justify-center gap-3 text-sm text-muted">
              <Button variant="secondary" className="px-2.5" aria-label="Página anterior" disabled={page === 1} onClick={() => setPage(page - 1)}>
                <ChevronLeft className="size-4" />
              </Button>
              Página {page} de {pages}
              <Button variant="secondary" className="px-2.5" aria-label="Página siguiente" disabled={page >= pages} onClick={() => setPage(page + 1)}>
                <ChevronRight className="size-4" />
              </Button>
            </div>
          ) : null}
        </>
      ) : null}

      <CustomerDialog customerId={selected} onClose={() => setSelected(null)} />
    </>
  )
}

function MobileCard({ customer: c, onClick }: { customer: CustomerListItem; onClick: () => void }) {
  return (
    <button onClick={onClick} className="w-full rounded-xl border border-line bg-surface px-4 py-3 text-left">
      <span className="flex items-center justify-between gap-2">
        <span className="truncate font-semibold text-ink">{c.name}</span>
        {c.isBlocked ? <Badge tone="danger">Bloqueado</Badge> : null}
      </span>
      <span className="block text-sm tabular-nums text-muted">{formatPhone(c.phone)}</span>
      <span className="mt-1 flex flex-wrap gap-x-3 text-xs text-muted">
        <span>{c.bookings} {c.bookings === 1 ? 'reserva' : 'reservas'}</span>
        {c.noShows > 0 ? (
          <span className="flex items-center gap-1 font-semibold text-danger">
            <CircleAlert className="size-3" aria-hidden /> {c.noShows} no vino
          </span>
        ) : null}
        <span>Próxima: {visit(c.nextBookingAt)}</span>
      </span>
    </button>
  )
}
