import { app } from 'electron'
import { existsSync } from 'node:fs'
import { join, resolve } from 'node:path'

export interface AppPaths {
  ajazzRoot: string
  ajazzPlugins: string
  ajazzProfiles: string
  ajazzExe?: string
  elgatoPlugins: string
  elgatoIconPacks: string
  managerRoot: string
  iconLibrary: string
  backupRoot: string
  packagedPlugins: string
}

function firstExisting(candidates: string[]): string | undefined {
  return candidates.find((candidate) => existsSync(candidate))
}

function findPackagedPlugins(): string {
  const candidates = app.isPackaged
    ? [join(process.resourcesPath, 'packages')]
    : [
        resolve(app.getAppPath(), '../ajazz-manager/release/Ajazz-Plugin-Manager/packages'),
        resolve(app.getAppPath(), '../ajazz-manager/release/packages'),
        resolve(app.getAppPath(), 'packages'),
      ]

  return firstExisting(candidates) ?? candidates[0]
}

export function getAppPaths(): AppPaths {
  const roaming = app.getPath('appData')
  const ajazzRoot = join(roaming, 'HotSpot', 'StreamDock')
  const managerRoot = join(roaming, 'AjazzPluginManager')
  const programFiles = process.env.ProgramFiles ?? 'C:\\Program Files'
  const programFilesX86 = process.env['ProgramFiles(x86)'] ?? 'C:\\Program Files (x86)'
  const ajazzExe = firstExisting([
    join(programFilesX86, 'HotSpot', 'Stream Dock AJAZZ.exe'),
    join(programFiles, 'HotSpot', 'Stream Dock AJAZZ.exe'),
  ])

  return {
    ajazzRoot,
    ajazzPlugins: join(ajazzRoot, 'plugins'),
    ajazzProfiles: join(ajazzRoot, 'profiles'),
    ajazzExe,
    elgatoPlugins: join(roaming, 'Elgato', 'StreamDeck', 'Plugins'),
    elgatoIconPacks: join(roaming, 'Elgato', 'StreamDeck', 'IconPacks'),
    managerRoot,
    iconLibrary: join(managerRoot, 'IconLibrary'),
    backupRoot: join(managerRoot, 'Backups'),
    packagedPlugins: findPackagedPlugins(),
  }
}
