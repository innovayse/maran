import { test, type Page } from '@playwright/test'
import {
  expectRegionFastLoad,
  expectRegionSlowFailure,
  expectRegionSlowLoad,
  type RegionLoadingScenario,
} from './fixtures/expect-region-loading'
import { stubSignedIn } from './fixtures/stub-auth-routes'
import { loaded, stubDashboard, stubDashboardProblem } from './fixtures/stub-dashboard-route'
import { stubHealthy } from './fixtures/stub-health-route'
import { stubModules } from './fixtures/stub-modules-route'
import type { Dashboard } from '../src/types/dashboard'
import type { PanelModule } from '../src/types/module'

// The landing page while the dashboard is on the way (#243): the count tiles and the resource
// tiles are drawn with their real labels and a placeholder for every figure.

const LICENSED: PanelModule[] = [
  { name: 'accounts', displayName: 'Accounts', tier: 'included', isEnabled: true },
  { name: 'sites', displayName: 'Sites', tier: 'included', isEnabled: true },
  { name: 'databases', displayName: 'Databases', tier: 'included', isEnabled: true },
]

// No attention notice: it is drawn above the counts once the answer is in, and is not part of
// what this spec measures.
const DASHBOARD: Dashboard = {
  isAdministrator: true,
  resources: loaded({
    cpuPercent: 37.4,
    memoryUsedBytes: 3 * 1024 * 1024 * 1024,
    memoryTotalBytes: 8 * 1024 * 1024 * 1024,
    diskUsedBytes: 44 * 1024 * 1024 * 1024,
    diskTotalBytes: 98 * 1024 * 1024 * 1024,
    networkRxBytes: 0,
    networkTxBytes: 0,
    loadAverage1m: 0.21,
    loadAverage5m: 0.34,
    loadAverage15m: 0.55,
  }),
  services: loaded([]),
  counts: {
    accounts: loaded(3),
    sites: loaded(7),
    databases: loaded(4),
  },
  attention: null,
  recentAudit: loaded([]),
}

/** The server's refusal the failure case answers with. */
const REFUSAL = 'The agent is not answering on this server.'

/**
 * Signs an administrator in with the three modules licensed.
 * @param page The Playwright page.
 * @returns Resolves once the routes are installed.
 */
const signIn = async (page: Page): Promise<void> => {
  await stubModules(page, LICENSED)
  await stubSignedIn(page)
  await stubHealthy(page)
}

/** How the dashboard screen is opened and answered. */
const DASHBOARD_SCENARIO: RegionLoadingScenario = {
  stub: async (page) => {
    await signIn(page)
    await stubDashboard(page, DASHBOARD)
  },
  stubFailure: async (page) => {
    await signIn(page)
    await stubDashboardProblem(page, REFUSAL)
  },
  pattern: '**/api/v1/dashboard',
  path: '/',
  anchor: (page) => {
    return page.getByRole('heading', { name: 'On this server' })
  },
  contentText: '0.21 · 0.34 · 0.55',
  failureText: REFUSAL,
}

test('dashboard: a slow answer shows placeholders under the real labels, then the content', async ({ page }) => {
  await expectRegionSlowLoad(page, DASHBOARD_SCENARIO)
})

test('dashboard: a fast answer shows no placeholder', async ({ page }) => {
  await expectRegionFastLoad(page, DASHBOARD_SCENARIO)
})

test('dashboard: a slow failure is an alert, never a skeleton', async ({ page }) => {
  await expectRegionSlowFailure(page, DASHBOARD_SCENARIO)
})
