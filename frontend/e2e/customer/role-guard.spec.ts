import { expect, test } from '@playwright/test'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import { forbidAccountsList } from '../fixtures/stub-my-account-route'
import { setPersistedLocale } from '../fixtures/set-locale'
import type { AuthenticatedUser } from '../../src/types/auth'
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

// The catalogue an administrator would see, so the guard cannot pass merely because the module
// guard already redirected: every admin-only route below belongs to a listed, enabled module.
const MODULES: PanelModule[] = [
  { name: 'accounts', displayName: 'Accounts', tier: 'included', isEnabled: true },
  { name: 'databases', displayName: 'Databases', tier: 'included', isEnabled: true },
  { name: 'backups', displayName: 'Backups', tier: 'included', isEnabled: true },
  { name: 'firewall', displayName: 'Firewall', tier: 'included', isEnabled: true },
  { name: 'monitoring', displayName: 'Monitoring', tier: 'included', isEnabled: true },
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
  await stubModules(page, [{ name: 'sites', displayName: 'Sites', tier: 'included', isEnabled: true }])

  await page.goto('/cron')

  await expect(page).toHaveURL('/not-available')
})

test('a module the licence locks still sends a customer to the upgrade page', async ({ page }) => {
  await stubSignedIn(page, CUSTOMER)
  await stubHealthy(page)
  await stubModules(page, [{ name: 'cron', displayName: 'Cron', tier: 'business', isEnabled: false }])

  await page.goto('/cron')

  await expect(page).toHaveURL('/upgrade/cron')
})

test('the way back from the not-available page leads to system status', async ({ page }) => {
  await stubSignedIn(page, CUSTOMER)
  await stubHealthy(page)
  await stubModules(page, MODULES)

  await page.goto('/settings/audit')
  await page.getByRole('link', { name: 'Back to system status' }).click()

  await expect(page).toHaveURL('/')
})

test('an administrator still reaches an admin-only screen', async ({ page }) => {
  // Positive control: the guard reads the role, not the flag alone.
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, MODULES)

  await page.goto('/settings/audit')

  await expect(page).toHaveURL('/settings/audit')
})
