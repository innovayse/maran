import { expect, test } from '../fixtures/guarded-test'
import { mainNavigation, openSection } from '../fixtures/open-full-shell'
import { openMarketplace } from '../fixtures/open-marketplace'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubLandingReads } from '../fixtures/stub-landing-reads'
import { stubModules } from '../fixtures/stub-modules-route'
import { stubMyAccount } from '../fixtures/stub-my-account-route'
import { stubShellReads } from '../fixtures/stub-shell-reads'
import {
  enabledWorld,
  marketplaceWorld,
  stubMarketplace,
  type MarketplaceRecorder,
} from '../fixtures/stub-marketplace-routes'
import type { MyAccount } from '../../src/types/account'
import type { AuthenticatedUser } from '../../src/types/auth'
import type { PanelModule } from '../../src/types/module'
import type { Page } from '@playwright/test'

// Issue #187: a customer is never offered the marketplace and cannot open it by address. The
// router flags are advice (the endpoints refuse a customer on their own, tested on the backend);
// what the SPA owes is that the entry is absent, the address lands on the "not available" page,
// and the page does not even ask the panel. The administrator opening the same address is the
// positive control that the guard reads the role.

/** The signed-in customer. */
const CUSTOMER: AuthenticatedUser = {
  id: '00000000-0000-0000-0000-000000000099',
  username: 'olive',
  email: 'olive@example.com',
  role: 'customer',
  accountId: '11111111-1111-1111-1111-111111111111',
}

/** The customer's own account, which the customer's menu is drawn from. */
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

/** What the panel reports a customer: the marketplace is not part of their area, so it is absent. */
const CUSTOMER_MODULES: PanelModule[] = [
  { name: 'sites', displayName: 'Sites', kindDisplayName: 'Core', tier: 'included', state: 'enabled' },
  { name: 'databases', displayName: 'Databases', kindDisplayName: 'Core', tier: 'included', state: 'enabled' },
]

/**
 * Signs the customer in with the shell stubbed and the marketplace stubs installed, so any request
 * the page made to the marketplace would be recorded.
 * @param page The Playwright page under test.
 * @returns The recorder of requests to the marketplace's path.
 */
const signInCustomer = async (page: Page): Promise<MarketplaceRecorder> => {
  await stubSignedIn(page, CUSTOMER)
  await stubHealthy(page)
  await stubShellReads(page)
  await stubMyAccount(page, OWN_ACCOUNT)
  await stubLandingReads(page, 'customer')
  await stubModules(page, CUSTOMER_MODULES)
  return stubMarketplace(page, enabledWorld())
}

test('a customer’s menu has no Marketplace entry', async ({ page }) => {
  await signInCustomer(page)
  await page.goto('/')

  await expect(mainNavigation(page)).toBeVisible()
  await expect(mainNavigation(page).getByRole('link').first()).toBeVisible()
  await expect(page.getByRole('link', { name: 'Marketplace', exact: true })).toHaveCount(0)
})

for (const path of ['/marketplace', '/marketplace/weather']) {
  test(`a customer opening ${path} lands on the not-available page and the page asks nothing`, async ({ page }) => {
    const recorder = await signInCustomer(page)

    await page.goto(path)

    await expect(page).toHaveURL('/not-available')
    await expect(page.getByRole('heading', { level: 1, name: 'Page not available' })).toBeVisible()
    await expect(page.getByRole('heading', { level: 1, name: 'Marketplace' })).toHaveCount(0)
    await page.waitForLoadState('networkidle')
    expect(recorder.requests).toEqual([])
  })
}

test('an administrator sees the Marketplace entry and opens the page at the same address', async ({ page }) => {
  // The positive control for both tests above: the menu entry exists and the address resolves.
  const recorder = await openMarketplace(page, marketplaceWorld(), '/')
  await openSection(page, 'Settings')
  await expect(mainNavigation(page).getByRole('link', { name: 'Marketplace', exact: true })).toBeVisible()

  await page.goto('/marketplace')

  await expect(page).toHaveURL('/marketplace')
  await expect(page.getByRole('heading', { level: 1, name: 'Marketplace' })).toBeVisible()
  expect(recorder.matching(/^GET \/api\/v1\/marketplace\/status/)).not.toEqual([])
})
