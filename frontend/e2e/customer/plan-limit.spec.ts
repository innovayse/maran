import { expect, test } from '../fixtures/guarded-test'
import { stubMyAccountResources } from '../fixtures/stub-account-resources-route'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubDatabases } from '../fixtures/stub-databases-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubLandingReads } from '../fixtures/stub-landing-reads'
import { stubModules } from '../fixtures/stub-modules-route'
import { stubMyAccount } from '../fixtures/stub-my-account-route'
import { stubShellReads } from '../fixtures/stub-shell-reads'
import { stubPhpVersions, stubSites } from '../fixtures/stub-sites-routes'
import type { MyAccount } from '../../src/types/account'
import type { AccountResourceSummary, AccountResourceUsage } from '../../src/types/accountResourceSummary'
import type { AuthenticatedUser } from '../../src/types/auth'
import type { PanelModule } from '../../src/types/module'
import type { Page } from '@playwright/test'

// Issue #312: at a plan limit the create action is withheld and the reason is shown in its place.
// Advice only — the endpoint refuses a create past the plan whatever the screen offers — so every
// "withheld" test here has a positive control in which the same screen offers the action.

const CUSTOMER: AuthenticatedUser = {
  id: '00000000-0000-0000-0000-000000000099',
  username: 'olive',
  email: 'olive@example.com',
  role: 'customer',
  accountId: '11111111-1111-1111-1111-111111111111',
}

const OWN_ACCOUNT: MyAccount = {
  id: CUSTOMER.accountId ?? '',
  name: 'olive',
  primaryDomain: 'olive.example.com',
  status: 'active',
  statusName: 'Active',
  plan: {
    displayName: 'Starter',
    diskQuotaMb: 1024,
    maxSites: 2,
    maxDatabases: 2,
    maxSftpUsers: 2,
    maxFtpUsers: 2,
    maxCronEntries: 2,
  },
}

const MODULES: PanelModule[] = [
  { name: 'sites', displayName: 'Sites', kindDisplayName: 'Core', tier: 'included', state: 'enabled' },
  { name: 'databases', displayName: 'Databases', kindDisplayName: 'Core', tier: 'included', state: 'enabled' },
]

const REASON = /your plan allows 2, and all of them are in use/

const row = (kind: AccountResourceUsage['kind'], used: number): AccountResourceUsage => {
  return { kind, count: { state: 'counted', used }, limit: { kind: 'limited', maximum: 2 } }
}

const signIn = async (page: Page, sitesUsed: number, databasesUsed: number): Promise<void> => {
  const summary: AccountResourceSummary = {
    accountId: CUSTOMER.accountId ?? '',
    resources: [row('sites', sitesUsed), row('databases', databasesUsed)],
  }
  await stubSignedIn(page, CUSTOMER)
  await stubHealthy(page)
  await stubShellReads(page)
  await stubModules(page, MODULES)
  await stubLandingReads(page, 'customer')
  await stubMyAccount(page, OWN_ACCOUNT)
  await stubMyAccountResources(page, summary)
  await stubSites(page, [])
  await stubPhpVersions(page, [])
  await stubDatabases(page, [])
}

const openFromSidebar = async (page: Page, name: string): Promise<void> => {
  await page.goto('/')
  await page.getByRole('navigation', { name: 'Main navigation' }).getByRole('link', { name }).first().click()
}

test('a customer at the site limit is told why instead of offered a new site', async ({ page }) => {
  await signIn(page, 2, 0)
  await openFromSidebar(page, 'Sites and domains')

  await expect(page.getByText(REASON)).toBeVisible()
  await expect(page.getByRole('main').getByRole('link', { name: 'New site' })).toHaveCount(0)
})

test('a customer under the site limit is offered a new site', async ({ page }) => {
  await signIn(page, 1, 0)
  await openFromSidebar(page, 'Sites and domains')

  await expect(page.getByRole('heading', { level: 1, name: 'Sites' })).toBeVisible()
  await expect(page.getByRole('main').getByRole('link', { name: 'New site' }).first()).toBeVisible()
  await expect(page.getByText(REASON)).toHaveCount(0)
})

test('a customer at the database limit sees the reason and no create form', async ({ page }) => {
  await signIn(page, 0, 2)
  await openFromSidebar(page, 'Databases')

  await expect(page.getByText(REASON)).toBeVisible()
  await expect(page.getByRole('button', { name: 'Create database' })).toHaveCount(0)
})

test('a customer under the database limit gets the create form', async ({ page }) => {
  await signIn(page, 0, 1)
  await openFromSidebar(page, 'Databases')

  await expect(page.getByRole('button', { name: 'Create database' })).toBeVisible()
  await expect(page.getByText(REASON)).toHaveCount(0)
})
