import { expect, test } from '../fixtures/guarded-test'
import { stubSignedIn, stubbedAdministrator } from '../fixtures/stub-auth-routes'
import { stubEmptyModules } from '../fixtures/stub-modules-route'
import { stubShellReads } from '../fixtures/stub-shell-reads'
import { stubHealthy } from '../fixtures/stub-health-route'
import { setPersistedLocale } from '../fixtures/set-locale'
import { stubAudit } from '../fixtures/stub-audit-routes'
import {
  ALERT_DELIVERY_OK,
  loaded,
  stubDashboard,
  SYNCHRONIZED_CLOCK,
  UNAVAILABLE,
  WITHHELD_DASHBOARD,
} from '../fixtures/stub-dashboard-route'
import { DEFAULT_LAYOUT, installLiveStream, stubDashboardLayout } from '../fixtures/stub-live-dashboard-stream'
import type { Dashboard } from '../../src/types/dashboard'
import type { Page } from '@playwright/test'

/**
 * The measured defect: `/settings/audit` was routed, rendered and tested, and no LINK in the
 * shell led to it — the sidebar had no entry and nothing else rendered an `<a>` to it, so a
 * person clicking could never arrive. These specs observe the RENDERED sidebar link, which is
 * the axis the static reachability gate declares it cannot see (an entry built but never
 * returned looks identical to it).
 */

/** What the panel tells an administrator who opens the dashboard: a server it could read nothing of. */
const SERVER_DASHBOARD: Dashboard = {
  audience: 'administrator',
  resources: UNAVAILABLE,
  services: UNAVAILABLE,
  counts: { accounts: loaded(0), sites: loaded(0), databases: loaded(0) },
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
 * Signs a person in with the given role, in Russian, with the shell's endpoints answered.
 * @param page The Playwright page whose network the routes are installed on.
 * @param role The role the panel reports for the signed-in user.
 * @returns Resolves once the routes are installed.
 */
const stubRussianShell = async (page: Page, role: 'admin' | 'customer'): Promise<void> => {
  await setPersistedLocale(page, 'ru')
  await stubSignedIn(page, { ...stubbedAdministrator, role })
  await stubHealthy(page)
  await stubEmptyModules(page)
  await stubShellReads(page)
  // Both tests open the home screen first: the dashboard answers per role, and the server's one
  // reads the person's layout and opens its live stream.
  await stubDashboard(page, role === 'admin' ? SERVER_DASHBOARD : WITHHELD_DASHBOARD)
  await stubDashboardLayout(page, DEFAULT_LAYOUT)
  await installLiveStream(page)
  await stubAudit(page, [])
}

test('an administrator reaches the journal from the sidebar by clicking its link', async ({ page }) => {
  await stubRussianShell(page, 'admin')

  await page.goto('/')

  // A real link, not a programmatic push: an <a> the browser can middle-click and a crawler of
  // clicks can find — asserted by VALUE in the non-English locale the defect was measured in.
  const navigation = page.getByRole('navigation', { name: 'Основная навигация' })
  // The journal is a child of Monitoring (issue #311): the person opens that submenu first.
  await navigation.getByRole('button', { name: 'Показать разделы «Мониторинг»' }).click()
  const link = navigation.getByRole('link', { name: 'Журнал аудита' })
  await expect(link).toBeVisible()
  await expect(link).toHaveAttribute('href', '/settings/audit')

  await link.click()

  await expect(page).toHaveURL('/settings/audit')
  // The page's own heading, not the first matching text: the sidebar's link carries the same words.
  await expect(page.getByRole('heading', { level: 1, name: 'Журнал аудита' })).toBeVisible()
})

test('a customer is not offered the journal link in the sidebar', async ({ page }) => {
  // Presentation, not authorization: the endpoint refuses a customer whatever the sidebar shows,
  // and a link that only ever answers 403 is a worse answer than no link. Any role the SPA does
  // not recognise keeps the link — the check errs permissive by design.
  await stubRussianShell(page, 'customer')

  await page.goto('/')

  const navigation = page.getByRole('navigation', { name: 'Основная навигация' })
  // Positive control on the same locator scope: the sidebar rendered its entries, so an empty
  // navigation cannot be what passes the absence assertion below.
  await expect(navigation.getByRole('link', { name: 'Состояние системы' })).toBeVisible()
  await expect(navigation.getByRole('link', { name: 'Журнал аудита' })).toHaveCount(0)
})
