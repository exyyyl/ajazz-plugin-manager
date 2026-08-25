import { useEffect, useMemo, useRef, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { useVirtualizer } from '@tanstack/react-virtual'
import { ChevronRight, Folder, FolderOpen, Images, Layers3, RefreshCw, Search, SlidersHorizontal, X } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { ErrorState, LoadingState } from '@/components/query-state'
import { cn, formatBytes } from '@/lib/utils'
import type { IconEntry, IconGroup, IconSource } from '../../../shared/types'

const sourceNames: Record<IconSource, string> = {
  library: 'Моя библиотека',
  elgato: 'Наборы Elgato',
  ajazz: 'Профили AJAZZ',
}

function useElementWidth<T extends HTMLElement>(ref: React.RefObject<T | null>): number {
  const [width, setWidth] = useState(0)
  useEffect(() => {
    const element = ref.current
    if (!element) return
    const observer = new ResizeObserver(([entry]) => setWidth(entry.contentRect.width))
    observer.observe(element)
    setWidth(element.clientWidth)
    return () => observer.disconnect()
  }, [ref])
  return width
}

function packKey(source: IconSource, packName: string): string {
  return `pack:${source}:${packName}`
}

function sourceKey(source: IconSource): string {
  return `source:${source}`
}

export function IconsPage(): React.JSX.Element {
  const [search, setSearch] = useState('')
  const [selectedGroup, setSelectedGroup] = useState('all')
  const [sourceFilter, setSourceFilter] = useState('all')
  const [formatFilter, setFormatFilter] = useState('all')
  const [selectedIcon, setSelectedIcon] = useState<IconEntry>()
  const query = useQuery({
    queryKey: ['icons'],
    queryFn: () => window.ajazz.icons.list(),
    staleTime: 30_000,
  })

  const grouped = useMemo(() => buildGroupTree(query.data?.groups ?? []), [query.data?.groups])
  const filtered = useMemo(() => {
    const needle = search.trim().toLocaleLowerCase('ru')
    return (query.data?.icons ?? []).filter((icon) => {
      const groupId = `${icon.source}:${icon.packName}:${icon.folder}`
      const groupMatches =
        selectedGroup === 'all' ||
        selectedGroup === sourceKey(icon.source) ||
        selectedGroup === packKey(icon.source, icon.packName) ||
        selectedGroup === `group:${groupId}`
      const searchMatches =
        !needle ||
        [icon.name, icon.packName, icon.folder, icon.extension].some((value) =>
          value.toLocaleLowerCase('ru').includes(needle),
        )
      const sourceMatches = sourceFilter === 'all' || icon.source === sourceFilter
      const formatMatches = formatFilter === 'all' || icon.extension === formatFilter
      return groupMatches && searchMatches && sourceMatches && formatMatches
    })
  }, [formatFilter, query.data?.icons, search, selectedGroup, sourceFilter])

  const selectedLabel = getSelectedLabel(selectedGroup, query.data?.groups ?? [])
  const formats = useMemo(
    () => [...new Set((query.data?.icons ?? []).map((icon) => icon.extension))].sort(),
    [query.data?.icons],
  )
  const hasFilters = Boolean(search || sourceFilter !== 'all' || formatFilter !== 'all')

  return (
    <section className="page page-icons">
      <div className="filter-toolbar">
        <div className="search-field">
          <Search className="size-4" />
          <Input value={search} onChange={(event) => setSearch(event.target.value)} placeholder="Поиск по имени, набору или папке" />
        </div>
        <div className="filter-divider" />
        <SlidersHorizontal className="filter-icon size-4" />
        <select className="filter-select" value={sourceFilter} onChange={(event) => setSourceFilter(event.target.value)}>
          <option value="all">Все источники</option>
          <option value="library">Моя библиотека</option>
          <option value="elgato">Наборы Elgato</option>
          <option value="ajazz">Профили AJAZZ</option>
        </select>
        <select className="filter-select filter-select-short" value={formatFilter} onChange={(event) => setFormatFilter(event.target.value)}>
          <option value="all">Все форматы</option>
          {formats.map((format) => <option key={format} value={format}>{format}</option>)}
        </select>
        {hasFilters ? (
          <Button variant="ghost" size="icon" onClick={() => { setSearch(''); setSourceFilter('all'); setFormatFilter('all') }} title="Сбросить фильтры">
            <X className="size-4" />
          </Button>
        ) : null}
        <div className="filter-spacer" />
        <div className="toolbar-count">{filtered.length} / {query.data?.icons.length ?? 0}</div>
        <Button variant="secondary" size="icon" onClick={() => void query.refetch()} disabled={query.isFetching} title="Обновить библиотеку">
          <RefreshCw className={query.isFetching ? 'size-4 animate-spin' : 'size-4'} />
        </Button>
      </div>

      {query.isPending ? <LoadingState label="Индексируем библиотеку иконок…" /> : null}
      {query.isError ? <ErrorState error={query.error} /> : null}

      {query.isSuccess ? (
        <div className="icon-workspace">
          <aside className="group-panel">
            <button
              className={cn('group-all', selectedGroup === 'all' && 'group-selected')}
              onClick={() => setSelectedGroup('all')}
            >
              <Layers3 className="size-4" />
              <span>Все иконки</span>
              <strong>{query.data.icons.length}</strong>
            </button>
            <div className="group-scroll">
              {grouped.map((source) => (
                <details key={source.source} open className="source-group">
                  <summary>
                    <ChevronRight className="chevron size-3.5" />
                    <Images className="size-4 text-violet-300" />
                    <button onClick={() => setSelectedGroup(sourceKey(source.source))}>{sourceNames[source.source]}</button>
                    <span>{source.count}</span>
                  </summary>
                  <div className="pack-list">
                    {source.packs.map((pack) => (
                      <details key={pack.name} open={source.packs.length < 5} className="pack-group">
                        <summary>
                          <ChevronRight className="chevron size-3.5" />
                          <Folder className="size-4 text-amber-300/80" />
                          <button
                            className={cn(selectedGroup === packKey(source.source, pack.name) && 'text-white')}
                            onClick={() => setSelectedGroup(packKey(source.source, pack.name))}
                            title={pack.name}
                          >
                            {pack.name}
                          </button>
                          <span>{pack.count}</span>
                        </summary>
                        <div className="folder-list">
                          {pack.groups.map((group) => (
                            <button
                              key={group.id}
                              className={cn('folder-item', selectedGroup === `group:${group.id}` && 'group-selected')}
                              onClick={() => setSelectedGroup(`group:${group.id}`)}
                              title={group.folder}
                            >
                              <FolderOpen className="size-3.5" />
                              <span>{group.folder}</span>
                              <strong>{group.count}</strong>
                            </button>
                          ))}
                        </div>
                      </details>
                    ))}
                  </div>
                </details>
              ))}
            </div>
          </aside>

          <div className="icon-browser">
            <div className="icon-browser-toolbar">
              <div className="min-w-0">
                <h2 title={selectedLabel}>{selectedLabel}</h2>
                <span>{filtered.length} иконок</span>
              </div>
            </div>
            <VirtualIconGrid icons={filtered} selected={selectedIcon?.id} onSelect={setSelectedIcon} />
          </div>

          <aside className="preview-panel">
            <div className="preview-title">Предпросмотр</div>
            {selectedIcon ? (
              <>
                <div className="preview-image-wrap">
                  <img src={selectedIcon.previewUrl} alt="" draggable={false} />
                </div>
                <h3>{selectedIcon.name}</h3>
                <dl>
                  <div><dt>Набор</dt><dd>{selectedIcon.packName}</dd></div>
                  <div><dt>Папка</dt><dd>{selectedIcon.folder}</dd></div>
                  <div><dt>Формат</dt><dd>{selectedIcon.extension}</dd></div>
                  <div><dt>Размер</dt><dd>{formatBytes(selectedIcon.size)}</dd></div>
                </dl>
              </>
            ) : (
              <div className="preview-empty">
                <Images className="size-7" />
                <span>Выберите иконку</span>
              </div>
            )}
          </aside>
        </div>
      ) : null}
    </section>
  )
}

function VirtualIconGrid({
  icons,
  selected,
  onSelect,
}: {
  icons: IconEntry[]
  selected?: string
  onSelect: (icon: IconEntry) => void
}): React.JSX.Element {
  const scrollRef = useRef<HTMLDivElement>(null)
  const width = useElementWidth(scrollRef)
  const columns = Math.max(1, Math.floor((width - 24) / 124))
  const rows = Math.ceil(icons.length / columns)
  const virtualizer = useVirtualizer({
    count: rows,
    getScrollElement: () => scrollRef.current,
    estimateSize: () => 134,
    overscan: 4,
  })

  useEffect(() => {
    virtualizer.measure()
  }, [columns, virtualizer])

  return (
    <div className="virtual-grid-scroll" ref={scrollRef}>
      {icons.length === 0 ? <div className="empty-state">В этой группе ничего не найдено.</div> : null}
      <div className="virtual-grid-space" style={{ height: virtualizer.getTotalSize() }}>
        {virtualizer.getVirtualItems().map((virtualRow) => {
          const rowIcons = icons.slice(virtualRow.index * columns, virtualRow.index * columns + columns)
          return (
            <div
              key={virtualRow.key}
              className="virtual-grid-row"
              style={{ transform: `translateY(${virtualRow.start}px)`, gridTemplateColumns: `repeat(${columns}, minmax(0, 1fr))` }}
            >
              {rowIcons.map((icon) => (
                <button
                  key={icon.id}
                  className={cn('icon-card', selected === icon.id && 'icon-card-selected')}
                  onClick={() => onSelect(icon)}
                  title={`${icon.name}\n${icon.folder}`}
                >
                  <span className="icon-image-wrap"><img src={icon.previewUrl} alt="" loading="lazy" draggable={false} /></span>
                  <span>{icon.name}</span>
                  <small>{icon.extension}</small>
                </button>
              ))}
            </div>
          )
        })}
      </div>
    </div>
  )
}

interface GroupTreeSource {
  source: IconSource
  count: number
  packs: Array<{ name: string; count: number; groups: IconGroup[] }>
}

function buildGroupTree(groups: IconGroup[]): GroupTreeSource[] {
  const sources = new Map<IconSource, Map<string, IconGroup[]>>()
  for (const group of groups) {
    const packs = sources.get(group.source) ?? new Map<string, IconGroup[]>()
    packs.set(group.packName, [...(packs.get(group.packName) ?? []), group])
    sources.set(group.source, packs)
  }

  return [...sources.entries()].map(([source, packs]) => {
    const packItems = [...packs.entries()].map(([name, packGroups]) => ({
      name,
      groups: packGroups,
      count: packGroups.reduce((sum, group) => sum + group.count, 0),
    }))
    return {
      source,
      packs: packItems,
      count: packItems.reduce((sum, pack) => sum + pack.count, 0),
    }
  })
}

function getSelectedLabel(selected: string, groups: IconGroup[]): string {
  if (selected === 'all') return 'Все иконки'
  if (selected.startsWith('source:')) return sourceNames[selected.slice(7) as IconSource]
  if (selected.startsWith('pack:')) return selected.split(':').slice(2).join(':')
  const group = groups.find((item) => `group:${item.id}` === selected)
  return group ? `${group.packName} / ${group.folder}` : 'Иконки'
}
