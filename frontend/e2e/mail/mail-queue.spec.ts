import { expect, test } from '../fixtures/guarded-test'
import { FULL_CATALOGUE, mainNavigation, openSection } from '../fixtures/open-full-shell'
import { setPersistedLocale } from '../fixtures/set-locale'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubLandingReads } from '../fixtures/stub-landing-reads'
import { queuedMessage, stubMailQueue, type MailQueueStub } from '../fixtures/stub-mail-queue-routes'
import { stubModules } from '../fixtures/stub-modules-route'
import { stubMyAccount } from '../fixtures/stub-my-account-route'
import { ADMIN_MODULES, stubResellerSession } from '../fixtures/stub-reseller-session'
import { ACME_HOSTING } from '../fixtures/stub-resellers-routes'
import { stubShellReads } from '../fixtures/stub-shell-reads'
import type { MyAccount } from '../../src/types/account'
import type { AuthenticatedUser } from '../../src/types/auth'
import type { PanelModule } from '../../src/types/module'
import type { MailQueueEntry } from '../../src/types/mailQueue'
import type { Page } from '@playwright/test'

// The mail queue page (issue #320), for administrators: one server page of the messages the host is
// holding, searched and sorted by the panel; a message is removed after a confirmation; the deferred
// ones are retried after another. The page and its menu item are the administrator's alone, and a
// reseller or a customer is neither offered the item nor allowed to ask. Every spec reaches the page
// through the sidebar's Mail section, by clicks, except the ones that prove a screen is refused.

/** The address the 390px check below opens; `e2e/ui-kit/route-coverage.spec.ts` reads it from here. */
const SWEPT_SCREEN = { path: '/mail/queue' }

/** The module whose licence the queue page is behind. */
const MAIL: PanelModule = { name: 'mail', displayName: 'Mail', kindDisplayName: 'Core', tier: 'included', state: 'enabled' }

/** Every module, mail included. */
const WITH_MAIL: PanelModule[] = [...FULL_CATALOGUE, MAIL]

/** A customer, for the "likewise" half of the role check. */
const CUSTOMER: AuthenticatedUser = {
  id: '00000000-0000-0000-0000-000000000099',
  username: 'olive',
  email: 'olive@example.com',
  role: 'customer',
  accountId: '11111111-1111-1111-1111-111111111111',
}

/** The account the customer above owns; the customer's shell reads it on every screen. */
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

/** A message from an ordinary sender. */
const NEWSLETTER = queuedMessage('AAAA1111BBBB', 'news@example.net', { recipientCount: 3, sizeBytes: 5 * 1024 })

/** A message whose sender is hostile text, which the page must draw as text. */
const HOSTILE = queuedMessage('CCCC2222DDDD', '<img src=x onerror=alert(1)>@evil.example', {
  queue: 'held',
  queueDisplayName: 'Held',
  deferralClass: 'none',
  deferralDisplayName: 'Not deferred',
  arrivedAt: '2026-11-07T09:30:00Z',
})

/**
 * Signs the administrator in on a desktop with every module licensed, and answers the reads the
 * shell and the landing screen make.
 * @param page The Playwright page under test.
 * @returns Resolves once the routes are installed.
 */
const signInAdministrator = async (page: Page): Promise<void> => {
  await page.setViewportSize({ width: 1440, height: 900 })
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubShellReads(page)
  await stubLandingReads(page)
  await stubModules(page, WITH_MAIL)
}

/**
 * Opens the landing screen and waits until the sidebar is drawn, the way the shell is reached.
 * @param page The Playwright page under test.
 * @returns Resolves once the sidebar's sections are on screen.
 */
const openLanding = async (page: Page): Promise<void> => {
  await page.goto('/')
  await expect(page.locator('nav [data-nav-section]').first()).toBeVisible()
}

/**
 * Opens the administrator's shell with the queue stubbed, then the Mail section's queue page by clicks.
 * @param page The Playwright page under test.
 * @param entries The messages the stub holds.
 * @param options How the stub answers besides its rows.
 * @returns The recorder of the stubbed queue.
 */
const openQueue = async (
  page: Page,
  entries: MailQueueEntry[],
  options: { truncated?: boolean; queueTotal?: number } = {},
): Promise<MailQueueStub> => {
  await signInAdministrator(page)
  const stub = await stubMailQueue(page, entries, options)
  await openLanding(page)
  await openSection(page, 'Mail')
  await mainNavigation(page).getByRole('link', { name: /^Mail queue/ }).click()
  await expect(page.getByRole('heading', { level: 1, name: 'Mail queue' })).toBeVisible()
  return stub
}

/**
 * Opens a row's actions menu and picks an item.
 * @param page The Playwright page under test.
 * @param queueId The message's id.
 * @param item The menu item's name.
 * @returns Resolves once the item was picked.
 */
const pickRowAction = async (page: Page, queueId: string, item: string): Promise<void> => {
  await page.getByRole('button', { name: `Actions for message ${queueId}` }).click()
  await page.getByRole('menuitem', { name: item, exact: true }).click()
}

test('the page lists the queued messages with their sender, queue and why they wait', async ({ page }) => {
  await openQueue(page, [NEWSLETTER, HOSTILE])

  const row = page.getByRole('row').filter({ hasText: NEWSLETTER.queueId })
  await expect(row).toContainText('news@example.net')
  await expect(row).toContainText('Waiting to retry')
  await expect(row).toContainText('Network problem')
  await expect(row).toContainText('5 KiB')
  await expect(page.getByRole('row').filter({ hasText: HOSTILE.queueId })).toContainText('Held')
})

test('a sender that looks like markup is drawn as text and runs nothing', async ({ page }) => {
  await openQueue(page, [HOSTILE])

  await expect(page.getByRole('cell', { name: HOSTILE.sender, exact: true })).toBeVisible()
  // The planted markup would have created an image element in the table had it been parsed.
  await expect(page.getByRole('table').locator('img')).toHaveCount(0)
})

test('an empty queue says so and draws no table', async ({ page }) => {
  await openQueue(page, [])

  await expect(page.getByText('The mail queue is empty')).toBeVisible()
  await expect(page.getByRole('table')).toHaveCount(0)
})

test('a search narrows the page on the server and the way back shows the whole queue', async ({ page }) => {
  const stub = await openQueue(page, [NEWSLETTER, HOSTILE])

  await page.getByRole('searchbox', { name: 'Search the queue' }).fill('news@')

  await expect(page.getByRole('row').filter({ hasText: HOSTILE.queueId })).toHaveCount(0)
  await expect(page.getByRole('row').filter({ hasText: NEWSLETTER.queueId })).toBeVisible()
  expect(stub.lists().at(-1)?.searchParams.get('search')).toBe('news@')

  await page.getByRole('searchbox', { name: 'Search the queue' }).fill('nobody-sends-as-this')
  await expect(page.getByText('No messages match')).toBeVisible()
  await page.getByRole('button', { name: 'Clear search and filters' }).click()
  await expect(page.getByRole('row').filter({ hasText: HOSTILE.queueId })).toBeVisible()
})

test('a sortable heading asks the server to sort', async ({ page }) => {
  const stub = await openQueue(page, [NEWSLETTER, HOSTILE])

  await page.getByRole('columnheader', { name: 'Sender' }).getByRole('button').click()

  await expect.poll(() => {
    return stub.lists().at(-1)?.searchParams.get('sort')
  }).toBe('sender')
})

test('a queue longer than the list says that only the newest messages are paged', async ({ page }) => {
  await openQueue(page, [NEWSLETTER], { truncated: true, queueTotal: 480 })

  await expect(page.getByText('Messages in the queue: 480.', { exact: false })).toBeVisible()
})

test('a queue that is not longer than the list shows no such notice', async ({ page }) => {
  // The inverse control for the notice above: it is not drawn for every queue.
  await openQueue(page, [NEWSLETTER])

  await expect(page.getByRole('row').filter({ hasText: NEWSLETTER.queueId })).toBeVisible()
  await expect(page.getByText('Only the newest ones the server lists', { exact: false })).toHaveCount(0)
})

test('a message is removed only after the administrator confirms', async ({ page }) => {
  const stub = await openQueue(page, [NEWSLETTER, HOSTILE])

  await pickRowAction(page, NEWSLETTER.queueId, 'Remove from the queue')
  const dialog = page.getByRole('dialog', { name: `Remove message ${NEWSLETTER.queueId}` })
  await expect(dialog).toBeVisible()
  // Backing out sends nothing: the inverse control of the removal below.
  await dialog.getByRole('button', { name: 'Cancel' }).click()
  expect(stub.removals()).toEqual([])
  await expect(page.getByRole('row').filter({ hasText: NEWSLETTER.queueId })).toBeVisible()

  await pickRowAction(page, NEWSLETTER.queueId, 'Remove from the queue')
  await page.getByRole('dialog', { name: `Remove message ${NEWSLETTER.queueId}` }).getByRole('button', { name: 'Remove message' }).click()

  await expect(page.getByText(`Message ${NEWSLETTER.queueId} was removed.`)).toBeVisible()
  await expect(page.getByRole('row').filter({ hasText: NEWSLETTER.queueId })).toHaveCount(0)
  await expect(page.getByRole('row').filter({ hasText: HOSTILE.queueId })).toBeVisible()
  expect(stub.removals()).toEqual([NEWSLETTER.queueId])
})

test('retrying the deferred mail asks first and then tells the panel once', async ({ page }) => {
  const stub = await openQueue(page, [NEWSLETTER])

  await page.getByRole('button', { name: 'Retry deferred mail' }).click()
  const dialog = page.getByRole('dialog', { name: 'Retry deferred mail' })
  await dialog.getByRole('button', { name: 'Cancel' }).click()
  expect(stub.flushes()).toBe(0)

  await page.getByRole('button', { name: 'Retry deferred mail' }).click()
  await page.getByRole('dialog', { name: 'Retry deferred mail' }).getByRole('button', { name: 'Retry now' }).click()

  await expect(page.getByText('Deferred mail is being retried.')).toBeVisible()
  expect(stub.flushes()).toBe(1)
})

test('a queue the panel could not read shows its message and no table, not an empty queue', async ({ page, guard }) => {
  guard.allow(502, /\/api\/v1\/mail-queue(\?.*)?$/)
  await signInAdministrator(page)
  await page.route(/\/api\/v1\/mail-queue(\?.*)?$/, async (route) => {
    await route.fulfill({
      status: 502,
      contentType: 'application/problem+json',
      body: JSON.stringify({ code: 'AgentUnavailable', title: 'The mail queue could not be read.', status: 502 }),
    })
  })
  await openLanding(page)
  await openSection(page, 'Mail')
  await mainNavigation(page).getByRole('link', { name: /^Mail queue/ }).click()

  await expect(page.getByText('The mail queue could not be read.')).toBeVisible()
  await expect(page.getByRole('table')).toHaveCount(0)
  await expect(page.getByText('The mail queue is empty')).toHaveCount(0)
})

test('a removal the panel refused shows its message and keeps the row', async ({ page, guard }) => {
  guard.allow(404, /\/api\/v1\/mail-queue\/AAAA1111BBBB$/)
  await openQueue(page, [NEWSLETTER])
  await page.route('**/api/v1/mail-queue/AAAA1111BBBB', async (route) => {
    await route.fulfill({
      status: 404,
      contentType: 'application/problem+json',
      body: JSON.stringify({ code: 'MailQueueMessageNotFound', title: 'That message is no longer in the queue.', status: 404 }),
    })
  })

  await pickRowAction(page, NEWSLETTER.queueId, 'Remove from the queue')
  await page.getByRole('dialog', { name: `Remove message ${NEWSLETTER.queueId}` }).getByRole('button', { name: 'Remove message' }).click()

  await expect(page.getByText('That message is no longer in the queue.')).toBeVisible()
  await expect(page.getByRole('row').filter({ hasText: NEWSLETTER.queueId })).toBeVisible()
})

test('a reseller is not offered the queue and is turned away from its address without asking for it', async ({ page }) => {
  const requests: string[] = []
  page.on('request', (request) => {
    requests.push(request.url())
  })
  await stubResellerSession(page, ACME_HOSTING, [...ADMIN_MODULES, MAIL])
  await stubMailQueue(page, [NEWSLETTER])
  await page.goto('/')

  // Positive control: the reseller's shell is drawn with its own pages.
  await expect(mainNavigation(page).getByRole('link', { name: 'Overview', exact: true })).toBeVisible()
  await expect(mainNavigation(page).getByRole('link', { name: /^Mail queue/ })).toHaveCount(0)

  await page.goto(SWEPT_SCREEN.path)

  await expect(page).toHaveURL('/not-available')
  expect(requests.filter((url) => {return /\/api\/v1\/mail-queue/.test(url)})).toEqual([])
})

test('a customer is not offered the queue and is turned away from its address without asking for it', async ({ page }) => {
  const requests: string[] = []
  page.on('request', (request) => {
    requests.push(request.url())
  })
  await page.setViewportSize({ width: 1440, height: 900 })
  await stubSignedIn(page, CUSTOMER)
  await stubHealthy(page)
  await stubShellReads(page)
  await stubMyAccount(page, OWN_ACCOUNT)
  await stubLandingReads(page, 'customer')
  await stubModules(page, WITH_MAIL)
  await stubMailQueue(page, [NEWSLETTER])
  await page.goto('/')
  await expect(mainNavigation(page).getByRole('link', { name: 'System status', exact: true })).toBeVisible()
  await expect(mainNavigation(page).getByRole('link', { name: /^Mail queue/ })).toHaveCount(0)

  await page.goto(SWEPT_SCREEN.path)

  await expect(page).toHaveURL('/not-available')
  expect(requests.filter((url) => {return /\/api\/v1\/mail-queue/.test(url)})).toEqual([])
})

for (const locale of ['en', 'ru', 'hy'] as const) {
  test(`the page fits a 390px screen in ${locale}`, async ({ page }) => {
    await setPersistedLocale(page, locale)
    await page.setViewportSize({ width: 390, height: 844 })
    await stubSignedIn(page)
    await stubHealthy(page)
    await stubShellReads(page)
    await stubModules(page, WITH_MAIL)
    await stubMailQueue(page, [
      queuedMessage('AAAA1111BBBB', 'a-sender-with-a-rather-long-local-part@a-rather-long-domain-name-for-a-phone.example.com'),
    ])
    await page.goto(SWEPT_SCREEN.path)

    // Positive control: the row is drawn before the page width is measured.
    await expect(page.getByText('AAAA1111BBBB')).toBeVisible()
    const extent = await page.evaluate(() => {
      const root = window.document.documentElement
      return { scrollWidth: root.scrollWidth, clientWidth: root.clientWidth }
    })
    expect(extent.scrollWidth).toEqual(extent.clientWidth)
  })
}
