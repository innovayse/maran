import { expect, test } from '../fixtures/guarded-test'
import { FULL_CATALOGUE, mainNavigation, openSection } from '../fixtures/open-full-shell'
import { setPersistedLocale } from '../fixtures/set-locale'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubLandingReads } from '../fixtures/stub-landing-reads'
import {
  ABSENT_LICENCE,
  CLAIM_URL,
  EXPIRING_LICENCE,
  INSTALLED_FRESH_BUT_UNREACHABLE,
  INSTALLED_WITHOUT_LEASE,
  INSTALLED_WITH_LEASE,
  NO_SERVER_ID,
  REFUSED_LICENCE,
  SERVER_ID,
  VALID_LICENCE,
  stubLicenceRoutes,
} from '../fixtures/stub-licence-routes'
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

test('a licence the backend flags as close to its expiry shows the warning, the renewal steps and the claim link', async ({ page }) => {
  await stubLicenceRoutes(page, EXPIRING_LICENCE)
  await openLicence(page)

  const warning = page.getByTestId('licence-expiry-warning')
  await expect(warning).toContainText('The installed licence expires on 2026-12-26 (days left: 14).')
  await expect(warning).toContainText('claim the licence again for this server on the marketplace, or install a new licence file')
  await expect(page.getByTestId('licence-expiry-claim-link')).toHaveAttribute('href', CLAIM_URL)
})

test.describe('the expiry day is the UTC day the warning names', () => {
  test.describe('in a zone ahead of UTC', () => {
    test.use({ timezoneId: 'Asia/Yerevan' })

    test('a licence expiring late on the 26th UTC reads as the 26th beside a warning that says the 26th', async ({ page }) => {
      await stubLicenceRoutes(page, { ...EXPIRING_LICENCE, expiry: '2026-12-26T22:00:00Z' })
      await openLicence(page)

      const card = page.getByTestId('licence-status')
      await expect(card.getByText('26 Dec 2026', { exact: true })).toBeVisible()
      await expect(card.getByText('27 Dec 2026', { exact: true })).toHaveCount(0)
      await expect(page.getByTestId('licence-expiry-warning')).toContainText('2026-12-26')
    })
  })

  test.describe('in a zone behind UTC', () => {
    test.use({ timezoneId: 'America/Los_Angeles' })

    test('a licence expiring at midnight UTC on the 26th reads as the 26th, not the 25th (control)', async ({ page }) => {
      await stubLicenceRoutes(page, EXPIRING_LICENCE)
      await openLicence(page)

      const card = page.getByTestId('licence-status')
      await expect(card.getByText('26 Dec 2026', { exact: true })).toBeVisible()
      await expect(card.getByText('25 Dec 2026', { exact: true })).toHaveCount(0)
    })
  })
})

test('a licence the backend does not flag shows no expiry warning (control for the one above)', async ({ page }) => {
  await stubLicenceRoutes(page, VALID_LICENCE)
  await openLicence(page)

  await expect(page.getByTestId('licence-status')).toBeVisible()
  await expect(page.getByTestId('licence-expiry-warning')).toHaveCount(0)
})

test('installing a licence further from its expiry clears the warning', async ({ page }) => {
  await stubLicenceRoutes(page, EXPIRING_LICENCE, { status: VALID_LICENCE })
  await openLicence(page)
  await expect(page.getByTestId('licence-expiry-warning')).toBeVisible()

  await page.getByLabel('Licence text').fill(PASTED)
  await page.getByRole('button', { name: 'Install licence' }).click()

  await expect(page.getByTestId('licence-installed')).toBeVisible()
  await expect(page.getByTestId('licence-expiry-warning')).toHaveCount(0)
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

test('the page names this server and links to the claim page that carries its ID', async ({ page }) => {
  await stubLicenceRoutes(page, ABSENT_LICENCE)
  await openLicence(page)

  // The ID is shown with no licence installed: that is when it is needed, to claim one.
  const card = page.getByTestId('licence-server')
  await expect(card.getByTestId('licence-server-id')).toContainText(SERVER_ID)
  const link = card.getByTestId('licence-claim-link')
  await expect(link).toHaveAttribute('href', CLAIM_URL)
  await expect(link).toHaveAttribute('target', '_blank')
  await expect(link).toHaveAttribute('rel', /noopener/)
  await expect(card.getByRole('button', { name: 'Copy server ID' })).toBeVisible()
  await expect(card.getByTestId('licence-server-unavailable')).toHaveCount(0)
  await expect(page.getByTestId('licence-lease-request')).toHaveCount(0)
})

test('copying the server ID puts exactly that ID on the clipboard', async ({ page, context }) => {
  await context.grantPermissions(['clipboard-read', 'clipboard-write'])
  await stubLicenceRoutes(page, VALID_LICENCE)
  await openLicence(page)

  await page.getByRole('button', { name: 'Copy server ID' }).click()

  await expect(page.getByRole('button', { name: 'Copied' })).toBeVisible()
  expect(await page.evaluate(() => { return navigator.clipboard.readText() })).toBe(SERVER_ID)

  // The confirmation is shown for a moment only: the control offers the copy again afterwards, so a later
  // copy that fails is never announced as the earlier one that worked.
  await expect(page.getByRole('button', { name: 'Copy server ID' })).toBeVisible({ timeout: 8000 })
  await expect(page.getByRole('button', { name: 'Copied' })).toHaveCount(0)
})

test('a panel that could not read its server ID says so and offers neither a copy control nor a link', async ({ page }) => {
  await stubLicenceRoutes(page, NO_SERVER_ID)
  await openLicence(page)

  const card = page.getByTestId('licence-server')
  await expect(card.getByTestId('licence-server-unavailable')).toContainText('could not read this server')
  await expect(card.getByTestId('licence-server-id')).toHaveCount(0)
  await expect(card.getByRole('button', { name: 'Copy server ID' })).toHaveCount(0)
  await expect(card.getByTestId('licence-claim-link')).toHaveCount(0)
})

test('an ID the marketplace would refuse is shown for copying, without a claim link', async ({ page }) => {
  await stubLicenceRoutes(page, { ...ABSENT_LICENCE, serverId: 'not-a-machine-id', claimUrl: null })
  await openLicence(page)

  const card = page.getByTestId('licence-server')
  await expect(card.getByTestId('licence-server-id')).toContainText('not-a-machine-id')
  await expect(card.getByRole('button', { name: 'Copy server ID' })).toBeVisible()
  await expect(card.getByTestId('licence-claim-link')).toHaveCount(0)
  await expect(card.getByTestId('licence-claim-unavailable')).toBeVisible()
})

test('installing shows that the lease was requested and stored, and the lease row follows', async ({ page }) => {
  await stubLicenceRoutes(page, ABSENT_LICENCE, { status: INSTALLED_WITH_LEASE })
  await openLicence(page)

  await page.getByLabel('Licence text').fill(PASTED)
  await page.getByRole('button', { name: 'Install licence' }).click()

  await expect(page.getByTestId('licence-installed')).toBeVisible()
  await expect(page.getByTestId('licence-lease-request')).toContainText('A lease was requested from the licence service and stored.')
  await expect(page.getByTestId('licence-lease')).toContainText('Fresh, valid until')
})

test('an install whose lease could not be requested still installs and says what went wrong', async ({ page }) => {
  await stubLicenceRoutes(page, ABSENT_LICENCE, { status: INSTALLED_WITHOUT_LEASE })
  await openLicence(page)

  await page.getByLabel('Licence text').fill(PASTED)
  await page.getByRole('button', { name: 'Install licence' }).click()

  await expect(page.getByTestId('licence-installed')).toBeVisible()
  await expect(page.getByTestId('licence-lease-request')).toContainText('could not be reached')
  await expect(page.getByTestId('licence-lease')).toContainText('No lease')
  await expect(page.getByTestId('licence-status').getByText('LIC-2026-0042')).toBeVisible()
})

test('the lease request note is in the success tone only when the request ended well, whatever the lease stands as', async ({ page }) => {
  await stubLicenceRoutes(page, ABSENT_LICENCE, { status: INSTALLED_FRESH_BUT_UNREACHABLE })
  await openLicence(page)

  await page.getByLabel('Licence text').fill(PASTED)
  await page.getByRole('button', { name: 'Install licence' }).click()

  const note = page.getByTestId('licence-lease-request')
  await expect(note).toContainText('could not be reached')
  await expect(note).toHaveClass(/border-warning/)
  await expect(note).not.toHaveClass(/border-success/)
})

test('a request that stored a lease is shown in the success tone (control for the warning tone)', async ({ page }) => {
  await stubLicenceRoutes(page, ABSENT_LICENCE, { status: INSTALLED_WITH_LEASE })
  await openLicence(page)

  await page.getByLabel('Licence text').fill(PASTED)
  await page.getByRole('button', { name: 'Install licence' }).click()

  await expect(page.getByTestId('licence-lease-request')).toHaveClass(/border-success/)
})

test('editing the pasted text drops the note about the last install’s lease request', async ({ page }) => {
  await stubLicenceRoutes(page, ABSENT_LICENCE, { status: INSTALLED_WITH_LEASE })
  await openLicence(page)
  await page.getByLabel('Licence text').fill(PASTED)
  await page.getByRole('button', { name: 'Install licence' }).click()
  await expect(page.getByTestId('licence-lease-request')).toBeVisible()

  await page.getByLabel('Licence text').fill(PASTED)

  await expect(page.getByTestId('licence-lease-request')).toHaveCount(0)
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
  await expect(page.getByTestId('licence-server').getByRole('button', { name: 'Скопировать ID сервера' })).toBeVisible()
  await expect(page.getByTestId('licence-claim-link')).toContainText('Получить лицензию для этого сервера')
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
  await expect(page.getByTestId('licence-server').getByRole('button', { name: 'Պատճենել սերվերի ID-ն' })).toBeVisible()
  await expect(page.getByTestId('licence-claim-link')).toContainText('Ստանալ լիցենզիա այս սերվերի համար')
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
