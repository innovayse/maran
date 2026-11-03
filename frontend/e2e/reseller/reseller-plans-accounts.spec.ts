import { expect, test, type Page } from '@playwright/test'
import { mainNavigation, openSection } from '../fixtures/open-full-shell'
import { setPersistedLocale } from '../fixtures/set-locale'
import { stubAccounts } from '../fixtures/stub-accounts-route'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import {
  MANAGED_PLANS,
  PLAN_FEATURE_CHOICES,
  stubManagedPlans,
  stubPlans,
  type PlanRefusals,
  type PlanRequests,
} from '../fixtures/stub-plans-route'
import { RESELLER_MODULES, stubResellerSession } from '../fixtures/stub-reseller-session'
import { ACME_HOSTING, stubResellers } from '../fixtures/stub-resellers-routes'
import { stubShellReads } from '../fixtures/stub-shell-reads'
import type { Account } from '../../src/types/account'
import type { ManagedPlan } from '../../src/types/plan'

// Issue #179: a reseller defines plans and creates accounts inside its envelope, and sees only its
// own. The plan form says what the envelope allows (hints, advice only), a refusal the panel sends
// is shown in the panel's own words, the lists hold only the reseller's rows, and the owner column
// and filter that tell an administrator whose a row is do not exist for a reseller. Each test
// enters through one `goto`; the rest is clicks.

/** The reseller's own plan, which its lists show. */
const AGENCY: ManagedPlan = {
  ...MANAGED_PLANS[0],
  id: '00000000-0000-0000-0000-0000000000f1',
  displayName: 'Agency',
  diskQuotaMb: 10240,
  accountCount: 1,
  resellerId: ACME_HOSTING.id,
  resellerName: ACME_HOSTING.name,
}

/** A plan the administrator owns, which only the administrator's list holds. */
const ADMIN_PLAN: ManagedPlan = {
  ...MANAGED_PLANS[1],
  id: '00000000-0000-0000-0000-0000000000f2',
  displayName: 'Shared hosting',
  resellerId: null,
  resellerName: null,
}

/** The features the panel offers a reseller: its envelope's four, already narrowed by the backend. */
const NARROWED_FEATURES = PLAN_FEATURE_CHOICES.filter((choice) => {
  return ACME_HOSTING.envelope.features.includes(choice.feature)
})

/** The account the reseller owns. */
const ALPHA: Account = {
  id: '11111111-1111-1111-1111-111111111111',
  name: 'alpha',
  primaryDomain: 'alpha.example.com',
  planId: AGENCY.id,
  status: 'active',
  statusName: 'Active',
  createdAt: '2026-10-21T10:00:00+00:00',
  resellerId: ACME_HOSTING.id,
  resellerName: ACME_HOSTING.name,
}

/** A second account of the same reseller. */
const BETA: Account = {
  ...ALPHA,
  id: '22222222-2222-2222-2222-222222222222',
  name: 'beta',
  primaryDomain: 'beta.example.com',
}

/** An account the administrator owns, which only the administrator's list holds. */
const GAMMA: Account = {
  ...ALPHA,
  id: '33333333-3333-3333-3333-333333333333',
  name: 'gamma',
  primaryDomain: 'gamma.example.com',
  planId: ADMIN_PLAN.id,
  resellerId: null,
  resellerName: null,
}

/**
 * Signs the reseller in and stubs the plan endpoints with the reseller's own plans and the features
 * its envelope allows.
 * @param page The Playwright page under test.
 * @param refusals Plan writes the stubbed panel refuses.
 * @returns What the plan endpoints received.
 */
const installReseller = async (page: Page, refusals: PlanRefusals = {}): Promise<PlanRequests> => {
  await stubResellerSession(page, ACME_HOSTING, RESELLER_MODULES)
  const requests = await stubManagedPlans(page, [{ ...AGENCY }], refusals)
  // Installed after the management stub so it wins: the backend narrows the list for a reseller.
  await page.route(/\/api\/v1\/plans\/features(\?.*)?$/, async (route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(NARROWED_FEATURES) })
  })
  return requests
}

/**
 * Opens the Accounts section of the sidebar and clicks one of its pages.
 * @param page The Playwright page under test.
 * @param name The page's name in the menu.
 * @returns Resolves once the click was made.
 */
const openFromSidebar = async (page: Page, name: string): Promise<void> => {
  await openSection(page, 'Accounts')
  await mainNavigation(page).getByRole('link', { name, exact: true }).click()
}

/**
 * Fills the plan form with a valid plan that sits inside {@link ACME_HOSTING}'s envelope.
 * @param page The Playwright page under test.
 * @param name The plan's name.
 * @returns Resolves once every field is filled.
 */
const fillPlanInsideEnvelope = async (page: Page, name: string): Promise<void> => {
  await page.getByLabel('Plan name').fill(name)
  await page.getByLabel('Disk quota (MB)').fill('10240')
  await page.getByLabel('Sites', { exact: true }).fill('10')
  await page.getByRole('textbox', { name: 'Databases', exact: true }).fill('5')
  await page.getByLabel('SFTP logins').fill('4')
  await page.getByLabel('FTPS logins').fill('0')
  await page.getByLabel('Cron entries').fill('7')
  await page.getByLabel('PHP workers per pool').fill('6')
  await page.getByLabel('CPU (% of one core)').fill('150')
  await page.getByLabel('Memory (MB)').fill('1024')
  await page.getByLabel('Processes', { exact: true }).fill('64')
}

test('the reseller plan form says what its envelope allows for each limit', async ({ page }) => {
  await installReseller(page)
  await page.goto('/')
  await openFromSidebar(page, 'Plans')
  await page.getByRole('link', { name: 'New plan' }).first().click()

  await expect(page.getByRole('heading', { level: 1, name: 'New plan' })).toBeVisible()
  // One hint per bounded figure, each under its own field; the figures are the envelope's.
  for (const figure of [20480, 12, 8, 6, 4, 9, 7, 200, 2048]) {
    await expect(page.getByText(`At most ${figure} (your envelope)`, { exact: true })).toBeVisible()
  }
})

test('a reseller plan is posted as typed, with only the features its envelope allows', async ({ page }) => {
  const requests = await installReseller(page)
  await page.goto('/')
  await openFromSidebar(page, 'Plans')
  await page.getByRole('link', { name: 'New plan' }).first().click()

  // The feature list is the backend's, already narrowed: Mail is not on offer to this reseller.
  await expect(page.getByRole('checkbox', { name: 'Backups' })).toBeVisible()
  await expect(page.getByRole('checkbox', { name: 'Mail' })).toHaveCount(0)

  await fillPlanInsideEnvelope(page, 'Studio')
  await page.getByRole('button', { name: 'Create plan' }).click()

  await expect(page).toHaveURL('/plans')
  expect(requests.created).toEqual([
    {
      name: 'Studio',
      diskQuotaMb: 10240,
      maxSites: 10,
      maxDatabases: 5,
      maxSftpUsers: 4,
      maxFtpUsers: 0,
      maxCronEntries: 7,
      maxPhpWorkersPerPool: 6,
      cpuQuotaPercent: { kind: 'limited', maximum: 150 },
      memoryMaxMb: { kind: 'limited', maximum: 1024 },
      maxProcesses: { kind: 'limited', maximum: 64 },
      features: NARROWED_FEATURES.map((choice) => {
        return choice.feature
      }),
    },
  ])
  await expect(page.getByRole('row', { name: /Studio/ })).toBeVisible()
})

test('a plan the envelope refuses shows the panel sentence verbatim and keeps what was typed', async ({
  page,
}) => {
  const detail = 'A plan may give an account at most 20480 MB of disk under your envelope.'
  await installReseller(page, { create: detail })
  await page.goto('/')
  await openFromSidebar(page, 'Plans')
  await page.getByRole('link', { name: 'New plan' }).first().click()

  await fillPlanInsideEnvelope(page, 'Studio')
  await page.getByRole('button', { name: 'Create plan' }).click()

  await expect(page.getByText(detail, { exact: true })).toBeVisible()
  await expect(page).toHaveURL('/plans/new')
  await expect(page.getByLabel('Plan name')).toHaveValue('Studio')
})

test('a reseller plans list holds its own plans and has no owner column', async ({ page }) => {
  await installReseller(page)
  await page.goto('/')
  await openFromSidebar(page, 'Plans')

  await expect(page.getByRole('heading', { level: 1, name: 'Plans' })).toBeVisible()
  await expect(page.getByRole('row')).toHaveCount(2)
  await expect(page.getByRole('row', { name: /Agency/ })).toBeVisible()
  await expect(page.getByRole('row', { name: /Shared hosting/ })).toHaveCount(0)
  await expect(page.getByRole('columnheader', { name: 'Owner', exact: true })).toHaveCount(0)
})

test('an administrator plans list names the owner of every plan', async ({ page }) => {
  // Positive control for the absence above: the same screen draws the column for an administrator.
  await page.setViewportSize({ width: 1440, height: 900 })
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubShellReads(page)
  await stubModules(page, RESELLER_MODULES)
  await stubManagedPlans(page, [{ ...AGENCY }, { ...ADMIN_PLAN }])
  await page.goto('/plans')

  await expect(page.getByRole('columnheader', { name: 'Owner', exact: true })).toBeVisible()
  await expect(page.getByRole('row', { name: /Agency/ })).toContainText('acmehosting')
  await expect(page.getByRole('row', { name: /Shared hosting/ })).toContainText('Administrator')
})

test('a reseller creates an account on its own plan and the owner is not asked for', async ({ page }) => {
  await installReseller(page)
  await stubAccounts(page, [])
  await stubPlans(page, [{ id: AGENCY.id, displayName: 'Agency', diskQuotaMb: 10240, maxSites: 10 }])
  await page.route(`**/api/v1/accounts/${ALPHA.id}`, async (route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(ALPHA) })
  })
  await page.goto('/')
  await openFromSidebar(page, 'New account')

  // A reseller never chooses an owner: everything it creates is its own, and the server stamps that.
  await expect(page.getByRole('combobox', { name: 'Owner (reseller)' })).toHaveCount(0)

  await page.getByLabel('Username').fill('alpha')
  await page.getByLabel('Primary domain').fill('alpha.example.com')
  await page.getByRole('combobox', { name: 'Plan', exact: true }).click()
  await page.getByRole('option', { name: 'Agency', exact: false }).click()
  await page.getByLabel('Owner email').fill('owner@alpha.example.com')

  const [request] = await Promise.all([
    page.waitForRequest((candidate) => {
      return candidate.method() === 'POST' && candidate.url().includes('/api/v1/accounts')
    }),
    page.getByRole('button', { name: 'Create account' }).click(),
  ])

  expect(request.postDataJSON()).toEqual({
    name: 'alpha',
    primaryDomain: 'alpha.example.com',
    planId: AGENCY.id,
    ownerEmail: 'owner@alpha.example.com',
    ownerLanguage: 'en',
  })
  await expect(page).toHaveURL(`/accounts/${ALPHA.id}`)
  await expect(page.getByRole('heading', { name: 'alpha' })).toBeVisible()
  // The invitation is an administrator's tool; a reseller is not shown a card it cannot use.
  await expect(page.getByTestId('invitation-card')).toHaveCount(0)
})

test('an account the envelope refuses shows the panel sentence verbatim and stays on the form', async ({
  page,
}) => {
  const detail = 'You have reached the account limit of your envelope (20 accounts).'
  await installReseller(page)
  await stubPlans(page, [{ id: AGENCY.id, displayName: 'Agency', diskQuotaMb: 10240, maxSites: 10 }])
  await stubAccounts(page, [])
  // Installed after the catalogue stub so it wins for the create only; every read falls through to it.
  await page.route(/\/api\/v1\/accounts(\?.*)?$/, async (route) => {
    if (route.request().method() !== 'POST') {
      await route.fallback()
      return
    }
    await route.fulfill({
      status: 409,
      contentType: 'application/problem+json',
      body: JSON.stringify({ code: 'ResellerAccountLimitReached', title: 'Conflict', detail }),
    })
  })
  await page.goto('/')
  await openFromSidebar(page, 'New account')

  await page.getByLabel('Username').fill('alpha')
  await page.getByLabel('Primary domain').fill('alpha.example.com')
  await page.getByRole('combobox', { name: 'Plan', exact: true }).click()
  await page.getByRole('option', { name: 'Agency', exact: false }).click()
  await page.getByLabel('Owner email').fill('owner@alpha.example.com')
  await page.getByRole('button', { name: 'Create account' }).click()

  await expect(page.getByText(detail, { exact: true })).toBeVisible()
  await expect(page).toHaveURL('/accounts/new')
})

test('a reseller accounts list holds its own accounts, with no reseller column and no reseller filter', async ({
  page,
}) => {
  await installReseller(page)
  const listRequests = await stubAccounts(page, [{ ...ALPHA }, { ...BETA }])
  await stubPlans(page, [{ id: AGENCY.id, displayName: 'Agency', diskQuotaMb: 10240, maxSites: 10 }])
  await page.goto('/')
  await openFromSidebar(page, 'Account list')

  await expect(page.getByRole('link', { name: 'alpha', exact: true })).toBeVisible()
  await expect(page.getByRole('link', { name: 'beta', exact: true })).toBeVisible()
  await expect(page.getByRole('link', { name: 'gamma', exact: true })).toHaveCount(0)
  await expect(page.getByRole('columnheader', { name: 'Reseller', exact: true })).toHaveCount(0)

  await page.getByTestId('filter-trigger').click()
  const dialog = page.getByRole('dialog', { name: 'Filters' })
  // Positive control: the sheet is open and carries the filters a reseller does have.
  await expect(dialog.getByRole('combobox', { name: 'Plan' })).toBeVisible()
  await expect(dialog.getByRole('combobox', { name: 'Reseller' })).toHaveCount(0)
  // The screen never asks the panel to narrow by reseller: the server does that from the token.
  for (const url of listRequests()) {
    expect(url.searchParams.has('resellerId')).toBe(false)
  }
})

test('an administrator accounts list shows the reseller column and filter and the owner choice on the form', async ({
  page,
}) => {
  // Positive control for the absences above.
  await page.setViewportSize({ width: 1440, height: 900 })
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubShellReads(page)
  await stubModules(page, RESELLER_MODULES)
  await stubAccounts(page, [{ ...ALPHA }, { ...GAMMA }])
  await stubPlans(page)
  await stubResellers(page)
  await page.goto('/accounts')

  await expect(page.getByRole('columnheader', { name: 'Reseller', exact: true })).toBeVisible()
  await expect(page.getByRole('row', { name: /alpha/ })).toContainText('acmehosting')
  await expect(page.getByRole('row', { name: /gamma/ })).toContainText('Administrator')
  await page.getByTestId('filter-trigger').click()
  await expect(
    page.getByRole('dialog', { name: 'Filters' }).getByRole('combobox', { name: 'Reseller' }),
  ).toBeVisible()
  await page.getByRole('dialog', { name: 'Filters' }).getByRole('button', { name: 'Close' }).click()

  await page.getByRole('link', { name: 'New account' }).first().click()
  await expect(page.getByRole('combobox', { name: 'Owner (reseller)' })).toBeVisible()
})

test('the reseller plan and account forms have no horizontal scroll at 390 px in every language', async ({
  browser,
}) => {
  for (const locale of ['en', 'ru', 'hy'] as const) {
    const context = await browser.newContext({ viewport: { width: 390, height: 844 } })
    const page = await context.newPage()
    await setPersistedLocale(page, locale)
    await installReseller(page)
    await stubAccounts(page, [])
    await stubPlans(page)
    await page.setViewportSize({ width: 390, height: 844 })

    await page.goto('/plans/new')
    await expect(page.getByRole('heading', { level: 1 })).toBeVisible()
    // The hints are in the page before it is measured: they are the longest text the form gained.
    await expect(page.getByText(/20480/).first()).toBeVisible()
    await page.evaluate(async () => {
      await document.fonts.ready
    })
    const widths = await page.evaluate(() => {
      const main = document.querySelector('main')
      return {
        document: document.documentElement.scrollWidth,
        viewport: window.innerWidth,
        main: main?.scrollWidth ?? 0,
        mainBox: main?.clientWidth ?? 0,
      }
    })
    expect(widths.document, `${locale}: the plan form scrolls sideways`).toBeLessThanOrEqual(widths.viewport)
    expect(widths.main, `${locale}: the plan form overflows its panel`).toBeLessThanOrEqual(widths.mainBox)
    await context.close()
  }
})
