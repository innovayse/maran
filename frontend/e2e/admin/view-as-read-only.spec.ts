import { expect, test } from '../fixtures/guarded-test'
import { FULL_CATALOGUE, mainNavigation, openSection } from '../fixtures/open-full-shell'
import { stubAccounts } from '../fixtures/stub-accounts-route'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubDatabases } from '../fixtures/stub-databases-routes'
import { loaded, stubDashboard, SYNCHRONIZED_CLOCK, ALERT_DELIVERY_OK, WITHHELD_DASHBOARD } from '../fixtures/stub-dashboard-route'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubInvitation } from '../fixtures/stub-invitation-routes'
import { DEFAULT_LAYOUT, stubDashboardLayout } from '../fixtures/stub-live-dashboard-stream'
import { stubModules } from '../fixtures/stub-modules-route'
import { stubMyAccount } from '../fixtures/stub-my-account-route'
import { stubAccountResources, stubMyAccountResources } from '../fixtures/stub-account-resources-route'
import { stubShellReads } from '../fixtures/stub-shell-reads'
import { stubViewAs, VIEW_AS_TOKEN } from '../fixtures/stub-view-as-routes'
import type { Account, MyAccount } from '../../src/types/account'
import type { AccountResourceSummary } from '../../src/types/accountResourceSummary'
import type { AuthenticatedUser } from '../../src/types/auth'
import type { Dashboard } from '../../src/types/dashboard'
import type { ViewingAs } from '../../src/types/viewAs'
import type { Page } from '@playwright/test'

// Issues #513 and #515: a view-as session is refused many endpoints and every write, and the SPA's
// advice layer must reflect it. The session asks for nothing it is refused (the landing screen's
// stream and checklist, the offers, the signed-in devices, the sub-users), does not offer the pages
// made of those, draws its write forms disabled, and says in its banner that it is read-only. The
// panel stays the authority: the stubs below refuse exactly what the panel refuses. Every
// assertion that something was NOT requested or NOT offered has its accepting control beside it:
// the same walk as the customer's own session, where it is.

/** The account viewed. */
const ACME: Account = {
  id: '00000000-0000-0000-0000-0000000000c1',
  name: 'acme',
  primaryDomain: 'acme.example.com',
  planId: '00000000-0000-0000-0000-0000000000d1',
  status: 'active',
  statusName: 'Active',
  createdAt: '2026-08-30T09:00:00+00:00',
  resellerId: null,
  resellerName: null,
}

/** The account as its own login reads it. */
const OWN_ACCOUNT: MyAccount = {
  id: ACME.id,
  name: 'acme',
  primaryDomain: 'acme.example.com',
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

/** What the account's page reads about the account's use of its plan. */
const OWN_RESOURCES: AccountResourceSummary = {
  accountId: ACME.id,
  resources: [{ kind: 'sites', count: { state: 'counted', used: 0 }, limit: { kind: 'limited', maximum: 5 } }],
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
  kind: 'account',
  subjectId: ACME.id,
  subjectName: 'acme',
  username: 'olive',
  operatorUsername: 'admin',
  expiresAt: '2099-01-01T00:15:00+00:00',
}

/** What the server's dashboard answers an administrator: enough for the screen to be the server's. */
const ADMINISTRATOR_DASHBOARD: Dashboard = {
  audience: 'administrator',
  resources: loaded({
    cpuPercent: 37.4,
    memoryUsedBytes: 3 * 1024 * 1024 * 1024,
    memoryTotalBytes: 8 * 1024 * 1024 * 1024,
    diskUsedBytes: 44 * 1024 * 1024 * 1024,
    diskReservedBytes: 5 * 1024 * 1024 * 1024,
    diskAvailableBytes: 49 * 1024 * 1024 * 1024,
    diskTotalBytes: 98 * 1024 * 1024 * 1024,
    networkRxBytes: 4_200_000_000_000,
    networkTxBytes: 1_100_000_000_000,
    loadAverage1m: 0.21,
    loadAverage5m: 0.34,
    loadAverage15m: 0.55,
  }),
  services: loaded([]),
  counts: { accounts: loaded(1), sites: loaded(0), databases: loaded(0) },
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

/** The paths a view-as session is refused, as the panel refuses them (`ViewAsAccess.Refused`, and the administrator-only reads). */
const REFUSED_PATHS: readonly string[] = [
  '/api/v1/dashboard/stream',
  '/api/v1/dashboard/first-run',
  '/api/v1/me/membership-offers',
  '/api/v1/sessions',
  '/api/v1/me/sub-users',
  '/api/v1/me/sub-users/features',
]

/**
 * The administrator's own reads {@link stubRefusals} refuses on purpose, so the walk can see them:
 * the dashboard's stream and its first-run checklist.
 */
const ADMINISTRATOR_REFUSALS = /\/api\/v1\/dashboard\/(stream|first-run)$/

/** What {@link stubRefusals} saw, by path: the reads a view-as token made, and the ones any other token made. */
interface RefusedReads {
  /** Reads made with the view-as token, each answered 403. */
  viewAs: string[]
  /** Reads made with any other token. */
  other: string[]
}

/**
 * Answers every path in {@link REFUSED_PATHS} with 403 and records who asked: the view-as token's
 * attempts are what a spec asserts never happen, and every other token's are the proof the walk was
 * able to see such a read (the administrator's own screen opens its stream). Counting and not
 * throwing: an exception inside a route handler does not fail the test that installed it.
 * @param page The Playwright page whose network the routes are installed on.
 * @returns The reads seen so far, by who made them; a live view.
 */
const stubRefusals = async (page: Page): Promise<RefusedReads> => {
  const reads: RefusedReads = { viewAs: [], other: [] }

  await page.route(/\/api\/v1\/(dashboard\/(stream|first-run)|me\/(membership-offers|sub-users)|sessions)/, async (route) => {
    const path = new URL(route.request().url()).pathname
    const viewAs = route.request().headers()['authorization'] === `Bearer ${VIEW_AS_TOKEN}`
    if (!REFUSED_PATHS.includes(path)) {
      await route.fallback()
      return
    }

    if (viewAs) {
      reads.viewAs.push(path)
      await route.fulfill({
        status: 403,
        contentType: 'application/problem+json',
        body: JSON.stringify({ title: 'Not available while viewing', detail: 'Not available while viewing', status: 403, code: 'HostViewAsNotAllowed' }),
      })
      return
    }

    reads.other.push(path)
    await route.fulfill({ status: 403, contentType: 'application/problem+json', body: JSON.stringify({ title: 'no', status: 403 }) })
  })

  return reads
}

/**
 * Signs the administrator in and answers the reads of the walk. The dashboard answers by token:
 * the server's for the administrator, a customer's for the view-as token.
 * @param page The Playwright page whose network the routes are installed on.
 * @returns Resolves once the routes are installed.
 */
const stubPanel = async (page: Page): Promise<void> => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubShellReads(page)
  await stubMyAccount(page, OWN_ACCOUNT)
  await stubMyAccountResources(page, OWN_RESOURCES)
  await stubModules(page, FULL_CATALOGUE)
  await stubAccounts(page, [ACME])
  await stubDatabases(page, [])
  // The administrator's own screens: the dashboard's layout, and the account's page (usage and invitation).
  await stubDashboardLayout(page, DEFAULT_LAYOUT)
  await stubAccountResources(page, { accountId: ACME.id, resources: [] })
  await stubInvitation(page, ACME.id, 'accepted')
  await page.route('**/api/v1/dashboard', async (route) => {
    const viewAs = route.request().headers()['authorization'] === `Bearer ${VIEW_AS_TOKEN}`
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(viewAs ? WITHHELD_DASHBOARD : ADMINISTRATOR_DASHBOARD),
    })
  })
  await page.route(`**/api/v1/accounts/${ACME.id}`, async (route) => {
    if (route.request().method() !== 'GET') {
      await route.fallback()
      return
    }

    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(ACME) })
  })
}

/**
 * Starts viewing as the account, arriving from the administrator's own dashboard and by clicks only,
 * so the dashboard's answer for the administrator is still held when viewing begins: the state the
 * 403s of issue #513 came from.
 * @param page The Playwright page under test.
 * @param reads What the refusal stub has seen, to prove the administrator's own screen opened its stream.
 * @returns Resolves once the customer's dashboard is on screen under the banner.
 */
const startViewing = async (page: Page, reads: RefusedReads): Promise<void> => {
  await page.goto('/')
  await expect(page.getByRole('heading', { level: 1, name: 'System status' })).toBeVisible()
  // The administrator's own screen has opened its stream: the precondition that makes the walk below
  // able to catch a stream opened for the customer by mistake.
  await expect.poll(() => { return reads.other }).toContain('/api/v1/dashboard/stream')

  await openSection(page, 'Accounts')
  await mainNavigation(page).getByRole('link', { name: 'Account list' }).click()
  await page.getByRole('link', { name: 'acme' }).click()
  await page.getByRole('button', { name: 'View as this account' }).click()

  await expect(page.getByRole('region', { name: 'Viewing as another account' })).toBeVisible()
  await expect(page.getByRole('heading', { level: 1, name: 'System status' })).toBeVisible()
  await expect(page.getByRole('heading', { name: 'Your account' })).toBeVisible()
}

test('viewing as an account asks for nothing the session is refused and does not offer the pages made of it', async ({ page, guard }) => {
  guard.allow(403, ADMINISTRATOR_REFUSALS)
  await stubPanel(page)
  await stubViewAs(page, OLIVE, VIEWING)
  const reads = await stubRefusals(page)

  await startViewing(page, reads)

  await openSection(page, 'My account')
  // The section's own link and its first page both read "My account" and open the same screen.
  await expect(mainNavigation(page).getByRole('link', { name: 'My account' }).first()).toBeVisible()
  await expect(mainNavigation(page).getByRole('link', { name: 'Signed-in devices' })).toHaveCount(0)
  await expect(mainNavigation(page).getByRole('link', { name: 'Sub-users' })).toHaveCount(0)

  await mainNavigation(page).getByRole('link', { name: 'My account' }).first().click()
  await expect(page.getByRole('heading', { level: 1, name: 'acme' })).toBeVisible()

  expect(reads.viewAs).toEqual([])
})

test('the customer own session, where nothing is viewed, is offered the pages and reads the dashboard', async ({ page }) => {
  // The accepting control of the walk above: the same screens signed in as the customer.
  await stubSignedIn(page, OLIVE)
  await stubHealthy(page)
  await stubShellReads(page)
  await stubMyAccount(page, OWN_ACCOUNT)
  await stubMyAccountResources(page, OWN_RESOURCES)
  await stubModules(page, FULL_CATALOGUE)
  await stubDashboard(page, WITHHELD_DASHBOARD)

  await page.goto('/')
  await openSection(page, 'My account')

  await expect(mainNavigation(page).getByRole('link', { name: 'Signed-in devices' })).toBeVisible()
  await expect(mainNavigation(page).getByRole('link', { name: 'Sub-users' })).toBeVisible()
})

test('a view-as session is not offered the devices page in the account menu', async ({ page, guard }) => {
  guard.allow(403, ADMINISTRATOR_REFUSALS)
  await stubPanel(page)
  await stubViewAs(page, OLIVE, VIEWING)
  const reads = await stubRefusals(page)

  await startViewing(page, reads)

  await page.getByRole('button', { name: 'Account menu' }).first().click()
  await expect(page.getByRole('menuitem', { name: 'Profile' })).toBeVisible()
  await expect(page.getByRole('menuitem', { name: 'Sessions' })).toHaveCount(0)
  expect(reads.viewAs).toEqual([])
})

test('the banner says the session is read-only and the create form is drawn disabled', async ({ page, guard }) => {
  guard.allow(403, ADMINISTRATOR_REFUSALS)
  await stubPanel(page)
  await stubViewAs(page, OLIVE, VIEWING)
  const reads = await stubRefusals(page)

  await startViewing(page, reads)
  await expect(page.getByRole('region', { name: 'Viewing as another account' })).toContainText('This session is read-only')

  await mainNavigation(page).getByRole('link', { name: 'Databases' }).first().click()

  await expect(page.getByRole('textbox', { name: 'Database name' })).toBeDisabled()
  await expect(page.getByRole('textbox', { name: 'User name' })).toBeDisabled()
  await expect(page.getByRole('button', { name: 'Create database' })).toBeDisabled()
})

test('the customer own session draws the same create form enabled', async ({ page }) => {
  // The accepting control of the read-only walk: nothing here disables a form by itself.
  await stubSignedIn(page, OLIVE)
  await stubHealthy(page)
  await stubShellReads(page)
  await stubMyAccount(page, OWN_ACCOUNT)
  await stubMyAccountResources(page, OWN_RESOURCES)
  await stubModules(page, FULL_CATALOGUE)
  await stubDatabases(page, [])
  await stubDashboard(page, WITHHELD_DASHBOARD)

  await page.goto('/databases')

  await expect(page.getByRole('textbox', { name: 'Database name' })).toBeEnabled()
  await expect(page.getByRole('textbox', { name: 'User name' })).toBeEnabled()
  await expect(page.getByRole('button', { name: 'Create database' })).toBeEnabled()
})
