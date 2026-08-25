import { useQuery } from '@tanstack/react-query'
import { Link, Outlet } from '@tanstack/react-router'
import { Activity, ArchiveRestore, Images, PlugZap } from 'lucide-react'
import type { LucideIcon } from 'lucide-react'
import { cn } from '@/lib/utils'

interface NavigationItem {
  to: '/plugins' | '/icons' | '/backups' | '/diagnostics'
  label: string
  icon: LucideIcon
}

const navigation: NavigationItem[] = [
  { to: '/plugins', label: 'Плагины', icon: PlugZap },
  { to: '/icons', label: 'Иконки', icon: Images },
  { to: '/backups', label: 'Резервные копии', icon: ArchiveRestore },
  { to: '/diagnostics', label: 'Диагностика', icon: Activity },
]

export function AppShell(): React.JSX.Element {
  const diagnostics = useQuery({
    queryKey: ['diagnostics'],
    queryFn: () => window.ajazz.diagnostics.read(),
    staleTime: 30_000,
  })

  return (
    <div className="app-shell">
      <div className="window-drag" aria-hidden="true" />
      <aside className="sidebar">
        <nav className="nav-list" aria-label="Основная навигация">
          {navigation.map(({ to, label, icon: Icon }) => (
            <Link
              key={to}
              to={to}
              className="nav-item"
              activeProps={{ className: 'nav-item nav-item-active' }}
            >
              <Icon className="size-[18px]" strokeWidth={1.8} />
              <span>{label}</span>
            </Link>
          ))}
        </nav>

        <div className="sidebar-status">
          <div className="sidebar-caption">Состояние</div>
          <StatusLine
            online={Boolean(diagnostics.data?.ajazzFound)}
            label={diagnostics.data?.ajazzFound ? 'AJAZZ найден' : 'AJAZZ не найден'}
          />
          <StatusLine
            online={Boolean(diagnostics.data?.elgatoFound)}
            label={
              diagnostics.data?.elgatoFound
                ? `Elgato · ${diagnostics.data.elgatoPluginCount} плагина`
                : 'Elgato не найден'
            }
          />
        </div>
      </aside>

      <main className={cn('main-content')}>
        <Outlet />
      </main>
    </div>
  )
}

function StatusLine({ online, label }: { online: boolean; label: string }): React.JSX.Element {
  return (
    <div className={cn('status-line', online ? 'text-emerald-300' : 'text-zinc-500')}>
      <span className={cn('size-1.5 rounded-full', online ? 'bg-emerald-400' : 'bg-zinc-600')} />
      <span>{label}</span>
    </div>
  )
}
