import { expect, test, type Locator, type Page } from '@playwright/test'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubFirewall } from '../fixtures/stub-firewall-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import type { WhitelistEntry } from '../../src/types/firewall'
import type { PanelModule } from '../../src/types/module'

// The whitelist is server-paged (#196): the page, its size, the search and the sort live in the
// URL, the panel answers one page of exempt ranges and their total, and the SPA filters nothing.

const LICENSED: PanelModule[] = [{ name: 'firewall', displayName: 'Firewall', tier: 'included', isEnabled: true }]

/**
 * Builds a catalogue of numbered exemptions: `10.0.1.0/24` noted `site 1`, and so on, oldest first.
 * @param count How many exemptions.
 * @returns The exemptions, in the panel's default order.
 */
const catalogue = (count: number): WhitelistEntry[] => {
  return Array.from({ length: count }, (_, index): WhitelistEntry => {
    const day = String((index % 28) + 1).padStart(2, '0')
    return {
      id: `00000000-0000-0000-0000-0000000002${String(index).padStart(2, '0')}`,
      cidr: `10.0.${index + 1}.0/24`,
      note: `site ${index + 1}`,
      createdAt: `2026-08-${day}T10:00:00+00:00`,
    }
  })
}

/**
 * Signs in as an administrator and serves the given exemptions.
 * @param page The Playwright page.
 * @param whitelist The exemptions the panel reports.
 * @returns Resolves once the stubs are installed.
 */
const openWith = async (page: Page, whitelist: WhitelistEntry[]): Promise<void> => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubFirewall(page, { rules: [], bans: [], whitelist })
}

/**
 * The range cells on screen, in order.
 * @param page The Playwright page.
 * @returns A locator over the table's first cell of each body row.
 */
const rangeCells = (page: Page): Locator => {
  return page.getByRole('table').getByRole('row').filter({ has: page.getByRole('cell') }).locator('td:first-child')
}

test('the firewall sidebar section opens the whitelist page, which reads its own list', async ({ page }) => {
  await openWith(page, catalogue(2))

  await page.goto('/firewall')
  const nav = page.getByRole('navigation', { name: 'Main navigation' })
  await nav.getByRole('link', { name: 'Whitelist', exact: true }).click()

  await expect(page).toHaveURL(/\/firewall\/whitelist$/)
  await expect(page.getByRole('heading', { level: 1, name: 'Never banned' })).toBeVisible()
  await expect(rangeCells(page)).toHaveText(['10.0.1.0/24', '10.0.2.0/24'])
})

test('a whitelist search reaches the note as well as the range, and survives a reload', async ({ page }) => {
  await openWith(page, catalogue(12))

  await page.goto('/firewall/whitelist')
  await page.getByRole('searchbox', { name: 'Search exemptions' }).fill('site 12')

  await expect(page).toHaveURL(/\/firewall\/whitelist\?search=site\+12$/)
  await expect(rangeCells(page)).toHaveText(['10.0.12.0/24'])

  await page.reload()

  await expect(page.getByRole('searchbox', { name: 'Search exemptions' })).toHaveValue('site 12')
  await expect(rangeCells(page)).toHaveText(['10.0.12.0/24'])
})

test('the next whitelist page is reachable, and a page past the end moves back to the last', async ({ page }) => {
  await openWith(page, catalogue(30))

  await page.goto('/firewall/whitelist')
  await page.getByRole('navigation', { name: 'Pagination' }).getByRole('button', { name: 'Next page' }).click()
  await expect(page).toHaveURL(/\?page=2$/)
  await expect(rangeCells(page).first()).toHaveText('10.0.26.0/24')

  await page.goto('/firewall/whitelist?page=7')
  await expect(page).toHaveURL(/\?page=2$/)
  await expect(rangeCells(page)).toHaveCount(5)
})

test('the range heading sorts the whitelist on the server', async ({ page }) => {
  await openWith(page, catalogue(3))

  await page.goto('/firewall/whitelist?sort=cidr')
  const heading = page.getByRole('columnheader', { name: 'Range' })
  await expect(heading).toHaveAttribute('aria-sort', 'ascending')
  await heading.getByRole('button').click()

  await expect(page).toHaveURL(/\?sort=-cidr$/)
  await expect(rangeCells(page)).toHaveText(['10.0.3.0/24', '10.0.2.0/24', '10.0.1.0/24'])
})

test('a whitelist search matching nothing is the no-results state, not the empty state', async ({ page }) => {
  await openWith(page, catalogue(3))

  await page.goto('/firewall/whitelist?search=nowhere')

  await expect(page.getByText('No exemptions match')).toBeVisible()
  await expect(page.getByText('No exemptions', { exact: true })).toHaveCount(0)

  // The last of the two: the search box's own clear button carries the same name.
  await page.getByRole('button', { name: 'Clear the search' }).last().click()
  await expect(page).toHaveURL(/\/firewall\/whitelist$/)
  await expect(rangeCells(page)).toHaveCount(3)
})
