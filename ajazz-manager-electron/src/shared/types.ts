export type Compatibility =
  | 'supported'
  | 'experimental'
  | 'protected'
  | 'unsupported'
  | 'installed-only'

export interface PluginEntry {
  id: string
  name: string
  version: string
  installedVersion?: string
  sourcePath: string
  sourceLabel: string
  codePath: string
  actionCount: number
  isInstalled: boolean
  isPackaged: boolean
  isIconEditor: boolean
  isProtected: boolean
  compatibility: Compatibility
}

export interface IconEntry {
  id: string
  name: string
  packName: string
  source: IconSource
  folder: string
  extension: string
  size: number
  previewUrl: string
}

export type IconSource = 'library' | 'elgato' | 'ajazz'

export interface IconGroup {
  id: string
  source: IconSource
  sourceLabel: string
  packName: string
  folder: string
  count: number
}

export interface IconLibraryResult {
  icons: IconEntry[]
  groups: IconGroup[]
}

export interface Diagnostics {
  ajazzFound: boolean
  ajazzRunning: boolean
  ajazzExe?: string
  ajazzPlugins: string
  elgatoFound: boolean
  elgatoPlugins: string
  elgatoPluginCount: number
  iconCount: number
  managerRoot: string
}

export interface AjazzApi {
  plugins: {
    list(): Promise<PluginEntry[]>
  }
  icons: {
    list(): Promise<IconLibraryResult>
  }
  diagnostics: {
    read(): Promise<Diagnostics>
  }
  app: {
    minimize(): Promise<void>
    toggleMaximize(): Promise<void>
    close(): Promise<void>
  }
}
