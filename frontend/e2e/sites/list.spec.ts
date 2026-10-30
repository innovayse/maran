import { expect, test, type Locator, type Page } from '@playwright/test'
import { stubAccountNames, stubAccounts } from '../fixtures/stub-accounts-route'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubPhpVersions, stubSites, stubSitesProblem } from '../fixtures/stub-sites-routes'
import type { Account } from '../../src/types/account'
import type { PanelModule } from '../../src/types/module'
import type { Site } from '../../src/types/site'

// The sites list is server-paged (#196): the page, its size, the search, the sort and the filters
// live in the URL, the panel answers one page, the total and each row's owner, and the SPA filters
// nothing.

const LICENSED_SITES: PanelModule[] = [
  { name: 'sites', displayName: 'Sites', tier: 'included', isEnabled: true },
]

const PHONE = { width: 390, height: 844 }
const DESKTOP = { width: 1440, height: 900 }

const ALICE: Account = {
  id: '22222222-2222-2222-2222-222222222222',
  name: 'alice',
  primaryDomain: 'alice.example.com',
  planId: '44444444-4444-4444-4444-444444444444',
  status: 'active',
  createdAt: '2026-08-01T10:00:00Z',
}

const BOB: Account = { ...ALICE, id: '55555555-5555-5555-5555-555555555555', name: 'bob', primaryDomain: 'bob.example.com' }

const ALPHA: Site = {
  id: '11111111-1111-1111-1111-111111111111',
  accountId: ALICE.id,
  ownerName: ALICE.name,
  domain: 'alpha.example.com',
  backendType: 'php',
  phpVersion: '8.3',
  status: 'enabled',
  createdAt: '2026-08-01T10:00:00Z',
}

const BETA: Site = {
  id: '33333333-3333-3333-3333-333333333333',
  accountId: BOB.id,
  ownerName: BOB.name,
  domain: 'beta.example.com',
  backendType: 'static',
  phpVersion: '',
  status: 'disabled',
  createdAt: '2026-08-02T10:00:00Z',
}

/**
 * Builds a catalogue of numbered sites: `site-01.example.com` and so on, alternating between
 * alice and bob, every fourth one static and every fifth one disabled.
 * @param count How many sites.
 * @returns The sites, in domain order.
 */
const catalogue = (count: number): Site[] => {
  return Array.from({ length: count }, (_, index): Site => {
    const number = String(index + 1).padStart(2, '0')
    const owner = index % 2 === 0 ? ALICE : BOB
    const isStatic = index % 4 === 3
    return {
      id: `00000000-0000-0000-0000-0000000000${number}`,
      accountId: owner.id,
      ownerName: owner.name,
      domain: `site-${number}.example.com`,
      backendType: isStatic ? 'static' : 'php',
      phpVersion: isStatic ? '' : '8.3',
      status: index % 5 === 4 ? 'disabled' : 'enabled',
      createdAt: `2026-08-${number}T10:00:00Z`,
    }
  })
}

/**
 * Signs in as the administrator and serves the given sites, two accounts and one PHP version.
 * @param page The Playwright page.
 * @param sites The sites the panel reports.
 * @returns Every list request the page made so far.
 */
const openWith = async (page: Page, sites: Site[]): Promise<() => readonly URL[]> => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED_SITES)
  await stubAccounts(page, [ALICE, BOB])
  await stubAccountNames(page, [ALICE, BOB])
  const requests = await stubSites(page, sites)
  await stubPhpVersions(page, [{ version: '8.3', isDefault: true }])
  return requests
}

/**
 * The domain links on screen, in order.
 * @param page The Playwright page.
 * @returns A locator over the table's domain links.
 */
const domainLinks = (page: Page): Locator => {
  return page.getByRole('table').getByRole('link')
}

test('sites list shows the empty state when the panel reports no sites', async ({ page }) => {
  await openWith(page, [])

  await page.goto('/sites')

  await expect(page.getByText('No sites yet')).toBeVisible()
  await expect(page.getByRole('table')).toHaveCount(0)
  await expect(page.getByRole('searchbox', { name: 'Search sites' })).toHaveCount(0)
})

test('sites list renders a row per site with its owner, backend and serving status', async ({ page }) => {
  await openWith(page, [ALPHA, BETA])

  await page.goto('/sites')

  const rows = page.getByRole('row')
  await expect(rows.filter({ hasText: 'alpha.example.com' })).toContainText('Enabled')
  await expect(rows.filter({ hasText: 'alpha.example.com' })).toContainText('8.3')
  await expect(rows.filter({ hasText: 'alpha.example.com' })).toContainText('alice')
  await expect(rows.filter({ hasText: 'beta.example.com' })).toContainText('Disabled')
  await expect(rows.filter({ hasText: 'beta.example.com' })).toContainText('Static')
  await expect(rows.filter({ hasText: 'beta.example.com' })).toContainText('bob')
  await expect(page.getByRole('columnheader', { name: 'Owner' })).toBeVisible()
})

// A screen nothing links to is a screen nobody has: the domain is the only way into the detail
// page, and every action a site has lives there.
test('the site domain links to that site detail page', async ({ page }) => {
  await openWith(page, [ALPHA])

  await page.goto('/sites')
  await page.getByRole('link', { name: 'alpha.example.com' }).click()

  await expect(page).toHaveURL(new RegExp(`/sites/${ALPHA.id}$`))
})

// The other way in: the sidebar entry the module catalogue drives.
test('the sidebar links to the sites list when the panel licenses the sites module', async ({ page }) => {
  await openWith(page, [])

  await page.goto('/')
  await page.getByRole('navigation').getByRole('link', { name: 'Sites' }).click()

  await expect(page).toHaveURL(/\/sites$/)
  await expect(page.getByRole('heading', { name: 'Sites' })).toBeVisible()
})

// rules/vue.md: "Error messages are produced by the backend, already localized, and rendered as-is."
test('sites list renders the backend RFC 7807 detail verbatim when the list request fails', async ({
  page,
}) => {
  const backendDetail = 'The sites store is temporarily unavailable. Try again in a moment.'
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED_SITES)
  await stubSitesProblem(page, backendDetail)

  await page.goto('/sites')

  await expect(page.getByRole('status').filter({ hasText: backendDetail })).toBeVisible()
  await expect(page.getByRole('table')).toHaveCount(0)
  await expect(page.getByText('No sites yet')).toHaveCount(0)
})

test('the disabled notice counts every page, not the rows on screen', async ({ page }) => {
  await openWith(page, catalogue(30))

  await page.goto('/sites?pageSize=10')

  // site-05, -10, -15, -20, -25 and -30 are disabled; only site-05 and site-10 are on page 1.
  await expect(page.getByText('6 sites are disabled.', { exact: false })).toBeVisible()
})

test('a search reaches the URL, asks the panel, and survives a reload', async ({ page }) => {
  const requests = await openWith(page, catalogue(30))

  await page.goto('/sites')
  await page.getByRole('searchbox', { name: 'Search sites' }).fill('site-1')

  await expect(page).toHaveURL(/\/sites\?search=site-1$/)
  await expect(domainLinks(page)).toHaveCount(10)
  expect(
    requests().some((url) => {
      return url.searchParams.get('search') === 'site-1'
    }),
  ).toBe(true)

  await page.reload()

  await expect(page.getByRole('searchbox', { name: 'Search sites' })).toHaveValue('site-1')
  await expect(domainLinks(page).first()).toHaveText('site-10.example.com')
})

test('the next and last pages are reachable, and a new page size goes back to the first', async ({ page }) => {
  await openWith(page, catalogue(60))

  await page.goto('/sites')
  const nav = page.getByRole('navigation', { name: 'Pagination' })
  await nav.getByRole('button', { name: 'Next page' }).click()
  await expect(page).toHaveURL(/\?page=2$/)
  await expect(domainLinks(page).first()).toHaveText('site-26.example.com')

  await nav.getByRole('button', { name: 'Last page' }).click()
  await expect(page).toHaveURL(/\?page=3$/)
  await expect(domainLinks(page)).toHaveCount(10)

  await nav.getByRole('combobox', { name: 'Rows per page' }).click()
  await page.getByRole('option', { name: '10', exact: true }).click()
  await expect(page).toHaveURL(/\?pageSize=10$/)
  await expect(domainLinks(page).first()).toHaveText('site-01.example.com')
})

test('a page past the end moves to the last page that has rows', async ({ page }) => {
  await openWith(page, catalogue(30))

  await page.goto('/sites?page=5')

  await expect(page).toHaveURL(/\?page=2$/)
  await expect(domainLinks(page)).toHaveCount(5)
})

test('Back from a site returns to the page of the list it was opened from', async ({ page }) => {
  await openWith(page, catalogue(30))

  await page.goto('/sites?page=2')
  await domainLinks(page).first().click()
  await expect(page).toHaveURL(/\/sites\/00000000-0000-0000-0000-000000000026$/)

  await page.goBack()

  await expect(page).toHaveURL(/\/sites\?page=2$/)
  await expect(domainLinks(page).first()).toHaveText('site-26.example.com')
})

test('a column heading sorts the list on the server', async ({ page }) => {
  const requests = await openWith(page, catalogue(3))

  await page.goto('/sites?sort=domain')
  const heading = page.getByRole('columnheader', { name: 'Domain' })
  await expect(heading).toHaveAttribute('aria-sort', 'ascending')
  await heading.getByRole('button').click()

  await expect(page).toHaveURL(/\?sort=-domain$/)
  await expect(domainLinks(page).first()).toHaveText('site-03.example.com')
  expect(requests().at(-1)?.searchParams.get('sort')).toBe('-domain')
})

test('a search matching nothing is the no-results state, not the empty state', async ({ page }) => {
  await openWith(page, catalogue(3))

  await page.goto('/sites?search=nowhere')

  await expect(page.getByText('No sites match')).toBeVisible()
  await expect(page.getByText('No sites yet')).toHaveCount(0)

  await page.getByRole('button', { name: 'Clear search and filters' }).click()
  await expect(page).toHaveURL(/\/sites$/)
  await expect(domainLinks(page)).toHaveCount(3)
})

test('filters applied in the sheet become removable chips', async ({ page }) => {
  const requests = await openWith(page, catalogue(8))

  await page.goto('/sites')
  await page.getByTestId('filter-trigger').click()
  const dialog = page.getByRole('dialog', { name: 'Filters' })
  await dialog.getByRole('combobox', { name: 'Account' }).click()
  await page.getByRole('option', { name: 'bob' }).click()
  await dialog.getByRole('combobox', { name: 'Backend' }).click()
  await page.getByRole('option', { name: 'Static' }).click()
  await dialog.getByRole('button', { name: 'Apply' }).click()

  await expect(page).toHaveURL(new RegExp(`account=${BOB.id}`))
  // The list read carries both filters; the disabled count read beside it carries the account only.
  expect(
    requests().some((url) => {
      return url.searchParams.get('accountId') === BOB.id && url.searchParams.get('backendType') === 'static'
    }),
  ).toBe(true)
  // site-04 and site-08 are bob's static sites.
  await expect(domainLinks(page)).toHaveText(['site-04.example.com', 'site-08.example.com'])
  const chips = page.getByTestId('active-filter-chips')
  await expect(chips).toContainText('Account: bob')
  await expect(chips).toContainText('Backend: Static')

  await page.getByRole('button', { name: 'Remove filter: Backend: Static' }).click()
  await expect(page).not.toHaveURL(/backend=/)
  await expect(domainLinks(page)).toHaveCount(4)

  await page.getByRole('button', { name: 'Clear all' }).click()
  await expect(page).toHaveURL(/\/sites$/)
  await expect(domainLinks(page)).toHaveCount(8)
})

test('at 390px the filters open as a bottom sheet', async ({ page }) => {
  await page.setViewportSize(PHONE)
  await openWith(page, catalogue(3))

  await page.goto('/sites')
  await page.getByTestId('filter-trigger').click()

  const sheet = page.getByTestId('filter-sheet')
  await expect(sheet).toHaveAttribute('role', 'dialog')
  await expect(sheet.getByRole('combobox', { name: 'Status' })).toBeVisible()
})

test('at 1440px the filters open as the centred dialog', async ({ page }) => {
  await page.setViewportSize(DESKTOP)
  await openWith(page, catalogue(3))

  await page.goto('/sites')
  await page.getByTestId('filter-trigger').click()

  await expect(page.getByTestId('filter-sheet')).toHaveCount(0)
  await expect(page.getByRole('dialog', { name: 'Filters' }).getByRole('combobox', { name: 'Status' })).toBeVisible()
})
