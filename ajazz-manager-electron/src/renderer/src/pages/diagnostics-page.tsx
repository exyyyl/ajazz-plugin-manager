import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Activity, CheckCircle2, Copy, FolderSearch, RefreshCw, SlidersHorizontal, XCircle } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { ErrorState, LoadingState } from '@/components/query-state'

export function DiagnosticsPage(): React.JSX.Element {
  const [filter, setFilter] = useState('all')
  const query = useQuery({ queryKey: ['diagnostics'], queryFn: () => window.ajazz.diagnostics.read() })
  const visible = (ok: boolean): boolean => filter === 'all' || (filter === 'ready' && ok) || (filter === 'issues' && !ok)

  return (
    <section className="page">
      <div className="filter-toolbar">
        <SlidersHorizontal className="filter-icon size-4" />
        <select className="filter-select" value={filter} onChange={(event) => setFilter(event.target.value)}>
          <option value="all">Все проверки</option>
          <option value="issues">Только проблемы</option>
          <option value="ready">Только готовые</option>
        </select>
        <div className="filter-spacer" />
        <Button variant="secondary" size="icon" onClick={() => void query.refetch()} disabled={query.isFetching} title="Обновить диагностику">
          <RefreshCw className={query.isFetching ? 'size-4 animate-spin' : 'size-4'} />
        </Button>
      </div>
      {query.isPending ? <LoadingState /> : null}
      {query.isError ? <ErrorState error={query.error} /> : null}
      {query.data ? (
        <div className="diagnostics-grid">
          {visible(query.data.ajazzFound) ? <DiagnosticCard title="Stream Dock AJAZZ" ok={query.data.ajazzFound} detail={query.data.ajazzExe ?? 'Приложение не найдено'} /> : null}
          {visible(query.data.elgatoFound) ? <DiagnosticCard title="Elgato Stream Deck" ok={query.data.elgatoFound} detail={`${query.data.elgatoPluginCount} плагина`} /> : null}
          {visible(query.data.iconCount > 0) ? <DiagnosticCard title="Библиотека иконок" ok={query.data.iconCount > 0} detail={`${query.data.iconCount} изображений`} /> : null}
          <div className="path-card">
            <FolderSearch className="size-5 text-violet-300" />
            <div><h2>Пути данных</h2><p>{query.data.ajazzPlugins}</p><p>{query.data.elgatoPlugins}</p><p>{query.data.managerRoot}</p></div>
            <Copy className="ml-auto size-4 text-zinc-600" />
          </div>
        </div>
      ) : null}
    </section>
  )
}

function DiagnosticCard({ title, ok, detail }: { title: string; ok: boolean; detail: string }): React.JSX.Element {
  return (
    <article className="diagnostic-card">
      {ok ? <CheckCircle2 className="size-5 text-emerald-300" /> : <XCircle className="size-5 text-red-300" />}
      <div><h2>{title}</h2><p>{detail}</p></div>
      <Activity className="ml-auto size-4 text-zinc-700" />
    </article>
  )
}
