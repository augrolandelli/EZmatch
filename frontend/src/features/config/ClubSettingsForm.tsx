import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useForm, useWatch } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { Bot } from 'lucide-react'
import { Alert, Button, Card, TextArea, TextField } from '../../shared/components/ui'
import { apiErrorMessage } from '../../shared/api/client'
import { useActiveClub } from '../auth/authStore'
import { getSettings, updateSettings } from './api'
import type { ClubSettings } from './api'

const int = (min: number, max: number, message: string) =>
  z.number({ error: message }).int(message).min(min, message).max(max, message)

const schema = z.object({
  name: z.string().trim().min(1, 'El nombre del club es obligatorio.').max(120),
  address: z.string().max(200),
  phone: z.string().max(20),
  assistantName: z.string().max(40, 'Máximo 40 caracteres.'),
  botInstructions: z.string().max(2000, 'Máximo 2000 caracteres.'),
  cancellationMinHours: int(0, 168, 'Entre 0 y 168 horas.'),
  minLeadMinutes: int(0, 1440, 'Entre 0 y 1440 minutos.'),
  bookingHorizonDays: int(1, 90, 'Entre 1 y 90 días.'),
  maxActiveBookingsPerCustomer: int(1, 10, 'Entre 1 y 10.'),
})
type FormValues = z.infer<typeof schema>

const toForm = (s: ClubSettings): FormValues => ({
  name: s.name,
  address: s.address ?? '',
  phone: s.phone ?? '',
  assistantName: s.assistantName ?? '',
  botInstructions: s.botInstructions ?? '',
  cancellationMinHours: s.cancellationMinHours,
  minLeadMinutes: s.minLeadMinutes,
  bookingHorizonDays: s.bookingHorizonDays,
  maxActiveBookingsPerCustomer: s.maxActiveBookingsPerCustomer,
})

export function ClubSettingsForm() {
  const club = useActiveClub()
  const queryClient = useQueryClient()
  const settings = useQuery({ queryKey: ['club', club?.id], queryFn: getSettings })
  const {
    register,
    handleSubmit,
    control,
    formState: { errors, isDirty },
  } = useForm<FormValues>({ resolver: zodResolver(schema), values: settings.data ? toForm(settings.data) : undefined })

  const save = useMutation({
    mutationFn: (v: FormValues) =>
      updateSettings({
        ...v,
        address: v.address || null,
        phone: v.phone || null,
        assistantName: v.assistantName || null,
        botInstructions: v.botInstructions || null,
      }),
    onSuccess: (data) => queryClient.setQueryData(['club', club?.id], data),
  })

  const assistant = useWatch({ control, name: 'assistantName' })?.trim()
  const clubName = useWatch({ control, name: 'name' })

  if (settings.isPending) return <p className="text-sm text-muted">Cargando…</p>
  if (settings.isError) return <Alert>{apiErrorMessage(settings.error)}</Alert>


  return (
    <form onSubmit={handleSubmit((v) => save.mutate(v))} noValidate className="flex max-w-2xl flex-col gap-6">
      <Card className="flex flex-col gap-4">
        <h2 className="font-semibold text-ink">Datos del club</h2>
        <TextField label="Nombre" {...register('name')} error={errors.name?.message} />
        <div className="grid gap-4 sm:grid-cols-2">
          <TextField label="Dirección" {...register('address')} error={errors.address?.message} />
          <TextField label="Teléfono" {...register('phone')} error={errors.phone?.message} hint="El bot lo pasa si alguien pide hablar por teléfono." />
        </div>
      </Card>

      <Card className="flex flex-col gap-4">
        <h2 className="flex items-center gap-2 font-semibold text-ink">
          <Bot className="size-5 text-brand" aria-hidden /> Asistente de WhatsApp
        </h2>
        <TextField
          label="Nombre del asistente"
          placeholder="Ej: Mati"
          {...register('assistantName')}
          error={errors.assistantName?.message}
          hint={`Se va a presentar como: "Hola! Soy ${assistant || 'el asistente de reservas'} de ${clubName || 'tu club'}".`}
        />
        <TextArea
          label="Información extra para el bot"
          rows={5}
          placeholder="Ej: Se alquilan paletas a $3.000. Hay estacionamiento gratis. Vestuarios con duchas."
          {...register('botInstructions')}
          error={errors.botInstructions?.message}
          hint="Lo que el bot puede contarle a los clientes además de turnos y precios. No inventa nada fuera de esto."
        />
      </Card>

      <Card className="flex flex-col gap-4">
        <h2 className="font-semibold text-ink">Reglas de reserva por WhatsApp</h2>
        <p className="-mt-2 text-sm text-muted">Desde el panel no rigen: el mostrador puede reservar y cancelar siempre.</p>
        <div className="grid gap-4 sm:grid-cols-2">
          <TextField
            label="Anticipación mínima para reservar (minutos)"
            type="number"
            inputMode="numeric"
            {...register('minLeadMinutes', { valueAsNumber: true })}
            error={errors.minLeadMinutes?.message}
          />
          <TextField
            label="Se puede reservar hasta (días)"
            type="number"
            inputMode="numeric"
            {...register('bookingHorizonDays', { valueAsNumber: true })}
            error={errors.bookingHorizonDays?.message}
          />
          <TextField
            label="Cancelar con al menos (horas)"
            type="number"
            inputMode="numeric"
            {...register('cancellationMinHours', { valueAsNumber: true })}
            error={errors.cancellationMinHours?.message}
            hint="Si falta menos, el bot deriva a una persona del club."
          />
          <TextField
            label="Máximo de reservas activas por persona"
            type="number"
            inputMode="numeric"
            {...register('maxActiveBookingsPerCustomer', { valueAsNumber: true })}
            error={errors.maxActiveBookingsPerCustomer?.message}
          />
        </div>
      </Card>

      {settings.data.chatwootInboxId ? (
        <p className="text-xs text-muted">
          WhatsApp vinculado: inbox {settings.data.chatwootInboxId} de Chatwoot
          {settings.data.chatwootAccountId ? ` (cuenta ${settings.data.chatwootAccountId})` : ''}.
        </p>
      ) : (
        <p className="text-xs text-warn">Este club todavía no tiene un WhatsApp vinculado: el bot no lo atiende.</p>
      )}

      {save.isError ? <Alert>{apiErrorMessage(save.error)}</Alert> : null}
      {save.isSuccess && !isDirty ? <Alert tone="success">Cambios guardados. El bot ya los usa.</Alert> : null}

      <div>
        <Button type="submit" loading={save.isPending} disabled={!isDirty}>
          Guardar cambios
        </Button>
      </div>
    </form>
  )
}
