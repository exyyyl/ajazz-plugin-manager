import { contextBridge, ipcRenderer } from 'electron'
import type { AjazzApi } from '../shared/types'

const api: AjazzApi = {
  plugins: {
    list: () => ipcRenderer.invoke('plugins:list'),
  },
  icons: {
    list: () => ipcRenderer.invoke('icons:list'),
  },
  diagnostics: {
    read: () => ipcRenderer.invoke('diagnostics:read'),
  },
  app: {
    minimize: () => ipcRenderer.invoke('app:minimize'),
    toggleMaximize: () => ipcRenderer.invoke('app:toggle-maximize'),
    close: () => ipcRenderer.invoke('app:close'),
  },
}

contextBridge.exposeInMainWorld('ajazz', api)
