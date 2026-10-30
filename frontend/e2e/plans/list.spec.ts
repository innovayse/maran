import { expect, test, type Locator, type Page } from '@playwright/test'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import { PLANS_PATTERN, stubManagedPlans } from '../fixtures/stub-plans-route'
import type { ManagedPlan } from '../../src/types/plan'
import type { PanelModule } from '../../src/types/module'

// The plans screen as a server-paged list (#196): the URL is its state, and the panel searches,
// sorts and pages it.

/** The accounts module licensed, which is what owns plans. */
const LICENSED_ACCOUNTS: PanelModule[] = [
  { name: 'accounts', displayName: 'Accounts', tier: 'included', isEnabled: true },
]

/**
 * Builds numbered plans: `Tier01`, `Tier02` and so on, each selling more disk than the one before
 * and carrying fewer accounts, so the default order and the accounts order are each other's reverse.
 * @param count How many plans.
 * @returns The plans, smallest disk first.
 */
const catalogue = (count: number): ManagedPlan[] => {
  return Array.from({ length: count }, (_, index): ManagedPlan => {
    const number = String(index + 1).padStart(2, '0')
    return {
      id: `00000000-0000-4000-8000-0000000000${number}`,
      displayName: `Tier${number}`,
      diskQuotaMb: (index + 1) * 1024,
      maxSites: 5,
      maxDatabases: 2,
      maxSftpUsers: 3,
      maxFtpUsers: 1,
      maxCronEntries: 1,
      maxPhpWorkersPerPool: 5,
      accountCount: count - index,
    }
  })
}

/**
 * Signs in, serves the given plans, and records every list request the page makes.
 * @param page The Playwright page.
 * @param plans The plans the panel reports.
 * @returns Every list request the page made so far.
 */
const openWith = async (page: Page, plans: ManagedPlan[]): Promise<() => readonly URL[]> => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED_ACCOUNTS)
  await stubManagedPlans(page, plans)
  const seen: URL[] = []
  page.on('request', (request) => {
    if (request.method() === 'GET' && PLANS_PATTERN.test(request.url())) {
      seen.push(new URL(request.url()))
    }
  })
  return (): readonly URL[] => {
    return seen
  }
}

/**
 * The plan rows on screen, header excluded.
 * @param page The Playwright page.
 * @returns A locator over the body rows of the plans table.
 */
const planRows = (page: Page): Locator => {
  return page.getByRole('table', { name: 'Plans and their limits' }).getByRole('row').filter({ hasText: /Tier\d\d/ })
}

test('a search reaches the URL, asks the panel, and survives a reload', async ({ page }) => {
  const requests = await openWith(page, catalogue(30))

  await page.goto('/plans')
  await page.getByRole('searchbox', { name: 'Search plans' }).fill('tier2')

  await expect(page).toHaveURL(/\/plans\?search=tier2$/)
  await expect(planRows(page)).toHaveCount(10)
  expect(requests().at(-1)?.searchParams.get('search')).toBe('tier2')

  await page.reload()

  await expect(page.getByRole('searchbox', { name: 'Search plans' })).toHaveValue('tier2')
  await expect(planRows(page)).toHaveCount(10)
  await expect(planRows(page).first()).toContainText('Tier20')
})

test('an empty search or sort is never sent to the panel', async ({ page }) => {
  const requests = await openWith(page, catalogue(3))

  await page.goto('/plans')
  await expect(planRows(page)).toHaveCount(3)

  const first = requests()[0]
  expect(first?.searchParams.get('page')).toBe('1')
  expect(first?.searchParams.get('pageSize')).toBe('25')
  expect(first?.searchParams.has('search')).toBe(false)
  expect(first?.searchParams.has('sort')).toBe(false)
})

test('the next page is reachable and is its own URL', async ({ page }) => {
  await openWith(page, catalogue(30))

  await page.goto('/plans')
  const nav = page.getByRole('navigation', { name: 'Pagination' })
  await expect(nav).toContainText('Showing 1–25 of 30')
  await expect(planRows(page).first()).toContainText('Tier01')

  await nav.getByRole('button', { name: 'Next page' }).click()

  await expect(page).toHaveURL(/\/plans\?page=2$/)
  await expect(planRows(page)).toHaveCount(5)
  await expect(planRows(page).first()).toContainText('Tier26')
  await expect(nav).toContainText('Showing 26–30 of 30')
})

test('the accounts heading sorts the list on the server and says so', async ({ page }) => {
  const requests = await openWith(page, catalogue(3))

  await page.goto('/plans')
  const heading = page.getByRole('columnheader', { name: 'Accounts' })
  await heading.getByRole('button').click()

  await expect(page).toHaveURL(/\/plans\?sort=accounts$/)
  await expect(heading).toHaveAttribute('aria-sort', 'ascending')
  // Tier03 carries the fewest accounts.
  await expect(planRows(page).first()).toContainText('Tier03')
  expect(requests().at(-1)?.searchParams.get('sort')).toBe('accounts')

  await heading.getByRole('button').click()
  await expect(page).toHaveURL(/\/plans\?sort=-accounts$/)
  await expect(heading).toHaveAttribute('aria-sort', 'descending')
  await expect(planRows(page).first()).toContainText('Tier01')
})

test('the name and disk headings sort too, and the limits that do not are plain headings', async ({ page }) => {
  await openWith(page, catalogue(3))

  await page.goto('/plans')

  await expect(page.getByRole('columnheader', { name: 'Name' }).getByRole('button')).toBeVisible()
  await expect(page.getByRole('columnheader', { name: 'Disk', exact: true }).getByRole('button')).toBeVisible()
  await expect(page.getByRole('columnheader', { name: 'Sites', exact: true }).getByRole('button')).toHaveCount(0)
})

test('a search matching nothing is the no-results state, not the empty state', async ({ page }) => {
  await openWith(page, catalogue(3))

  await page.goto('/plans?search=nobody')

  await expect(page.getByText('No plans match')).toBeVisible()
  await expect(page.getByText('No plans yet')).toHaveCount(0)
  await expect(page.getByRole('searchbox', { name: 'Search plans' })).toBeVisible()

  await page.getByRole('button', { name: 'Clear the search' }).last().click()
  await expect(page).toHaveURL(/\/plans$/)
  await expect(planRows(page)).toHaveCount(3)
})
