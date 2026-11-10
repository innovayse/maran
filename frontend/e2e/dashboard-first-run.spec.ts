import { expect, test } from './fixtures/guarded-test'
import { FULL_CATALOGUE } from './fixtures/open-full-shell'
import { stubAccounts } from './fixtures/stub-accounts-route'
import { setPersistedLocale } from './fixtures/set-locale'
import { stubSignedIn } from './fixtures/stub-auth-routes'
import { ALERT_DELIVERY_OK, loaded, stubDashboard, SYNCHRONIZED_CLOCK, UNAVAILABLE, WITHHELD_DASHBOARD } from './fixtures/stub-dashboard-route'
import { stubHealthy } from './fixtures/stub-health-route'
import { stubIdleLiveDashboard } from './fixtures/stub-live-dashboard-stream'
import { COMPLETE_CHECKLIST, stubFirstRun } from './fixtures/stub-notification-routes'
import { stubModules } from './fixtures/stub-modules-route'
import { stubQuietOwnAccount } from './fixtures/stub-quiet-own-account'
import { stubShellReads } from './fixtures/stub-shell-reads'
import type { AuthenticatedUser } from '../src/types/auth'
import type { Dashboard } from '../src/types/dashboard'
import type { FirstRunChecklist, FirstRunStep } from '../src/types/firstRun'
import type { Page } from '@playwright/test'

// Issue #164: the administrator's dashboard carries a first-run checklist while a new server still
// lacks something. The panel decides which steps exist, whether each is done and whether the whole
// list is complete; the SPA words the steps and maps each destination to a route it has. A step the
// panel could not check is shown as that, never as "to do", and a finished list draws no card.

/** An administrator's dashboard with nothing needing attention, so the checklist is the subject. */
const DASHBOARD: Dashboard = {
  audience: 'administrator',
  resources: UNAVAILABLE,
  services: UNAVAILABLE,
  counts: { accounts: loaded(1), sites: loaded(1), databases: loaded(1) },
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

/** A customer, who is never shown the server and never asks for its checklist. */
const CUSTOMER: AuthenticatedUser = {
  id: '00000000-0000-0000-0000-000000000099',
  username: 'olive',
  email: 'olive@example.com',
  role: 'customer',
  accountId: '11111111-1111-1111-1111-111111111111',
}

/**
 * Builds one step of the stubbed checklist.
 * @param key Which step it is.
 * @param done Whether it is done: true, false, or `null` for a step the panel could not check.
 * @param routeName The closed destination name the panel sends.
 * @returns The step.
 */
const step = (key: string, done: boolean | null, routeName: string): FirstRunStep => {
  return { key, done: done === null ? { state: 'unavailable' } : { state: 'loaded', data: done }, routeName }
}

/** A server with a plan and nothing else yet, and a certificate source the panel could not read. */
const PARTLY_DONE: FirstRunChecklist = {
  complete: false,
  steps: [
    step('plan', true, 'plans'),
    step('account', false, 'accounts'),
    step('site', false, 'sites'),
    step('backupDestination', false, 'backupDestinations'),
    step('certificate', null, 'certificates'),
  ],
}

/**
 * Signs an administrator in with every module licensed, the dashboard stubbed and the checklist
 * answering what a spec names, then opens the landing screen.
 * @param page The Playwright page under test.
 * @param checklist What the stubbed panel answers.
 * @returns Resolves once the landing screen's heading is drawn.
 */
const openDashboard = async (page: Page, checklist: FirstRunChecklist): Promise<void> => {
  await page.setViewportSize({ width: 1440, height: 900 })
  await stubSignedIn(page)
  await stubShellReads(page)
  await stubHealthy(page)
  await stubModules(page, FULL_CATALOGUE)
  await stubDashboard(page, DASHBOARD)
  await stubIdleLiveDashboard(page)
  await stubFirstRun(page, checklist)
  await page.goto('/')
  await expect(page.getByRole('heading', { level: 1 })).toBeVisible()
}

test('the card lists each step with whether it is done and how many are', async ({ page }) => {
  await openDashboard(page, PARTLY_DONE)

  const card = page.getByTestId('first-run-checklist')
  await expect(card.getByText('Get this server ready')).toBeVisible()
  await expect(card.getByText('1 of 5 steps done')).toBeVisible()
  await expect(card.locator('[data-step="plan"]')).toContainText('Done')
  await expect(card.locator('[data-step="account"]')).toContainText('To do')
  await expect(card.locator('[data-step="site"]')).toContainText('To do')
})

test('a step the panel could not check is shown as that, not as to do', async ({ page }) => {
  await openDashboard(page, PARTLY_DONE)

  const certificate = page.getByTestId('first-run-checklist').locator('[data-step="certificate"]')
  await expect(certificate).toContainText('Could not be checked')
  await expect(certificate).not.toContainText('To do')
  // Its page is not built yet, so the row offers no link rather than an invented one.
  await expect(certificate.getByRole('link')).toHaveCount(0)
})

test('a step that is not done links to the screen where it is done', async ({ page }) => {
  await openDashboard(page, PARTLY_DONE)
  await stubAccounts(page, [])

  const card = page.getByTestId('first-run-checklist')
  // A finished step has nothing left to open; an open one does.
  await expect(card.locator('[data-step="plan"]').getByRole('link')).toHaveCount(0)
  await card.getByRole('link', { name: 'Open accounts' }).click()

  await expect(page).toHaveURL('/accounts')
})

test('a finished checklist draws no card', async ({ page }) => {
  await openDashboard(page, COMPLETE_CHECKLIST)

  await expect(page.getByTestId('first-run-checklist')).toHaveCount(0)
})

test('the card appears for an unfinished list and not for a finished one, so the absence means something', async ({ page }) => {
  // The inverse control of the test above: the same screen with an unfinished list draws the card.
  await openDashboard(page, PARTLY_DONE)

  await expect(page.getByTestId('first-run-checklist')).toBeVisible()
})

test('a checklist that cannot be read draws no card and leaves the rest of the screen up', async ({ page, guard }) => {
  await page.setViewportSize({ width: 1440, height: 900 })
  await stubSignedIn(page)
  await stubShellReads(page)
  await stubHealthy(page)
  await stubModules(page, FULL_CATALOGUE)
  await stubDashboard(page, DASHBOARD)
  await stubIdleLiveDashboard(page)
  // The unreadable checklist is the subject of this test.
  guard.allow(500, /\/api\/v1\/dashboard\/first-run$/)
  await page.route(/\/api\/v1\/dashboard\/first-run(\?.*)?$/, async (route) => {
    await route.fulfill({ status: 500, contentType: 'application/problem+json', body: JSON.stringify({ title: 'Failed', status: 500 }) })
  })

  await page.goto('/')

  await expect(page.getByRole('heading', { level: 1 })).toBeVisible()
  await expect(page.getByText('Latest activity')).toBeVisible()
  await expect(page.getByTestId('first-run-checklist')).toHaveCount(0)
})

test('a customer\'s dashboard never asks for the checklist', async ({ page }) => {
  const urls: string[] = []
  page.on('request', (request) => {
    urls.push(request.url())
  })
  await page.setViewportSize({ width: 1440, height: 900 })
  await stubSignedIn(page, CUSTOMER)
  await stubShellReads(page)
  await stubQuietOwnAccount(page, CUSTOMER.accountId ?? '')
  await stubHealthy(page)
  await stubModules(page, FULL_CATALOGUE)
  await stubDashboard(page, WITHHELD_DASHBOARD)

  await page.goto('/')
  await expect(page.getByRole('heading', { level: 1 })).toBeVisible()

  expect(urls.filter((url) => {return url.includes('/dashboard/first-run')})).toEqual([])
  await expect(page.getByTestId('first-run-checklist')).toHaveCount(0)
})

test('on a phone the card fits the screen', async ({ page }) => {
  await openDashboard(page, PARTLY_DONE)
  await page.setViewportSize({ width: 390, height: 844 })

  await expect(page.getByTestId('first-run-checklist')).toBeVisible()
  const extent = await page.evaluate(() => {return { scroll: document.documentElement.scrollWidth, inner: window.innerWidth }})
  expect(extent.scroll).toBeLessThanOrEqual(extent.inner)
})

const LOCALES = [
  { code: 'ru', title: 'Подготовьте сервер к работе', progress: 'Выполнено шагов: 1 из 5', todo: 'Нужно сделать', unchecked: 'Не удалось проверить' },
  { code: 'hy', title: 'Պատրաստեք սերվերը աշխատանքին', progress: 'Կատարված քայլեր՝ 1/5', todo: 'Պետք է անել', unchecked: 'Չհաջողվեց ստուգել' },
] as const

for (const locale of LOCALES) {
  test(`the card speaks ${locale.code}`, async ({ page }) => {
    await setPersistedLocale(page, locale.code)
    await openDashboard(page, PARTLY_DONE)

    const card = page.getByTestId('first-run-checklist')
    await expect(card.getByText(locale.title)).toBeVisible()
    await expect(card.getByText(locale.progress)).toBeVisible()
    await expect(card.locator('[data-step="account"]')).toContainText(locale.todo)
    await expect(card.locator('[data-step="certificate"]')).toContainText(locale.unchecked)
  })
}
