import React from 'react'
import ReactDOM from 'react-dom/client'
import '@fontsource-variable/manrope'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import {
  createRootRoute,
  createRoute,
  createHashHistory,
  createRouter,
  Navigate,
  RouterProvider,
} from '@tanstack/react-router'
import { AppShell } from '@/components/app-shell'
import { BackupsPage } from '@/pages/backups-page'
import { DiagnosticsPage } from '@/pages/diagnostics-page'
import { IconsPage } from '@/pages/icons-page'
import { PluginsPage } from '@/pages/plugins-page'
import './styles.css'

const rootRoute = createRootRoute({ component: AppShell })
const indexRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/',
  component: () => <Navigate to="/plugins" replace />,
})
const pluginsRoute = createRoute({ getParentRoute: () => rootRoute, path: '/plugins', component: PluginsPage })
const iconsRoute = createRoute({ getParentRoute: () => rootRoute, path: '/icons', component: IconsPage })
const backupsRoute = createRoute({ getParentRoute: () => rootRoute, path: '/backups', component: BackupsPage })
const diagnosticsRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/diagnostics',
  component: DiagnosticsPage,
})

const routeTree = rootRoute.addChildren([indexRoute, pluginsRoute, iconsRoute, backupsRoute, diagnosticsRoute])
const router = createRouter({ routeTree, history: createHashHistory(), defaultPreload: 'intent' })
const queryClient = new QueryClient({
  defaultOptions: {
    queries: { retry: 1, refetchOnWindowFocus: false },
  },
})

declare module '@tanstack/react-router' {
  interface Register {
    router: typeof router
  }
}

ReactDOM.createRoot(document.getElementById('root')!).render(
  <React.StrictMode>
    <QueryClientProvider client={queryClient}>
      <RouterProvider router={router} />
    </QueryClientProvider>
  </React.StrictMode>,
)
