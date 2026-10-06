import type { Page } from '@playwright/test'
import type { Dashboard } from '../../src/types/dashboard'

/**
 * What the panel answers a caller it filled nothing for: a customer, or an administrator on a host
 * where every module refused.
 *
 * Exported so a spec asserting "nothing of the server's state is drawn" says what it means with the
 * payload the panel really sends, rather than with a hand-built object that could drift from it.
 */
export const WITHHELD_DASHBOARD: Dashboard = {
  isAdministrator: false,
  resources: null,
  services: [],
  counts: null,
  attention: null,
  recentAudit: [],
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
