import { expect, test } from '../fixtures/guarded-test'
import { stubAudit } from '../fixtures/stub-audit-routes'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubLandingReads } from '../fixtures/stub-landing-reads'
import { stubModules } from '../fixtures/stub-modules-route'
import { forbidAccountsList, stubMyAccount } from '../fixtures/stub-my-account-route'
import { setPersistedLocale } from '../fixtures/set-locale'
import { stubShellReads } from '../fixtures/stub-shell-reads'
import type { MyAccount } from '../../src/types/account'
import type { AuthenticatedUser } from '../../src/types/auth'
import type { Page } from '@playwright/test'
import type { PanelModule } from '../../src/types/module'

// Issue #202: a customer who reaches an administrator's screen by URL lands on one localized
// "not available" page, never on the upgrade wall and never on an admin page wrapped around a 403.
// The guard is advice; the endpoints stay the boundary (rules/security.md), which is why the
// accounts list is stubbed with the panel's own `HostForbidden` problem and counted: the page must
// not even ask.

const CUSTOMER: AuthenticatedUser = {
  id: '00000000-0000-0000-0000-000000000099',
  username: 'olive',
  email: 'olive@example.com',
  role: 'customer',
  accountId: '11111111-1111-1111-1111-111111111111',
}

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

/**
 * Answers what the signed-in shell of a customer reads on every screen, and the landing screen the
 * way back from the not-available page leads to.
 * @param page The Playwright page under test.
 * @returns Resolves once the routes are installed.
 */
const stubCustomerShell = async (page: Page): Promise<void> => {
  await stubShellReads(page)
  await stubMyAccount(page, OWN_ACCOUNT)
  await stubLandingReads(page, 'customer')
}

// The catalogue an administrator would see, so the guard cannot pass merely because the module
// guard already redirected: every admin-only route below belongs to a listed, enabled module.
const MODULES: PanelModule[] = [
  { name: 'accounts', displayName: 'Accounts', kindDisplayName: 'Core', tier: 'included', state: 'enabled' },
  { name: 'databases', displayName: 'Databases', kindDisplayName: 'Core', tier: 'included', state: 'enabled' },
  { name: 'backups', displayName: 'Backups', kindDisplayName: 'Core', tier: 'included', state: 'enabled' },
  { name: 'firewall', displayName: 'Firewall', kindDisplayName: 'Core', tier: 'included', state: 'enabled' },
  { name: 'monitoring', displayName: 'Monitoring', kindDisplayName: 'Core', tier: 'included', state: 'enabled' },
]

const ADMIN_ONLY_PATHS = [
  '/accounts',
  '/accounts/new',
  '/plans',
  '/databases/grant-repair',
  '/backups/schedule',
  '/backups/destinations',
  '/firewall',
  '/firewall/bans',
  '/firewall/whitelist',
  '/monitoring',
  '/settings/security-policy',
  '/settings/smtp',
  '/settings/alerts',
  '/settings/php',
  '/settings/audit',
]

const HEADINGS = {
  en: 'Page not available',
  ru: 'Страница недоступна',
  hy: 'Էջը հասանելի չէ',
} as const

for (const path of ADMIN_ONLY_PATHS) {
  test(`a customer opening ${path} lands on the not-available page`, async ({ page }) => {
    await stubSignedIn(page, CUSTOMER)
    await stubHealthy(page)
    await stubCustomerShell(page)
    await stubModules(page, MODULES)
    const accountsListCalls = await forbidAccountsList(page)

    await page.goto(path)

    await expect(page).toHaveURL('/not-available')
    await expect(page.getByRole('heading', { level: 1, name: HEADINGS.en })).toBeVisible()
    await expect(page.getByRole('heading', { level: 1, name: 'Upgrade required' })).toHaveCount(0)
    expect(accountsListCalls()).toBe(0)
  })
}

for (const locale of ['ru', 'hy'] as const) {
  test(`the not-available page is localized in ${locale}`, async ({ page }) => {
    await setPersistedLocale(page, locale)
    await stubSignedIn(page, CUSTOMER)
    await stubHealthy(page)
    await stubCustomerShell(page)
    await stubModules(page, MODULES)

    await page.goto('/firewall')

    await expect(page).toHaveURL('/not-available')
    await expect(page.getByRole('heading', { level: 1, name: HEADINGS[locale] })).toBeVisible()
  })
}

test('a module missing from a customer catalogue is not available, not an upgrade', async ({ page }) => {
  // The panel filters the catalogue by role, so "absent" means "not part of your area".
  await stubSignedIn(page, CUSTOMER)
  await stubHealthy(page)
  await stubCustomerShell(page)
  await stubModules(page, [{ name: 'sites', displayName: 'Sites', kindDisplayName: 'Core', tier: 'included', state: 'enabled' }])

  await page.goto('/cron')

  await expect(page).toHaveURL('/not-available')
})

test('a module the licence locks still sends a customer to the upgrade page', async ({ page }) => {
  await stubSignedIn(page, CUSTOMER)
  await stubHealthy(page)
  await stubCustomerShell(page)
  await stubModules(page, [{ name: 'cron', displayName: 'Cron', kindDisplayName: 'Core', tier: 'planGated', state: 'locked' }])

  await page.goto('/cron')

  await expect(page).toHaveURL('/upgrade/cron')
})

test('the way back from the not-available page leads to system status', async ({ page }) => {
  await stubSignedIn(page, CUSTOMER)
  await stubHealthy(page)
  await stubCustomerShell(page)
  await stubModules(page, MODULES)

  await page.goto('/settings/audit')
  await page.getByRole('link', { name: 'Back to system status' }).click()

  await expect(page).toHaveURL('/')
})

test('an administrator still reaches an admin-only screen', async ({ page }) => {
  // Positive control: the guard reads the role, not the flag alone.
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubShellReads(page)
  await stubModules(page, MODULES)
  await stubAudit(page, [])

  await page.goto('/settings/audit')

  await expect(page).toHaveURL('/settings/audit')
})
