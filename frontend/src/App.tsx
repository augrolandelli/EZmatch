import { useQuery } from '@tanstack/react-query'
import { api } from './shared/api/client'

type Health = { status: string; database: string }

// Placeholder de Fase 0: verifica la conexión panel → API → base. La agenda llega en Fase 3.
export default function App() {
  const health = useQuery({
    queryKey: ['health'],
    queryFn: async () => (await api.get<Health>('/api/health')).data,
  })

  return (
    <main className="min-h-screen grid place-items-center bg-slate-50 p-4">
      <div className="text-center">
        <h1 className="text-3xl font-bold text-slate-900">EZmatch</h1>
        <p className="mt-2 text-slate-600">Panel del club</p>
        <p className="mt-6 text-sm text-slate-500">
          API:{' '}
          {health.isPending
            ? 'conectando…'
            : health.isError
              ? 'sin conexión'
              : `${health.data.status} · base ${health.data.database}`}
        </p>
      </div>
    </main>
  )
}
