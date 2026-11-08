import { expect, type Page } from '@playwright/test'
import type { Dashboard } from '../../src/types/dashboard'
import type { DashboardLayout } from '../../src/types/dashboardLayout'
import type { PanelModule } from '../../src/types/module'
import { FULL_CATALOGUE } from './open-full-shell'
import { setPersistedLocale } from './set-locale'
import { stubSignedIn } from './stub-auth-routes'
import { ALERT_DELIVERY_OK, loaded, stubDashboard, SYNCHRONIZED_CLOCK } from './stub-dashboard-route'
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
import { COMPLETE_CHECKLIST, stubFirstRun, stubQuietNotificationFeed } from './stub-notification-routes'

/**
 * The one-shot read the live specs begin with. Its figures differ from every live frame's on
 * purpose (processor 11.1 against the stream's 37.4), so a spec can tell which of the two it is
 * looking at.
 */
export const ONE_SHOT: Dashboard = {
  audience: 'administrator',
  resources: loaded({
    cpuPercent: 11.1,
    memoryUsedBytes: 1024 * 1024 * 1024,
    memoryTotalBytes: 8 * 1024 * 1024 * 1024,
    diskUsedBytes: 10 * 1024 * 1024 * 1024,
    diskReservedBytes: 0,
    diskAvailableBytes: 88 * 1024 * 1024 * 1024,
    diskTotalBytes: 98 * 1024 * 1024 * 1024,
    networkRxBytes: 0,
    networkTxBytes: 0,
    loadAverage1m: 0.01,
    loadAverage5m: 0.02,
    loadAverage15m: 0.03,
  }),
  services: loaded([{ service: 'webServer', name: 'Web server', state: 'running', detail: 'active (running)' }]),
  counts: { accounts: loaded(1), sites: loaded(1), databases: loaded(1) },
  attention: {
    certificatesExpiringSoon: loaded(0),
    failedBackups: loaded(0),
    sitesOnEndOfLifePhp: loaded(0),
    alertDelivery: ALERT_DELIVERY_OK,
    bannedAddresses: loaded(0),
    failedTasks: loaded(0),
    clock: SYNCHRONIZED_CLOCK,
  },
  recentAudit: loaded([]),
}

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
 * Every other `/api/v1` request gets an empty 404 problem (installed first, so any narrower stub
 * wins), because these specs are about the dashboard and an unanswered request would otherwise
 * reach a proxy that is not there.
 * @param page The Playwright page under test.
 * @param options Language, layout, width and licensed modules.
 * @returns The recorder of layout saves.
 */
export const openLiveDashboard = async (
  page: Page,
  options: OpenLiveDashboardOptions = {},
): Promise<DashboardLayoutStub> => {
  await page.setViewportSize({ width: options.width ?? 1440, height: 900 })
  await page.route(/\/api\/v1\//, async (route) => {
    await route.fulfill({
      status: 404,
      contentType: 'application/problem+json',
      body: JSON.stringify({ title: 'Not found', status: 404 }),
    })
  })
  if (options.locale !== undefined) {
    await setPersistedLocale(page, options.locale)
  }
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, options.modules ?? FULL_CATALOGUE)
  await stubDashboard(page, ONE_SHOT)
  await stubFirstRun(page, COMPLETE_CHECKLIST)
  await stubQuietNotificationFeed(page)
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
