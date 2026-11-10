import { expect, test } from '../fixtures/guarded-test'
import { installNetworkGuard } from '../fixtures/network-guard'
import { mainNavigation, openSection } from '../fixtures/open-full-shell'
import { setPersistedLocale } from '../fixtures/set-locale'
import { stubAccounts } from '../fixtures/stub-accounts-route'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubDashboard, WITHHELD_DASHBOARD } from '../fixtures/stub-dashboard-route'
import { stubDatabases } from '../fixtures/stub-databases-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import { stubMyAccount } from '../fixtures/stub-my-account-route'
import { stubAccountResources, stubMyAccountResources } from '../fixtures/stub-account-resources-route'
import { stubManagedPlans, stubPlans } from '../fixtures/stub-plans-route'
import {
  RESELLER_USER,
  stubMyReseller,
  stubResellerSession,
  stubViewAsChain,
  type ViewAsChainCalls,
} from '../fixtures/stub-reseller-session'
import { ACME_HOSTING, stubResellers } from '../fixtures/stub-resellers-routes'
import { stubShellReads } from '../fixtures/stub-shell-reads'
import { stubPhpVersions, stubSites } from '../fixtures/stub-sites-routes'
import type { Account, MyAccount } from '../../src/types/account'
import type { AccountResourceSummary } from '../../src/types/accountResourceSummary'
import type { AuthenticatedUser } from '../../src/types/auth'
import type { PanelModule } from '../../src/types/module'
import type { ViewingAs } from '../../src/types/viewAs'
import type { Page } from '@playwright/test'

// Issues #314 and #317 for resellers. An administrator views the panel as a reseller from the
// reseller's page; a reseller views the panel as one of its own accounts. The "You are viewing as"
// banner follows the viewer from page to page, Back returns to the page the viewing started from,
// and a start the panel refuses leaves everything where it was. One hop only: a view-as session
// never starts another. Each test enters through one `goto`; the rest is clicks.

/** The catalogue both operators and the viewed customer need: accounts, sites and databases. */
const MODULES: PanelModule[] = [
  { name: 'accounts', displayName: 'Accounts', kindDisplayName: 'Core', tier: 'included', state: 'enabled' },
  { name: 'sites', displayName: 'Sites', kindDisplayName: 'Core', tier: 'included', state: 'enabled' },
  { name: 'databases', displayName: 'Databases', kindDisplayName: 'Core', tier: 'included', state: 'enabled' },
]

/** What the panel reports while an administrator views as the reseller. */
const VIEWING_RESELLER: ViewingAs = {
  kind: 'reseller',
  subjectId: ACME_HOSTING.id,
  subjectName: ACME_HOSTING.name,
  username: RESELLER_USER.username,
  operatorUsername: 'admin',
  expiresAt: '2099-01-01T00:15:00+00:00',
}

/** The reseller's own account, which the reseller views the panel as. */
const ALPHA: Account = {
  id: '11111111-1111-1111-1111-111111111111',
  name: 'alpha',
  primaryDomain: 'alpha.example.com',
  planId: '00000000-0000-0000-0000-0000000000f1',
  status: 'active',
  statusName: 'Active',
  createdAt: '2026-10-21T10:00:00+00:00',
  resellerId: ACME_HOSTING.id,
  resellerName: ACME_HOSTING.name,
}

/** The login of that account, which the view-as token signs in as. */
const OLIVE: AuthenticatedUser = {
  id: '00000000-0000-0000-0000-000000000099',
  username: 'olive',
  email: 'olive@example.com',
  role: 'customer',
  accountId: ALPHA.id,
}

/** What the panel reports while the reseller views as its account. */
const VIEWING_ACCOUNT: ViewingAs = {
  kind: 'account',
  subjectId: ALPHA.id,
  subjectName: 'alpha',
  username: 'olive',
  operatorUsername: RESELLER_USER.username,
  expiresAt: '2099-01-01T00:15:00+00:00',
}

/**
 * The account as its own login reads it. While viewing, the shell asks `GET /api/v1/accounts/me`
 * for the plan's features; an unanswered read is a 401, which the SPA correctly treats as an
 * expired token and answers by renewing back to the operator's own session.
 */
const OWN_ACCOUNT: MyAccount = {
  id: ALPHA.id,
  name: 'alpha',
  primaryDomain: 'alpha.example.com',
  status: 'active',
  statusName: 'Active',
  plan: {
    displayName: 'Agency',
    diskQuotaMb: 10240,
    maxSites: 10,
    maxDatabases: 5,
    maxSftpUsers: 4,
    maxFtpUsers: 0,
    maxCronEntries: 7,
  },
}

/** What the customer dashboard reads about the account's use of its plan while viewing. */
const OWN_RESOURCES: AccountResourceSummary = {
  accountId: ALPHA.id,
  resources: [{ kind: 'sites', count: { state: 'counted', used: 0 }, limit: { kind: 'limited', maximum: 10 } }],
}

/**
 * The banner's region, by its label in the interface language.
 * @param page The Playwright page under test.
 * @param label The region's accessible name.
 * @returns Its locator.
 */
const banner = (page: Page, label: string): ReturnType<Page['getByRole']> => {
  return page.getByRole('region', { name: label })
}

/**
 * Signs the administrator in and answers what the walk reads: the resellers, the reseller's own
 * reads for once the view starts, and the lists the reseller's menu leads to.
 * @param page The Playwright page whose network the routes are installed on.
 * @param refusal When set, every start is refused with this sentence.
 * @returns A live view of the view-as calls made.
 */
const installAdministrator = async (page: Page, refusal?: string): Promise<ViewAsChainCalls> => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubShellReads(page)
  await stubModules(page, MODULES)
  await stubMyReseller(page)
  await stubResellers(page)
  await stubAccounts(page, [])
  await stubPlans(page, [])
  await stubManagedPlans(page, [])
  return stubViewAsChain(page, { viewedAs: RESELLER_USER, viewing: VIEWING_RESELLER, refusal })
}

/**
 * Signs the reseller in and answers what the walk reads: its accounts, the account's page and the
 * customer screens the viewed account sees.
 * @param page The Playwright page whose network the routes are installed on.
 * @param refusal When set, every start is refused with this sentence.
 * @returns A live view of the view-as calls made.
 */
const installReseller = async (page: Page, refusal?: string): Promise<ViewAsChainCalls> => {
  await stubResellerSession(page, ACME_HOSTING, MODULES)
  await stubMyAccount(page, OWN_ACCOUNT)
  await stubMyAccountResources(page, OWN_RESOURCES)
  // The account's own page, which the reseller opens before it views as the account, reads the same figures.
  await stubAccountResources(page, OWN_RESOURCES)
  await stubDashboard(page, WITHHELD_DASHBOARD)
  await stubAccounts(page, [ALPHA])
  await stubPlans(page, [])
  await stubSites(page, [])
  await stubPhpVersions(page, [])
  await stubDatabases(page, [])
  await page.route(`**/api/v1/accounts/${ALPHA.id}`, async (route) => {
    if (route.request().method() !== 'GET') {
      await route.fallback()
      return
    }
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(ALPHA) })
  })
  return stubViewAsChain(page, { viewedAs: OLIVE, viewing: VIEWING_ACCOUNT, refusal })
}

/**
 * Opens the resellers list and then the reseller, by clicking its name.
 * @param page The Playwright page under test.
 * @returns Resolves once the reseller's page shows its "View as this reseller" action.
 */
const openReseller = async (page: Page): Promise<void> => {
  await page.goto('/resellers')
  await page.getByRole('link', { name: 'acmehosting', exact: true }).click()
  await expect(page.getByRole('button', { name: 'View as this reseller' })).toBeVisible()
}

/**
 * Opens the reseller's accounts list and then the account, by clicking its name.
 * @param page The Playwright page under test.
 * @returns Resolves once the account's page shows its "View as this account" action.
 */
const openAccount = async (page: Page): Promise<void> => {
  await page.goto('/accounts')
  await page.getByRole('link', { name: 'alpha', exact: true }).click()
  await expect(page.getByRole('button', { name: 'View as this account' })).toBeVisible()
}

test('an administrator viewing as a reseller sees the banner on every page and Back returns to the reseller', async ({
  page,
}) => {
  const calls = await installAdministrator(page)
  await openReseller(page)

  await page.getByRole('button', { name: 'View as this reseller' }).click()

  await expect(page).toHaveURL('/reseller')
  const region = banner(page, 'Viewing as another reseller')
  await expect(region).toContainText('You are viewing as the reseller acmehosting (acmehosting).')
  expect(calls.started).toBe(1)

  // The banner stays on every page the viewer clicks to, and the menu is the reseller's own.
  await expect(page.getByRole('heading', { level: 1, name: 'Your reseller account' })).toBeVisible()
  await openSection(page, 'Accounts')
  await mainNavigation(page).getByRole('link', { name: 'Account list', exact: true }).click()
  await expect(page.getByRole('heading', { level: 1, name: 'Accounts' })).toBeVisible()
  await expect(region).toBeVisible()

  await mainNavigation(page).getByRole('link', { name: 'Plans', exact: true }).click()
  await expect(page.getByRole('heading', { level: 1, name: 'Plans' })).toBeVisible()
  await expect(region).toBeVisible()
  // Role-exact: viewing as a reseller does not leave the administrator's Resellers entry behind.
  await expect(mainNavigation(page).getByRole('link', { name: 'Resellers', exact: true })).toHaveCount(0)

  await region.getByRole('button', { name: 'Back to admin' }).click()

  await expect(region).toHaveCount(0)
  await expect(page).toHaveURL(`/resellers/${ACME_HOSTING.id}`)
  await expect(page.getByRole('heading', { level: 1, name: 'acmehosting' })).toBeVisible()
  expect(calls.ended).toBe(1)
  // The administrator is back: the Resellers entry is offered again.
  await openSection(page, 'Accounts')
  await expect(mainNavigation(page).getByRole('link', { name: 'Resellers', exact: true })).toBeVisible()
})

test('the Back button of the reseller banner works from the keyboard', async ({ page }) => {
  const calls = await installAdministrator(page)
  await openReseller(page)
  await page.getByRole('button', { name: 'View as this reseller' }).click()

  const back = banner(page, 'Viewing as another reseller').getByRole('button', { name: 'Back to admin' })
  await back.focus()
  await expect(back).toBeFocused()
  await page.keyboard.press('Enter')

  await expect(page).toHaveURL(`/resellers/${ACME_HOSTING.id}`)
  expect(calls.ended).toBe(1)
})

test('a reseller viewing as its own account sees the banner on every page and Back returns to the account', async ({
  page,
}) => {
  const calls = await installReseller(page)
  await openAccount(page)

  await page.getByRole('button', { name: 'View as this account' }).click()

  const region = banner(page, 'Viewing as another account')
  await expect(region).toContainText('You are viewing as alpha (olive).')
  expect(calls.started).toBe(1)

  await mainNavigation(page).getByRole('link', { name: 'Sites' }).first().click()
  await expect(page.getByRole('heading', { level: 1 })).toBeVisible()
  await expect(region).toBeVisible()

  await mainNavigation(page).getByRole('link', { name: 'Databases' }).first().click()
  await expect(page.getByRole('heading', { level: 1 })).toBeVisible()
  await expect(region).toBeVisible()

  // The operator named in Back is the reseller, whose own session it returns to.
  await region.getByRole('button', { name: 'Back to acmehosting' }).click()

  await expect(region).toHaveCount(0)
  await expect(page).toHaveURL(`/accounts/${ALPHA.id}`)
  expect(calls.ended).toBe(1)
})

test('a reseller account page offers "View as this account" and no administrator-only card', async ({ page }) => {
  await installReseller(page)

  await openAccount(page)

  await expect(page.getByTestId('invitation-card')).toHaveCount(0)
  await expect(page.getByRole('button', { name: 'View as this reseller' })).toHaveCount(0)
})

test('a refused start for an account shows the panel sentence and the reseller stays where it was', async ({
  page,
  guard,
}) => {
  guard.allow(404, /\/api\/v1\/view-as$/)
  const detail = 'That account was not found, or it has no login to view as.'
  const calls = await installReseller(page, detail)
  await openAccount(page)

  await page.getByRole('button', { name: 'View as this account' }).click()

  await expect(page.getByText(detail, { exact: true })).toBeVisible()
  await expect(banner(page, 'Viewing as another account')).toHaveCount(0)
  await expect(page).toHaveURL(`/accounts/${ALPHA.id}`)
  expect(calls.started).toBe(0)
})

test('a refused start for a reseller shows the panel sentence and the administrator stays where it was', async ({
  page,
  guard,
}) => {
  guard.allow(404, /\/api\/v1\/view-as\/reseller$/)
  const detail = 'That reseller was not found, or it has no login to view as.'
  const calls = await installAdministrator(page, detail)
  await openReseller(page)

  await page.getByRole('button', { name: 'View as this reseller' }).click()

  await expect(page.getByText(detail, { exact: true })).toBeVisible()
  await expect(banner(page, 'Viewing as another reseller')).toHaveCount(0)
  await expect(page).toHaveURL(`/resellers/${ACME_HOSTING.id}`)
  expect(calls.started).toBe(0)
})

test('the banner follows the panel: signed in as the administrator, the panel reports no session and shows none', async ({
  page,
}) => {
  await installAdministrator(page)

  await openReseller(page)

  await expect(banner(page, 'Viewing as another reseller')).toHaveCount(0)
  await expect(banner(page, 'Viewing as another account')).toHaveCount(0)
})

test('the reseller banner is drawn in Russian and Armenian without scrolling sideways at 390 px', async ({
  browser,
}) => {
  const words = {
    ru: {
      action: 'Просмотреть от имени этого реселлера',
      region: 'Просмотр от имени другого реселлера',
      text: 'Вы просматриваете панель от имени реселлера acmehosting (acmehosting).',
    },
    hy: {
      action: 'Դիտել որպես այս ռեսելլեր',
      region: 'Դիտում է այլ ռեսելլեր',
      text: 'Դուք դիտում եք որպես ռեսելլեր acmehosting (acmehosting)։',
    },
  } as const
  for (const locale of ['ru', 'hy'] as const) {
    const context = await browser.newContext({ viewport: { width: 390, height: 844 } })
    const page = await context.newPage()
    // This page is not the fixture's, so it is guarded here: the same findings, asserted per language below.
    const guard = await installNetworkGuard(page, 'stubbed')
    await setPersistedLocale(page, locale)
    await installAdministrator(page)

    await page.goto(`/resellers/${ACME_HOSTING.id}`)
    await page.getByRole('button', { name: words[locale].action }).click()

    const region = banner(page, words[locale].region)
    await expect(region).toContainText(words[locale].text)
    await page.evaluate(async () => {
      await document.fonts.ready
    })
    const widths = await page.evaluate(() => {
      return { document: document.documentElement.scrollWidth, viewport: window.innerWidth }
    })
    expect(widths.document, `${locale}: the banner page scrolls sideways`).toBeLessThanOrEqual(widths.viewport)
    expect(await guard.problems(), `${locale}: the page asked for, or was given, something it should not have`).toEqual([])
    await context.close()
  }
})
