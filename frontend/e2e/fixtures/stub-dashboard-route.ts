import type { Page } from '@playwright/test'
import type { AlertDeliveryStatus, Dashboard, DashboardSection } from '../../src/types/dashboard'
import type { ClockSync } from '../../src/types/monitoring'

/**
 * A section the panel read, carrying what it answered with.
 * @param data What the module answered with.
 * @returns The loaded section.
 */
export const loaded = <T>(data: T): DashboardSection<T> => {
  return { state: 'loaded', data }
}

/** A section the panel could not read: no data at all, never an empty list or a zero. */
export const UNAVAILABLE: DashboardSection<never> = { state: 'unavailable' }

/**
 * A clock the panel read and found synchronised, with the sentence its English resources carry: the
 * attention payload every spec sends unless it is about the clock (issue #150).
 */
export const SYNCHRONIZED_CLOCK: DashboardSection<ClockSync> = loaded<ClockSync>({
  state: 'synchronized',
  description: 'The clock is synchronised with a time source.',
})

/** Alert delivery the panel read with both channels untroubled: the payload every spec sends unless it is about delivery (issue #142). */
export const ALERT_DELIVERY_OK: DashboardSection<AlertDeliveryStatus> = loaded<AlertDeliveryStatus>({
  mail: { state: 'ok', lastFailureAt: null, failureMessage: null },
  webhook: { state: 'unknown', lastFailureAt: null, failureMessage: null },
})

/**
 * What the panel answers a caller it filled nothing for: a customer. The shape has no section at
 * all; a section that could not be read is {@link UNAVAILABLE}, on an administrator's answer.
 *
 * Exported so a spec asserting "nothing of the server's state is drawn" says what it means with the
 * payload the panel really sends, rather than with a hand-built object that could drift from it.
 */
export const WITHHELD_DASHBOARD: Dashboard = {
  audience: 'customer',
}

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
    licenceExpiring: loaded(0),
    alertDelivery: ALERT_DELIVERY_OK,
    bannedAddresses: loaded(0),
    failedTasks: loaded(0),
    clock: SYNCHRONIZED_CLOCK,
  },
  recentAudit: loaded([]),
}

/**
 * Fulfils `GET /api/v1/dashboard` with the payload a spec names.
 * @param page The Playwright page whose network the route is installed on.
 * @param dashboard What the panel answers.
 * @returns Resolves once the route is installed.
 */
export const stubDashboard = async (page: Page, dashboard: Dashboard): Promise<void> => {
  await page.route('**/api/v1/dashboard', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(dashboard),
    })
  })
}

/**
 * Fulfils `GET /api/v1/dashboard` with an RFC 7807 problem body, so a spec can assert the panel's
 * own message is rendered verbatim (rules/vue.md: "the backend owns their text").
 * @param page The Playwright page whose network the route is installed on.
 * @param detail The backend-localized message the stub reports in `detail`.
 * @returns Resolves once the route is installed.
 */
export const stubDashboardProblem = async (page: Page, detail: string): Promise<void> => {
  await page.route('**/api/v1/dashboard', async (route) => {
    await route.fulfill({
      status: 503,
      contentType: 'application/problem+json',
      body: JSON.stringify({ code: 'agent_unavailable', title: 'Service unavailable', detail }),
    })
  })
}
