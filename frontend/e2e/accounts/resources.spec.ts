import { expect, test } from '../fixtures/guarded-test'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubAccountResources, stubMyAccountResources } from '../fixtures/stub-account-resources-route'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubPlans, THREE_PLANS } from '../fixtures/stub-plans-route'
import { stubInvitation } from '../fixtures/stub-invitation-routes'
import { stubMembershipOffers } from '../fixtures/stub-membership-offers-routes'
import { stubModules } from '../fixtures/stub-modules-route'
import { stubShellReads } from '../fixtures/stub-shell-reads'
import { stubMyAccount } from '../fixtures/stub-my-account-route'
import { setPersistedLocale } from '../fixtures/set-locale'
import type { AccountDetail, MyAccount } from '../../src/types/account'
import type { AccountResourceSummary } from '../../src/types/accountResourceSummary'
import type { AuthenticatedUser } from '../../src/types/auth'
import type { Page } from '@playwright/test'

// Issue #223: an account's page shows what the account holds against its plan, each resource a link
// to its module's list narrowed to the account. Every state is its own case with no number in it: a count
// the server could not take says "Unavailable", and a resource the plan does not bound says "no
// limit" and draws no bar.

const ACCOUNT: AccountDetail = {
  id: '00000000-0000-0000-0000-0000000000c1',
  name: 'acme',
  primaryDomain: 'acme.example.com',
  planId: '00000000-0000-0000-0000-0000000000d1',
  status: 'active',
  statusName: 'Active',
  createdAt: '2026-08-30T09:00:00+00:00',
  resellerId: null,
  resellerName: null,
  planDisplayName: 'Starter',
  ownerEmail: 'owner@acme.example.com',
}

/** Sites and databases counted against a limit, cron unreadable, backups unbounded; no FTPS row. */
const SUMMARY: AccountResourceSummary = {
  accountId: ACCOUNT.id,
  resources: [
    { kind: 'sites', count: { state: 'counted', used: 2 }, limit: { kind: 'limited', maximum: 5 } },
    { kind: 'databases', count: { state: 'counted', used: 5 }, limit: { kind: 'limited', maximum: 5 } },
    { kind: 'sftpLogins', count: { state: 'counted', used: 1 }, limit: { kind: 'limited', maximum: 3 } },
    { kind: 'cronEntries', count: { state: 'unavailable' }, limit: { kind: 'limited', maximum: 10 } },
    { kind: 'backups', count: { state: 'counted', used: 3 }, limit: { kind: 'unlimited' } },
  ],
}

const OWN_ACCOUNT: MyAccount = {
  id: '66666666-6666-6666-6666-666666666666',
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

const CUSTOMER: AuthenticatedUser = {
  id: '00000000-0000-0000-0000-000000000099',
  username: 'olive',
  email: 'olive@example.com',
  role: 'customer',
  accountId: OWN_ACCOUNT.id,
}

/**
 * Stubs the account page, its invitation and its resource summary.
 * @param page The Playwright page.
 * @returns Resolves once the routes are installed.
 */
const stubDetail = async (page: Page): Promise<void> => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, [{ name: 'accounts', displayName: 'Accounts', kindDisplayName: 'Core', tier: 'included', state: 'enabled' }])
  await stubShellReads(page)
  await stubInvitation(page, ACCOUNT.id, 'pending')
  await page.route(`**/api/v1/accounts/${ACCOUNT.id}`, async (route) => {
    if (route.request().method() !== 'GET') {
      await route.fallback()
      return
    }
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(ACCOUNT) })
  })
  await stubAccountResources(page, SUMMARY)
}

test('the card shows each counted resource as used of its limit, with a meter', async ({ page }) => {
  await stubDetail(page)

  await page.goto(`/accounts/${ACCOUNT.id}`)

  const card = page.getByTestId('account-resources')
  await expect(card.getByTestId('resource-sites')).toContainText('2 of 5')
  await expect(card.getByTestId('resource-sites').getByRole('progressbar', { name: 'Sites' })).toBeVisible()
  await expect(card.getByTestId('resource-databases')).toContainText('5 of 5')
})

test('an uncountable resource says unavailable and draws no bar', async ({ page }) => {
  await stubDetail(page)

  await page.goto(`/accounts/${ACCOUNT.id}`)

  const cron = page.getByTestId('resource-cronEntries')
  await expect(cron).toContainText('Unavailable')
  await expect(cron.getByRole('progressbar')).toHaveCount(0)
})

test('a resource the plan does not bound shows its count and no limit, without a bar', async ({ page }) => {
  await stubDetail(page)

  await page.goto(`/accounts/${ACCOUNT.id}`)

  const backups = page.getByTestId('resource-backups')
  await expect(backups).toContainText('3 · no limit')
  await expect(backups.getByRole('progressbar')).toHaveCount(0)
})

test('a module the server does not count has no row', async ({ page }) => {
  await stubDetail(page)

  await page.goto(`/accounts/${ACCOUNT.id}`)

  await expect(page.getByTestId('resource-sites')).toBeVisible()
  await expect(page.getByTestId('resource-ftpLogins')).toHaveCount(0)
})

test('each resource links to its module list narrowed to the account', async ({ page }) => {
  await stubDetail(page)

  await page.goto(`/accounts/${ACCOUNT.id}`)

  const card = page.getByTestId('account-resources')
  const expected: Record<string, string> = {
    Sites: `/sites?account=${ACCOUNT.id}`,
    Databases: `/databases?account=${ACCOUNT.id}`,
    'SFTP logins': `/file-transfer/sftp?account=${ACCOUNT.id}`,
    'Cron entries': `/cron?account=${ACCOUNT.id}`,
    Backups: `/backups?account=${ACCOUNT.id}`,
  }
  for (const [name, href] of Object.entries(expected)) {
    await expect(card.getByRole('link', { name, exact: true })).toHaveAttribute('href', href)
  }
})

// FTPS logins have their own page under File transfer, so the row links there and not to the SFTP one.
test('the FTPS logins resource links to the FTPS page narrowed to the account', async ({ page }) => {
  await stubDetail(page)
  // Registered after the detail's own summary, so this one answers.
  await stubAccountResources(page, {
    accountId: ACCOUNT.id,
    resources: [{ kind: 'ftpLogins', count: { state: 'counted', used: 1 }, limit: { kind: 'limited', maximum: 3 } }],
  })

  await page.goto(`/accounts/${ACCOUNT.id}`)

  await expect(
    page.getByTestId('account-resources').getByRole('link', { name: 'FTPS logins', exact: true }),
  ).toHaveAttribute('href', `/file-transfer/ftps?account=${ACCOUNT.id}`)
})

for (const locale of ['en', 'hy'] as const) {
  test(`the card fits a 390px screen in ${locale}`, async ({ page }) => {
    await setPersistedLocale(page, locale)
    await page.setViewportSize({ width: 390, height: 844 })
    await stubDetail(page)

    await page.goto(`/accounts/${ACCOUNT.id}`)
    await expect(page.getByTestId('resource-sites')).toBeVisible()

    const widths = await page.evaluate(() => {
      return { document: document.documentElement.scrollWidth, viewport: window.innerWidth }
    })
    expect(widths.document).toBeLessThanOrEqual(widths.viewport)
  })
}

test('a customer sees their own summary on their account page', async ({ page }) => {
  await stubSignedIn(page, CUSTOMER)
  await stubHealthy(page)
  await stubModules(page, [])
  await stubShellReads(page)
  // The page reads the membership offers addressed to the caller.
  await stubMembershipOffers(page, [])
  await stubMyAccount(page, OWN_ACCOUNT)
  await stubMyAccountResources(page, { ...SUMMARY, accountId: OWN_ACCOUNT.id })

  await page.goto('/my-account')

  await expect(page.getByTestId('resource-sites')).toContainText('2 of 5')
  await expect(page.getByTestId('resource-cronEntries')).toContainText('Unavailable')
})

/**
 * Stubs the account page with a summary that follows the plan: before the move the account is on
 * limits of 5 sites, after an accepted move on limits of 25. A refused move changes nothing.
 * @param page The Playwright page.
 * @param refuse Whether the panel refuses the plan change.
 * @returns Resolves once the routes are installed.
 */
const stubPlanChange = async (page: Page, refuse: boolean): Promise<void> => {
  let moved = false
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, [{ name: 'accounts', displayName: 'Accounts', kindDisplayName: 'Core', tier: 'included', state: 'enabled' }])
  await stubShellReads(page)
  await stubInvitation(page, ACCOUNT.id, 'pending')
  await stubPlans(page, THREE_PLANS)
  await page.route(`**/api/v1/accounts/${ACCOUNT.id}`, async (route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(ACCOUNT) })
  })
  await page.route(`**/api/v1/accounts/${ACCOUNT.id}/plan`, async (route) => {
    if (refuse) {
      await route.fulfill({
        status: 409,
        contentType: 'application/problem+json',
        body: JSON.stringify({ code: 'AccountPlanQuotaBelowUsage', title: 'Refused.', detail: 'Refused.' }),
      })
      return
    }
    moved = true
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(ACCOUNT) })
  })
  await page.route(`**/api/v1/accounts/${ACCOUNT.id}/resource-summary`, async (route) => {
    const maximum = moved ? 25 : 5
    const summary: AccountResourceSummary = {
      accountId: ACCOUNT.id,
      resources: [{ kind: 'sites', count: { state: 'counted', used: 0 }, limit: { kind: 'limited', maximum } }],
    }
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(summary) })
  })
}

/**
 * Opens the page, then chooses the second of the three plans in the Change plan dialog and sends it.
 * @param page The Playwright page.
 * @returns Resolves once the dialog's button has been pressed.
 */
const changePlanToOther = async (page: Page): Promise<void> => {
  await page.goto(`/accounts/${ACCOUNT.id}`)
  await expect(page.getByTestId('resource-sites')).toContainText('0 of 5')
  await page.getByRole('button', { name: 'Change plan' }).click()
  const dialog = page.getByRole('dialog', { name: 'Change the plan of acme' })
  await dialog.getByRole('combobox', { name: 'Plan', exact: true }).click()
  await page.getByRole('option', { name: THREE_PLANS[1].displayName, exact: false }).click()
  await dialog.getByRole('button', { name: 'Change plan' }).click()
}

test('after Change plan the Resources card shows the new plan limits without reopening the page (#494)', async ({ page }) => {
  await stubPlanChange(page, false)

  await changePlanToOther(page)

  await expect(page.getByTestId('resource-sites')).toContainText('0 of 25')
})

test('a refused Change plan leaves the Resources card on the old limits (#494 control)', async ({ page, guard }) => {
  // The refusal is the subject: the plan change answers 409.
  guard.allow(409, new RegExp(`/api/v1/accounts/${ACCOUNT.id}/plan$`))
  await stubPlanChange(page, true)

  await changePlanToOther(page)

  await expect(page.getByRole('dialog')).toBeVisible()
  await expect(page.getByTestId('resource-sites')).toContainText('0 of 5')
})
