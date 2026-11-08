import { expect, test } from '@playwright/test'
import { stubSignedIn } from './fixtures/stub-auth-routes'
import { ALERT_DELIVERY_OK, loaded, stubDashboard, SYNCHRONIZED_CLOCK, UNAVAILABLE } from './fixtures/stub-dashboard-route'
import { stubHealthy } from './fixtures/stub-health-route'
import { stubEmptyModules } from './fixtures/stub-modules-route'
import type { Dashboard } from '../src/types/dashboard'

// The outcome badge of the dashboard's latest-activity card prints the word the entry carries, so
// the bundle holds no copy of the vocabulary (issue #330).

/** A dashboard whose only failed entry carries an outcome name no bundle contains. */
const WITH_REFUSAL: Dashboard = {
  audience: 'administrator',
  resources: UNAVAILABLE,
  services: UNAVAILABLE,
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
  recentAudit: loaded([
    {
      id: '00000000-0000-0000-0000-0000000000d1',
      occurredAt: '2026-09-09T08:00:00+00:00',
      actorUsername: 'mallory',
      impersonatorUsername: null,
      action: 'LoginFailed',
      actionName: 'Sign-in refused',
      subject: 'mallory',
      ipAddress: '198.51.100.4',
      succeeded: false,
      outcomeName: 'Turned away at the dashboard',
    },
  ]),
}

test('the latest-activity badge prints the outcome name the entry carries', async ({ page }) => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubEmptyModules(page)
  await stubDashboard(page, WITH_REFUSAL)

  await page.goto('/')

  await expect(page.getByText('Turned away at the dashboard', { exact: true })).toBeVisible()
})
