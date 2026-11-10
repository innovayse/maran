import { expect, type Page } from '@playwright/test'
import type { DashboardLayout } from '../../src/types/dashboardLayout'
import type { PanelModule } from '../../src/types/module'
import { FULL_CATALOGUE } from './open-full-shell'
import { setPersistedLocale } from './set-locale'
import { stubSignedIn } from './stub-auth-routes'
import { loaded, ONE_SHOT, stubDashboard } from './stub-dashboard-route'
import { stubHealthy } from './stub-health-route'
import {
  DEFAULT_LAYOUT,
  installLiveStream,
  pushFrame,
  HELLO,
  reading,
  streamOpens,
  stubDashboardLayout,
  totals,
  ACTIVITY,
  type DashboardLayoutStub,
} from './stub-live-dashboard-stream'
import { stubModules } from './stub-modules-route'
import { COMPLETE_CHECKLIST, stubFirstRun } from './stub-notification-routes'
import { stubShellReads } from './stub-shell-reads'

/** What {@link openLiveDashboard} can be told. */
export interface OpenLiveDashboardOptions {
  /** The language to open in; English by default. */
  locale?: 'en' | 'ru' | 'hy'
  /** The layout the panel holds; every widget and shortcut by default. */
  layout?: DashboardLayout
  /** The viewport width; 1440 by default. */
  width?: number
  /** The licensed modules; every module by default. */
  modules?: PanelModule[]
}

/**
 * Signs an administrator in with every module licensed, answers the reads the dashboard makes, and
 * opens it. The stream is the controllable stand-in: nothing is live until the spec pushes frames
 * (or calls {@link goLive}).
 *
 * The reads the shell makes on every screen are answered with their quiet answers first, so any
 * narrower stub a spec adds afterwards wins. Nothing else is answered: a spec that follows a
 * shortcut to another screen stubs that screen's reads itself, and the guard fails it otherwise.
 * @param page The Playwright page under test.
 * @param options Language, layout, width and licensed modules.
 * @returns The recorder of layout saves.
 */
export const openLiveDashboard = async (
  page: Page,
  options: OpenLiveDashboardOptions = {},
): Promise<DashboardLayoutStub> => {
  await page.setViewportSize({ width: options.width ?? 1440, height: 900 })
  if (options.locale !== undefined) {
    await setPersistedLocale(page, options.locale)
  }
  await stubSignedIn(page)
  await stubShellReads(page)
  await stubHealthy(page)
  await stubModules(page, options.modules ?? FULL_CATALOGUE)
  await stubDashboard(page, ONE_SHOT)
  await stubFirstRun(page, COMPLETE_CHECKLIST)
  const layout: DashboardLayoutStub = await stubDashboardLayout(page, options.layout ?? DEFAULT_LAYOUT)
  await installLiveStream(page)
  await page.goto('/')
  await expect(page.getByTestId('live-status')).toBeVisible()
  return layout
}

/**
 * Makes the open dashboard live, once the page has opened its stream: the `hello`, a short history, the newest reading, and the totals,
 * services and activity pushes.
 * @param page The page the stand-in is installed on.
 * @returns Resolves once the frames are written.
 */
export const goLive = async (page: Page): Promise<void> => {
  // The page opens the stream only after the one-shot read has told it who it is for; frames
  // written before the request is made would go nowhere.
  await expect
    .poll(async () => {
      return (await streamOpens(page)).length
    })
    .toBeGreaterThan(0)
  await pushFrame(page, 'hello', HELLO)
  await pushFrame(page, 'history', { points: [reading(0), reading(2), reading(4)] })
  await pushFrame(page, 'services', {
    services: loaded([{ service: 'webServer', name: 'Web server', state: 'running', detail: 'active (running)' }]),
  })
  await pushFrame(page, 'totals', totals())
  await pushFrame(page, 'activity', ACTIVITY)
}
