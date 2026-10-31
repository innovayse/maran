import { expect, test, type Page } from '@playwright/test'
import { stubAudit } from '../fixtures/stub-audit-routes'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubEmptyModules } from '../fixtures/stub-modules-route'
import type { AuditEvent } from '../../src/types/audit'

// The audit journal as a server page (#196): search, sort, filters and pages are the panel's.

const SIGN_IN: AuditEvent = {
  id: '00000000-0000-0000-0000-0000000000c1',
  occurredAt: '2026-09-10T09:00:00+00:00',
  actorUsername: 'admin',
  action: 'LoginSucceeded',
  actionName: 'Signed in',
  subject: 'admin',
  ipAddress: '203.0.113.7',
  succeeded: true,
}

const REFUSED: AuditEvent = {
  id: '00000000-0000-0000-0000-0000000000c2',
  occurredAt: '2026-09-09T08:00:00+00:00',
  actorUsername: 'mallory',
  action: 'LoginFailed',
  actionName: 'Sign-in refused',
  subject: 'mallory',
  ipAddress: '198.51.100.4',
  succeeded: false,
}

const AUGUST: AuditEvent = {
  ...SIGN_IN,
  id: '00000000-0000-0000-0000-0000000000c3',
  occurredAt: '2026-08-15T12:00:00+00:00',
  actorUsername: 'zoe',
  subject: 'zoe',
}

/**
 * Opens the journal over a set of entries, newest first as the panel orders them.
 * @param page The Playwright page.
 * @param events The journal the stubbed panel holds.
 * @returns Every list request the page made, oldest first.
 */
const openJournal = async (page: Page, events: AuditEvent[]): Promise<() => readonly URL[]> => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubEmptyModules(page)
  return await stubAudit(page, events)
}

test('the search reaches the panel and narrows the journal', async ({ page }) => {
  const requests = await openJournal(page, [SIGN_IN, REFUSED])

  await page.goto('/settings/audit')
  await expect(page.getByRole('cell', { name: '203.0.113.7', exact: true })).toBeVisible()

  await page.getByRole('searchbox', { name: 'Search the journal' }).fill('mallory')

  await expect(page).toHaveURL(/search=mallory/)
  await expect(page.getByRole('cell', { name: '203.0.113.7', exact: true })).toHaveCount(0)
  await expect(page.getByRole('cell', { name: '198.51.100.4', exact: true })).toBeVisible()
  expect(requests().at(-1)?.searchParams.get('search')).toBe('mallory')
})

test('a column heading sorts the journal by actor on the server', async ({ page }) => {
  const requests = await openJournal(page, [AUGUST, SIGN_IN, REFUSED])

  await page.goto('/settings/audit')
  const heading = page.getByRole('columnheader', { name: 'Who' })
  await heading.getByRole('button').click()

  await expect(page).toHaveURL(/sort=actor/)
  await expect(heading).toHaveAttribute('aria-sort', 'ascending')
  await expect(page.getByRole('row').nth(1)).toContainText('admin')
  expect(requests().at(-1)?.searchParams.get('sort')).toBe('actor')
})

test('action, outcome and a date window applied in the sheet become chips and reach the panel', async ({ page }) => {
  const requests = await openJournal(page, [SIGN_IN, REFUSED, AUGUST])

  await page.goto('/settings/audit')
  await page.getByTestId('filter-trigger').click()
  const dialog = page.getByRole('dialog', { name: 'Filters' })
  await dialog.getByRole('combobox', { name: 'Action' }).click()
  // The action is offered by the name the panel gives it.
  await page.getByRole('option', { name: 'Signed in' }).click()
  await dialog.getByRole('combobox', { name: 'Outcome' }).click()
  await page.getByRole('option', { name: 'Succeeded' }).click()
  await dialog.getByLabel('From').fill('2026-09-01')
  await dialog.getByLabel('Until').fill('2026-09-30')
  await dialog.getByRole('button', { name: 'Apply' }).click()

  await expect(page).toHaveURL(/action=LoginSucceeded/)
  await expect(page).toHaveURL(/outcome=succeeded/)
  const expectedFrom = await page.evaluate(() => {
    return new Date(2026, 8, 1).toISOString()
  })
  const sent = requests().at(-1)
  expect(sent?.searchParams.get('action')).toBe('LoginSucceeded')
  expect(sent?.searchParams.get('outcome')).toBe('succeeded')
  expect(sent?.searchParams.get('from')).toBe(expectedFrom)
  expect(sent?.searchParams.get('to')).toMatch(/^2026-(09-30|10-01)T/)
  // Only September's successful sign-in is left; August's is outside the window.
  await expect(page.getByRole('cell', { name: '203.0.113.7', exact: true })).toHaveCount(1)
  await expect(page.getByRole('cell', { name: 'zoe', exact: true })).toHaveCount(0)

  const chips = page.getByTestId('active-filter-chips')
  await expect(chips).toContainText('Action: Signed in')
  await expect(chips).toContainText('Outcome: Succeeded')
  await page.getByRole('button', { name: 'Clear all' }).click()
  await expect(page).toHaveURL(/\/settings\/audit$/)
})

test('an inverted window is refused by the panel and its sentence is shown', async ({ page }) => {
  await openJournal(page, [SIGN_IN])
  await page.route(/\/api\/v1\/audit\?.*from=/, async (route) => {
    await route.fulfill({
      status: 400,
      contentType: 'application/problem+json',
      body: JSON.stringify({ code: 'AuditDateRangeInverted', title: 'Invalid', detail: 'The start is after the end.' }),
    })
  })

  await page.goto('/settings/audit?from=2026-09-30&to=2026-09-01')

  await expect(page.getByText('The start is after the end.')).toBeVisible()
  await expect(page.getByRole('table')).toHaveCount(0)
})

test('a search matching nothing is the no-results state, not the empty journal', async ({ page }) => {
  await openJournal(page, [SIGN_IN])

  await page.goto('/settings/audit?search=nobody')

  await expect(page.getByText('No entries match')).toBeVisible()
  await expect(page.getByText('Nothing recorded yet')).toHaveCount(0)
  await page.getByRole('button', { name: 'Clear search and filters' }).click()
  await expect(page.getByRole('cell', { name: '203.0.113.7', exact: true })).toBeVisible()
})

test('the journal pages on the server and keeps the page in the URL', async ({ page }) => {
  const catalogue = Array.from({ length: 12 }, (_, index): AuditEvent => {
    return { ...SIGN_IN, id: `00000000-0000-0000-0000-0000000001${String(index).padStart(2, '0')}` }
  })
  const requests = await openJournal(page, catalogue)

  await page.goto('/settings/audit?pageSize=10')
  await expect(page.getByRole('cell', { name: '203.0.113.7', exact: true })).toHaveCount(10)

  await page.getByRole('navigation', { name: 'Pagination' }).getByRole('button', { name: 'Next page' }).click()

  await expect(page).toHaveURL(/page=2/)
  await expect(page.getByRole('cell', { name: '203.0.113.7', exact: true })).toHaveCount(2)
  expect(requests().at(-1)?.searchParams.get('page')).toBe('2')
})
