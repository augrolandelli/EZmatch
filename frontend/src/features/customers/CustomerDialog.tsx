import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Ban, MessageCircle, ShieldCheck } from 'lucide-react'
import { Modal } from '../../shared/components/Modal'
import { Alert, Button, TextArea, TextField } from '../../shared/components/ui'
import { apiErrorMessage } from '../../shared/api/client'
import { formatMoney, formatPhone, formatTime, parseIsoDate, whatsappLink } from '../../shared/format'
import { useActiveClub } from '../auth/authStore'
import { Badge } from '../agenda/AgendaGrid'
import type { Booking } from '../agenda/types'
import { getCustomer, setBlocked, updateCustomer } from './api'
import type { CustomerDetail } from './api'

const historyDate = new Intl.DateTimeFormat('es-AR', { weekday: 'short', day: 'numeric', month: 'short' })

/** Detalle del cliente: números, notas internas, bloqueo e historial de reservas. */
export function CustomerDialog({ customerId, onClose }: { customerId: string | null; onClose: () => void }) {
  const club = useActiveClub()
  const detail = useQuery({
    queryKey: ['customers', 'detail', club?.id, customerId],
    queryFn: () => getCustomer(customerId!),
    enabled: customerId !== null,
  })
  return (
    <Modal open={customerId !== null} onClose={onClose} title={detail.data?.name ?? 'Cliente'}>
      {detail.isPending ? (
        <p className="text-sm text-muted">Cargando…</p>
      ) : detail.isError ? (
        <Alert>{apiErrorMessage(detail.error)}</Alert>
      ) : (
        <CustomerDetails key={detail.data.id} customer={detail.data} />
      )}
    </Modal>
  )
}

function CustomerDetails({ customer }: { customer: CustomerDetail }) {
  const club = useActiveClub()
  const queryClient = useQueryClient()
  const [name, setName] = useState(customer.name)
  const [notes, setNotes] = useState(customer.notes ?? '')
  const [confirmBlock, setConfirmBlock] = useState(false)

  const refresh = (data: CustomerDetail) => {
    queryClient.setQueryData(['customers', 'detail', club?.id, customer.id], data)
    void queryClient.invalidateQueries({ queryKey: ['customers', 'directory', club?.id] })
    void queryClient.invalidateQueries({ queryKey: ['agenda', club?.id] })
  }
  const save = useMutation({ mutationFn: () => updateCustomer(customer.id, name, notes.trim() || null), onSuccess: refresh })
  const block = useMutation({
    mutationFn: (blocked: boolean) => setBlocked(customer.id, blocked),
    onSuccess: (data) => {
      setConfirmBlock(false)
      refresh(data)
    },
  })
  const dirty = name.trim() !== customer.name || (notes.trim() || null) !== customer.notes

  return (
    <div className="flex max-h-[70dvh] flex-col gap-4 overflow-y-auto">
      <div className="flex flex-wrap items-center gap-2">
        <a
          href={whatsappLink(customer.phone)}
          target="_blank"
          rel="noreferrer"
          className="inline-flex items-center gap-1.5 text-sm font-medium text-brand hover:underline"
        >
          <MessageCircle className="size-4" aria-hidden /> {formatPhone(customer.phone)}
        </a>
        {customer.isBlocked ? <Badge tone="danger">Bloqueado</Badge> : null}
      </div>

      <dl className="grid grid-cols-4 gap-2 text-center">
        <Stat label="Reservas" value={String(customer.bookings)} />
        <Stat label="No vino" value={String(customer.noShows)} danger={customer.noShows > 0} />
        <Stat label="Canceló" value={String(customer.cancellations)} />
        <Stat label="Pagó" value={formatMoney(customer.paidAmount)} />
      </dl>

      {save.isError || block.isError ? <Alert>{apiErrorMessage(save.error ?? block.error)}</Alert> : null}

      <TextField label="Nombre" name="name" value={name} onChange={(e) => setName(e.target.value)} />
      <TextArea
        label="Notas internas"
        name="notes"
        rows={3}
        placeholder="Ej: juega los martes con el mismo grupo, pide factura…"
        value={notes}
        onChange={(e) => setNotes(e.target.value)}
        hint="Solo las ve el club; el bot no."
      />
      <div className="flex flex-wrap items-center justify-between gap-2">
        {confirmBlock ? (
          <div className="flex flex-1 flex-wrap items-center gap-2 rounded-lg border border-danger/40 p-2.5 text-sm">
            <span className="flex-1">¿Bloquearlo? No va a poder reservar por WhatsApp.</span>
            <Button variant="ghost" onClick={() => setConfirmBlock(false)}>
              No
            </Button>
            <Button variant="danger" loading={block.isPending} onClick={() => block.mutate(true)}>
              Bloquear
            </Button>
          </div>
        ) : customer.isBlocked ? (
          <Button variant="secondary" loading={block.isPending} onClick={() => block.mutate(false)}>
            <ShieldCheck className="size-4" aria-hidden /> Desbloquear
          </Button>
        ) : (
          <Button variant="dangerGhost" onClick={() => setConfirmBlock(true)}>
            <Ban className="size-4" aria-hidden /> Bloquear
          </Button>
        )}
        {!confirmBlock ? (
          <Button loading={save.isPending} disabled={!dirty || !name.trim()} onClick={() => save.mutate()}>
            Guardar
          </Button>
        ) : null}
      </div>

      <section>
        <h3 className="mb-2 text-sm font-semibold text-ink">Historial</h3>
        {customer.history.length === 0 ? (
          <p className="text-sm text-muted">Sin reservas.</p>
        ) : (
          <ul className="flex flex-col divide-y divide-line rounded-lg border border-line">
            {customer.history.map((b) => (
              <HistoryRow key={b.id} booking={b} />
            ))}
          </ul>
        )}
      </section>
    </div>
  )
}

function HistoryRow({ booking: b }: { booking: Booking }) {
  const status =
    b.status === 'Cancelled' ? (
      <Badge tone="neutral">Cancelada</Badge>
    ) : b.status === 'NoShow' ? (
      <Badge tone="danger">No vino</Badge>
    ) : b.paymentStatus === 'Paid' ? (
      <Badge tone="success">Pagado</Badge>
    ) : (
      <Badge tone="warn">Debe</Badge>
    )
  return (
    <li className={`flex items-center gap-3 px-3 py-2 text-sm ${b.status === 'Cancelled' ? 'text-muted' : 'text-ink'}`}>
      <span className="w-24 shrink-0 capitalize">{historyDate.format(parseIsoDate(b.date))}</span>
      <span className="flex-1 truncate tabular-nums">
        {formatTime(b.startTime)} · {b.courtName}
      </span>
      {b.source === 'WhatsApp' ? <MessageCircle className="size-3.5 shrink-0 text-muted" aria-label="Reservó por WhatsApp" /> : null}
      {status}
    </li>
  )
}

function Stat({ label, value, danger = false }: { label: string; value: string; danger?: boolean }) {
  return (
    <div className="rounded-lg bg-surface-2 px-2 py-2">
      <dt className="text-[11px] text-muted">{label}</dt>
      <dd className={`text-sm font-bold tabular-nums ${danger ? 'text-danger' : 'text-ink'}`}>{value}</dd>
    </div>
  )
}
