import { useState } from 'react'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { CircleAlert, MessageCircle } from 'lucide-react'
import { Modal } from '../../shared/components/Modal'
import { Alert, Button, TextField } from '../../shared/components/ui'
import { apiErrorMessage } from '../../shared/api/client'
import { formatLongDate, formatMoney, formatPhone, formatTime, whatsappLink } from '../../shared/format'
import { useActiveClub } from '../auth/authStore'
import { Badge } from './AgendaGrid'
import { cancelBooking, setNoShow, setPayment } from './api'
import type { AgendaCourt, AgendaItem } from './types'

/** Detalle de una reserva con las acciones del mostrador: cobrar, "no vino" y cancelar. */
type Selected = { court: AgendaCourt; item: AgendaItem }

/**
 * @param now Hora de referencia (ms) para saber si el turno ya empezó: la de la última carga de la agenda.
 */
export function BookingDialog({
  selected,
  date,
  now,
  onClose,
}: {
  selected: Selected | null
  date: string
  now: number
  onClose: () => void
}) {
  const booking = selected?.item.booking ?? null
  return (
    <Modal open={booking !== null} onClose={onClose} title={booking?.customerName ?? ''}>
      {/* key: estado limpio (confirmación de cancelación) para cada reserva. */}
      {selected && booking ? <BookingDetails key={booking.id} selected={selected} date={date} now={now} onClose={onClose} /> : null}
    </Modal>
  )
}

function BookingDetails({ selected, date, now, onClose }: { selected: Selected; date: string; now: number; onClose: () => void }) {
  const club = useActiveClub()
  const queryClient = useQueryClient()
  const [confirmingCancel, setConfirmingCancel] = useState(false)
  const [reason, setReason] = useState('')

  const done = () => {
    void queryClient.invalidateQueries({ queryKey: ['agenda', club?.id] })
    onClose()
  }
  const booking = selected.item.booking!
  const pay = useMutation({ mutationFn: (paid: boolean) => setPayment(booking.id, paid), onSuccess: done })
  const noShow = useMutation({ mutationFn: (value: boolean) => setNoShow(booking.id, value), onSuccess: done })
  const cancel = useMutation({ mutationFn: () => cancelBooking(booking.id, reason.trim() || null), onSuccess: done })
  const error = pay.error ?? noShow.error ?? cancel.error

  const { court, item } = selected
  const started = Date.parse(item.startsAt) <= now
  const isNoShow = booking.status === 'NoShow'
  const isPaid = booking.paymentStatus === 'Paid'

  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-wrap gap-1.5">
        {isNoShow ? <Badge tone="danger">No vino</Badge> : isPaid ? <Badge tone="success">Pagado</Badge> : <Badge tone="warn">Pendiente de pago</Badge>}
        <Badge tone="neutral">{booking.source === 'WhatsApp' ? 'Reservó por WhatsApp' : 'Reserva del mostrador'}</Badge>
      </div>

      {booking.customerIsBlocked ? (
        <p className="flex items-center gap-2 text-sm text-danger">
          <CircleAlert className="size-4" aria-hidden /> Cliente bloqueado
        </p>
      ) : null}

      <dl className="grid grid-cols-[auto_1fr] gap-x-5 gap-y-2 text-sm">
        <dt className="text-muted">Turno</dt>
        <dd className="font-medium text-ink">
          {court.name} · {formatTime(item.startTime)} a {formatTime(item.endTime)}
        </dd>
        <dt className="text-muted">Día</dt>
        <dd className="font-medium text-ink first-letter:uppercase">{formatLongDate(date)}</dd>
        <dt className="text-muted">Precio</dt>
        <dd className="font-medium text-ink">{formatMoney(booking.price)}</dd>
        <dt className="text-muted">Teléfono</dt>
        <dd>
          <a
            href={whatsappLink(booking.customerPhone)}
            target="_blank"
            rel="noreferrer"
            className="inline-flex items-center gap-1.5 font-medium text-brand hover:underline"
          >
            <MessageCircle className="size-4" aria-hidden />
            {formatPhone(booking.customerPhone)}
          </a>
        </dd>
      </dl>

      {error ? <Alert>{apiErrorMessage(error)}</Alert> : null}

      {confirmingCancel ? (
        <div className="flex flex-col gap-3 rounded-lg border border-danger/40 p-3">
          <p className="text-sm font-medium text-ink">¿Cancelar esta reserva? El turno queda libre.</p>
          <TextField label="Motivo (opcional)" name="reason" value={reason} onChange={(e) => setReason(e.target.value)} />
          <div className="flex justify-end gap-2">
            <Button variant="ghost" onClick={() => setConfirmingCancel(false)}>
              Volver
            </Button>
            <Button variant="danger" loading={cancel.isPending} onClick={() => cancel.mutate()}>
              Cancelar reserva
            </Button>
          </div>
        </div>
      ) : (
        <div className="flex flex-col gap-2 sm:flex-row sm:flex-wrap">
          {!isNoShow ? (
            <Button variant={isPaid ? 'secondary' : 'primary'} loading={pay.isPending} onClick={() => pay.mutate(!isPaid)}>
              {isPaid ? 'Marcar como no pagado' : `Cobrar ${formatMoney(booking.price)}`}
            </Button>
          ) : null}
          {started ? (
            <Button variant="secondary" loading={noShow.isPending} onClick={() => noShow.mutate(!isNoShow)}>
              {isNoShow ? 'Deshacer "no vino"' : 'No vino'}
            </Button>
          ) : null}
          <Button variant="dangerGhost" className="sm:ml-auto" onClick={() => setConfirmingCancel(true)}>
            Cancelar reserva
          </Button>
        </div>
      )}
    </div>
  )
}
