import { createHash } from 'node:crypto'
import { readFile, readdir, stat } from 'node:fs/promises'
import { basename, dirname, extname, join, relative, sep } from 'node:path'
import type { AppPaths } from './paths'
import type { IconEntry, IconGroup, IconLibraryResult, IconSource } from '../shared/types'

const extensions = new Set(['.png', '.jpg', '.jpeg', '.gif', '.bmp', '.svg', '.webp'])
const sourceLabels: Record<IconSource, string> = {
  library: 'Моя библиотека',
  elgato: 'Наборы Elgato',
  ajazz: 'Профили AJAZZ',
}

async function imageFiles(root: string): Promise<string[]> {
  const files: string[] = []

  async function walk(folder: string): Promise<void> {
    let entries
    try {
      entries = await readdir(folder, { withFileTypes: true })
    } catch {
      return
    }

    for (const entry of entries) {
      if (entry.name === '.history') continue
      const path = join(folder, entry.name)
      if (entry.isDirectory()) await walk(path)
      else if (entry.isFile() && extensions.has(extname(entry.name).toLowerCase())) files.push(path)
    }
  }

  await walk(root)
  return files
}

function assetId(path: string): string {
  return createHash('sha1').update(path.toLowerCase()).digest('hex')
}

function normalizedFolder(path: string): string {
  return path.split(sep).filter(Boolean).join(' / ') || 'Корень'
}

async function addIcons(
  result: IconEntry[],
  assetMap: Map<string, string>,
  root: string,
  source: IconSource,
  packName: string,
): Promise<void> {
  for (const path of await imageFiles(root)) {
    await addIcon(result, assetMap, path, root, source, packName)
  }
}

async function addIcon(
  result: IconEntry[],
  assetMap: Map<string, string>,
  path: string,
  root: string,
  source: IconSource,
  packName: string,
): Promise<void> {
  const id = assetId(path)
  const info = await stat(path)
  const folder = normalizedFolder(relative(root, dirname(path)))
  assetMap.set(id, path)
  result.push({
    id,
    name: basename(path, extname(path)),
    packName,
    source,
    folder,
    extension: extname(path).slice(1).toUpperCase(),
    size: info.size,
    previewUrl: `ajazz-icon://asset/${id}`,
  })
}

async function readPackName(folder: string): Promise<string> {
  const fallback = basename(folder).replace(/\.sdIconPack$/i, '')
  try {
    const manifest = JSON.parse(await readFile(join(folder, 'manifest.json'), 'utf8')) as { Name?: unknown }
    return typeof manifest.Name === 'string' && manifest.Name.trim() ? manifest.Name : fallback
  } catch {
    return fallback
  }
}

async function addLibrary(result: IconEntry[], assetMap: Map<string, string>, paths: AppPaths): Promise<void> {
  let packs
  try {
    packs = await readdir(paths.iconLibrary, { withFileTypes: true })
  } catch {
    return
  }

  const looseFiles = packs.filter((entry) => entry.isFile() && extensions.has(extname(entry.name).toLowerCase()))
  for (const file of looseFiles) {
    await addIcon(result, assetMap, join(paths.iconLibrary, file.name), paths.iconLibrary, 'library', 'Без набора')
  }

  for (const pack of packs.filter((entry) => entry.isDirectory())) {
    await addIcons(result, assetMap, join(paths.iconLibrary, pack.name), 'library', pack.name)
  }
}

async function addElgatoPacks(result: IconEntry[], assetMap: Map<string, string>, paths: AppPaths): Promise<void> {
  let packs
  try {
    packs = await readdir(paths.elgatoIconPacks, { withFileTypes: true })
  } catch {
    return
  }

  for (const pack of packs.filter((entry) => entry.isDirectory() && entry.name.toLowerCase().endsWith('.sdiconpack'))) {
    const folder = join(paths.elgatoIconPacks, pack.name)
    await addIcons(result, assetMap, join(folder, 'icons'), 'elgato', await readPackName(folder))
  }
}

async function addAjazzProfiles(result: IconEntry[], assetMap: Map<string, string>, paths: AppPaths): Promise<void> {
  let profiles
  try {
    profiles = await readdir(paths.ajazzProfiles, { withFileTypes: true })
  } catch {
    return
  }

  for (const profile of profiles.filter((entry) => entry.isDirectory())) {
    const name = profile.name.replace(/\.sdProfile$/i, '')
    await addIcons(result, assetMap, join(paths.ajazzProfiles, profile.name), 'ajazz', name)
  }
}

function createGroups(icons: IconEntry[]): IconGroup[] {
  const groups = new Map<string, IconGroup>()
  for (const icon of icons) {
    const id = `${icon.source}:${icon.packName}:${icon.folder}`
    const existing = groups.get(id)
    if (existing) existing.count += 1
    else {
      groups.set(id, {
        id,
        source: icon.source,
        sourceLabel: sourceLabels[icon.source],
        packName: icon.packName,
        folder: icon.folder,
        count: 1,
      })
    }
  }
  return [...groups.values()].sort(
    (left, right) =>
      left.sourceLabel.localeCompare(right.sourceLabel, 'ru') ||
      left.packName.localeCompare(right.packName, 'ru') ||
      left.folder.localeCompare(right.folder, 'ru'),
  )
}

export async function discoverIcons(paths: AppPaths, assetMap: Map<string, string>): Promise<IconLibraryResult> {
  const icons: IconEntry[] = []
  assetMap.clear()
  await Promise.all([
    addLibrary(icons, assetMap, paths),
    addElgatoPacks(icons, assetMap, paths),
    addAjazzProfiles(icons, assetMap, paths),
  ])
  icons.sort(
    (left, right) =>
      left.source.localeCompare(right.source) ||
      left.packName.localeCompare(right.packName, 'ru') ||
      left.folder.localeCompare(right.folder, 'ru') ||
      left.name.localeCompare(right.name, 'ru'),
  )
  return { icons, groups: createGroups(icons) }
}
