import { expect, type Locator, type Page } from '@playwright/test'
import { ONE_SHOT, stubDashboard } from './stub-dashboard-route'
import { setPersistedLocale } from './set-locale'
import { stubSignedIn } from './stub-auth-routes'
import { stubHealthy } from './stub-health-route'
import { DEFAULT_LAYOUT, installLiveStream, stubDashboardLayout } from './stub-live-dashboard-stream'
import {
  COMPILED_IN_MODULE,
  PACKAGED_ID,
  packagedModule,
  stubLiveCatalogue,
  stubModuleFrame,
  type FrameStub,
  type LiveCatalogue,
  type SpecLocale,
} from './stub-packaged-module'
import { stubShellReads } from './stub-shell-reads'

/** What a spec gets back from {@link openPackagedShell}. */
export interface PackagedShell {
  /** The catalogue the page reads; replace it to simulate an install, a removal or a failure. */
  catalogue: LiveCatalogue
  /** The frame route; its counters tell how many times the module was mounted. */
  frame: FrameStub
}

/** The options of {@link openPackagedShell}. */
export interface PackagedShellOptions {
  /** The bundle the sample module's frame serves. */
  script: string
  /** The interface language. English by default. */
  locale?: SpecLocale
  /** Where to open. The dashboard by default. */
  path?: string
  /** The viewport. A desktop by default. */
  viewport?: { width: number; height: number }
  /**
   * Stubs the spec's own endpoints. Called after the shell's quiet answers, because the route
   * installed last wins; a stub of the same address installed before {@link openPackagedShell}
   * would be shadowed by them.
   */
  stubs?: (page: Page) => Promise<void>
  /** The sample module's catalogue entry, when a spec needs a variant (degraded, failed, no frame). */
  module?: (locale: SpecLocale) => ReturnType<typeof packagedModule>
}

/**
 * Signs in as an administrator on a panel with one installed (packaged) module and one compiled-in
 * module, serving the installed module's frame with a chosen bundle, and opens a path.
 *
 * The reads the shell makes on every screen get their quiet answers, and so do the reads of the
 * system status screen (the dashboard, its layout and a stream stand-in), which is where the menu's
 * first entry leads. Nothing is answered by a catch-all: under the guarded `test` an unanswered
 * `/api/v1` request fails the spec, so every other endpoint a spec needs goes through `stubs`.
 * @param page The Playwright page under test.
 * @param options The bundle, language, address and viewport.
 * @returns The catalogue and frame handles.
 */
export const openPackagedShell = async (page: Page, options: PackagedShellOptions): Promise<PackagedShell> => {
  const locale = options.locale ?? 'en'
  await page.setViewportSize(options.viewport ?? { width: 1440, height: 900 })
  await setPersistedLocale(page, locale)
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubShellReads(page)
  await stubDashboard(page, ONE_SHOT)
  await stubDashboardLayout(page, DEFAULT_LAYOUT)
  await installLiveStream(page)
  await options.stubs?.(page)
  const build = options.module ?? packagedModule
  const catalogue = await stubLiveCatalogue(page, (language) => {
    return [COMPILED_IN_MODULE, build(language)]
  })
  const frame = await stubModuleFrame(page, { script: options.script })
  await page.goto(options.path ?? '/')
  await expect(page.locator('nav [data-nav-section], nav a').first()).toBeVisible()
  return { catalogue, frame }
}

/**
 * The sidebar link to the sample module's first screen.
 * @param page The Playwright page under test.
 * @returns A locator for the link, found by its address so every language finds it.
 */
export const packagedMenuLink = (page: Page): Locator => {
  return page.locator(`nav a[href="/m/${PACKAGED_ID}/hello"]`).first()
}
