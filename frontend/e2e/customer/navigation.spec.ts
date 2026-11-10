import { expect, test } from '../fixtures/guarded-test'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubLandingReads } from '../fixtures/stub-landing-reads'
import { stubModules } from '../fixtures/stub-modules-route'
import { stubMyAccount } from '../fixtures/stub-my-account-route'
import { stubShellReads } from '../fixtures/stub-shell-reads'
import type { MyAccount } from '../../src/types/account'
import type { AuthenticatedUser } from '../../src/types/auth'
import type { PanelModule } from '../../src/types/module'

// Issue #202: the sidebar offers a customer their own area and nothing that is the server's. The
// role is read from one place (`useCallerRole`), so this spec also guards that the sidebar did not
// keep a copy of its own.

const OWN_ACCOUNT: MyAccount = {
  id: '11111111-1111-1111-1111-111111111111',
  name: 'olive',
  primaryDomain: 'olive.example.com',
  status: 'active',
  statusName: 'Active',
  plan: {
    displayName: 'Starter',
    diskQuotaMb: 1024,
    maxSites: 5,
    maxDatabases: 5,
    maxSftpUsers: 5,
    maxFtpUsers: 5,
    maxCronEntries: 5,
  },
}

const CUSTOMER: AuthenticatedUser = {
  id: '00000000-0000-0000-0000-000000000099',
  username: 'olive',
  email: 'olive@example.com',
  role: 'customer',
  accountId: '11111111-1111-1111-1111-111111111111',
}

const CUSTOMER_MODULES: PanelModule[] = [
  { name: 'sites', displayName: 'Sites', kindDisplayName: 'Core', tier: 'included', state: 'enabled' },
  { name: 'databases', displayName: 'Databases', kindDisplayName: 'Core', tier: 'included', state: 'enabled' },
  { name: 'backups', displayName: 'Backups', kindDisplayName: 'Core', tier: 'included', state: 'enabled' },
]

test('a customer sidebar carries no administrator entries', async ({ page }) => {
  await stubSignedIn(page, CUSTOMER)
  await stubHealthy(page)
  await stubShellReads(page)
  await stubModules(page, CUSTOMER_MODULES)
  await stubMyAccount(page, OWN_ACCOUNT)
  await stubLandingReads(page, 'customer')

  await page.goto('/')

  const navigation = page.getByRole('navigation', { name: 'Main navigation' })
  await expect(navigation.getByRole('link', { name: 'Sites' }).first()).toBeVisible()

  for (const name of ['Audit journal', 'Plans', 'Grant repair', 'Schedule', 'Destinations', 'PHP versions']) {
    await expect(navigation.getByRole('link', { name })).toHaveCount(0)
  }
})

test('the not-available page has its own breadcrumb name', async ({ page }) => {
  await stubSignedIn(page, CUSTOMER)
  await stubHealthy(page)
  await stubShellReads(page)
  await stubModules(page, CUSTOMER_MODULES)
  await stubMyAccount(page, OWN_ACCOUNT)

  await page.goto('/not-available')

  // The breadcrumb must name the screen in words, never fall back to the route's machine name.
  await expect(page.locator('body')).not.toContainText('not-available')
  await expect(page.getByRole('heading', { level: 1, name: 'Page not available' })).toBeVisible()
})

test('an administrator sidebar still offers the audit journal', async ({ page }) => {
  // Positive control for the absences above.
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubShellReads(page)
  await stubModules(page, CUSTOMER_MODULES)
  await stubLandingReads(page, 'administrator')

  await page.goto('/')

  const navigation = page.getByRole('navigation', { name: 'Main navigation' })
  // The journal is a child of Monitoring (issue #311), so its submenu is opened first.
  await navigation.getByRole('button', { name: 'Show the Monitoring pages' }).click()
  await expect(navigation.getByRole('link', { name: 'Audit journal' })).toBeVisible()
})
