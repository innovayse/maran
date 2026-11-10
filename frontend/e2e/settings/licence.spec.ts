import { expect, test } from '../fixtures/guarded-test'
import { FULL_CATALOGUE, mainNavigation, openSection } from '../fixtures/open-full-shell'
import { setPersistedLocale } from '../fixtures/set-locale'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubLandingReads } from '../fixtures/stub-landing-reads'
import { ABSENT_LICENCE, REFUSED_LICENCE, VALID_LICENCE, stubLicenceRoutes } from '../fixtures/stub-licence-routes'
import { stubModules } from '../fixtures/stub-modules-route'
import { stubMyAccount } from '../fixtures/stub-my-account-route'
import { stubShellReads } from '../fixtures/stub-shell-reads'
import type { MyAccount } from '../../src/types/account'
import type { AuthenticatedUser } from '../../src/types/auth'
import type { Page } from '@playwright/test'

// The administrator-only "Licence" screen (issue #8): the status the backend reports, rendered as
// the backend worded it, and the form that installs a pasted licence. Every spec but the locale,
// narrow-screen and customer-entry ones reaches the page through the sidebar's Settings group.

/** The address the 390px check below opens; `e2e/ui-kit/route-coverage.spec.ts` reads it from here. */
const SWEPT_SCREEN = { path: '/settings/licence' }

/** A pasted licence: long JSON, as the real thing is. */
const PASTED = '{"licenceId":"LIC-2026-0042","tier":"business","signature":"abc123"}'

/** A customer, for the "never sees it" half of the check. */
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
  primaryDomain: 'olive-shop.example.com',
  status: 'active',
  statusName: 'Active',
  plan: { displayName: 'Starter', diskQuotaMb: 1024, maxSites: 1, maxDatabases: 1, maxSftpUsers: 1, maxFtpUsers: 1, maxCronEntries: 1 },
}

/**
 * Opens the licence page by the menu, as a person would.
 * @param page The Playwright page under test.
 * @returns Resolves once the page's heading is on screen.
 */
const openLicence = async (page: Page): Promise<void> => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubShellReads(page)
  await stubLandingReads(page)
  await stubModules(page, FULL_CATALOGUE)
  await page.goto('/')
  await openSection(page, 'Settings')
  await mainNavigation(page).getByRole('link', { name: 'Licence', exact: true }).click()
  await expect(page.getByRole('heading', { level: 1, name: 'Licence' })).toBeVisible()
}

test('a valid licence shows its facts and the backend’s own sentence', async ({ page }) => {
  await stubLicenceRoutes(page, VALID_LICENCE)
  await openLicence(page)

  const card = page.getByTestId('licence-status')
  await expect(card.getByText('Valid', { exact: true })).toBeVisible()
  await expect(card.getByText('The licence is valid and covers 3 paid modules.')).toBeVisible()
  await expect(card.getByText('LIC-2026-0042')).toBeVisible()
  await expect(card.getByText('business')).toBeVisible()
  await expect(card.getByText('reseller')).toBeVisible()
  await expect(page.getByTestId('licence-lease')).toContainText('Fresh, valid until')
  await expect(page.getByTestId('licence-lease-notice')).toHaveCount(0)
  await expect(page.getByTestId('licence-offline')).toHaveCount(0)
})

test('a panel with no licence says so and shows no licence facts', async ({ page }) => {
  await stubLicenceRoutes(page, ABSENT_LICENCE)
  await openLicence(page)

  const card = page.getByTestId('licence-status')
  await expect(card.getByText('No licence is installed.')).toBeVisible()
  await expect(card.getByText('Paste a licence below to unlock the paid modules.')).toBeVisible()
  await expect(card.getByText('Licence ID')).toHaveCount(0)
  await expect(card.getByText('Modules', { exact: true })).toHaveCount(0)
})

test('a refused licence shows the reason and the advice the backend worded', async ({ page }) => {
  await stubLicenceRoutes(page, REFUSED_LICENCE)
  await openLicence(page)

  const card = page.getByTestId('licence-status')
  await expect(card.getByText('Refused', { exact: true })).toBeVisible()
  await expect(card.getByText('The licence has expired')).toBeVisible()
  await expect(card.getByText('Install a current licence.')).toBeVisible()
})

test('an expired lease shows the notice, and a fresh one does not', async ({ page }) => {
  await stubLicenceRoutes(page, { ...VALID_LICENCE, lease: 'expired', leaseDisplayName: 'Expired', isOffline: true })
  await openLicence(page)

  await expect(page.getByTestId('licence-lease-notice')).toContainText('The licence lease has expired.')
  await expect(page.getByTestId('licence-offline')).toContainText('offline licence')
})

test('installing a licence sends the pasted text, shows the new status and empties the field', async ({ page }) => {
  const calls = await stubLicenceRoutes(page, ABSENT_LICENCE, { status: VALID_LICENCE })
  await openLicence(page)

  await page.getByLabel('Licence text').fill(PASTED)
  await page.getByRole('button', { name: 'Install licence' }).click()

  await expect(page.getByTestId('licence-installed')).toContainText('The licence was installed.')
  await expect(page.getByTestId('licence-status').getByText('LIC-2026-0042')).toBeVisible()
  await expect(page.getByLabel('Licence text')).toHaveValue('')
  expect(calls.installs).toEqual([{ rawLicenceText: PASTED }])
})

test('an empty field is refused before any request, and a filled one is sent', async ({ page }) => {
  const calls = await stubLicenceRoutes(page, ABSENT_LICENCE, { status: VALID_LICENCE })
  await openLicence(page)

  await page.getByRole('button', { name: 'Install licence' }).click()
  await expect(page.getByText('Paste the licence text first.')).toBeVisible()
  expect(calls.installs).toHaveLength(0)

  // The inverse control: the same button, with text in the field, sends it.
  await page.getByLabel('Licence text').fill(PASTED)
  await page.getByRole('button', { name: 'Install licence' }).click()
  await expect.poll(() => {return calls.installs.length}).toBe(1)
})

test('a refused install shows the backend’s sentence and keeps the pasted text', async ({ page, guard }) => {
  guard.allow(400, /\/api\/v1\/licence-status\/install$/)
  await stubLicenceRoutes(page, ABSENT_LICENCE, { refusal: 'The licence signature does not match this product.' })
  await openLicence(page)

  await page.getByLabel('Licence text').fill(PASTED)
  await page.getByRole('button', { name: 'Install licence' }).click()

  await expect(page.getByText('The licence signature does not match this product.')).toBeVisible()
  await expect(page.getByTestId('licence-installed')).toHaveCount(0)
  await expect(page.getByLabel('Licence text')).toHaveValue(PASTED)
  await expect(page.getByTestId('licence-status').getByText('No licence is installed.')).toBeVisible()
})

test('the whole install can be done from the keyboard', async ({ page }) => {
  const calls = await stubLicenceRoutes(page, ABSENT_LICENCE, { status: VALID_LICENCE })
  await openLicence(page)

  const field = page.getByLabel('Licence text')
  await field.focus()
  await field.fill(PASTED)
  await page.keyboard.press('Tab')
  await expect(page.getByRole('button', { name: 'Install licence' })).toBeFocused()
  await page.keyboard.press('Enter')

  await expect(page.getByTestId('licence-installed')).toBeVisible()
  expect(calls.installs).toEqual([{ rawLicenceText: PASTED }])
})

test('a customer is offered no link to the licence page', async ({ page }) => {
  await stubLicenceRoutes(page, VALID_LICENCE)
  await stubSignedIn(page, CUSTOMER)
  await stubHealthy(page)
  await stubShellReads(page)
  await stubMyAccount(page, OWN_ACCOUNT)
  await stubLandingReads(page, 'customer')
  await stubModules(page, FULL_CATALOGUE)
  await page.goto('/')

  await expect(mainNavigation(page)).toBeVisible()
  await expect(mainNavigation(page).getByRole('link', { name: 'Licence', exact: true })).toHaveCount(0)
})

test('a customer who opens the address is turned away and the licence is never read', async ({ page }) => {
  const calls = await stubLicenceRoutes(page, VALID_LICENCE)
  await stubSignedIn(page, CUSTOMER)
  await stubHealthy(page)
  await stubShellReads(page)
  await stubMyAccount(page, OWN_ACCOUNT)
  await stubModules(page, FULL_CATALOGUE)
  await page.goto(SWEPT_SCREEN.path)

  await expect(mainNavigation(page)).toBeVisible()
  await expect(page.getByTestId('licence-status')).toHaveCount(0)
  await expect(page.getByRole('button', { name: 'Install licence' })).toHaveCount(0)
  expect(calls.reads).toBe(0)
})

test('an administrator who opens the address sees the page (control for the customer check)', async ({ page }) => {
  const calls = await stubLicenceRoutes(page, VALID_LICENCE)
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubShellReads(page)
  await stubModules(page, FULL_CATALOGUE)
  await page.goto(SWEPT_SCREEN.path)

  await expect(page.getByTestId('licence-status')).toBeVisible()
  await expect(page.getByRole('button', { name: 'Install licence' })).toBeVisible()
  expect(calls.reads).toBeGreaterThan(0)
})

test('the page is translated into Russian and Armenian, with the backend’s text left as sent', async ({ page }) => {
  await stubLicenceRoutes(page, VALID_LICENCE)
  await setPersistedLocale(page, 'ru')
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubShellReads(page)
  await stubModules(page, FULL_CATALOGUE)
  await page.goto(SWEPT_SCREEN.path)

  await expect(page.getByRole('heading', { level: 1, name: 'Лицензия' })).toBeVisible()
  await expect(page.getByRole('button', { name: 'Установить лицензию' })).toBeVisible()
  await expect(page.getByTestId('licence-status').getByText('The licence is valid and covers 3 paid modules.')).toBeVisible()
})

test('the page is translated into Armenian', async ({ page }) => {
  await stubLicenceRoutes(page, VALID_LICENCE)
  await setPersistedLocale(page, 'hy')
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubShellReads(page)
  await stubModules(page, FULL_CATALOGUE)
  await page.goto(SWEPT_SCREEN.path)

  await expect(page.getByRole('heading', { level: 1, name: 'Լիցենզիա' })).toBeVisible()
  await expect(page.getByRole('button', { name: 'Տեղադրել լիցենզիան' })).toBeVisible()
})

test('the page fits a 390px screen in Armenian', async ({ page }) => {
  await stubLicenceRoutes(page, { ...VALID_LICENCE, lease: 'expired', leaseDisplayName: 'Expired', isOffline: true })
  await setPersistedLocale(page, 'hy')
  await page.setViewportSize({ width: 390, height: 844 })
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubShellReads(page)
  await stubModules(page, FULL_CATALOGUE)
  await page.goto(SWEPT_SCREEN.path)

  await expect(page.getByRole('heading', { level: 1 })).toBeVisible()
  await expect(page.getByTestId('licence-status')).toBeVisible()
  const overflow = await page.evaluate(() => {
    return document.documentElement.scrollWidth - document.documentElement.clientWidth
  })
  expect(overflow).toBeLessThanOrEqual(0)
})
