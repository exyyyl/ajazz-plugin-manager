import { ArchiveRestore } from 'lucide-react'

export function BackupsPage(): React.JSX.Element {
  return (
    <section className="page">
      <div className="feature-placeholder">
        <ArchiveRestore className="size-8 text-violet-300" />
        <h2>Перенесём на следующем этапе</h2>
        <p>Существующие резервные копии останутся на месте и будут подключены к этому экрану.</p>
      </div>
    </section>
  )
}
