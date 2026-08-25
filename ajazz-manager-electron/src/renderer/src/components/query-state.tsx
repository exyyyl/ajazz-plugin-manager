import { AlertTriangle, LoaderCircle } from 'lucide-react'

export function LoadingState({ label = 'Загружаем данные…' }: { label?: string }): React.JSX.Element {
  return (
    <div className="state-panel">
      <LoaderCircle className="size-5 animate-spin text-violet-300" />
      <span>{label}</span>
    </div>
  )
}

export function ErrorState({ error }: { error: unknown }): React.JSX.Element {
  return (
    <div className="state-panel border-red-400/15 bg-red-500/[0.06] text-red-200">
      <AlertTriangle className="size-5" />
      <span>{error instanceof Error ? error.message : 'Не удалось загрузить данные.'}</span>
    </div>
  )
}
