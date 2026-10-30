import { expect, test, type Locator, type Page } from '@playwright/test'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import { stubAccounts, stubAccountsProblem } from '../fixtures/stub-accounts-route'
import { stubPlans } from '../fixtures/stub-plans-route'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import type { Account, Plan } from '../../src/types/account'
import type { PanelModule } from '../../src/types/module'

// The accounts list is server-paged (#196): the page, its size, the search, the sort and the
// filters live in the URL, the panel answers one page and the total, and the SPA filters nothing.

/** Catalogue every spec here starts from: the accounts module licensed, so the route is reachable. */
const LICENSED_ACCOUNTS: PanelModule[] = [
  { name: 'accounts', displayName: 'Accounts', tier: 'included', isEnabled: true },
]

const PHONE = { width: 390, height: 844 }
const DESKTOP = { width: 1440, height: 900 }

const STARTER: Plan = {
  id: '22222222-2222-2222-2222-222222222222',
  displayName: 'Starter',
  diskQuotaMb: 1024,
  maxSites: 5,
  maxDatabases: 5,
  maxSftpUsers: 5,
}

const BUSINESS: Plan = { ...STARTER, id: '44444444-4444-4444-4444-444444444444', displayName: 'Business' }

/**
 * Builds a catalogue of numbered accounts: `acct-01`, `acct-02` and so on, every third one
 * suspended and every other one on the business plan, so search, filters and pages all have
 * something to narrow.
 * @param count How many accounts.
 * @returns The accounts, in name order.
 */
const catalogue = (count: number): Account[] => {
  return Array.from({ length: count }, (_, index): Account => {
    const number = String(index + 1).padStart(2, '0')
    return {
      id: `00000000-0000-0000-0000-0000000000${number}`,
      name: `acct-${number}`,
      primaryDomain: `acct-${number}.example.com`,
      planId: index % 2 === 0 ? STARTER.id : BUSINESS.id,
      status: index % 3 === 2 ? 'suspended' : 'active',
      createdAt: `2026-08-${number}T10:00:00Z`,
    }
  })
}

/**
 * Signs in and serves the given accounts and both plans.
 * @param page The Playwright page.
 * @param accounts The accounts the panel reports.
 * @returns Every list request the page made so far.
 */
const openWith = async (page: Page, accounts: Account[]): Promise<() => readonly URL[]> => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED_ACCOUNTS)
  await stubPlans(page, [STARTER, BUSINESS])
  return await stubAccounts(page, accounts)
}

/**
 * The account-name links on screen, in order.
 * @param page The Playwright page.
 * @returns A locator over the table's name links.
 */
const nameLinks = (page: Page): Locator => {
  return page.getByRole('table').getByRole('link')
}

test('accounts list shows the empty state when the panel reports no accounts', async ({ page }) => {
  await openWith(page, [])

  await page.goto('/accounts')

  await expect(page.getByText('No accounts yet')).toBeVisible()
  await expect(page.getByText('Create the first hosting account to get started.')).toBeVisible()
  await expect(page.getByRole('table')).toHaveCount(0)
  await expect(page.getByRole('searchbox', { name: 'Search accounts' })).toHaveCount(0)
})

test('accounts list renders a row per account with its backend-reported status', async ({ page }) => {
  await openWith(page, catalogue(3))

  await page.goto('/accounts')

  const rows = page.getByRole('row')
  await expect(rows.filter({ hasText: 'acct-01.example.com' })).toContainText('Active')
  await expect(rows.filter({ hasText: 'acct-03.example.com' })).toContainText('Suspended')
  await expect(page.getByRole('cell', { name: 'acct-01', exact: true })).toBeVisible()
})

// rules/vue.md: "Error messages are produced by the backend, already localized, and rendered
// as-is." A failed list load must show the server's `detail`, and nothing the SPA made up.
test('accounts list renders the backend RFC 7807 detail verbatim when the list request fails', async ({
  page,
}) => {
  await stubSignedIn(page)
  const backendDetail = 'The accounts store is temporarily unavailable. Try again in a moment.'
  await stubHealthy(page)
  await stubModules(page, LICENSED_ACCOUNTS)
  await stubAccountsProblem(page, backendDetail)

  await page.goto('/accounts')

  await expect(page.getByRole('status').filter({ hasText: backendDetail })).toBeVisible()
  await expect(page.getByRole('table')).toHaveCount(0)
  await expect(page.getByText('No accounts yet')).toHaveCount(0)
})

test('a search reaches the URL, asks the panel, and survives a reload', async ({ page }) => {
  const requests = await openWith(page, catalogue(30))

  await page.goto('/accounts')
  await page.getByRole('searchbox', { name: 'Search accounts' }).fill('acct-2')

  await expect(page).toHaveURL(/\/accounts\?search=acct-2$/)
  await expect(nameLinks(page)).toHaveCount(10)
  expect(requests().at(-1)?.searchParams.get('search')).toBe('acct-2')

  await page.reload()

  await expect(page.getByRole('searchbox', { name: 'Search accounts' })).toHaveValue('acct-2')
  await expect(nameLinks(page)).toHaveCount(10)
  await expect(nameLinks(page).first()).toHaveText('acct-20')
})

test('an empty search, sort or filter is never sent to the panel', async ({ page }) => {
  const requests = await openWith(page, catalogue(3))

  await page.goto('/accounts')
  await expect(nameLinks(page)).toHaveCount(3)

  const first = requests()[0]
  expect(first?.searchParams.get('page')).toBe('1')
  expect(first?.searchParams.get('pageSize')).toBe('25')
  expect(first?.searchParams.has('search')).toBe(false)
  expect(first?.searchParams.has('sort')).toBe(false)
  expect(first?.searchParams.has('planId')).toBe(false)
  expect(first?.searchParams.has('status')).toBe(false)
})

test('the next and last pages are reachable and each is its own URL', async ({ page }) => {
  await openWith(page, catalogue(60))

  await page.goto('/accounts')
  const nav = page.getByRole('navigation', { name: 'Pagination' })
  await expect(nav).toContainText('Showing 1–25 of 60')

  await nav.getByRole('button', { name: 'Next page' }).click()
  await expect(page).toHaveURL(/\?page=2$/)
  await expect(nameLinks(page).first()).toHaveText('acct-26')

  await nav.getByRole('button', { name: 'Last page' }).click()
  await expect(page).toHaveURL(/\?page=3$/)
  await expect(nameLinks(page)).toHaveCount(10)
  await expect(nav).toContainText('Showing 51–60 of 60')
})

test('a new page size goes back to the first page', async ({ page }) => {
  await openWith(page, catalogue(60))

  await page.goto('/accounts?page=3')
  const nav = page.getByRole('navigation', { name: 'Pagination' })
  await nav.getByRole('combobox', { name: 'Rows per page' }).click()
  await page.getByRole('option', { name: '50', exact: true }).click()

  await expect(page).toHaveURL(/\?pageSize=50$/)
  await expect(nameLinks(page)).toHaveCount(50)
  await expect(nameLinks(page).first()).toHaveText('acct-01')
})

test('a page past the end moves to the last page that has rows', async ({ page }) => {
  await openWith(page, catalogue(30))

  await page.goto('/accounts?page=9')

  await expect(page).toHaveURL(/\?page=2$/)
  await expect(nameLinks(page)).toHaveCount(5)
})

test('Back from an account returns to the page of the list it was opened from', async ({ page }) => {
  await openWith(page, catalogue(30))

  await page.goto('/accounts?page=2&search=acct')
  await expect(nameLinks(page).first()).toHaveText('acct-26')
  await nameLinks(page).first().click()
  await expect(page).toHaveURL(/\/accounts\/00000000-0000-0000-0000-000000000026$/)

  await page.goBack()

  await expect(page).toHaveURL(/\/accounts\?page=2&search=acct$/)
  await expect(page.getByRole('searchbox', { name: 'Search accounts' })).toHaveValue('acct')
  await expect(nameLinks(page).first()).toHaveText('acct-26')
})

test('a column heading sorts the list on the server and says so', async ({ page }) => {
  const requests = await openWith(page, catalogue(3))

  await page.goto('/accounts')
  const heading = page.getByRole('columnheader', { name: 'Name' })
  await heading.getByRole('button').click()
  await expect(page).toHaveURL(/\?sort=name$/)
  await expect(heading).toHaveAttribute('aria-sort', 'ascending')

  await heading.getByRole('button').click()
  await expect(page).toHaveURL(/\?sort=-name$/)
  await expect(heading).toHaveAttribute('aria-sort', 'descending')
  await expect(nameLinks(page).first()).toHaveText('acct-03')
  expect(requests().at(-1)?.searchParams.get('sort')).toBe('-name')
})

test('a search matching nothing is the no-results state, not the empty state', async ({ page }) => {
  await openWith(page, catalogue(3))

  await page.goto('/accounts?search=nobody')

  await expect(page.getByText('No accounts match')).toBeVisible()
  await expect(page.getByText('No accounts yet')).toHaveCount(0)
  await expect(page.getByRole('searchbox', { name: 'Search accounts' })).toBeVisible()

  await page.getByRole('button', { name: 'Clear search and filters' }).click()
  await expect(page).toHaveURL(/\/accounts$/)
  await expect(nameLinks(page)).toHaveCount(3)
})

test('filters applied in the sheet become removable chips', async ({ page }) => {
  const requests = await openWith(page, catalogue(9))

  await page.goto('/accounts')
  await page.getByTestId('filter-trigger').click()
  const dialog = page.getByRole('dialog', { name: 'Filters' })
  await dialog.getByRole('combobox', { name: 'Plan' }).click()
  await page.getByRole('option', { name: 'Starter' }).click()
  await dialog.getByRole('combobox', { name: 'Status' }).click()
  await page.getByRole('option', { name: 'Suspended' }).click()
  await dialog.getByRole('button', { name: 'Apply' }).click()

  await expect(page).toHaveURL(new RegExp(`plan=${STARTER.id}`))
  await expect(page).toHaveURL(/status=suspended/)
  expect(requests().at(-1)?.searchParams.get('planId')).toBe(STARTER.id)
  // acct-03 and acct-09 are the suspended accounts on the starter plan.
  await expect(nameLinks(page)).toHaveText(['acct-03', 'acct-09'])
  const chips = page.getByTestId('active-filter-chips')
  await expect(chips).toContainText('Plan: Starter')
  await expect(chips).toContainText('Status: Suspended')

  await page.getByRole('button', { name: 'Remove filter: Status: Suspended' }).click()
  await expect(page).not.toHaveURL(/status=/)
  await expect(chips).not.toContainText('Status')
  await expect(nameLinks(page)).toHaveCount(5)

  await page.getByRole('button', { name: 'Clear all' }).click()
  await expect(page).toHaveURL(/\/accounts$/)
  await expect(page.getByTestId('active-filter')).toHaveCount(0)
})

test('the plan link from the plans screen arrives as a removable chip', async ({ page }) => {
  await openWith(page, catalogue(4))

  await page.goto(`/accounts?plan=${BUSINESS.id}`)

  await expect(page.getByTestId('active-filter-chips')).toContainText('Plan: Business')
  await expect(nameLinks(page)).toHaveText(['acct-02', 'acct-04'])
})

test('at 390px the filters open as a bottom sheet', async ({ page }) => {
  await page.setViewportSize(PHONE)
  await openWith(page, catalogue(3))

  await page.goto('/accounts')
  await page.getByTestId('filter-trigger').click()

  const sheet = page.getByTestId('filter-sheet')
  await expect(sheet).toHaveAttribute('role', 'dialog')
  await expect(sheet.getByRole('combobox', { name: 'Plan' })).toBeVisible()
})

test('at 1440px the filters open as the centred dialog', async ({ page }) => {
  await page.setViewportSize(DESKTOP)
  await openWith(page, catalogue(3))

  await page.goto('/accounts')
  await page.getByTestId('filter-trigger').click()

  await expect(page.getByTestId('filter-sheet')).toHaveCount(0)
  await expect(page.getByRole('dialog', { name: 'Filters' }).getByRole('combobox', { name: 'Plan' })).toBeVisible()
})
