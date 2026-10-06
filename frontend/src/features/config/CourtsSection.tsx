import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { ArrowDown, ArrowUp, Copy, Pencil, Plus, Umbrella, Wand2 } from 'lucide-react'
import { Alert, Button, Card, Checkbox, SelectField, TextField } from '../../shared/components/ui'
import { Modal } from '../../shared/components/Modal'
import { apiErrorMessage } from '../../shared/api/client'
import { formatMoney, formatTime } from '../../shared/format'
import { useActiveClub } from '../auth/authStore'
import { sportLabel } from '../agenda/types'
import type { Sport } from '../agenda/types'
import {
  copySlots,
  createCourt,
  generateSlots,
  getCourts,
  getSlots,
  reorderCourts,
  replaceSlots,
  updateCourt,
  weekDays,
} from './api'
import type { Court, CourtInput, DayOfWeek, SlotTemplate } from './api'

const sports = Object.keys(sportLabel) as Sport[]

/** Canchas del club y la grilla semanal de la elegida. */
export function CourtsSection() {
  const club = useActiveClub()
  const queryClient = useQueryClient()
  const courts = useQuery({ queryKey: ['courts', club?.id], queryFn: getCourts })
  const [selectedId, setSelectedId] = useState<string | null>(null)
  const [editing, setEditing] = useState<Court | 'new' | null>(null)

  const reorder = useMutation({
    mutationFn: reorderCourts,
    onSuccess: (data) => {
      queryClient.setQueryData(['courts', club?.id], data)
      void queryClient.invalidateQueries({ queryKey: ['agenda', club?.id] })
    },
  })

  if (courts.isPending) return <p className="text-sm text-muted">Cargando…</p>
  if (courts.isError) return <Alert>{apiErrorMessage(courts.error)}</Alert>

  const list = courts.data
  const selected = list.find((c) => c.id === selectedId) ?? list[0] ?? null
  const move = (index: number, delta: number) => {
    const ids = list.map((c) => c.id)
    const [item] = ids.splice(index, 1)
    ids.splice(index + delta, 0, item)
    reorder.mutate(ids)
  }

  return (
    <div className="grid gap-6 lg:grid-cols-[18rem_1fr]">
      <div className="flex flex-col gap-2">
        {list.map((court, i) => (
          <div
            key={court.id}
            className={`flex items-center gap-2 rounded-xl border px-3 py-2.5 ${
              selected?.id === court.id ? 'border-brand bg-brand-soft' : 'border-line bg-surface'
            }`}
          >
            <button onClick={() => setSelectedId(court.id)} className="min-w-0 flex-1 text-left">
              <p className="flex items-center gap-1.5 truncate text-sm font-semibold text-ink">
                {court.name}
                {court.isCovered ? <Umbrella className="size-3.5 text-muted" aria-label="Techada" /> : null}
              </p>
              <p className="text-xs text-muted">
                {sportLabel[court.sport]} · {court.slotCount} turnos/semana
                {court.isActive ? '' : ' · inactiva'}
              </p>
            </button>
            <div className="flex flex-col">
              <button aria-label="Subir" disabled={i === 0} onClick={() => move(i, -1)} className="rounded p-0.5 text-muted hover:text-ink disabled:opacity-30">
                <ArrowUp className="size-3.5" />
              </button>
              <button aria-label="Bajar" disabled={i === list.length - 1} onClick={() => move(i, 1)} className="rounded p-0.5 text-muted hover:text-ink disabled:opacity-30">
                <ArrowDown className="size-3.5" />
              </button>
            </div>
            <button aria-label={`Editar ${court.name}`} onClick={() => setEditing(court)} className="rounded-lg p-1.5 text-muted hover:bg-surface-2 hover:text-ink">
              <Pencil className="size-4" />
            </button>
          </div>
        ))}
        <Button variant="secondary" onClick={() => setEditing('new')}>
          <Plus className="size-4" aria-hidden /> Agregar cancha
        </Button>
        {reorder.isError ? <Alert>{apiErrorMessage(reorder.error)}</Alert> : null}
      </div>

      {selected ? <WeekGrid key={selected.id} court={selected} courts={list} /> : <Card className="text-sm text-muted">Agregá la primera cancha del club.</Card>}

      <CourtDialog court={editing} onClose={() => setEditing(null)} onSaved={(c) => setSelectedId(c.id)} />
    </div>
  )
}

/** Grilla semanal de una cancha: una columna por día, cada turno como chip editable. */
function WeekGrid({ court, courts }: { court: Court; courts: Court[] }) {
  const club = useActiveClub()
  const slots = useQuery({ queryKey: ['slots', club?.id, court.id], queryFn: () => getSlots(court.id) })
  const [slotDialog, setSlotDialog] = useState<{ slot: SlotTemplate | null; day: DayOfWeek } | null>(null)
  const [generating, setGenerating] = useState(false)
  const [copying, setCopying] = useState(false)

  return (
    <Card className="min-w-0 p-0">
      <div className="flex flex-wrap items-center justify-between gap-3 border-b border-line p-4">
        <div>
          <h2 className="font-semibold text-ink">Horarios de {court.name}</h2>
          <p className="text-xs text-muted">Los cambios valen para reservas nuevas; las ya hechas no se tocan.</p>
        </div>
        <div className="flex flex-wrap gap-2">
          <Button onClick={() => setGenerating(true)}>
            <Wand2 className="size-4" aria-hidden /> Generar horarios
          </Button>
          <Button variant="secondary" disabled={courts.length < 2 || !slots.data?.length} onClick={() => setCopying(true)}>
            <Copy className="size-4" aria-hidden /> Copiar a otras canchas
          </Button>
        </div>
      </div>

      {slots.isPending ? (
        <p className="p-4 text-sm text-muted">Cargando…</p>
      ) : slots.isError ? (
        <div className="p-4">
          <Alert>{apiErrorMessage(slots.error)}</Alert>
        </div>
      ) : (
        <div className="grid grid-cols-1 gap-px overflow-x-auto bg-line sm:grid-cols-7">
          {weekDays.map((day) => {
            const daySlots = slots.data.filter((s) => s.dayOfWeek === day.value)
            return (
              <div key={day.value} className="flex min-w-32 flex-col gap-1.5 bg-surface p-2">
                <p className="px-1 text-xs font-semibold uppercase tracking-wide text-muted">
                  {day.long} <span className="font-normal normal-case">· {daySlots.length}</span>
                </p>
                {daySlots.map((s) => (
                  <button
                    key={s.id}
                    onClick={() => setSlotDialog({ slot: s, day: s.dayOfWeek })}
                    className="rounded-lg border border-line px-2 py-1.5 text-left text-xs hover:border-brand hover:bg-brand-soft"
                  >
                    <span className="block font-semibold tabular-nums text-ink">
                      {formatTime(s.startTime)} <span className="font-normal text-muted">· {s.durationMinutes}'</span>
                    </span>
                    <span className="text-muted">{formatMoney(s.price)}</span>
                  </button>
                ))}
                <button
                  onClick={() => setSlotDialog({ slot: null, day: day.value })}
                  className="flex items-center justify-center gap-1 rounded-lg border border-dashed border-line py-1.5 text-xs text-muted hover:border-brand hover:text-brand"
                  aria-label={`Agregar turno el ${day.long}`}
                >
                  <Plus className="size-3.5" aria-hidden /> Turno
                </button>
              </div>
            )
          })}
        </div>
      )}

      {slots.data ? (
        <>
          <SlotDialog target={slotDialog} court={court} slots={slots.data} onClose={() => setSlotDialog(null)} />
          <GenerateDialog open={generating} court={court} onClose={() => setGenerating(false)} />
          <CopyDialog open={copying} court={court} courts={courts} onClose={() => setCopying(false)} />
        </>
      ) : null}
    </Card>
  )
}

function useGridSaved(courtId: string) {
  const club = useActiveClub()
  const queryClient = useQueryClient()
  return (data?: SlotTemplate[]) => {
    if (data) queryClient.setQueryData(['slots', club?.id, courtId], data)
    void queryClient.invalidateQueries({ queryKey: ['courts', club?.id] })
    void queryClient.invalidateQueries({ queryKey: ['agenda', club?.id] })
  }
}

function CourtDialog({ court, onClose, onSaved }: { court: Court | 'new' | null; onClose: () => void; onSaved: (c: Court) => void }) {
  const isNew = court === 'new'
  return (
    <Modal open={court !== null} onClose={onClose} title={isNew ? 'Nueva cancha' : 'Editar cancha'}>
      {court ? <CourtForm key={isNew ? 'new' : court.id} court={isNew ? null : court} onClose={onClose} onSaved={onSaved} /> : null}
    </Modal>
  )
}

function CourtForm({ court, onClose, onSaved }: { court: Court | null; onClose: () => void; onSaved: (c: Court) => void }) {
  const club = useActiveClub()
  const queryClient = useQueryClient()
  const [form, setForm] = useState<CourtInput>({
    name: court?.name ?? '',
    sport: court?.sport ?? 'Padel',
    isCovered: court?.isCovered ?? false,
    isActive: court?.isActive ?? true,
  })
  const save = useMutation({
    mutationFn: () => (court ? updateCourt(court.id, form) : createCourt(form)),
    onSuccess: (saved) => {
      void queryClient.invalidateQueries({ queryKey: ['courts', club?.id] })
      void queryClient.invalidateQueries({ queryKey: ['agenda', club?.id] })
      onSaved(saved)
      onClose()
    },
  })
  return (
    <form
      noValidate
      onSubmit={(e) => {
        e.preventDefault()
        save.mutate()
      }}
      className="flex flex-col gap-4"
    >
      {save.isError ? <Alert>{apiErrorMessage(save.error)}</Alert> : null}
      <TextField label="Nombre" name="name" autoFocus placeholder="Ej: Cancha 1, Central" value={form.name} onChange={(e) => setForm({ ...form, name: e.target.value })} />
      <SelectField label="Deporte" name="sport" value={form.sport} onChange={(e) => setForm({ ...form, sport: e.target.value as Sport })}>
        {sports.map((s) => (
          <option key={s} value={s}>
            {sportLabel[s]}
          </option>
        ))}
      </SelectField>
      <Checkbox label="Techada" checked={form.isCovered} onChange={(e) => setForm({ ...form, isCovered: e.target.checked })} />
      <Checkbox
        label="Activa (aparece en la agenda y el bot la ofrece)"
        checked={form.isActive}
        onChange={(e) => setForm({ ...form, isActive: e.target.checked })}
      />
      {court === null ? <p className="text-xs text-muted">Después de crearla, cargale los horarios con "Generar horarios".</p> : null}
      <div className="flex justify-end gap-2 pt-2">
        <Button type="button" variant="ghost" onClick={onClose}>
          Cancelar
        </Button>
        <Button type="submit" loading={save.isPending} disabled={!form.name.trim()}>
          Guardar
        </Button>
      </div>
    </form>
  )
}

function SlotDialog({
  target,
  court,
  slots,
  onClose,
}: {
  target: { slot: SlotTemplate | null; day: DayOfWeek } | null
  court: Court
  slots: SlotTemplate[]
  onClose: () => void
}) {
  return (
    <Modal open={target !== null} onClose={onClose} title={target?.slot ? 'Editar turno' : 'Nuevo turno'}>
      {target ? <SlotForm key={target.slot?.id ?? `new-${target.day}`} target={target} court={court} slots={slots} onClose={onClose} /> : null}
    </Modal>
  )
}

function SlotForm({
  target,
  court,
  slots,
  onClose,
}: {
  target: { slot: SlotTemplate | null; day: DayOfWeek }
  court: Court
  slots: SlotTemplate[]
  onClose: () => void
}) {
  const saved = useGridSaved(court.id)
  const [day, setDay] = useState<DayOfWeek>(target.day)
  const [start, setStart] = useState(target.slot ? formatTime(target.slot.startTime) : '20:00')
  const [duration, setDuration] = useState(target.slot?.durationMinutes ?? 90)
  const [price, setPrice] = useState(target.slot?.price ?? 0)

  const others = slots
    .filter((s) => s.id !== target.slot?.id)
    .map((s) => ({ dayOfWeek: s.dayOfWeek, startTime: s.startTime, durationMinutes: s.durationMinutes, price: s.price }))
  const save = useMutation({
    mutationFn: (remove: boolean) =>
      replaceSlots(court.id, remove ? others : [...others, { dayOfWeek: day, startTime: start, durationMinutes: duration, price }]),
    onSuccess: (data) => {
      saved(data)
      onClose()
    },
  })

  return (
    <form
      noValidate
      onSubmit={(e) => {
        e.preventDefault()
        save.mutate(false)
      }}
      className="flex flex-col gap-4"
    >
      {save.isError ? <Alert>{apiErrorMessage(save.error)}</Alert> : null}
      <SelectField label="Día" name="day" value={day} onChange={(e) => setDay(e.target.value as DayOfWeek)}>
        {weekDays.map((d) => (
          <option key={d.value} value={d.value}>
            {d.long}
          </option>
        ))}
      </SelectField>
      <div className="grid grid-cols-2 gap-4">
        <TextField label="Empieza" name="start" type="time" value={start} onChange={(e) => setStart(e.target.value)} />
        <TextField label="Duración (min)" name="duration" type="number" inputMode="numeric" value={duration} onChange={(e) => setDuration(Number(e.target.value))} />
      </div>
      <TextField label="Precio ($)" name="price" type="number" inputMode="numeric" value={price} onChange={(e) => setPrice(Number(e.target.value))} />
      <div className="flex flex-wrap justify-between gap-2 pt-2">
        {target.slot ? (
          <Button type="button" variant="dangerGhost" loading={save.isPending && save.variables === true} onClick={() => save.mutate(true)}>
            Eliminar turno
          </Button>
        ) : (
          <span />
        )}
        <div className="flex gap-2">
          <Button type="button" variant="ghost" onClick={onClose}>
            Cancelar
          </Button>
          <Button type="submit" loading={save.isPending && save.variables === false}>
            Guardar
          </Button>
        </div>
      </div>
    </form>
  )
}

const presets: { label: string; days: DayOfWeek[] }[] = [
  { label: 'Lun a vie', days: ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday'] },
  { label: 'Fin de semana', days: ['Saturday', 'Sunday'] },
  { label: 'Todos', days: weekDays.map((d) => d.value) },
]

function minutesOf(time: string): number {
  const [h, m] = time.split(':').map(Number)
  return h * 60 + m
}

function GenerateDialog({ open, court, onClose }: { open: boolean; court: Court; onClose: () => void }) {
  return (
    <Modal open={open} onClose={onClose} title={`Generar horarios · ${court.name}`}>
      {open ? <GenerateForm court={court} onClose={onClose} /> : null}
    </Modal>
  )
}

function GenerateForm({ court, onClose }: { court: Court; onClose: () => void }) {
  const saved = useGridSaved(court.id)
  const defaultDuration = court.sport === 'Padel' ? 90 : 60
  const [days, setDays] = useState<DayOfWeek[]>(presets[2].days)
  const [first, setFirst] = useState('08:00')
  const [last, setLast] = useState('23:00')
  const [duration, setDuration] = useState(defaultDuration)
  const [every, setEvery] = useState<number | null>(null)
  const [price, setPrice] = useState(0)
  const [peak, setPeak] = useState(false)
  const [peakFrom, setPeakFrom] = useState('18:00')
  const [peakPrice, setPeakPrice] = useState(0)

  const step = every ?? duration
  const span = (minutesOf(last) - minutesOf(first) + 1440) % 1440
  const perDay = step > 0 ? Math.floor(span / step) + 1 : 0
  const toggleDay = (d: DayOfWeek) => setDays(days.includes(d) ? days.filter((x) => x !== d) : [...days, d])

  const generate = useMutation({
    mutationFn: () =>
      generateSlots(court.id, {
        days,
        firstStart: first,
        lastStart: last,
        durationMinutes: duration,
        everyMinutes: every,
        price,
        peakFrom: peak ? peakFrom : null,
        peakPrice: peak ? peakPrice : null,
      }),
    onSuccess: (data) => {
      saved(data)
      onClose()
    },
  })

  return (
    <form
      noValidate
      onSubmit={(e) => {
        e.preventDefault()
        generate.mutate()
      }}
      className="flex flex-col gap-4"
    >
      {generate.isError ? <Alert>{apiErrorMessage(generate.error)}</Alert> : null}

      <fieldset className="flex flex-col gap-2">
        <legend className="mb-1 text-sm font-medium text-ink">Días</legend>
        <div className="flex flex-wrap gap-1.5">
          {presets.map((p) => (
            <button key={p.label} type="button" onClick={() => setDays(p.days)} className="rounded-full border border-line px-2.5 py-1 text-xs text-muted hover:border-brand hover:text-brand">
              {p.label}
            </button>
          ))}
        </div>
        <div className="flex flex-wrap gap-x-4 gap-y-2">
          {weekDays.map((d) => (
            <Checkbox key={d.value} label={d.short} checked={days.includes(d.value)} onChange={() => toggleDay(d.value)} />
          ))}
        </div>
      </fieldset>

      <div className="grid grid-cols-2 gap-4">
        <TextField label="Primer turno" name="first" type="time" value={first} onChange={(e) => setFirst(e.target.value)} />
        <TextField label="Último turno" name="last" type="time" value={last} onChange={(e) => setLast(e.target.value)} hint="Puede ser pasada la medianoche." />
        <TextField label="Duración (min)" name="duration" type="number" inputMode="numeric" value={duration} onChange={(e) => setDuration(Number(e.target.value))} />
        <TextField
          label="Cada (min)"
          name="every"
          type="number"
          inputMode="numeric"
          placeholder={String(duration)}
          value={every ?? ''}
          onChange={(e) => setEvery(e.target.value ? Number(e.target.value) : null)}
          hint="Vacío = uno detrás del otro."
        />
      </div>
      <TextField label="Precio ($)" name="price" type="number" inputMode="numeric" value={price || ''} onChange={(e) => setPrice(Number(e.target.value))} />

      <Checkbox label="Precio distinto en horario pico" checked={peak} onChange={(e) => setPeak(e.target.checked)} />
      {peak ? (
        <div className="grid grid-cols-2 gap-4">
          <TextField label="Pico desde" name="peakFrom" type="time" value={peakFrom} onChange={(e) => setPeakFrom(e.target.value)} />
          <TextField label="Precio pico ($)" name="peakPrice" type="number" inputMode="numeric" value={peakPrice || ''} onChange={(e) => setPeakPrice(Number(e.target.value))} />
        </div>
      ) : null}

      <Alert tone="info">
        Se van a crear {perDay} turnos por día ({perDay * days.length} en la semana) y reemplazan los horarios actuales de esos días.
      </Alert>

      <div className="flex justify-end gap-2">
        <Button type="button" variant="ghost" onClick={onClose}>
          Cancelar
        </Button>
        <Button type="submit" loading={generate.isPending} disabled={days.length === 0 || perDay === 0}>
          Generar
        </Button>
      </div>
    </form>
  )
}

function CopyDialog({ open, court, courts, onClose }: { open: boolean; court: Court; courts: Court[]; onClose: () => void }) {
  return (
    <Modal open={open} onClose={onClose} title={`Copiar horarios de ${court.name}`}>
      {open ? <CopyForm court={court} courts={courts} onClose={onClose} /> : null}
    </Modal>
  )
}

function CopyForm({ court, courts, onClose }: { court: Court; courts: Court[]; onClose: () => void }) {
  const club = useActiveClub()
  const queryClient = useQueryClient()
  const others = courts.filter((c) => c.id !== court.id)
  const [targets, setTargets] = useState<string[]>(others.filter((c) => c.sport === court.sport).map((c) => c.id))
  const copy = useMutation({
    mutationFn: () => copySlots(court.id, targets),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['slots', club?.id] })
      void queryClient.invalidateQueries({ queryKey: ['courts', club?.id] })
      void queryClient.invalidateQueries({ queryKey: ['agenda', club?.id] })
      onClose()
    },
  })
  return (
    <div className="flex flex-col gap-4">
      {copy.isError ? <Alert>{apiErrorMessage(copy.error)}</Alert> : null}
      <p className="text-sm text-muted">La grilla de las canchas elegidas se reemplaza por la de {court.name}.</p>
      <div className="flex flex-col gap-2">
        {others.map((c) => (
          <Checkbox
            key={c.id}
            label={`${c.name} · ${sportLabel[c.sport]}`}
            checked={targets.includes(c.id)}
            onChange={(e) => setTargets(e.target.checked ? [...targets, c.id] : targets.filter((t) => t !== c.id))}
          />
        ))}
      </div>
      <div className="flex justify-end gap-2">
        <Button variant="ghost" onClick={onClose}>
          Cancelar
        </Button>
        <Button loading={copy.isPending} disabled={targets.length === 0} onClick={() => copy.mutate()}>
          Copiar
        </Button>
      </div>
    </div>
  )
}
