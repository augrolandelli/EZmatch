import { useEffect, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { CircleAlert, UserRound } from 'lucide-react'
import { Modal } from '../../shared/components/Modal'
import { Alert, Button, TextField } from '../../shared/components/ui'
import { apiErrorMessage } from '../../shared/api/client'
import { formatLongDate, formatMoney, formatPhone, formatTime } from '../../shared/format'
import { useActiveClub } from '../auth/authStore'
import { createBooking, searchCustomers } from './api'
import type { AgendaCourt, AgendaItem, CustomerSummary } from './types'

function useDebounced<T>(value: T, ms: number): T {
  const [debounced, setDebounced] = useState(value)
  useEffect(() => {
    const id = setTimeout(() => setDebounced(value), ms)
    return () => clearTimeout(id)
  }, [value, ms])
  return debounced
}

type Slot = { court: AgendaCourt; item: AgendaItem }

/** Reserva manual en un turno libre (alguien que llamó o vino al mostrador). */
export function NewBookingDialog({ slot, date, onClose }: { slot: Slot | null; date: string; onClose: () => void }) {
  return (
    <Modal open={slot !== null} onClose={onClose} title="Nueva reserva">
      {/* key: formulario limpio para cada turno. */}
      {slot ? <NewBookingForm key={`${slot.court.id}-${slot.item.startMinute}`} slot={slot} date={date} onClose={onClose} /> : null}
    </Modal>
  )
}

function NewBookingForm({ slot, date, onClose }: { slot: Slot; date: string; onClose: () => void }) {
  const club = useActiveClub()
  const queryClient = useQueryClient()
  const [phone, setPhone] = useState('')
  const [name, setName] = useState('')
  const [term, setTerm] = useState('')
  const [picked, setPicked] = useState<CustomerSummary | null>(null)
  const debouncedTerm = useDebounced(term, 250)

  const suggestions = useQuery({
    queryKey: ['customers', club?.id, debouncedTerm],
    queryFn: () => searchCustomers(debouncedTerm),
    enabled: debouncedTerm.trim().length >= 2 && !picked,
  })

  const mutation = useMutation({
    mutationFn: () =>
      createBooking({
        courtId: slot.court.id,
        date,
        startTime: slot.item.startTime,
        phone,
        customerName: name.trim() || null,
      }),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['agenda', club?.id] })
      onClose()
    },
  })

  const pick = (c: CustomerSummary) => {
    setPicked(c)
    setPhone(formatPhone(c.phone))
    setName(c.name)
  }

  const visibleSuggestions = picked ? [] : (suggestions.data ?? [])

  return (
    <form
      noValidate
      onSubmit={(e) => {
        e.preventDefault()
        mutation.mutate()
      }}
      className="flex flex-col gap-4"
    >
      <div className="rounded-lg bg-surface-2 px-3 py-2.5 text-sm">
        <p className="font-semibold text-ink">
          {slot.court.name} · {formatTime(slot.item.startTime)} a {formatTime(slot.item.endTime)}
        </p>
        <p className="text-muted first-letter:uppercase">
          {formatLongDate(date)}
          {slot.item.price != null ? ` · ${formatMoney(slot.item.price)}` : ''}
        </p>
      </div>

      {mutation.isError ? <Alert>{apiErrorMessage(mutation.error)}</Alert> : null}

      <TextField
        label="Teléfono"
        name="phone"
        type="tel"
        inputMode="tel"
        autoComplete="off"
        autoFocus
        hint="Código de área + número, sin 0 ni 15. Ej: 341 555 0001"
        value={phone}
        onChange={(e) => {
          setPhone(e.target.value)
          setTerm(e.target.value)
          setPicked(null)
        }}
      />
      <TextField
        label="Nombre"
        name="customerName"
        autoComplete="off"
        hint="Si ya es cliente del club, alcanza con el teléfono."
        value={name}
        onChange={(e) => {
          setName(e.target.value)
          setTerm(e.target.value)
          setPicked(null)
        }}
      />

      {visibleSuggestions.length > 0 ? (
        <ul className="-mt-2 overflow-hidden rounded-lg border border-line" aria-label="Clientes encontrados">
          {visibleSuggestions.map((c) => (
            <li key={c.id}>
              <button
                type="button"
                onClick={() => pick(c)}
                className="flex w-full items-center gap-2 px-3 py-2 text-left text-sm hover:bg-surface-2"
              >
                <UserRound className="size-4 shrink-0 text-muted" aria-hidden />
                <span className="flex-1 truncate font-medium text-ink">{c.name}</span>
                <span className="tabular-nums text-muted">{formatPhone(c.phone)}</span>
              </button>
            </li>
          ))}
        </ul>
      ) : null}

      {picked?.isBlocked ? (
        <p className="flex items-center gap-2 text-sm text-danger">
          <CircleAlert className="size-4" aria-hidden /> Este cliente está bloqueado (no puede reservar por WhatsApp).
        </p>
      ) : null}

      <div className="flex justify-end gap-2 pt-2">
        <Button type="button" variant="ghost" onClick={onClose}>
          Cancelar
        </Button>
        <Button type="submit" loading={mutation.isPending} disabled={phone.trim().length === 0}>
          Reservar
        </Button>
      </div>
    </form>
  )
}
