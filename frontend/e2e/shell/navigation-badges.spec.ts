import { expect, test } from '../fixtures/guarded-test'
import { FULL_CATALOGUE, mainNavigation, openFullShell, openSection } from '../fixtures/open-full-shell'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubLandingReads } from '../fixtures/stub-landing-reads'
import { stubMailQueue } from '../fixtures/stub-mail-queue-routes'
import { stubModules } from '../fixtures/stub-modules-route'
import { stubPendingUpdates } from '../fixtures/stub-monitoring-routes'
import { stubMyAccount } from '../fixtures/stub-my-account-route'
import { stubResellerSession, ADMIN_MODULES } from '../fixtures/stub-reseller-session'
import { ACME_HOSTING } from '../fixtures/stub-resellers-routes'
import { stubShellReads } from '../fixtures/stub-shell-reads'
import type { MyAccount } from '../../src/types/account'
import type { AuthenticatedUser } from '../../src/types/auth'
import type { PanelModule } from '../../src/types/module'
import type { Page } from '@playwright/test'

// Issue #320: the dashboard item carries the needs-attention count, read from a narrow endpoint. A
// figure the panel could not take draws no badge at all, never a wrong one. The Servers > Updates
// item carries the number of pending package updates (issues #324, #320), by the same rule: a count
// that could not be read draws no badge and a count of zero draws none. The Mail queue item carries
// the number of messages in the queue (issue #320) by the same rule, and it is the administrator's
// alone: a reseller and a customer are not offered it and the shell never asks for its figure.
// Navigation is by clicks only.

const CUSTOMER: AuthenticatedUser = {
  id: '00000000-0000-0000-0000-000000000099',
  username: 'olive',
  email: 'olive@example.com',
  role: 'customer',
  accountId: '11111111-1111-1111-1111-111111111111',
}

/** The customer's own account, which the customer's shell reads; the plan lists no features, so every item is offered. */
const CUSTOMER_ACCOUNT: MyAccount = {
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

/** The narrow endpoint the needs-attention badge is read from. */
const ATTENTION_COUNT = /\/api\/v1\/dashboard\/attention-count(\?.*)?$/

/** The narrow endpoint the pending-updates badge is read from. */
const UPDATES_COUNT = /\/api\/v1\/monitoring\/updates\/count(\?.*)?$/

/** The narrow endpoint the mail-queue badge is read from. */
const MAIL_QUEUE_COUNT = /\/api\/v1\/mail-queue\/count(\?.*)?$/

/** The module the Mail queue item belongs to; the full catalogue does not list it. */
const MAIL: PanelModule = { name: 'mail', displayName: 'Mail', kindDisplayName: 'Core', tier: 'included', state: 'enabled' }

/**
 * Opens the administrator's shell with the attention endpoint answering what a spec names. The
 * answer is installed after the shell's catch-all so it wins, and the page is reloaded because the
 * shell reads the figure when it mounts.
 * @param page The Playwright page under test.
 * @param status The HTTP status the endpoint answers with.
 * @param body The JSON body it answers with.
 * @returns Resolves once the reloaded shell is drawn.
 */
const openWithAttention = async (page: Page, status: number, body: unknown): Promise<void> => {
  await openFullShell(page)
  await page.route(ATTENTION_COUNT, async (route) => {
    await route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(body) })
  })
  await page.reload()
  await expect(page.locator('nav [data-nav-section]').first()).toBeVisible()
}

/**
 * Opens the administrator's shell with the updates-count endpoint answering what a spec names, then
 * opens the Servers section so its pages are in the tree. Installed after the shell's catch-all so
 * it wins, and the page is reloaded because the shell reads the figure when it mounts.
 * @param page The Playwright page under test.
 * @param status The HTTP status the endpoint answers with.
 * @param body The JSON body it answers with.
 * @returns Resolves once the reloaded shell is drawn and the Servers pages are listed.
 */
const openWithUpdates = async (page: Page, status: number, body: unknown): Promise<void> => {
  await openFullShell(page)
  // The Updates item leads to its screen: its list and classifications are answered here, and the
  // count below replaces the one this stub gives.
  await stubPendingUpdates(page, [])
  await page.route(UPDATES_COUNT, async (route) => {
    await route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(body) })
  })
  await page.reload()
  await expect(page.locator('nav [data-nav-section]').first()).toBeVisible()
  await openSection(page, 'Servers')
}

test('the status item shows how many things need attention and says what it counts', async ({ page }) => {
  await openWithAttention(page, 200, { state: 'loaded', data: 4 })

  // The count is announced with what it counts, in the link's accessible name.
  const status = mainNavigation(page).getByRole('link', { name: 'System status 4 need attention' })
  await expect(status).toBeVisible()
  await status.click()
  await expect(page.getByRole('heading', { level: 1 })).toBeVisible()
})

test('nothing needing attention draws no badge', async ({ page }) => {
  await openWithAttention(page, 200, { state: 'loaded', data: 0 })

  await expect(mainNavigation(page).getByRole('link', { name: 'System status', exact: true })).toBeVisible()
})

test('a figure the panel could not take draws no badge rather than a wrong one', async ({ page }) => {
  await openWithAttention(page, 200, { state: 'unavailable' })

  await expect(mainNavigation(page).getByRole('link', { name: 'System status', exact: true })).toBeVisible()
  await expect(mainNavigation(page).getByRole('link', { name: /need attention/ })).toHaveCount(0)
})

test('a refused or failed read draws no badge either', async ({ page, guard }) => {
  // The refusal is the point: the figure cannot be taken.
  guard.allow(503, ATTENTION_COUNT)
  await openWithAttention(page, 503, { title: 'Service unavailable', status: 503 })

  await expect(mainNavigation(page).getByRole('link', { name: 'System status', exact: true })).toBeVisible()
  await expect(mainNavigation(page).getByRole('link', { name: /need attention/ })).toHaveCount(0)
})

/**
 * Opens the administrator's shell with the mail-queue count endpoint answering what a spec names,
 * then opens the Mail section so its pages are in the tree. Installed after the shell's catch-all so
 * it wins, and the page is reloaded because the shell reads the figure when it mounts.
 * @param page The Playwright page under test.
 * @param status The HTTP status the endpoint answers with.
 * @param body The JSON body it answers with.
 * @returns Resolves once the reloaded shell is drawn and the Mail pages are listed.
 */
const openWithMailQueue = async (page: Page, status: number, body: unknown): Promise<void> => {
  await openFullShell(page, '/', [...FULL_CATALOGUE, MAIL])
  // The Mail queue item leads to its screen: its list is answered here, and the count below replaces
  // the one this stub gives.
  await stubMailQueue(page, [])
  await page.route(MAIL_QUEUE_COUNT, async (route) => {
    await route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(body) })
  })
  await page.reload()
  await expect(page.locator('nav [data-nav-section]').first()).toBeVisible()
  await openSection(page, 'Mail')
}

test('the Mail queue item shows how many messages are queued and says what it counts', async ({ page }) => {
  await openWithMailQueue(page, 200, { count: 12 })

  // The count is announced with what it counts, in the link's accessible name.
  const queue = mainNavigation(page).getByRole('link', { name: 'Mail queue 12 in the mail queue' })
  await expect(queue).toBeVisible()
  await queue.click()
  await expect(page).toHaveURL('/mail/queue')
})

test('an empty mail queue draws no badge', async ({ page }) => {
  await openWithMailQueue(page, 200, { count: 0 })

  await expect(mainNavigation(page).getByRole('link', { name: 'Mail queue', exact: true })).toBeVisible()
  await expect(mainNavigation(page).getByRole('link', { name: /in the mail queue/ })).toHaveCount(0)
})

test('a mail queue the panel could not read draws no badge rather than a zero', async ({ page, guard }) => {
  // The refusal is the point: the queue cannot be read.
  guard.allow(502, MAIL_QUEUE_COUNT)
  await openWithMailQueue(page, 502, { title: 'The mail queue could not be read.', status: 502 })

  // Positive control: the Mail queue row is there, so the missing badge is the read's doing.
  await expect(mainNavigation(page).getByRole('link', { name: 'Mail queue', exact: true })).toBeVisible()
  await expect(mainNavigation(page).getByRole('link', { name: /in the mail queue/ })).toHaveCount(0)
})

test('a reseller is not offered the mail queue and never asks for its figure', async ({ page }) => {
  const requests: string[] = []
  page.on('request', (request) => {
    requests.push(request.url())
  })
  await stubResellerSession(page, ACME_HOSTING, [...ADMIN_MODULES, MAIL])
  await page.route(MAIL_QUEUE_COUNT, async (route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ count: 12 }) })
  })
  await page.goto('/')

  // Positive control: the reseller's shell is drawn, so the missing item is the role's doing.
  await expect(mainNavigation(page).getByRole('link', { name: 'Overview', exact: true })).toBeVisible()
  await expect(mainNavigation(page).getByRole('link', { name: /^Mail queue/ })).toHaveCount(0)
  expect(requests.filter((url) => {return MAIL_QUEUE_COUNT.test(url)})).toEqual([])
})

test('the Updates item shows how many updates are waiting and says what it counts', async ({ page }) => {
  await openWithUpdates(page, 200, { count: 7 })

  // The count is announced with what it counts, in the link's accessible name.
  const updates = mainNavigation(page).getByRole('link', { name: 'Updates 7 updates available' })
  await expect(updates).toBeVisible()
  await updates.click()
  await expect(page).toHaveURL('/servers/updates')
})

test('the Updates badge keeps the total when the security count is also known', async ({ page }) => {
  await openWithUpdates(page, 200, { count: 7, securityCount: 2 })

  await expect(mainNavigation(page).getByRole('link', { name: 'Updates 7 updates available' })).toBeVisible()
})

test('no updates waiting draws no badge', async ({ page }) => {
  await openWithUpdates(page, 200, { count: 0 })

  await expect(mainNavigation(page).getByRole('link', { name: 'Updates', exact: true })).toBeVisible()
  await expect(mainNavigation(page).getByRole('link', { name: /updates available/ })).toHaveCount(0)
})

test('a package cache the panel could not read draws no badge rather than a zero', async ({ page, guard }) => {
  // The refusal is the point: the package cache cannot be read.
  guard.allow(502, UPDATES_COUNT)
  await openWithUpdates(page, 502, { title: 'The update catalogue could not be read.', status: 502 })

  // Positive control: the Updates row is there, so the missing badge is the read's doing.
  await expect(mainNavigation(page).getByRole('link', { name: 'Updates', exact: true })).toBeVisible()
  await expect(mainNavigation(page).getByRole('link', { name: /updates available/ })).toHaveCount(0)
})

test('a customer is never shown, and never asks for, the server-wide figures', async ({ page }) => {
  const requests: string[] = []
  page.on('request', (request) => {
    requests.push(request.url())
  })
  await stubSignedIn(page, CUSTOMER)
  await stubHealthy(page)
  await stubModules(page, [...FULL_CATALOGUE, MAIL])
  await stubShellReads(page)
  await stubLandingReads(page, 'customer')
  await stubMyAccount(page, CUSTOMER_ACCOUNT)
  await page.goto('/')
  await expect(mainNavigation(page).getByRole('link', { name: 'System status', exact: true })).toBeVisible()

  expect(requests.filter((url) => {return ATTENTION_COUNT.test(url) || UPDATES_COUNT.test(url) || MAIL_QUEUE_COUNT.test(url)})).toEqual([])
})
