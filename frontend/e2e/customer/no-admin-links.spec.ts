import { expect, test, type Page } from '@playwright/test'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubBackups } from '../fixtures/stub-backups-routes'
import { stubDatabases } from '../fixtures/stub-databases-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import { forbidAccountsList, stubMyAccount } from '../fixtures/stub-my-account-route'
import { stubSites } from '../fixtures/stub-sites-routes'
import { stubEmptyTasks } from '../fixtures/stub-tasks-routes'
import type { MyAccount } from '../../src/types/account'
import type { AuthenticatedUser } from '../../src/types/auth'
import type { PanelModule } from '../../src/types/module'

/**
 * A customer is not offered a way into a server-wide screen: the in-page links to Grant repair,
 * the backup schedule and destinations, PHP versions and the security policy are not drawn for
 * them. Presentation only: the endpoints behind them refuse a customer on their own.
 */

/** The account the customer owns. */
const OWN_ACCOUNT: MyAccount = {
  id: '11111111-1111-1111-1111-111111111111',
  name: 'olive',
  primaryDomain: 'olive.example.com',
  status: 'active',
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

/** The signed-in customer. */
const CUSTOMER: AuthenticatedUser = {
  id: '00000000-0000-0000-0000-000000000099',
  username: 'olive',
  email: 'olive@example.com',
  role: 'customer',
  accountId: OWN_ACCOUNT.id,
}

/** The modules the visited screens belong to. */
const MODULES: PanelModule[] = [
  { name: 'sites', displayName: 'Sites', tier: 'included', isEnabled: true },
  { name: 'databases', displayName: 'Databases', tier: 'included', isEnabled: true },
  { name: 'backups', displayName: 'Backups', tier: 'included', isEnabled: true },
]

/** Every link name a customer must not be offered, on any of the screens below. */
const ADMIN_LINKS = ['Grant repair', 'Backup schedule', 'Backup destinations', 'PHP versions', 'Security policy']

/**
 * Signs the customer in with every list the visited screens read stubbed empty.
 * @param page The Playwright page under test.
 * @returns How many times the accounts list was requested so far.
 */
const signInCustomer = async (page: Page): Promise<() => number> => {
  await stubSignedIn(page, CUSTOMER)
  await stubHealthy(page)
  await stubModules(page, MODULES)
  await stubEmptyTasks(page)
  await stubMyAccount(page, OWN_ACCOUNT)
  await stubDatabases(page, [])
  await stubBackups(page, [])
  await stubSites(page, [])
  await page.route('**/api/v1/sessions', async (route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: '[]' })
  })
  return forbidAccountsList(page)
}

/**
 * Asserts that none of the administrator links is drawn anywhere on the page, sidebar included.
 * @param page The Playwright page under test.
 * @returns Resolves once every link has been checked.
 */
const expectNoAdminLinks = async (page: Page): Promise<void> => {
  for (const name of ADMIN_LINKS) {
    await expect(page.getByRole('link', { name, exact: true })).toHaveCount(0)
  }
}

for (const path of ['/databases', '/backups', '/sites', '/settings/sessions']) {
  test(`a customer is offered no administrator link on ${path}`, async ({ page }) => {
    const accountsCalls = await signInCustomer(page)

    await page.goto(path)
    // Wait for the screen itself before asserting an absence, or "no link" would pass on a blank page.
    await expect(page.getByRole('heading', { level: 1 })).toBeVisible()

    await expectNoAdminLinks(page)
    expect(accountsCalls()).toBe(0)
  })
}
