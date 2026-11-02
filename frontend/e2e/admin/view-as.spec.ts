import { expect, test, type Page } from '@playwright/test'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import { stubInvitation } from '../fixtures/stub-invitation-routes'
import { stubAccounts } from '../fixtures/stub-accounts-route'
import { stubDashboard, WITHHELD_DASHBOARD } from '../fixtures/stub-dashboard-route'
import { stubSites, stubPhpVersions } from '../fixtures/stub-sites-routes'
import { stubDatabases } from '../fixtures/stub-databases-routes'
import { mainNavigation } from '../fixtures/open-full-shell'
import { stubViewAs, stubViewAsRefused } from '../fixtures/stub-view-as-routes'
import type { Account } from '../../src/types/account'
import type { AuthenticatedUser } from '../../src/types/auth'
import type { ViewingAs } from '../../src/types/viewAs'

// Issues #317 and #314: an administrator views the panel as an account's login from the account's
// page, the "You are viewing as" banner follows them from page to page, Back returns them to their
// own session, and a refused start leaves them where they were. Navigation is by clicks only; the
// one `goto` is the entry point every account spec uses.

/** The account viewed. */
const ACME: Account = {
  id: '00000000-0000-0000-0000-0000000000c1',
  name: 'acme',
  primaryDomain: 'acme.example.com',
  planId: '00000000-0000-0000-0000-0000000000d1',
  status: 'active',
  createdAt: '2026-08-30T09:00:00+00:00',
}

/** The account's login, which the view-as token signs in as. */
const OLIVE: AuthenticatedUser = {
  id: '00000000-0000-0000-0000-000000000099',
  username: 'olive',
  email: 'olive@example.com',
  role: 'customer',
  accountId: ACME.id,
}

/** What the panel reports about the session while it lasts. */
const VIEWING: ViewingAs = {
  accountId: ACME.id,
  accountName: 'acme',
  username: 'olive',
  operatorUsername: 'admin',
  expiresAt: '2099-01-01T00:15:00+00:00',
}

/**
 * Signs the administrator in and answers the account list, the account's page and the customer
 * screens the walk visits.
 * @param page The Playwright page whose network the routes are installed on.
 * @returns Resolves once the routes are installed.
 */
const stubPanel = async (page: Page): Promise<void> => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, [
    { name: 'accounts', displayName: 'Accounts', tier: 'included', isEnabled: true },
    { name: 'sites', displayName: 'Sites', tier: 'included', isEnabled: true },
    { name: 'databases', displayName: 'Databases', tier: 'included', isEnabled: true },
  ])
  await stubAccounts(page, [ACME])
  await stubInvitation(page, ACME.id, 'accepted')
  await stubDashboard(page, WITHHELD_DASHBOARD)
  await stubSites(page, [])
  await stubPhpVersions(page, [])
  await stubDatabases(page, [])
  await page.route(`**/api/v1/accounts/${ACME.id}`, async (route) => {
    if (route.request().method() !== 'GET') {
      await route.fallback()
      return
    }

    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(ACME) })
  })
}

/**
 * Opens the account list and then the account, by clicking its name.
 * @param page The Playwright page under test.
 * @returns Resolves once the account's page shows its "View as" action.
 */
const openAccount = async (page: Page): Promise<void> => {
  await page.goto('/accounts')
  await page.getByRole('link', { name: 'acme' }).click()
  await expect(page.getByRole('button', { name: 'View as this account' })).toBeVisible()
}

/**
 * The banner's region.
 * @param page The Playwright page under test.
 * @returns Its locator.
 */
const banner = (page: Page): ReturnType<Page['getByRole']> => {
  return page.getByRole('region', { name: 'Viewing as another account' })
}

test('viewing as an account shows the banner on every page and Back returns the administrator', async ({ page }) => {
  await stubPanel(page)
  const calls = await stubViewAs(page, OLIVE, VIEWING)

  await openAccount(page)
  await page.getByRole('button', { name: 'View as this account' }).click()

  await expect(banner(page)).toContainText('You are viewing as acme (olive).')
  expect(calls.started).toBe(1)

  // The banner stays on every customer page the operator clicks to.
  await mainNavigation(page).getByRole('link', { name: 'Sites' }).first().click()
  await expect(page.getByRole('heading', { level: 1 })).toBeVisible()
  await expect(banner(page)).toBeVisible()

  await mainNavigation(page).getByRole('link', { name: 'Databases' }).first().click()
  await expect(page.getByRole('heading', { level: 1 })).toBeVisible()
  await expect(banner(page)).toBeVisible()

  await banner(page).getByRole('button', { name: 'Back to admin' }).click()

  await expect(banner(page)).toHaveCount(0)
  await expect(page).toHaveURL(`/accounts/${ACME.id}`)
  expect(calls.ended).toBe(1)
})

test('the banner follows the panel: a token the panel no longer reports as viewing shows none', async ({ page }) => {
  await stubPanel(page)
  await stubViewAs(page, OLIVE, VIEWING)

  // Signed in as the administrator, the panel reports no view-as session, so there is no banner even
  // on the account's page where viewing starts.
  await openAccount(page)

  await expect(banner(page)).toHaveCount(0)
})

test('a refused start shows the panel sentence and changes nothing', async ({ page }) => {
  await stubPanel(page)
  await stubViewAsRefused(page, 'That account was not found, or it has no login to view as.')

  await openAccount(page)
  await page.getByRole('button', { name: 'View as this account' }).click()

  await expect(page.getByText('That account was not found, or it has no login to view as.')).toBeVisible()
  await expect(banner(page)).toHaveCount(0)
  await expect(page).toHaveURL(`/accounts/${ACME.id}`)
})
