import { open, readFile, readdir } from 'node:fs/promises'
import { basename, extname, join } from 'node:path'
import type { AppPaths } from './paths'
import type { Compatibility, PluginEntry } from '../shared/types'

const TWITCH_ID = 'com.elgato.twitch.sdPlugin'

interface ManifestSummary {
  id: string
  name: string
  version: string
  codePath: string
  actionCount: number
  hasIconEditor: boolean
  isProtected: boolean
}

async function directoryExists(path: string): Promise<boolean> {
  try {
    return (await readdir(path)).length >= 0
  } catch {
    return false
  }
}

async function pluginFolders(root: string): Promise<string[]> {
  try {
    const entries = await readdir(root, { withFileTypes: true })
    return entries
      .filter((entry) => entry.isDirectory() && entry.name.toLowerCase().endsWith('.sdplugin'))
      .map((entry) => join(root, entry.name))
  } catch {
    return []
  }
}

function stripBom(value: string): string {
  return value.charCodeAt(0) === 0xfeff ? value.slice(1) : value
}

async function readJson(path: string): Promise<Record<string, unknown>> {
  return JSON.parse(stripBom(await readFile(path, 'utf8'))) as Record<string, unknown>
}

async function isProtectedManifest(path: string): Promise<boolean> {
  const handle = await open(path, 'r')
  try {
    const signature = Buffer.alloc(6)
    await handle.read(signature, 0, 6, 0)
    return signature.toString('ascii') === 'ELGATO'
  } finally {
    await handle.close()
  }
}

async function readProtectedManifest(folder: string): Promise<ManifestSummary> {
  const folderName = basename(folder)
  const id = folderName.replace(/\.sdPlugin$/i, '')
  let localization: Record<string, unknown> = {}
  try {
    localization = await readJson(join(folder, 'en.json'))
  } catch {
    // A protected plugin can still be shown using its folder name.
  }

  const files = await readdir(folder, { withFileTypes: true })
  const executable = files.find((entry) => entry.isFile() && extname(entry.name).toLowerCase() === '.exe')

  return {
    id,
    name: typeof localization.Name === 'string' ? localization.Name : id,
    version: 'защищён',
    codePath: executable?.name ?? '',
    actionCount: Object.keys(localization).filter((key) => key.toLowerCase().startsWith(`${id.toLowerCase()}.`)).length,
    hasIconEditor: false,
    isProtected: true,
  }
}

async function readManifest(folder: string): Promise<ManifestSummary> {
  const manifestPath = join(folder, 'manifest.json')
  if (await isProtectedManifest(manifestPath)) return readProtectedManifest(folder)

  const manifest = await readJson(manifestPath)
  return {
    id: typeof manifest.UUID === 'string' ? manifest.UUID : '',
    name: typeof manifest.Name === 'string' ? manifest.Name : basename(folder),
    version: typeof manifest.Version === 'string' ? manifest.Version : '—',
    codePath: typeof manifest.CodePath === 'string' ? manifest.CodePath : '',
    actionCount: Array.isArray(manifest.Actions) ? manifest.Actions.length : 0,
    hasIconEditor: typeof manifest.IconEditorPath === 'string',
    isProtected: false,
  }
}

function compatibilityFor(id: string, manifest: ManifestSummary, packaged: boolean): Compatibility {
  if (id.toLowerCase() === TWITCH_ID.toLowerCase() && packaged) return 'supported'
  if (manifest.isProtected) return 'protected'
  if (manifest.actionCount === 0) return 'unsupported'
  return 'experimental'
}

function createEntry(
  folder: string,
  manifest: ManifestSummary,
  packaged: boolean,
  sourceLabel: string,
): PluginEntry {
  const id = basename(folder)
  return {
    id,
    name: manifest.name || id,
    version: manifest.version,
    sourcePath: folder,
    sourceLabel,
    codePath: manifest.codePath,
    actionCount: manifest.actionCount,
    isInstalled: false,
    isPackaged: packaged,
    isIconEditor: manifest.hasIconEditor,
    isProtected: manifest.isProtected,
    compatibility: compatibilityFor(id, manifest, packaged),
  }
}

async function addSource(
  result: Map<string, PluginEntry>,
  root: string,
  packaged: boolean,
  sourceLabel: string,
): Promise<void> {
  for (const folder of await pluginFolders(root)) {
    const id = basename(folder).toLowerCase()
    if (result.has(id)) continue
    try {
      result.set(id, createEntry(folder, await readManifest(folder), packaged, sourceLabel))
    } catch (error) {
      console.warn(`Plugin skipped: ${folder}`, error)
    }
  }
}

export async function discoverPlugins(paths: AppPaths): Promise<PluginEntry[]> {
  const result = new Map<string, PluginEntry>()
  await addSource(result, paths.packagedPlugins, true, 'Встроенный пакет')
  await addSource(result, paths.elgatoPlugins, false, 'Elgato Stream Deck')

  for (const folder of await pluginFolders(paths.ajazzPlugins)) {
    const key = basename(folder).toLowerCase()
    try {
      const manifest = await readManifest(folder)
      let entry = result.get(key)
      if (!entry) {
        entry = createEntry(folder, manifest, false, 'Установлен в AJAZZ')
        entry.compatibility = 'installed-only'
        result.set(key, entry)
      }
      entry.isInstalled = true
      entry.installedVersion = manifest.version
    } catch (error) {
      console.warn(`Installed plugin skipped: ${folder}`, error)
    }
  }

  const order: Record<Compatibility, number> = {
    supported: 0,
    experimental: 1,
    protected: 2,
    unsupported: 3,
    'installed-only': 4,
  }

  return [...result.values()].sort(
    (left, right) => order[left.compatibility] - order[right.compatibility] || left.name.localeCompare(right.name, 'ru'),
  )
}

export async function countElgatoPlugins(paths: AppPaths): Promise<number> {
  if (!(await directoryExists(paths.elgatoPlugins))) return 0
  return (await pluginFolders(paths.elgatoPlugins)).length
}
