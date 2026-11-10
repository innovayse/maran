import type { Page, Request } from '@playwright/test'
import type { PanelModule } from '../../src/types/module'

/** The machine name of the sample module the specs install. */
export const PACKAGED_ID = 'hello'

/** The version the sample module runs. */
export const PACKAGED_VERSION = '1.0.0'

/** The frame address the panel builds for it. */
export const PACKAGED_FRAME_URL = `/module-frames/${PACKAGED_ID}/${PACKAGED_VERSION}/`

/** The interface languages the panel ships. */
export type SpecLocale = 'en' | 'ru' | 'hy'

/** What the backend localizes for the sample module, per language. */
export const PACKAGED_TEXT: Readonly<Record<SpecLocale, { name: string; menu: string; title: string; other: string }>> = {
  en: { name: 'Hello module', menu: 'Hello menu', title: 'Hello from the module', other: 'The module\'s other screen' },
  ru: { name: 'Модуль «Привет»', menu: 'Меню «Привет»', title: 'Привет от модуля', other: 'Другой экран модуля' },
  hy: { name: 'Բարև մոդուլ', menu: 'Բարև ընտրացանկ', title: 'Բարև մոդուլից', other: 'Մոդուլի մյուս էկրանը' },
}

/**
 * The catalogue entry of the installed sample module, in the language the request carried.
 * @param locale The language the backend localized in.
 * @param overrides Members to replace, for a degraded or failed variant.
 * @returns The entry.
 */
export const packagedModule = (locale: SpecLocale, overrides: Partial<PanelModule> = {}): PanelModule => {
  const text = PACKAGED_TEXT[locale]
  return {
    name: PACKAGED_ID,
    displayName: text.name,
    kindDisplayName: 'Installed',
    tier: 'included',
    state: 'enabled',
    version: PACKAGED_VERSION,
    lifecycle: 'active',
    isPackaged: true,
    spa: { frameUrl: PACKAGED_FRAME_URL, integrity: 'sha384-stub', locales: ['en'] },
    failureDisplay: null,
    menu: [{ key: 'hello', routeName: 'hello', iconKey: 'grid', label: text.menu, order: 1 }],
    routes: [
      { name: 'hello', path: '/hello', title: text.title, requiresModule: null },
      { name: 'other', path: '/other', title: text.other, requiresModule: null },
    ],
    ...overrides,
  }
}

/** A compiled-in module the catalogue also carries, so the shell has something besides the sample. */
export const COMPILED_IN_MODULE: PanelModule = {
  name: 'sites',
  displayName: 'Sites',
  kindDisplayName: 'Core',
  tier: 'included',
  state: 'enabled',
}

/** A handle on the catalogue the specs serve, which a spec changes between reads. */
export interface LiveCatalogue {
  /**
   * Replaces what the next read returns.
   * @param build Builds the catalogue for the language a read carries.
   * @returns Nothing.
   */
  replace: (build: (locale: SpecLocale) => PanelModule[]) => void
  /** Every URL the shell asked the API for, in order, catalogue reads included. */
  apiRequests: string[]
  /**
   * How many times the catalogue was read.
   * @returns The count.
   */
  reads: () => number
}

/**
 * Reads the language a request carried from its `Accept-Language` header.
 * @param request The intercepted request.
 * @returns The language, English when the header is absent or unknown.
 */
const localeOf = (request: Request): SpecLocale => {
  const header = (request.headers()['accept-language'] ?? '').slice(0, 2)
  return header === 'ru' || header === 'hy' ? header : 'en'
}

/**
 * Serves `GET /api/v1/modules` from a catalogue the spec can replace, localized per request the
 * way the backend does, and records every API request the page makes.
 * @param page The Playwright page whose network the routes are installed on.
 * @param initial Builds the first catalogue for a language.
 * @returns The handle that replaces the catalogue and exposes the request log.
 */
export const stubLiveCatalogue = async (
  page: Page,
  initial: (locale: SpecLocale) => PanelModule[],
): Promise<LiveCatalogue> => {
  let build = initial
  let reads = 0
  const apiRequests: string[] = []
  page.on('request', (request) => {
    const url = new URL(request.url())
    if (url.pathname.startsWith('/api/v1/')) {
      apiRequests.push(url.pathname)
    }
  })
  await page.route('**/api/v1/modules', async (route) => {
    reads += 1
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(build(localeOf(route.request()))),
    })
  })
  return {
    replace: (next): void => {
      build = next
    },
    apiRequests,
    reads: (): number => {
      return reads
    },
  }
}

/** How the stubbed panel serves the sample module's frame. */
export interface FrameStubOptions {
  /** The bundle served as `entry.js`. */
  script: string
  /** The module whose address is served; the sample by default. */
  moduleId?: string
  /** The version whose address is served; the sample's by default. */
  version?: string
}

/** A handle on the stubbed frame route. */
export interface FrameStub {
  /**
   * How many times the frame document was requested: one per mount.
   * @returns The count.
   */
  documentLoads: () => number
  /**
   * Replaces the bundle the next mount loads, to let a retry succeed where the first attempt failed.
   * @param script The new bundle.
   * @returns Nothing.
   */
  setScript: (script: string) => void
}

/**
 * The `sha384-<base64>` integrity value of a script, computed with the Web Crypto API both Node and
 * the browser provide.
 * @param script The script source.
 * @returns The integrity attribute value.
 */
const integrityOf = async (script: string): Promise<string> => {
  const digest = await crypto.subtle.digest('SHA-384', new TextEncoder().encode(script))
  return `sha384-${btoa(String.fromCharCode(...new Uint8Array(digest)))}`
}

/**
 * The document the panel's frame route generates: nothing inline, one classic script with its
 * integrity hash, exactly as the contract describes.
 * @param script The bundle the document loads.
 * @returns The HTML.
 */
const frameDocument = async (script: string): Promise<string> => {
  const integrity = await integrityOf(script)
  return (
    '<!doctype html><html lang="en"><head><meta charset="utf-8"><title>module</title></head><body>' +
    `<script src="entry.js" integrity="${integrity}" crossorigin="anonymous"></script></body></html>`
  )
}

/**
 * Serves the frame route `/module-frames/{id}/{version}/` with the headers the real one sends:
 * the frame CSP, `nosniff`, a wildcard CORS header (the opaque-origin frame needs it for the
 * integrity check) and no frame-denying header. Anything else under the prefix is a 404, never a 403.
 * @param page The Playwright page whose network the route is installed on.
 * @param options The bundle and, optionally, the address to serve it at.
 * @returns The handle that counts document loads.
 */
export const stubModuleFrame = async (page: Page, options: FrameStubOptions): Promise<FrameStub> => {
  const moduleId = options.moduleId ?? PACKAGED_ID
  const version = options.version ?? PACKAGED_VERSION
  const prefix = `/module-frames/${moduleId}/${version}/`
  let documentLoads = 0
  let script = options.script
  const headers = {
    'x-content-type-options': 'nosniff',
    'access-control-allow-origin': '*',
    'referrer-policy': 'no-referrer',
    'content-security-policy':
      "default-src 'none'; script-src 'self'; style-src 'self'; img-src 'self' data:; font-src 'self'; connect-src 'none'; frame-ancestors 'self'; base-uri 'none'; form-action 'none'",
  }
  await page.route('**/module-frames/**', async (route) => {
    const path = new URL(route.request().url()).pathname
    if (path === prefix) {
      documentLoads += 1
      await route.fulfill({
        status: 200,
        contentType: 'text/html; charset=utf-8',
        headers: { ...headers, 'cache-control': 'no-store' },
        body: await frameDocument(script),
      })
      return
    }
    if (path === `${prefix}entry.js`) {
      await route.fulfill({
        status: 200,
        contentType: 'text/javascript',
        headers: { ...headers, 'cache-control': 'public, max-age=31536000, immutable' },
        body: script,
      })
      return
    }
    await route.fulfill({ status: 404, contentType: 'text/plain', body: 'not found' })
  })
  return {
    documentLoads: (): number => {
      return documentLoads
    },
    setScript: (next): void => {
      script = next
    },
  }
}
