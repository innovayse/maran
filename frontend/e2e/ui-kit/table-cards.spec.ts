import { expect, test, type Page } from '@playwright/test'
import { delayRoute } from '../fixtures/delay-route'
import { stubAccounts } from '../fixtures/stub-accounts-route'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import { stubPlans, THREE_PLANS } from '../fixtures/stub-plans-route'
import type { Account } from '../../src/types/account'
import type { PanelModule } from '../../src/types/module'

// Issue #217, point 1: below `sm` a `UiTable` draws each row as a card from the same column schema,
// sorting stays reachable through a control derived from the sortable columns, and the loading
// placeholders take the card's shape. From `sm` up the table is the table it always was. The
// accounts list is the screen used: a real page, so the cards are measured where people meet them.

const ACCOUNT: Account = {
  id: '00000000-0000-0000-0000-0000000000c1',
  name: 'acme-industrial-holdings',
  primaryDomain: 'acme.example.com',
  planId: THREE_PLANS[0]?.id ?? '',
  status: 'active',
  createdAt: '2026-08-30T09:00:00+00:00',
}

const MODULES: PanelModule[] = [{ name: 'accounts', displayName: 'Accounts', tier: 'included', isEnabled: true }]

/**
 * Stubs the shell and the accounts list.
 * @param page The page under test.
 * @returns Resolves once every route is installed.
 */
const stubList = async (page: Page): Promise<void> => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, MODULES)
  await stubPlans(page, THREE_PLANS)
  await stubAccounts(page, [ACCOUNT])
}

test('at 1440px the list is the ordinary table, with sortable headings and no cards', async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 })
  await stubList(page)
  await page.goto('/accounts')

  const table = page.getByRole('table', { name: 'Hosting accounts' })
  await expect(table.getByText(ACCOUNT.name)).toBeVisible()
  await expect(page.getByTestId('table-scroller')).toHaveAttribute('data-layout', 'table')
  await expect(table.getByRole('columnheader', { name: 'Status' })).toBeVisible()
  await expect(table.getByRole('button', { name: 'Status' })).toBeVisible()
  await expect(page.getByTestId('table-sort-bar')).toHaveCount(0)
  await expect(page.locator('td[data-placement]:not([data-placement="column"])')).toHaveCount(0)
})

test('at 390px each row is a card: the identity as its title, the other columns as labelled lines', async ({
  page,
}) => {
  await page.setViewportSize({ width: 390, height: 844 })
  await stubList(page)
  await page.goto('/accounts')

  const table = page.getByRole('table', { name: 'Hosting accounts' })
  await expect(page.getByTestId('table-scroller')).toHaveAttribute('data-layout', 'cards')
  const row = table.getByRole('row').filter({ hasText: ACCOUNT.name })
  await expect(row.locator('td[data-placement="title"]')).toContainText(ACCOUNT.name)
  const status = row.locator('td[data-placement="line"]').filter({ hasText: 'Status' })
  await expect(status.getByText('Status', { exact: true })).toBeVisible()
  await expect(status.getByText('Active')).toBeVisible()
  // The header row stays for assistive technology, but is not drawn.
  await expect(table.getByRole('columnheader', { name: 'Status' })).toHaveCount(1)
  await expect(table.getByRole('button', { name: 'Status' })).toHaveCount(0)
})

test('at 390px the sort control offers the sortable columns and drives the same sort', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 })
  await stubList(page)
  await page.goto('/accounts')
  await expect(page.getByText(ACCOUNT.name)).toBeVisible()

  const bar = page.getByTestId('table-sort-bar')
  const direction = bar.getByRole('button', { name: 'Sort descending' })
  await expect(direction).toBeDisabled()

  await bar.getByRole('combobox', { name: 'Sort by' }).click()
  await expect(page.getByRole('option')).toHaveText(['Name', 'Status', 'Created'])
  await page.getByRole('option', { name: 'Status' }).click()
  await expect(page).toHaveURL(/[?&]sort=status(&|$)/)

  await direction.click()
  await expect(page).toHaveURL(/[?&]sort=-status(&|$)/)
  await expect(bar.getByRole('button', { name: 'Sort ascending' })).toBeEnabled()
})

test('at 390px the loading placeholders take the card shape', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 })
  await stubList(page)
  await delayRoute(page, /\/api\/v1\/accounts(\?.*)?$/, 1500)
  await page.goto('/accounts')

  const placeholder = page.locator('tbody tr[aria-hidden="true"]').first()
  await expect(placeholder.locator('td[data-placement="title"] [data-skeleton]')).toBeVisible()
  await expect(placeholder.locator('td[data-placement="line"]').first()).toBeVisible()
  await expect(placeholder.locator('td[data-placement="line"] > span[aria-hidden="true"]').first()).toHaveText(
    'Primary domain',
  )
  await expect(page.getByText(ACCOUNT.name)).toBeVisible()
})
