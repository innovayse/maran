import { expect, test, type Page } from '@playwright/test'
import { mainNavigation, openSection } from '../fixtures/open-full-shell'
import { setPersistedLocale } from '../fixtures/set-locale'
import { stubAccounts } from '../fixtures/stub-accounts-route'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import { stubManagedPlans, stubPlans } from '../fixtures/stub-plans-route'
import { ADMIN_MODULES, stubMyReseller, stubResellerSession } from '../fixtures/stub-reseller-session'
import { ACME_HOSTING, stubResellers } from '../fixtures/stub-resellers-routes'
import type { AuthenticatedUser } from '../../src/types/auth'

// Issue #179, the critical check: the role flags are ROLE-EXACT. A reseller signed in never sees an
// administrator's screen, route or menu item, and a customer never sees either the administrator's
// or the reseller's. Every catalogue below lists the modules an administrator has, so a refusal can
// only be the role guard's and never the module guard's. The guards are advice (the endpoints
// refuse by role regardless); what is asserted here is the advice, and that no request for an
// administrator's data is even attempted. Each test enters through one `goto`; the rest is clicks.

/** A customer, for the "likewise" half of the check. */
const CUSTOMER: AuthenticatedUser = {
  id: '00000000-0000-0000-0000-000000000099',
  username: 'olive',
  email: 'olive@example.com',
  role: 'customer',
  accountId: '11111111-1111-1111-1111-111111111111',
}

/** An address of a reseller's own detail and edit pages, which only an administrator may open. */
const RESELLER_PAGES = [`/resellers/${ACME_HOSTING.id}`, `/resellers/${ACME_HOSTING.id}/edit`, `/resellers/${ACME_HOSTING.id}/api-keys`]

/** Every screen only an administrator may open: the role guard sends anyone else away. */
const ADMIN_ONLY_PATHS = [
  '/resellers',
  '/resellers/new',
  ...RESELLER_PAGES,
  '/databases/grant-repair',
  '/backups/schedule',
  '/backups/destinations',
  '/firewall',
  '/firewall/bans',
  '/firewall/whitelist',
  '/monitoring',
  '/servers/updates',
  '/settings/security-policy',
  '/settings/smtp',
  '/settings/alerts',
  '/settings/php',
  '/settings/audit',
  '/settings/panel-certificate',
  '/settings/panel-update',
]

/**
 * The menu entries only an administrator is offered, as the start of an accessible name. A
 * disabled "coming soon" row's name continues after the label, so each one is a prefix match; a
 * prefix and not a substring, because "Profile and security" is a reseller's own page.
 */
const ADMIN_ENTRIES: RegExp[] = [
  /^System status/,
  /^Resellers/,
  /^Sites and domains/,
  /^Databases/,
  /^Files and access/,
  /^Backups/,
  /^Security/,
  /^Firewall/,
  /^Servers/,
  /^Monitoring/,
  /^PHP versions/,
  /^Grant repair/,
  /^Panel/,
  /^Outgoing mail/,
  /^Alert settings/,
  /^Administrators and roles/,
  /^Modules and licence/,
]

/**
 * Counts the requests the page makes for the resellers collection, which only an administrator may
 * read. `reseller/me` is a different path and is a reseller's own read.
 * @param page The Playwright page under test.
 * @returns A function reporting how many such requests were made so far.
 */
const countResellerListReads = (page: Page): (() => number) => {
  let count = 0
  page.on('request', (request) => {
    if (/\/api\/v1\/resellers(\/|\?|$)/.test(request.url())) {
      count += 1
    }
  })
  return (): number => {
    return count
  }
}

/**
 * Signs a customer in with the administrator's catalogue.
 * @param page The Playwright page under test.
 * @returns Resolves once the routes are installed.
 */
const signInAsCustomer = async (page: Page): Promise<void> => {
  await page.setViewportSize({ width: 1440, height: 900 })
  await stubSignedIn(page, CUSTOMER)
  await stubHealthy(page)
  await stubModules(page, ADMIN_MODULES)
}

test('a reseller sidebar offers its own pages and none of an administrator', async ({ page }) => {
  await stubResellerSession(page, ACME_HOSTING, ADMIN_MODULES)
  await page.goto('/')

  const navigation = mainNavigation(page)
  // Positive controls: the sidebar is drawn and carries what a reseller uses.
  await expect(navigation.getByRole('link', { name: 'Overview', exact: true })).toBeVisible()
  await expect(navigation.getByRole('button', { name: 'Show the Accounts pages' })).toBeVisible()

  for (const entry of ADMIN_ENTRIES) {
    await expect(navigation.getByRole('link', { name: entry }), `${String(entry)} is offered to a reseller`).toHaveCount(0)
  }
})

test('a reseller Accounts section holds Account list, New account and Plans, and no Resellers', async ({
  page,
}) => {
  await stubResellerSession(page, ACME_HOSTING, ADMIN_MODULES)
  await page.goto('/')
  await openSection(page, 'Accounts')

  const navigation = mainNavigation(page)
  await expect(navigation.getByRole('link', { name: 'Account list', exact: true })).toBeVisible()
  await expect(navigation.getByRole('link', { name: 'New account', exact: true })).toBeVisible()
  await expect(navigation.getByRole('link', { name: 'Plans', exact: true })).toBeVisible()
  await expect(navigation.getByRole('link', { name: /^Resellers/ })).toHaveCount(0)
})

test('a reseller Settings section, where it exists, offers only its own pages', async ({ page }) => {
  await stubResellerSession(page, ACME_HOSTING, ADMIN_MODULES)
  await page.goto('/')
  await openSection(page, 'Settings')

  const navigation = mainNavigation(page)
  await expect(navigation.getByRole('link', { name: 'Profile and security', exact: true })).toBeVisible()
  await expect(navigation.getByRole('link', { name: 'Two-step verification', exact: true })).toBeVisible()
  for (const entry of [/^Panel/, /^Outgoing mail/, /^Administrators and roles/, /^Modules and licence/]) {
    await expect(navigation.getByRole('link', { name: entry }), `${String(entry)} is offered to a reseller`).toHaveCount(0)
  }
})

test('a reseller is offered its own audit journal, under Activity and not under Monitoring', async ({ page }) => {
  await stubResellerSession(page, ACME_HOSTING, ADMIN_MODULES)
  await page.goto('/')

  // A section with one page is drawn as that page under the section's name: "Activity" is the journal.
  const navigation = mainNavigation(page)
  await expect(navigation.getByRole('link', { name: 'Activity', exact: true })).toHaveAttribute('href', '/reseller/audit')
  await expect(navigation.getByRole('link', { name: /^Monitoring/ })).toHaveCount(0)
})

test('an administrator is still offered the entries a reseller is not', async ({ page }) => {
  // Positive control for the absences above: the same regexes find these entries for an administrator.
  await page.setViewportSize({ width: 1440, height: 900 })
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, ADMIN_MODULES)
  await page.goto('/')

  const navigation = mainNavigation(page)
  await expect(navigation.getByRole('link', { name: /^Servers/ })).toBeVisible()
  await expect(navigation.getByRole('link', { name: /^Monitoring/ })).toBeVisible()
  await expect(navigation.getByRole('link', { name: /^Security/ })).toBeVisible()
  await openSection(page, 'Accounts')
  await expect(navigation.getByRole('link', { name: 'Resellers', exact: true })).toBeVisible()
})

test('a reseller opening the system status address lands on its own overview', async ({ page }) => {
  await stubResellerSession(page, ACME_HOSTING, ADMIN_MODULES)

  await page.goto('/')

  await expect(page).toHaveURL('/reseller')
  await expect(page.getByRole('heading', { level: 1, name: 'Your reseller account' })).toBeVisible()
  await expect(mainNavigation(page).getByRole('link', { name: 'Overview', exact: true })).toBeVisible()
})

for (const path of ADMIN_ONLY_PATHS) {
  test(`a reseller opening ${path} lands on the not-available page and asks for nothing`, async ({ page }) => {
    await stubResellerSession(page, ACME_HOSTING, ADMIN_MODULES)
    await stubResellers(page)
    const resellerReads = countResellerListReads(page)

    await page.goto(path)

    await expect(page).toHaveURL('/not-available')
    await expect(page.getByRole('heading', { level: 1, name: 'Page not available' })).toBeVisible()
    expect(resellerReads()).toBe(0)
  })
}

for (const path of ['/accounts', '/accounts/new', '/plans', '/plans/new']) {
  test(`a reseller still reaches ${path}, which an administrator and a reseller share`, async ({ page }) => {
    // Positive control: `managerOnly` admits a reseller, so the guard is not simply refusing everyone.
    await stubResellerSession(page, ACME_HOSTING, ADMIN_MODULES)
    await stubAccounts(page, [])
    await stubPlans(page, [])
    await stubManagedPlans(page, [])

    await page.goto(path)

    await expect(page).toHaveURL(path)
    await expect(page.getByRole('heading', { level: 1, name: 'Page not available' })).toHaveCount(0)
  })
}

test('an administrator still reaches the resellers list and a reseller page', async ({ page }) => {
  // Positive control: the guard reads the role, so an administrator is never turned away.
  await page.setViewportSize({ width: 1440, height: 900 })
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, ADMIN_MODULES)
  await stubResellers(page)

  await page.goto('/resellers')

  await expect(page).toHaveURL('/resellers')
  await expect(page.getByRole('heading', { level: 1, name: 'Resellers' })).toBeVisible()
  await page.getByRole('link', { name: 'acmehosting', exact: true }).click()
  await expect(page).toHaveURL(`/resellers/${ACME_HOSTING.id}`)
})

test('a customer sidebar offers neither the resellers nor the reseller pages', async ({ page }) => {
  await signInAsCustomer(page)
  await page.goto('/')

  const navigation = mainNavigation(page)
  await expect(navigation.getByRole('link', { name: /^Sites/ }).first()).toBeVisible()
  for (const name of [/^Resellers/, /^Overview/, /^Plans/, /^Account list/, /^New account/, /^Servers/, /^Monitoring/]) {
    await expect(navigation.getByRole('link', { name }), `${String(name)} is offered to a customer`).toHaveCount(0)
  }
})

for (const path of ['/resellers', '/resellers/new', ...RESELLER_PAGES, '/accounts', '/accounts/new', '/plans', '/plans/new']) {
  test(`a customer opening ${path} lands on the not-available page`, async ({ page }) => {
    await signInAsCustomer(page)
    await stubResellers(page)
    const resellerReads = countResellerListReads(page)

    await page.goto(path)

    await expect(page).toHaveURL('/not-available')
    await expect(page.getByRole('heading', { level: 1, name: 'Page not available' })).toBeVisible()
    expect(resellerReads()).toBe(0)
  })
}

test('an administrator opening the reseller overview lands on the not-available page', async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 })
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, ADMIN_MODULES)
  await stubMyReseller(page)

  await page.goto('/reseller')

  await expect(page).toHaveURL('/not-available')
  await expect(page.getByRole('heading', { level: 1, name: 'Page not available' })).toBeVisible()
})

test('a customer opening the reseller overview lands on the not-available page', async ({ page }) => {
  await signInAsCustomer(page)
  await stubMyReseller(page)

  await page.goto('/reseller')

  await expect(page).toHaveURL('/not-available')
  await expect(page.getByRole('heading', { level: 1, name: 'Page not available' })).toBeVisible()
})

test('a reseller still reaches the reseller overview, which only a reseller may open', async ({ page }) => {
  // Positive control: `resellerOnly` admits a reseller, so the guard is not simply refusing everyone.
  await stubResellerSession(page, ACME_HOSTING, ADMIN_MODULES)

  await page.goto('/reseller')

  await expect(page).toHaveURL('/reseller')
  await expect(page.getByRole('heading', { level: 1, name: 'Your reseller account' })).toBeVisible()
})

test('the role flags send a reseller away from the resellers screens in Russian and Armenian', async ({
  browser,
}) => {
  const headings = { ru: 'Страница недоступна', hy: 'Էջը հասանելի չէ' } as const
  for (const locale of ['ru', 'hy'] as const) {
    const context = await browser.newContext({ viewport: { width: 1440, height: 900 } })
    const page = await context.newPage()
    await setPersistedLocale(page, locale)
    await stubResellerSession(page, ACME_HOSTING, ADMIN_MODULES)

    await page.goto('/resellers')

    await expect(page).toHaveURL('/not-available')
    await expect(page.getByRole('heading', { level: 1, name: headings[locale] })).toBeVisible()
    await context.close()
  }
})
