/// <reference types="vite/client" />

import type { AjazzApi } from '../../shared/types'

declare global {
  interface Window {
    ajazz: AjazzApi
  }
}

export {}
