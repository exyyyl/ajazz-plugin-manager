import { useMemo, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import {
  CheckCircle2,
  FlaskConical,
  PackageCheck,
  Puzzle,
  RefreshCw,
  Search,
  ShieldAlert,
  SlidersHorizontal,
  TriangleAlert,
  X,
} from 'lucide-react'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { ErrorState, LoadingState } from '@/components/query-state'
import type { Compatibility, PluginEntry } from '../../../shared/types'

const compatibilityLabels: Record<Compatibility, string> = {
  supported: 'Проверен',
  experimental: 'Экспериментальный',
  protected: 'Защищён Elgato',
  unsupported: 'Не поддерживается',
  'installed-only': 'Только установлен',
}

function statusIcon(plugin: PluginEntry): React.JSX.Element {
  if (plugin.compatibility === 'supported') return <CheckCircle2 className="size-4 text-emerald-300" />
  if (plugin.compatibility === 'experimental') return <FlaskConical className="size-4 text-amber-300" />
  if (plugin.compatibility === 'protected') return <ShieldAlert className="size-4 text-violet-300" />
  return <TriangleAlert className="size-4 text-zinc-500" />
}

function badgeVariant(plugin: PluginEntry): 'success' | 'warning' | 'default' | 'muted' {
  if (plugin.compatibility === 'supported') return 'success'
  if (plugin.compatibility === 'experimental') return 'warning'
  if (plugin.compatibility === 'protected') return 'default'
  return 'muted'
}

export function PluginsPage(): React.JSX.Element {
  const [search, setSearch] = useState('')
  const [installation, setInstallation] = useState('all')
  const [compatibility, setCompatibility] = useState('all')
  const [source, setSource] = useState('all')
  const query = useQuery({
    queryKey: ['plugins'],
    queryFn: () => window.ajazz.plugins.list(),
  })

  const plugins = useMemo(() => {
    const needle = search.trim().toLocaleLowerCase('ru')
    return (query.data ?? []).filter((plugin) => {
      const matchesSearch =
        !needle ||
        [plugin.name, plugin.id, plugin.sourceLabel].some((value) =>
          value.toLocaleLowerCase('ru').includes(needle),
        )
      const matchesInstallation =
        installation === 'all' ||
        (installation === 'installed' && plugin.isInstalled) ||
        (installation === 'available' && !plugin.isInstalled)
      const matchesCompatibility = compatibility === 'all' || plugin.compatibility === compatibility
      const matchesSource = source === 'all' || plugin.sourceLabel === source
      return matchesSearch && matchesInstallation && matchesCompatibility && matchesSource
    })
  }, [compatibility, installation, query.data, search, source])

  const sources = useMemo(
    () => [...new Set((query.data ?? []).map((plugin) => plugin.sourceLabel))].sort((a, b) => a.localeCompare(b, 'ru')),
    [query.data],
  )
  const hasFilters = Boolean(search || installation !== 'all' || compatibility !== 'all' || source !== 'all')

  function resetFilters(): void {
    setSearch('')
    setInstallation('all')
    setCompatibility('all')
    setSource('all')
  }

  return (
    <section className="page">
      <div className="filter-toolbar">
        <div className="search-field">
          <Search className="size-4" />
          <Input value={search} onChange={(event) => setSearch(event.target.value)} placeholder="Поиск по названию или ID" />
        </div>
        <div className="filter-divider" />
        <SlidersHorizontal className="filter-icon size-4" />
        <select className="filter-select" value={installation} onChange={(event) => setInstallation(event.target.value)}>
          <option value="all">Любая установка</option>
          <option value="installed">Установленные</option>
          <option value="available">Не установленные</option>
        </select>
        <select className="filter-select" value={compatibility} onChange={(event) => setCompatibility(event.target.value)}>
          <option value="all">Любая совместимость</option>
          <option value="supported">Проверенные</option>
          <option value="experimental">Экспериментальные</option>
          <option value="protected">Защищённые Elgato</option>
          <option value="unsupported">Не поддерживаются</option>
          <option value="installed-only">Только установленные</option>
        </select>
        <select className="filter-select" value={source} onChange={(event) => setSource(event.target.value)}>
          <option value="all">Все источники</option>
          {sources.map((item) => <option key={item} value={item}>{item}</option>)}
        </select>
        {hasFilters ? (
          <Button variant="ghost" size="icon" onClick={resetFilters} title="Сбросить фильтры">
            <X className="size-4" />
          </Button>
        ) : null}
        <div className="filter-spacer" />
        <div className="toolbar-count">{plugins.length} / {query.data?.length ?? 0}</div>
        <Button variant="secondary" size="icon" onClick={() => void query.refetch()} disabled={query.isFetching} title="Обновить список">
          <RefreshCw className={query.isFetching ? 'size-4 animate-spin' : 'size-4'} />
        </Button>
      </div>

      {query.isPending ? <LoadingState label="Ищем плагины Stream Deck…" /> : null}
      {query.isError ? <ErrorState error={query.error} /> : null}

      {query.isSuccess ? (
        <div className="plugin-grid">
          {plugins.map((plugin) => (
            <article className="plugin-card" key={plugin.id}>
              <div className="plugin-icon">
                <Puzzle className="size-6" strokeWidth={1.7} />
              </div>
              <div className="plugin-body">
                <div className="plugin-title-row">
                  <div className="min-w-0">
                    <h2 title={plugin.name}>{plugin.name}</h2>
                    <div className="plugin-id" title={plugin.id}>{plugin.id}</div>
                  </div>
                  <Badge variant={badgeVariant(plugin)}>
                    {statusIcon(plugin)}
                    {compatibilityLabels[plugin.compatibility]}
                  </Badge>
                </div>
                <div className="plugin-meta">
                  <span>Версия {plugin.version}</span>
                  <span>{plugin.actionCount} действий</span>
                  <span>{plugin.sourceLabel}</span>
                </div>
                <div className="plugin-footer">
                  <div className={plugin.isInstalled ? 'installed-state text-emerald-300' : 'installed-state'}>
                    <PackageCheck className="size-4" />
                    {plugin.isInstalled ? `Установлен ${plugin.installedVersion ?? ''}` : 'Не установлен'}
                  </div>
                  <Button size="sm" variant="secondary" disabled title="Перенос установки будет следующим этапом">
                    {plugin.isInstalled ? 'Обновить' : 'Установить'}
                  </Button>
                </div>
              </div>
            </article>
          ))}
          {plugins.length === 0 ? <div className="empty-state">По этому запросу ничего не найдено.</div> : null}
        </div>
      ) : null}
    </section>
  )
}
