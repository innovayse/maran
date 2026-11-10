import { expect, test } from '../fixtures/guarded-test'
import { setPersistedLocale } from '../fixtures/set-locale'
import { stubSignedIn, stubSignedOut } from '../fixtures/stub-auth-routes'
import { ALERT_DELIVERY_OK, loaded, stubDashboard, SYNCHRONIZED_CLOCK, UNAVAILABLE } from '../fixtures/stub-dashboard-route'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubIdleLiveDashboard } from '../fixtures/stub-live-dashboard-stream'
import { stubEmptyModules } from '../fixtures/stub-modules-route'
import { stubShellReads } from '../fixtures/stub-shell-reads'
import type { Dashboard } from '../../src/types/dashboard'

// Issue #149: `index.html` declares `lang="en"` for the frame before any script runs, and the
// locale store replaces it. These cover the two paths the shell's own switcher spec does not: a
// language that is already chosen at boot, and the switcher on a signed-out screen.

/** A quiet administrator's dashboard: the landing screen the booted panel opens on, with nothing on it to read. */
const QUIET_DASHBOARD: Dashboard = {
  audience: 'administrator',
  resources: UNAVAILABLE,
  services: UNAVAILABLE,
  counts: { accounts: loaded(0), sites: loaded(0), databases: loaded(0) },
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

test('a panel booted in Armenian declares Armenian, and its skip link is Armenian too', async ({ page }) => {
  await stubSignedIn(page)
  await stubShellReads(page)
  await setPersistedLocale(page, 'hy')
  await stubHealthy(page)
  await stubEmptyModules(page)
  await stubDashboard(page, QUIET_DASHBOARD)
  await stubIdleLiveDashboard(page)

  await page.goto('/')

  await expect(page.locator('html')).toHaveAttribute('lang', 'hy')
  // Tab only once the shell is drawn: the route guards read the host before the first screen, and a
  // key pressed while they are still reading lands on a document that is then replaced.
  await expect(page.getByRole('navigation').first()).toBeVisible()
  await page.keyboard.press('Tab')
  await expect(page.getByRole('link', { name: 'Անցնել բովանդակությանը' })).toBeFocused()
})

test('the language switcher on the sign-in screen moves the document language with it', async ({ page }) => {
  await stubSignedOut(page)
  await setPersistedLocale(page, 'en')
  await stubHealthy(page)
  await stubEmptyModules(page)

  await page.goto('/login')
  await expect(page.locator('html')).toHaveAttribute('lang', 'en')

  await page.getByTestId('locale-switcher').locator('[aria-haspopup="menu"]').click()
  await page.getByRole('menuitemradio', { name: 'Русский', exact: true }).click()

  await expect(page.locator('html')).toHaveAttribute('lang', 'ru')
})
