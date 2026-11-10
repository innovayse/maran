import { expect, test } from '../fixtures/guarded-test'
import { ONE_SHOT, stubDashboard } from '../fixtures/stub-dashboard-route'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubIdleLiveDashboard } from '../fixtures/stub-live-dashboard-stream'
import { stubModules } from '../fixtures/stub-modules-route'
import { stubShellReads } from '../fixtures/stub-shell-reads'
import { stubAccounts } from '../fixtures/stub-accounts-route'
import { stubPlans } from '../fixtures/stub-plans-route'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import type { Account } from '../../src/types/account'
import type { PanelModule } from '../../src/types/module'
import type { Page } from '@playwright/test'

// Issue #330: the account states the list filters by and prints come from the panel, named by it.
// Every name below is deliberately NOT the SPA's old wording, so a name that shows up on screen can
// only have travelled through the panel's list.

/** Catalogue every spec here starts from: the accounts module licensed, so the route is reachable. */
const LICENSED_ACCOUNTS: PanelModule[] = [
  { name: 'accounts', displayName: 'Accounts', kindDisplayName: 'Core', tier: 'included', state: 'enabled' },
]

/** Two accounts, one in each state, in name order. */
const ACCOUNTS: Account[] = [
  {
    id: '00000000-0000-0000-0000-000000000a01',
    name: 'harbor',
    primaryDomain: 'harbor.example.com',
    planId: '22222222-2222-2222-2222-222222222222',
    status: 'active',
    statusName: 'Live',
    createdAt: '2026-09-01T10:00:00Z',
    resellerId: null,
    resellerName: null,
  },
  {
    id: '00000000-0000-0000-0000-000000000a02',
    name: 'lantern',
    primaryDomain: 'lantern.example.com',
    planId: '22222222-2222-2222-2222-222222222222',
    status: 'suspended',
    statusName: 'Paused',
    createdAt: '2026-09-02T10:00:00Z',
    resellerId: null,
    resellerName: null,
  },
]

/**
 * Signs in, serves the accounts and answers the state list with the given body.
 * @param page The Playwright page.
 * @param statuses The state list the panel answers with, or `'unreadable'` for a refused read.
 * @returns Every list request the page made so far.
 */
const openWith = async (
  page: Page,
  statuses: { status: string; statusName: string }[] | 'unreadable',
): Promise<() => readonly URL[]> => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED_ACCOUNTS)
  await stubShellReads(page)
  await stubPlans(page)
  const requests = await stubAccounts(page, ACCOUNTS)
  // Installed after the shared stub, so it answers first.
  await page.route('**/api/v1/accounts/statuses', async (route) => {
    if (statuses === 'unreadable') {
      await route.fulfill({
        status: 500,
        contentType: 'application/problem+json',
        body: JSON.stringify({ title: 'The state list is unavailable.' }),
      })
      return
    }
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(statuses) })
  })
  return requests
}

/** A panel that names the two states with its own words. */
const PANEL_NAMES = [
  { status: 'active', statusName: 'Live' },
  { status: 'suspended', statusName: 'Paused' },
]

test('the badges, the filter options and the chip use the names the panel gave', async ({ page }) => {
  const requests = await openWith(page, PANEL_NAMES)
  // The test starts on the home screen, which reads the dashboard, its layout and its live stream.
  await stubDashboard(page, ONE_SHOT)
  await stubIdleLiveDashboard(page)
  await page.goto('/')
  await page.getByRole('navigation', { name: 'Main navigation' }).getByRole('link', { name: 'Accounts' }).click()

  await expect(page.getByRole('row', { name: /harbor/ })).toContainText('Live')
  await expect(page.getByRole('row', { name: /lantern/ })).toContainText('Paused')

  await page.getByTestId('filter-trigger').click()
  const dialog = page.getByRole('dialog', { name: 'Filters' })
  await dialog.getByRole('combobox', { name: 'Status' }).click()
  await expect(page.getByRole('option')).toHaveText(['Any', 'Live', 'Paused'])
  await page.getByRole('option', { name: 'Paused' }).click()
  await dialog.getByRole('button', { name: 'Apply' }).click()

  await expect(page.getByTestId('active-filter-chips')).toContainText('Status: Paused')
  expect(requests().at(-1)?.searchParams.get('status')).toBe('suspended')
})

test('a link naming a state the panel does not list is dropped once the list is read', async ({ page }) => {
  const requests = await openWith(page, PANEL_NAMES)

  await page.goto('/accounts?status=archived')

  await expect(page.getByRole('row', { name: /harbor/ })).toBeVisible()
  await expect(page.getByRole('row', { name: /lantern/ })).toBeVisible()
  await expect(page.getByTestId('active-filter-chips')).toHaveCount(0)
  expect(requests().at(-1)?.searchParams.has('status')).toBe(false)
})

test('a link naming a state the panel lists is honoured and shown as a chip', async ({ page }) => {
  const requests = await openWith(page, PANEL_NAMES)

  await page.goto('/accounts?status=suspended')

  await expect(page.getByTestId('active-filter-chips')).toContainText('Status: Paused')
  await expect(page.getByRole('row', { name: /lantern/ })).toBeVisible()
  await expect(page.getByRole('row', { name: /harbor/ })).toHaveCount(0)
  expect(requests().at(-1)?.searchParams.get('status')).toBe('suspended')
})

test('an unreadable state list leaves the filter at "Any" and does not discard a linked state', async ({ page, guard }) => {
  // The refusal is the subject: the state list answers 500.
  guard.allow(500, /\/api\/v1\/accounts\/statuses$/)
  const requests = await openWith(page, 'unreadable')

  await page.goto('/accounts?status=suspended')

  // The server stays the authority: the list asks for what the link named, and answers it.
  await expect(page.getByRole('row', { name: /lantern/ })).toBeVisible()
  expect(requests().at(-1)?.searchParams.get('status')).toBe('suspended')

  await page.getByTestId('filter-trigger').click()
  const dialog = page.getByRole('dialog', { name: 'Filters' })
  await dialog.getByRole('combobox', { name: 'Status' }).click()
  await expect(page.getByRole('option')).toHaveText(['Any'])
})
