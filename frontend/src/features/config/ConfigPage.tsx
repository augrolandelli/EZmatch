import { useSearchParams } from 'react-router-dom'
import { Card, PageHeader, Tabs } from '../../shared/components/ui'
import { useActiveClub } from '../auth/authStore'
import { ClubSettingsForm } from './ClubSettingsForm'
import { CourtsSection } from './CourtsSection'
import { BlocksSection } from './BlocksSection'

type Section = 'club' | 'canchas' | 'bloqueos'

/** Configuración del club (dueño o SuperAdmin). La sección elegida queda en la URL. */
export default function ConfigPage() {
  const club = useActiveClub()
  const [params, setParams] = useSearchParams()
  const section = (['club', 'canchas', 'bloqueos'] as Section[]).find((s) => s === params.get('seccion')) ?? 'canchas'

  if (!club) {
    return (
      <Card className="mx-auto mt-10 max-w-md text-center">
        <p className="font-semibold text-ink">Elegí un club</p>
        <p className="mt-1 text-sm text-muted">Usá el selector de club para configurar uno.</p>
      </Card>
    )
  }

  return (
    <>
      <PageHeader title="Configuración" subtitle={club.name} />
      <Tabs<Section>
        value={section}
        onChange={(s) => setParams({ seccion: s }, { replace: true })}
        items={[
          { value: 'canchas', label: 'Canchas y horarios' },
          { value: 'bloqueos', label: 'Bloqueos' },
          { value: 'club', label: 'Club y bot' },
        ]}
      />
      {section === 'club' ? <ClubSettingsForm key={club.id} /> : section === 'bloqueos' ? <BlocksSection key={club.id} /> : <CourtsSection key={club.id} />}
    </>
  )
}
