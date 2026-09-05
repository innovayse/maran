import { expect, test } from '@playwright/test'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import { stubAccounts, stubAccountsProblem } from '../fixtures/stub-accounts-route'
import type { PanelModule } from '../../src/types/module'
import { stubSignedIn } from '../fixtures/stub-auth-routes'

/** Catalogue every spec here starts from: the accounts module licensed, so the route is reachable. */
const LICENSED_ACCOUNTS: PanelModule[] = [
  { name: 'accounts', displayName: 'Accounts', tier: 'included', isEnabled: true },
]

test('accounts list shows the empty state when the panel reports no accounts', async ({ page }) => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED_ACCOUNTS)
  await stubAccounts(page, [])

  await page.goto('/accounts')

  await expect(page.getByText('No accounts yet')).toBeVisible()
  await expect(page.getByText('Create the first hosting account to get started.')).toBeVisible()
  await expect(page.getByRole('table')).toHaveCount(0)
})

test('accounts list renders a row per account with its backend-reported status', async ({ page }) => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED_ACCOUNTS)
  await stubAccounts(page, [
    {
      id: '11111111-1111-1111-1111-111111111111',
      name: 'alpha',
      primaryDomain: 'alpha.example.com',
      planId: '22222222-2222-2222-2222-222222222222',
      status: 'active',
      createdAt: '2026-08-01T10:00:00Z',
    },
    {
      id: '33333333-3333-3333-3333-333333333333',
      name: 'beta',
      primaryDomain: 'beta.example.com',
      planId: '22222222-2222-2222-2222-222222222222',
      status: 'suspended',
      createdAt: '2026-08-02T10:00:00Z',
    },
  ])

  await page.goto('/accounts')

  const rows = page.getByRole('row')
  await expect(rows.filter({ hasText: 'alpha.example.com' })).toContainText('Active')
  await expect(rows.filter({ hasText: 'beta.example.com' })).toContainText('Suspended')
  await expect(page.getByRole('cell', { name: 'alpha', exact: true })).toBeVisible()
  await expect(page.getByRole('cell', { name: 'beta', exact: true })).toBeVisible()
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

  await expect(page.getByRole('status')).toHaveText(backendDetail)
  await expect(page.getByRole('table')).toHaveCount(0)
  await expect(page.getByText('No accounts yet')).toHaveCount(0)
})
