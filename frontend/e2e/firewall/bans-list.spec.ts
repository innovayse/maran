import { expect, test, type Locator, type Page } from '@playwright/test'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubFirewall } from '../fixtures/stub-firewall-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import type { Ban } from '../../src/types/firewall'
import type { PanelModule } from '../../src/types/module'

// The bans list is server-paged (#196): the page, its size, the search and the sort live in the
// URL, the panel answers one page of the bans in force and their total, and the SPA filters nothing.

const LICENSED: PanelModule[] = [{ name: 'firewall', displayName: 'Firewall', tier: 'included', isEnabled: true }]

/**
 * Builds a catalogue of numbered bans: `198.51.100.1`, `198.51.100.2` and so on, newest first, each
 * placed a minute before the one listed above it.
 * @param count How many bans.
 * @returns The bans, in the panel's default order.
 */
const catalogue = (count: number): Ban[] => {
  return Array.from({ length: count }, (_, index): Ban => {
    const minute = String(59 - (index % 60)).padStart(2, '0')
    return {
      id: `00000000-0000-0000-0000-0000000001${String(index).padStart(2, '0')}`,
      ipAddress: `198.51.100.${index + 1}`,
      reason: 'bruteForce',
      failures: 10,
      bannedAt: `2026-09-01T11:${minute}:00+00:00`,
      expiresAt: null,
    }
  })
}

/**
 * Signs in as an administrator and serves the given bans.
 * @param page The Playwright page.
 * @param bans The bans the panel reports.
 * @returns Resolves once the stubs are installed.
 */
const openWith = async (page: Page, bans: Ban[]): Promise<void> => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubFirewall(page, { rules: [], bans, whitelist: [] })
}

/**
 * The address cells on screen, in order.
 * @param page The Playwright page.
 * @returns A locator over the table's first cell of each body row.
 */
const addressCells = (page: Page): Locator => {
  return page.getByRole('table').getByRole('row').filter({ has: page.getByRole('cell') }).locator('td:first-child')
}

test('the firewall sidebar section opens the bans page, which reads its own list', async ({ page }) => {
  await openWith(page, catalogue(2))

  await page.goto('/firewall')
  const nav = page.getByRole('navigation', { name: 'Main navigation' })
  await nav.getByRole('link', { name: 'Bans', exact: true }).click()

  await expect(page).toHaveURL(/\/firewall\/bans$/)
  await expect(page.getByRole('heading', { level: 1, name: 'Bans' })).toBeVisible()
  await expect(addressCells(page)).toHaveText(['198.51.100.1', '198.51.100.2'])
})

test('a ban search reaches the URL, asks the panel, and survives a reload', async ({ page }) => {
  const requests: URL[] = []
  await openWith(page, catalogue(30))
  page.on('request', (request) => {
    if (request.method() === 'GET' && request.url().includes('/api/v1/firewall/bans')) {
      requests.push(new URL(request.url()))
    }
  })

  await page.goto('/firewall/bans')
  await page.getByRole('searchbox', { name: 'Search bans' }).fill('100.2')

  await expect(page).toHaveURL(/\/firewall\/bans\?search=100\.2$/)
  await expect(addressCells(page)).toHaveCount(11)
  expect(
    requests.some((url) => {
      return url.searchParams.get('search') === '100.2'
    }),
  ).toBe(true)

  await page.reload()

  await expect(page.getByRole('searchbox', { name: 'Search bans' })).toHaveValue('100.2')
  await expect(addressCells(page).first()).toHaveText('198.51.100.2')
})

test('the next and last ban pages are reachable, and a new page size goes back to the first', async ({ page }) => {
  await openWith(page, catalogue(60))

  await page.goto('/firewall/bans')
  const nav = page.getByRole('navigation', { name: 'Pagination' })
  await nav.getByRole('button', { name: 'Next page' }).click()
  await expect(page).toHaveURL(/\?page=2$/)
  await expect(addressCells(page).first()).toHaveText('198.51.100.26')

  await nav.getByRole('button', { name: 'Last page' }).click()
  await expect(page).toHaveURL(/\?page=3$/)
  await expect(addressCells(page)).toHaveCount(10)

  await nav.getByRole('combobox', { name: 'Rows per page' }).click()
  await page.getByRole('option', { name: '10', exact: true }).click()
  await expect(page).toHaveURL(/\?pageSize=10$/)
  await expect(addressCells(page).first()).toHaveText('198.51.100.1')
})

test('a ban page past the end moves to the last page that has rows', async ({ page }) => {
  await openWith(page, catalogue(30))

  await page.goto('/firewall/bans?page=5')

  await expect(page).toHaveURL(/\?page=2$/)
  await expect(addressCells(page)).toHaveCount(5)
})

test('the address heading sorts the bans on the server', async ({ page }) => {
  await openWith(page, catalogue(3))

  await page.goto('/firewall/bans?sort=address')
  const heading = page.getByRole('columnheader', { name: 'Address' })
  await expect(heading).toHaveAttribute('aria-sort', 'ascending')
  await heading.getByRole('button').click()

  await expect(page).toHaveURL(/\?sort=-address$/)
  await expect(addressCells(page)).toHaveText(['198.51.100.3', '198.51.100.2', '198.51.100.1'])
})

test('a ban search matching nothing is the no-results state, not the empty state', async ({ page }) => {
  await openWith(page, catalogue(3))

  await page.goto('/firewall/bans?search=nowhere')

  await expect(page.getByText('No bans match')).toBeVisible()
  await expect(page.getByText('Nobody is banned')).toHaveCount(0)

  // The last of the two: the search box's own clear button carries the same name.
  await page.getByRole('button', { name: 'Clear the search' }).last().click()
  await expect(page).toHaveURL(/\/firewall\/bans$/)
  await expect(addressCells(page)).toHaveCount(3)
})

// After a lift the page asks the panel again rather than dropping the row itself: the panel decides
// which episodes the lift covered, and the total moves.
test('lifting a ban reads the page again from the panel', async ({ page }) => {
  await openWith(page, catalogue(2))

  await page.goto('/firewall/bans')
  const row = page.getByRole('row').filter({ hasText: '198.51.100.1' })
  await row.getByRole('button', { name: 'Actions for 198.51.100.1' }).click()
  await page.getByRole('menuitem', { name: 'Lift the ban' }).click()
  await page
    .getByRole('dialog', { name: 'Lift the ban on 198.51.100.1' })
    .getByRole('button', { name: 'Yes, lift it' })
    .click()

  await expect(addressCells(page)).toHaveText(['198.51.100.2'])
})
