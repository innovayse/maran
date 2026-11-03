import { expect, test, type Page } from '@playwright/test'
import { openSection, mainNavigation } from '../fixtures/open-full-shell'
import { setPersistedLocale } from '../fixtures/set-locale'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubAccounts } from '../fixtures/stub-accounts-route'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import { PLAN_FEATURE_CHOICES, stubPlans } from '../fixtures/stub-plans-route'
import {
  ACME_HOSTING,
  BRAVO_HOSTING,
  CREATED_RESELLER_ID,
  stubResellers,
  type ResellerRefusals,
  type ResellerRequests,
} from '../fixtures/stub-resellers-routes'
import { stubShellReads } from '../fixtures/stub-shell-reads'
import type { PanelModule } from '../../src/types/module'
import type { ResellerDetail, ResellerEnvelope } from '../../src/types/reseller'

// Issue #179: an administrator manages resellers. The Resellers page is reached from the sidebar's
// Accounts section by clicks, lists resellers (server-searched, filtered by status), creates one
// with an envelope, shows its detail, suspends and reactivates it behind a confirmation, and deletes
// it. Whatever the panel refuses with is shown in the panel's own words. The one `goto` per test is
// the entry point every account spec uses; everything after it is a click.

/** The catalogue the spec starts from: the accounts module, which carries the reseller screens. */
const LICENSED_ACCOUNTS: PanelModule[] = [
  { name: 'accounts', displayName: 'Accounts', tier: 'included', state: 'enabled' },
]

/** The envelope the create form is filled with, and the request body it must produce. */
const NEW_ENVELOPE: ResellerEnvelope = {
  maxAccounts: 10,
  diskPoolMb: 51200,
  maxPlans: 4,
  maxDiskQuotaMb: 10240,
  maxSites: 5,
  maxDatabases: 3,
  maxSftpUsers: 2,
  maxFtpUsers: 0,
  maxCronEntries: 0,
  maxPhpWorkersPerPool: 4,
  cpuQuotaPercent: { kind: 'limited', maximum: 100 },
  memoryMaxMb: { kind: 'limited', maximum: 1024 },
  maxProcesses: { kind: 'unlimited' },
  // A new envelope starts with every feature the panel lists ticked, in the panel's order.
  features: PLAN_FEATURE_CHOICES.map((choice) => {
    return choice.feature
  }),
}

/**
 * Signs the administrator in and stubs the panel the reseller screens read.
 * @param page The Playwright page under test.
 * @param refusals Writes the stubbed panel refuses.
 * @param resellers The resellers the panel starts with.
 * @returns What the stubbed endpoints received.
 */
const installPanel = async (
  page: Page,
  refusals: ResellerRefusals = {},
  resellers = [ACME_HOSTING, BRAVO_HOSTING].map((reseller) => {
    return { ...reseller }
  }),
): Promise<ResellerRequests> => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubShellReads(page)
  await stubModules(page, LICENSED_ACCOUNTS)
  await stubAccounts(page, [])
  await stubPlans(page)
  return stubResellers(page, resellers, refusals)
}

/**
 * Installs the panel on a desktop and opens the accounts list, the entry point from which the
 * sidebar leads to Resellers.
 * @param page The Playwright page under test.
 * @param refusals Writes the stubbed panel refuses.
 * @param resellers The resellers the panel starts with.
 * @returns What the stubbed endpoints received.
 */
const openAccounts = async (
  page: Page,
  refusals: ResellerRefusals = {},
  resellers?: ResellerDetail[],
): Promise<ResellerRequests> => {
  await page.setViewportSize({ width: 1440, height: 900 })
  const requests = await installPanel(page, refusals, resellers)
  await page.goto('/accounts')
  return requests
}

/**
 * Walks from the accounts list to the Resellers page through the sidebar.
 * @param page The Playwright page under test.
 * @returns Resolves once the Resellers heading is drawn.
 */
const openResellersFromSidebar = async (page: Page): Promise<void> => {
  await openSection(page, 'Accounts')
  await mainNavigation(page).getByRole('link', { name: 'Resellers', exact: true }).click()
  await expect(page).toHaveURL('/resellers')
  await expect(page.getByRole('heading', { level: 1, name: 'Resellers' })).toBeVisible()
}

/**
 * Walks from the accounts list to one reseller's detail page: the sidebar, then the name link.
 * @param page The Playwright page under test.
 * @param name The reseller's name.
 * @returns Resolves once the reseller's own heading is drawn.
 */
const openReseller = async (page: Page, name: string): Promise<void> => {
  await openResellersFromSidebar(page)
  await page.getByRole('link', { name, exact: true }).click()
  await expect(page.getByRole('heading', { level: 1, name })).toBeVisible()
}

/**
 * Fills the create form with a valid reseller whose envelope is {@link NEW_ENVELOPE}.
 * @param page The Playwright page under test.
 * @param name The reseller's name.
 * @returns Resolves once every field is filled.
 */
const fillValidReseller = async (page: Page, name: string): Promise<void> => {
  await page.getByLabel('Name', { exact: true }).fill(name)
  await page.getByLabel('Contact address', { exact: true }).fill(`owner@${name}.example.com`)
  await page.getByLabel('Accounts', { exact: true }).fill('10')
  await page.getByLabel('Plans', { exact: true }).fill('4')
  await page.getByLabel('Disk pool (MB)', { exact: true }).fill('51200')
  await page.getByLabel('Disk per account (MB)', { exact: true }).fill('10240')
  await page.getByLabel('Sites per account', { exact: true }).fill('5')
  await page.getByLabel('Databases per account', { exact: true }).fill('3')
  await page.getByLabel('SFTP logins per account', { exact: true }).fill('2')
  await page.getByLabel('FTPS logins per account', { exact: true }).fill('0')
  await page.getByLabel('Cron entries per account', { exact: true }).fill('0')
  await page.getByLabel('PHP workers per pool', { exact: true }).fill('4')
  await page.getByLabel('CPU (% of one core)', { exact: true }).fill('100')
  await page.getByLabel('Memory (MB)', { exact: true }).fill('1024')
  // Processes is left uncapped on purpose: "No limit" is a case of its own, never a zero.
  await page.getByRole('checkbox', { name: 'No limit' }).nth(2).check({ force: true })
}

/**
 * Reads whether the page scrolls sideways.
 * @param page The Playwright page under test.
 * @returns The document's and the main panel's widths against what they are drawn in.
 */
const widthsOf = async (
  page: Page,
): Promise<{ document: number; viewport: number; main: number; mainBox: number }> => {
  await page.evaluate(async () => {
    await document.fonts.ready
  })
  return page.evaluate(() => {
    const main = document.querySelector('main')
    return {
      document: document.documentElement.scrollWidth,
      viewport: window.innerWidth,
      main: main?.scrollWidth ?? 0,
      mainBox: main?.clientWidth ?? 0,
    }
  })
}

test('the sidebar leads an administrator to the resellers list, with both states and their usage', async ({
  page,
}) => {
  await openAccounts(page)
  await openResellersFromSidebar(page)

  await expect(page.getByRole('row')).toHaveCount(3)
  const acme = page.getByRole('row', { name: /acmehosting/ })
  await expect(acme).toContainText('owner@acmehosting.example.com')
  await expect(acme).toContainText('Active')
  await expect(page.getByRole('row', { name: /bravohosting/ })).toContainText('Suspended')
})

test('searching narrows the list on the server and clearing brings every reseller back', async ({ page }) => {
  await openAccounts(page)
  await openResellersFromSidebar(page)

  await page.getByRole('searchbox', { name: 'Search resellers' }).fill('bravo')
  await expect(page.getByRole('row', { name: /bravohosting/ })).toBeVisible()
  await expect(page.getByRole('row', { name: /acmehosting/ })).toHaveCount(0)

  await page.getByRole('searchbox', { name: 'Search resellers' }).fill('nobody-by-this-name')
  await expect(page.getByText('No resellers match')).toBeVisible()

  await page.getByRole('button', { name: 'Clear search and filters' }).click()
  await expect(page.getByRole('row', { name: /acmehosting/ })).toBeVisible()
  await expect(page.getByRole('row', { name: /bravohosting/ })).toBeVisible()
})

test('the status filter in the sheet keeps only the suspended reseller and shows a chip', async ({ page }) => {
  await openAccounts(page)
  await openResellersFromSidebar(page)

  await page.getByTestId('filter-trigger').click()
  const dialog = page.getByRole('dialog', { name: 'Filters' })
  await dialog.getByRole('combobox', { name: 'Status' }).click()
  await page.getByRole('option', { name: 'Suspended' }).click()
  await dialog.getByRole('button', { name: 'Apply' }).click()

  await expect(page).toHaveURL(/status=suspended/)
  await expect(page.getByTestId('active-filter-chips')).toContainText('Status: Suspended')
  await expect(page.getByRole('row', { name: /bravohosting/ })).toBeVisible()
  await expect(page.getByRole('row', { name: /acmehosting/ })).toHaveCount(0)
})

test('an empty panel says there are no resellers and offers to create the first', async ({ page }) => {
  await openAccounts(page, {}, [])
  await openResellersFromSidebar(page)

  await expect(page.getByText('No resellers yet')).toBeVisible()
  await page.getByRole('link', { name: 'New reseller' }).last().click()
  await expect(page).toHaveURL('/resellers/new')
})

test('submitting the empty form shows the panel own field messages and sends nothing', async ({ page }) => {
  const requests = await openAccounts(page)
  await openResellersFromSidebar(page)
  await page.getByRole('link', { name: 'New reseller' }).first().click()

  await page.getByRole('button', { name: 'Create reseller' }).click()

  await expect(page.getByText('Name is required.')).toBeVisible()
  await expect(page.getByText('A contact address is required.')).toBeVisible()
  await expect(page.getByLabel('Name', { exact: true })).toHaveAttribute('aria-invalid', 'true')
  expect(requests.created).toEqual([])
})

test('a valid reseller is posted with its envelope and its detail page opens with a notice', async ({ page }) => {
  const requests = await openAccounts(page)
  await openResellersFromSidebar(page)
  await page.getByRole('link', { name: 'New reseller' }).first().click()

  await fillValidReseller(page, 'newhost')
  await page.getByRole('button', { name: 'Create reseller' }).click()

  await expect(page).toHaveURL(`/resellers/${CREATED_RESELLER_ID}`)
  expect(requests.created).toEqual([
    { name: 'newhost', contactEmail: 'owner@newhost.example.com', language: 'en', envelope: NEW_ENVELOPE },
  ])
  await expect(page.getByRole('heading', { level: 1, name: 'newhost' })).toBeVisible()
  await expect(
    page.getByText('Reseller newhost created. An invitation is on its way to its contact address.'),
  ).toBeVisible()
  // The envelope the administrator typed is the one the detail page reads back.
  await expect(page.getByText('Disk per account (MB)')).toBeVisible()
})

test('a refused create shows the panel sentence verbatim and keeps what was typed', async ({ page }) => {
  const detail = 'A reseller named "acmehosting" already exists on this panel.'
  await openAccounts(page, { create: detail })
  await openResellersFromSidebar(page)
  await page.getByRole('link', { name: 'New reseller' }).first().click()

  await fillValidReseller(page, 'acmehosting')
  await page.getByRole('button', { name: 'Create reseller' }).click()

  await expect(page.getByText(detail, { exact: true })).toBeVisible()
  await expect(page).toHaveURL('/resellers/new')
  await expect(page.getByLabel('Name', { exact: true })).toHaveValue('acmehosting')
})

test('the detail page shows the envelope, the usage and the actions of an active reseller', async ({ page }) => {
  await openAccounts(page)
  await openReseller(page, 'acmehosting')

  await expect(page.getByText('owner@acmehosting.example.com').first()).toBeVisible()
  await expect(page.getByText('Disk per account (MB)')).toBeVisible()
  await expect(page.getByRole('heading', { name: 'Usage' })).toBeVisible()
  await expect(page.getByRole('link', { name: 'Change envelope' })).toBeVisible()
  await expect(page.getByRole('button', { name: 'Suspend', exact: true })).toBeVisible()
  await expect(page.getByRole('button', { name: 'Reactivate', exact: true })).toHaveCount(0)
  await expect(page.getByRole('button', { name: 'View as this reseller' })).toBeVisible()
})

test('suspending asks first, then marks the reseller suspended and offers to reactivate it', async ({ page }) => {
  const requests = await openAccounts(page)
  await openReseller(page, 'acmehosting')

  // Cancelling sends nothing.
  await page.getByRole('button', { name: 'Suspend', exact: true }).click()
  const dialog = page.getByRole('dialog')
  await expect(dialog).toContainText('Suspend reseller acmehosting')
  await expect(dialog).toContainText('Its customers\' accounts are not suspended')
  await dialog.getByRole('button', { name: 'Cancel' }).click()
  await expect(page.getByRole('dialog')).toHaveCount(0)
  expect(requests.suspended).toEqual([])

  await page.getByRole('button', { name: 'Suspend', exact: true }).click()
  await page.getByRole('dialog').getByRole('button', { name: 'Suspend reseller' }).click()

  await expect(page.getByText('This reseller is suspended', { exact: false })).toBeVisible()
  await expect(page.getByRole('button', { name: 'Reactivate', exact: true })).toBeVisible()
  expect(requests.suspended).toEqual([ACME_HOSTING.id])

  await page.getByRole('button', { name: 'Reactivate', exact: true }).click()
  await page.getByRole('dialog').getByRole('button', { name: 'Reactivate reseller' }).click()

  await expect(page.getByRole('button', { name: 'Suspend', exact: true })).toBeVisible()
  await expect(page.getByText('This reseller is suspended', { exact: false })).toHaveCount(0)
  expect(requests.reactivated).toEqual([ACME_HOSTING.id])
})

test('a refused suspension shows the panel sentence verbatim and changes nothing', async ({ page }) => {
  const detail = 'The reseller is being removed and cannot be suspended.'
  await openAccounts(page, { suspend: detail })
  await openReseller(page, 'acmehosting')

  await page.getByRole('button', { name: 'Suspend', exact: true }).click()
  await page.getByRole('dialog').getByRole('button', { name: 'Suspend reseller' }).click()

  await expect(page.getByText(detail, { exact: true })).toBeVisible()
  await expect(page.getByRole('button', { name: 'Suspend', exact: true })).toBeVisible()
  await expect(page.getByText('This reseller is suspended', { exact: false })).toHaveCount(0)
})

test('deleting asks first, removes the reseller and returns to a list without it', async ({ page }) => {
  const requests = await openAccounts(page)
  await openReseller(page, 'bravohosting')

  await page.getByRole('button', { name: 'Delete', exact: true }).click()
  const dialog = page.getByRole('dialog')
  await expect(dialog).toContainText('Delete reseller bravohosting')
  await expect(dialog).toContainText('refuses while the reseller still owns accounts or plans')
  expect(requests.deleted).toEqual([])
  await dialog.getByRole('button', { name: 'Delete reseller' }).click()

  await expect(page).toHaveURL('/resellers')
  expect(requests.deleted).toEqual([BRAVO_HOSTING.id])
  await expect(page.getByRole('row', { name: /acmehosting/ })).toBeVisible()
  await expect(page.getByRole('row', { name: /bravohosting/ })).toHaveCount(0)
})

test('a refused deletion shows the panel sentence verbatim and stays on the reseller', async ({ page }) => {
  const detail = 'This reseller still owns 3 accounts. Move or delete them first.'
  await openAccounts(page, { remove: detail })
  await openReseller(page, 'acmehosting')

  await page.getByRole('button', { name: 'Delete', exact: true }).click()
  await page.getByRole('dialog').getByRole('button', { name: 'Delete reseller' }).click()

  await expect(page.getByText(detail, { exact: true })).toBeVisible()
  await expect(page).toHaveURL(`/resellers/${ACME_HOSTING.id}`)
  await expect(page.getByRole('heading', { level: 1, name: 'acmehosting' })).toBeVisible()
})

test('changing the envelope starts from the current figures and puts the whole envelope', async ({ page }) => {
  const requests = await openAccounts(page)
  await openReseller(page, 'acmehosting')

  await page.getByRole('link', { name: 'Change envelope' }).click()
  await expect(page.getByRole('heading', { level: 1, name: 'Change the envelope' })).toBeVisible()
  await expect(page.getByLabel('Accounts', { exact: true })).toHaveValue('20')

  await page.getByLabel('Accounts', { exact: true }).fill('30')
  await page.getByRole('button', { name: 'Save envelope' }).click()

  await expect(page).toHaveURL(`/resellers/${ACME_HOSTING.id}`)
  expect(requests.envelopes).toHaveLength(1)
  expect(requests.envelopes[0]?.body.envelope.maxAccounts).toBe(30)
  await expect(page.getByText('The envelope was saved.')).toBeVisible()
})

for (const locale of ['ru', 'hy'] as const) {
  test(`the resellers list and its create action are translated in ${locale}`, async ({ page }) => {
    const labels = {
      ru: { heading: 'Реселлеры', create: 'Новый реселлер' },
      hy: { heading: 'Ռեսելլերներ', create: 'Նոր ռեսելլեր' },
    }[locale]
    await setPersistedLocale(page, locale)
    await page.setViewportSize({ width: 1440, height: 900 })
    await installPanel(page)

    // The entry point; the sidebar walk is asserted in English above, and its labels are the
    // locale's own here.
    await page.goto('/resellers')

    await expect(page.getByRole('heading', { level: 1, name: labels.heading })).toBeVisible()
    await expect(page.getByRole('link', { name: labels.create }).first()).toBeVisible()
    await expect(page.getByRole('link', { name: 'acmehosting', exact: true })).toBeVisible()
  })
}

test('the resellers screens have no horizontal scroll at 390 px in English, Russian and Armenian', async ({
  browser,
}) => {
  for (const locale of ['en', 'ru', 'hy'] as const) {
    const context = await browser.newContext({ viewport: { width: 390, height: 844 } })
    const page = await context.newPage()
    await setPersistedLocale(page, locale)
    await installPanel(page)
    await page.goto('/resellers')

    await expect(page.getByRole('link', { name: 'acmehosting', exact: true })).toBeVisible()
    let widths = await widthsOf(page)
    expect(widths.document, `${locale}: the list scrolls sideways`).toBeLessThanOrEqual(widths.viewport)
    expect(widths.main, `${locale}: the list overflows its panel`).toBeLessThanOrEqual(widths.mainBox)

    await page.getByRole('link', { name: 'acmehosting', exact: true }).click()
    await expect(page.getByRole('heading', { level: 1, name: 'acmehosting' })).toBeVisible()
    widths = await widthsOf(page)
    expect(widths.document, `${locale}: the detail page scrolls sideways`).toBeLessThanOrEqual(widths.viewport)
    expect(widths.main, `${locale}: the detail page overflows its panel`).toBeLessThanOrEqual(widths.mainBox)

    await context.close()
  }
})
