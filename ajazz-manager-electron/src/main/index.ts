import { app, BrowserWindow, ipcMain, net, protocol, type IpcMainInvokeEvent } from 'electron'
import { execFileSync } from 'node:child_process'
import { existsSync } from 'node:fs'
import { join, resolve } from 'node:path'
import { pathToFileURL, fileURLToPath } from 'node:url'
import { discoverIcons } from './icons'
import { countElgatoPlugins, discoverPlugins } from './plugins'
import { getAppPaths } from './paths'

const currentDirectory = fileURLToPath(new URL('.', import.meta.url))
const iconAssets = new Map<string, string>()

protocol.registerSchemesAsPrivileged([
  {
    scheme: 'ajazz-icon',
    privileges: { standard: true, secure: true, supportFetchAPI: true },
  },
])

function isTrustedSender(url: string): boolean {
  if (url.startsWith('file://')) return true
  if (!app.isPackaged && /^https?:\/\/(localhost|127\.0\.0\.1)(:\d+)?\//.test(url)) return true
  return false
}

function isTrustedEvent(event: IpcMainInvokeEvent): boolean {
  return Boolean(event.senderFrame && isTrustedSender(event.senderFrame.url))
}

function isAjazzRunning(): boolean {
  try {
    return execFileSync('tasklist.exe', ['/FI', 'IMAGENAME eq Stream Dock AJAZZ.exe', '/NH'], {
      encoding: 'utf8',
      windowsHide: true,
    })
      .toLowerCase()
      .includes('stream dock ajazz.exe')
  } catch {
    return false
  }
}

function registerIpc(): void {
  ipcMain.handle('plugins:list', async (event) => {
    if (!isTrustedEvent(event)) throw new Error('Недоверенный источник IPC.')
    const plugins = await discoverPlugins(getAppPaths())
    console.info(`[plugins] discovered: ${plugins.length}`)
    return plugins
  })

  ipcMain.handle('icons:list', async (event) => {
    if (!isTrustedEvent(event)) throw new Error('Недоверенный источник IPC.')
    const result = await discoverIcons(getAppPaths(), iconAssets)
    console.info(`[icons] discovered: ${result.icons.length}, groups: ${result.groups.length}`)
    return result
  })

  ipcMain.handle('diagnostics:read', async (event) => {
    if (!isTrustedEvent(event)) throw new Error('Недоверенный источник IPC.')
    const paths = getAppPaths()
    const icons = await discoverIcons(paths, iconAssets)
    const result = {
      ajazzFound: Boolean(paths.ajazzExe),
      ajazzRunning: isAjazzRunning(),
      ajazzExe: paths.ajazzExe,
      ajazzPlugins: paths.ajazzPlugins,
      elgatoFound: existsSync(paths.elgatoPlugins),
      elgatoPlugins: paths.elgatoPlugins,
      elgatoPluginCount: await countElgatoPlugins(paths),
      iconCount: icons.icons.length,
      managerRoot: paths.managerRoot,
    }
    console.info(`[diagnostics] ajazz=${result.ajazzFound}, elgato=${result.elgatoFound}, icons=${result.iconCount}`)
    return result
  })

  ipcMain.handle('app:minimize', (event) => {
    if (!isTrustedEvent(event)) return
    BrowserWindow.fromWebContents(event.sender)?.minimize()
  })
  ipcMain.handle('app:toggle-maximize', (event) => {
    if (!isTrustedEvent(event)) return
    const window = BrowserWindow.fromWebContents(event.sender)
    if (!window) return
    if (window.isMaximized()) window.unmaximize()
    else window.maximize()
  })
  ipcMain.handle('app:close', (event) => {
    if (!isTrustedEvent(event)) return
    BrowserWindow.fromWebContents(event.sender)?.close()
  })
}

function findWindowIcon(): string | undefined {
  const candidates = [
    resolve(app.getAppPath(), 'resources/AjazzPluginManager.ico'),
    resolve(app.getAppPath(), '../ajazz-manager/assets/AjazzPluginManager.ico'),
    join(process.resourcesPath, 'AjazzPluginManager.ico'),
  ]
  return candidates.find(existsSync)
}

function createWindow(): void {
  const window = new BrowserWindow({
    width: 1240,
    height: 800,
    minWidth: 980,
    minHeight: 640,
    show: false,
    backgroundColor: '#0b0d13',
    icon: findWindowIcon(),
    title: 'Ajazz Plugin Manager',
    titleBarStyle: 'hidden',
    titleBarOverlay: {
      color: '#0b0d13',
      symbolColor: '#a1a1aa',
      height: 44,
    },
    webPreferences: {
      preload: join(currentDirectory, '../preload/index.cjs'),
      nodeIntegration: false,
      contextIsolation: true,
      sandbox: true,
    },
  })

  window.once('ready-to-show', () => window.show())
  window.webContents.on('did-finish-load', () => console.info(`[renderer] loaded: ${window.webContents.getURL()}`))
  window.webContents.on('preload-error', (_event, preloadPath, error) => console.error(`[preload] ${preloadPath}`, error))

  if (!app.isPackaged && process.env.ELECTRON_RENDERER_URL) {
    void window.loadURL(process.env.ELECTRON_RENDERER_URL)
  } else {
    void window.loadFile(join(currentDirectory, '../renderer/index.html'))
  }
}

app.whenReady().then(() => {
  protocol.handle('ajazz-icon', (request) => {
    const id = new URL(request.url).pathname.replace(/^\//, '')
    const path = iconAssets.get(id)
    if (!path) return new Response('Not found', { status: 404 })
    return net.fetch(pathToFileURL(path).toString())
  })

  registerIpc()
  createWindow()

  app.on('activate', () => {
    if (BrowserWindow.getAllWindows().length === 0) createWindow()
  })
})

app.on('window-all-closed', () => {
  if (process.platform !== 'darwin') app.quit()
})
