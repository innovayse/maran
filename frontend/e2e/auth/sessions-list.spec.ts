import { expect, test, type Locator, type Page } from '@playwright/test'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubEmptyModules } from '../fixtures/stub-modules-route'
import { stubSessions } from '../fixtures/stub-sessions-route'
import type { Session } from '../../src/types/auth'

// The signed-in devices list is server-paged (#196): the page, its size, the search and the sort
// live in the URL, the panel answers one page of the caller's own sessions and their total, and the
// SPA filters nothing. Whose sessions they are is the token's, never the request's — that half is
// the backend's to prove (SessionListEndpointTests); this spec proves the page asks and renders.

/**
 * Builds a catalogue of numbered devices, newest first; the first is the one making the request.
 * @param count How many sessions.
 * @returns The sessions, in the panel's default order.
 */
const catalogue = (count: number): Session[] => {
  return Array.from({ length: count }, (_, index): Session => {
    const number = String(index + 1).padStart(2, '0')
    const day = String(30 - (index % 28)).padStart(2, '0')
    return {
      id: `00000000-0000-0000-0000-0000000003${number}`,
      issuedAt: `2026-08-${day}T09:00:00+00:00`,
      expiresAt: `2026-09-${String(13 - (index % 12)).padStart(2, '0')}T09:00:00+00:00`,
      ipAddress: `203.0.113.${index + 1}`,
      userAgent: `Device ${number}`,
      isCurrent: index === 0,
    }
  })
}

/**
 * Signs in and serves the given sessions.
 * @param page The Playwright page.
 * @param sessions The devices the panel reports.
 * @returns Every list request the page made so far.
 */
const openWith = async (page: Page, sessions: Session[]): Promise<() => readonly URL[]> => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubEmptyModules(page)
  return await stubSessions(page, sessions)
}

/**
 * The address cells on screen, in order.
 * @param page The Playwright page.
 * @returns A locator over the table's second cell of each body row.
 */
const addressCells = (page: Page): Locator => {
  return page.getByRole('table').getByRole('row').filter({ has: page.getByRole('cell') }).locator('td:nth-child(2)')
}

test('a session search reaches the URL, asks the panel, and survives a reload', async ({ page }) => {
  const requests = await openWith(page, catalogue(30))

  await page.goto('/settings/sessions')
  await page.getByRole('searchbox', { name: 'Search devices' }).fill('Device 07')

  await expect(page).toHaveURL(/\/settings\/sessions\?search=Device\+07$/)
  await expect(addressCells(page)).toHaveText(['203.0.113.7'])
  expect(
    requests().some((url) => {
      return url.searchParams.get('search') === 'Device 07'
    }),
  ).toBe(true)

  await page.reload()

  await expect(page.getByRole('searchbox', { name: 'Search devices' })).toHaveValue('Device 07')
  await expect(addressCells(page)).toHaveText(['203.0.113.7'])
})

test('the next session page is reachable, and a page past the end moves back to the last', async ({ page }) => {
  await openWith(page, catalogue(30))

  await page.goto('/settings/sessions')
  await page.getByRole('navigation', { name: 'Pagination' }).getByRole('button', { name: 'Next page' }).click()
  await expect(page).toHaveURL(/\?page=2$/)
  await expect(addressCells(page).first()).toHaveText('203.0.113.26')

  await page.goto('/settings/sessions?page=9')
  await expect(page).toHaveURL(/\?page=2$/)
  await expect(addressCells(page)).toHaveCount(5)
})

test('the signed-in heading sorts the sessions on the server', async ({ page }) => {
  const requests = await openWith(page, catalogue(3))

  await page.goto('/settings/sessions?sort=issuedAt')
  const heading = page.getByRole('columnheader', { name: 'Signed in' })
  await expect(heading).toHaveAttribute('aria-sort', 'ascending')
  await expect(addressCells(page)).toHaveText(['203.0.113.3', '203.0.113.2', '203.0.113.1'])
  await heading.getByRole('button').click()

  await expect(page).toHaveURL(/\?sort=-issuedAt$/)
  await expect(addressCells(page)).toHaveText(['203.0.113.1', '203.0.113.2', '203.0.113.3'])
  expect(requests().at(-1)?.searchParams.get('sort')).toBe('-issuedAt')
})

// The current device is marked by the panel, on whichever page it falls; the SPA does not decide it.
test('the device making the request stays marked as this device after a sort moves it', async ({ page }) => {
  await openWith(page, catalogue(3))

  await page.goto('/settings/sessions?sort=issuedAt')

  const current = page.getByRole('row').filter({ hasText: 'Device 01' })
  await expect(current).toContainText('This device')
  await expect(page.getByText('This device')).toHaveCount(1)
})

test('a session search matching nothing is the no-results state, not the empty state', async ({ page }) => {
  await openWith(page, catalogue(3))

  await page.goto('/settings/sessions?search=nowhere')

  await expect(page.getByText('No devices match')).toBeVisible()
  await expect(page.getByText('No other devices')).toHaveCount(0)

  // The last of the two: the search box's own clear button carries the same name.
  await page.getByRole('button', { name: 'Clear the search' }).last().click()
  await expect(page).toHaveURL(/\/settings\/sessions$/)
  await expect(addressCells(page)).toHaveCount(3)
})
