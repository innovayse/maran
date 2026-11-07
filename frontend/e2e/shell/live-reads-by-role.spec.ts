import { expect, test, type Page } from '@playwright/test'
import { FULL_CATALOGUE, mainNavigation } from '../fixtures/open-full-shell'
import { stubAccounts } from '../fixtures/stub-accounts-route'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubDashboard, WITHHELD_DASHBOARD, loaded } from '../fixtures/stub-dashboard-route'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubInvitation } from '../fixtures/stub-invitation-routes'
import { stubModules } from '../fixtures/stub-modules-route'
import { stubMyAccountResources } from '../fixtures/stub-account-resources-route'
import { stubMyAccount } from '../fixtures/stub-my-account-route'
import { stubResellerSession } from '../fixtures/stub-reseller-session'
import { stubShellReads } from '../fixtures/stub-shell-reads'
import { stubViewAs } from '../fixtures/stub-view-as-routes'
import type { Account, MyAccount } from '../../src/types/account'
import type { AccountResourceSummary } from '../../src/types/accountResourceSummary'
import type { AuthenticatedUser } from '../../src/types/auth'
import type { ViewingAs } from '../../src/types/viewAs'

// Issue #415: the shell polls and streams only what the signed-in role may call. A reseller has no
// tasks screen and no server-wide figures, so it makes none of those requests; an administrator who
// views as a customer drops the administrator's badge, timers and streams with the identity, and the
// customer's shell asks for nothing an administrator alone may read. The administrator's own shell
// is the positive control: the same observer sees the requests there, so an empty list for the
// reseller means nothing was asked, not that the observer was blind.

/** The tasks endpoints the shell reads: the running stream and the failed count. */
const TASKS_READS = /\/api\/v1\/tasks\/(running\/stream|failed-count)(\?.*)?$/

/** The server-wide figures only an administrator may read. */
const SERVER_FIGURES = /\/api\/v1\/(dashboard\/attention-count|monitoring\/updates\/count)(\?.*)?$/

/** The attention figure alone, which the navigation badge reads. */
const ATTENTION_COUNT = /\/api\/v1\/dashboard\/attention-count(\?.*)?$/

/** The account an administrator views as a customer. */
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

/** The account as its own login reads it while viewing. */
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

/** What the customer dashboard reads about the account's use of its plan while viewing. */
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

/** What the panel reports about the view-as session while it lasts. */
const VIEWING: ViewingAs = {
  kind: 'account',
  subjectId: ACME.id,
  subjectName: 'acme',
  username: 'olive',
  operatorUsername: 'admin',
  expiresAt: '2099-01-01T00:15:00+00:00',
}

/**
 * Records the URL of every request the page makes to the panel's API.
 * @param page The Playwright page under test.
 * @returns A live list of the request URLs.
 */
const recordRequests = (page: Page): string[] => {
  const urls: string[] = []
  page.on('request', (request) => {
    urls.push(request.url())
  })
  return urls
}

/**
 * Counts the recorded requests that match a pattern.
 * @param urls The recorded request URLs.
 * @param pattern What to count.
 * @returns How many matched.
 */
const countOf = (urls: readonly string[], pattern: RegExp): number => {
  return urls.filter((url) => {
    return pattern.test(url)
  }).length
}

test('a reseller session asks for no tasks and no server-wide figures', async ({ page }) => {
  const urls = recordRequests(page)
  await stubResellerSession(page)

  await page.goto('/')
  await expect(mainNavigation(page).getByRole('link').first()).toBeVisible()

  expect(countOf(urls, TASKS_READS)).toBe(0)
  expect(countOf(urls, SERVER_FIGURES)).toBe(0)
})

test('an administrator session asks for the tasks stream and the figures, so the observer is not blind', async ({
  page,
}) => {
  const urls = recordRequests(page)
  await page.setViewportSize({ width: 1440, height: 900 })
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubShellReads(page)
  await stubModules(page, FULL_CATALOGUE)

  await page.goto('/')
  await expect(mainNavigation(page).getByRole('link').first()).toBeVisible()

  await expect.poll(() => {
    return countOf(urls, /\/api\/v1\/tasks\/running\/stream(\?.*)?$/)
  }).toBeGreaterThan(0)
  await expect.poll(() => {
    return countOf(urls, ATTENTION_COUNT)
  }).toBeGreaterThan(0)
})

test('after view-as the customer shell has no attention badge and asks for no attention count', async ({ page }) => {
  const urls = recordRequests(page)
  await page.setViewportSize({ width: 1440, height: 900 })
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubShellReads(page)
  await stubMyAccount(page, OWN_ACCOUNT)
  await stubMyAccountResources(page, OWN_RESOURCES)
  await stubModules(page, FULL_CATALOGUE)
  await stubAccounts(page, [ACME])
  await stubInvitation(page, ACME.id, 'accepted')
  await stubDashboard(page, WITHHELD_DASHBOARD)
  await page.route(`**/api/v1/accounts/${ACME.id}`, async (route) => {
    if (route.request().method() !== 'GET') {
      await route.fallback()
      return
    }
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(ACME) })
  })
  // The administrator's shell has something that needs attention: the badge the customer must not inherit.
  await page.route(ATTENTION_COUNT, async (route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(loaded(1)) })
  })
  await stubViewAs(page, OLIVE, VIEWING)

  await page.goto('/accounts')
  await expect(mainNavigation(page).getByRole('link', { name: 'System status 1 need attention' })).toBeVisible()
  await page.getByRole('link', { name: 'acme' }).click()

  const askedBefore = countOf(urls, ATTENTION_COUNT)
  await page.getByRole('button', { name: 'View as this account' }).click()
  await expect(page.getByRole('region', { name: 'Viewing as another account' })).toBeVisible()

  // The customer's shell is drawn (its own menu is there) and carries no badge of the administrator's.
  await expect(mainNavigation(page).getByRole('link', { name: 'Sites' }).first()).toBeVisible()
  await expect(mainNavigation(page).getByRole('link', { name: /need attention/ })).toHaveCount(0)
  expect(countOf(urls, ATTENTION_COUNT)).toBe(askedBefore)
})
