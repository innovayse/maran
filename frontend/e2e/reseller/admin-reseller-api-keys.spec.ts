import { expect, test, type Page } from '@playwright/test'
import { overseenKeyPaths, apiKeyRow, stubApiKeys, type ApiKeyCalls } from '../fixtures/stub-api-keys-routes'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubAccounts } from '../fixtures/stub-accounts-route'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import { stubPlans } from '../fixtures/stub-plans-route'
import { ACME_HOSTING, stubResellers } from '../fixtures/stub-resellers-routes'
import { stubShellReads } from '../fixtures/stub-shell-reads'
import type { ApiKey } from '../../src/types/apiKey'
import type { PanelModule } from '../../src/types/module'

// Issue #435: an administrator sees one reseller's API keys from that reseller's page and revokes
// one without suspending the reseller. It is the API keys screen asking the reseller-scoped
// endpoint (`/api/v1/resellers/{id}/api-keys`); it lists and revokes but offers no create, because
// the panel has no endpoint that mints a key for a reseller. Each test enters through one `goto`;
// the rest is clicks.

/** The catalogue the spec starts from: the accounts module, which carries the reseller screens. */
const LICENSED_ACCOUNTS: PanelModule[] = [
  { name: 'accounts', displayName: 'Accounts', kindDisplayName: 'Core', tier: 'included', state: 'enabled' },
]

const PROVISIONING = apiKeyRow({ id: '30000000-0000-0000-0000-000000000001', name: 'Provisioning', prefix: '5a6b7c8d', scopes: ['accounts.read'] })
const OLD_EXPORT = apiKeyRow({
  id: '30000000-0000-0000-0000-000000000002',
  name: 'Old export',
  prefix: 'e1f2a3b4',
  revokedAt: '2026-08-20T00:00:00+00:00',
  status: 'revoked',
})

/**
 * Signs the administrator in, stubs the reseller screens and the reseller's keys, opens the
 * reseller's page and follows its API keys button.
 * @param page The Playwright page under test.
 * @param keys The keys the reseller holds.
 * @returns The calls the keys stub will record.
 */
const openOverseenKeys = async (page: Page, keys: ApiKey[]): Promise<ApiKeyCalls> => {
  await page.setViewportSize({ width: 1440, height: 900 })
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubShellReads(page)
  await stubModules(page, LICENSED_ACCOUNTS)
  await stubAccounts(page, [])
  await stubPlans(page)
  await stubResellers(page)
  const calls = await stubApiKeys(
    page,
    { keys, scopes: [{ value: 'accounts.read', name: 'Look at accounts' }] },
    overseenKeyPaths(ACME_HOSTING.id),
  )
  await page.goto(`/resellers/${ACME_HOSTING.id}`)
  await expect(page.getByRole('heading', { level: 1, name: 'acmehosting' })).toBeVisible()
  await page.getByRole('main').getByRole('link', { name: 'API keys', exact: true }).click()
  await expect(page.getByRole('heading', { level: 1, name: 'API keys of the reseller' })).toBeVisible()
  return calls
}

test('the reseller page leads to its keys, read from the reseller-scoped endpoint', async ({ page }) => {
  const calls = await openOverseenKeys(page, [PROVISIONING, OLD_EXPORT])

  await expect(page).toHaveURL(`/resellers/${ACME_HOSTING.id}/api-keys`)
  await expect(page.getByRole('row').filter({ hasText: 'Provisioning' }).getByText('In force')).toBeVisible()
  await expect(page.getByRole('row').filter({ hasText: 'Old export' }).getByText('Withdrawn')).toBeVisible()
  expect(calls.lists.length).toBeGreaterThan(0)
  expect(
    calls.lists.every((url) => {
      return url.pathname === `/api/v1/resellers/${ACME_HOSTING.id}/api-keys`
    }),
  ).toBe(true)
})

test('the oversight screen offers no create and never reads the scopes a create dialog needs', async ({ page }) => {
  const calls = await openOverseenKeys(page, [PROVISIONING])

  await expect(page.getByRole('button', { name: 'Create key' })).toHaveCount(0)
  expect(calls.scopeReads).toBe(0)
})

test('a reseller with no keys says so in words about the reseller', async ({ page }) => {
  await openOverseenKeys(page, [])

  await expect(page.getByText('This reseller has not created any API keys.')).toBeVisible()
  await expect(page.getByRole('table')).toHaveCount(0)
})

test('the status filter uses the states the panel names and narrows on the panel', async ({ page }) => {
  const calls = await openOverseenKeys(page, [PROVISIONING, OLD_EXPORT])

  await page.getByRole('button', { name: 'Filters' }).click()
  const dialog = page.getByRole('dialog', { name: 'Filters' })
  await dialog.getByRole('combobox', { name: 'State' }).click()
  await page.getByRole('option', { name: 'Withdrawn' }).click()
  await dialog.getByRole('button', { name: 'Apply' }).click()

  await expect(page.getByRole('row').filter({ hasText: 'Old export' })).toBeVisible()
  await expect(page.getByRole('row').filter({ hasText: 'Provisioning' })).toHaveCount(0)
  expect(
    calls.lists.some((url) => {
      return url.searchParams.get('status') === 'revoked'
    }),
  ).toBe(true)
})

test('revoking a reseller key asks first, then sends the DELETE to the reseller-scoped endpoint', async ({ page }) => {
  const calls = await openOverseenKeys(page, [PROVISIONING])

  const row = page.getByRole('row').filter({ hasText: 'Provisioning' })
  await row.getByRole('button', { name: 'Actions for the key Provisioning' }).click()
  await page.getByRole('menuitem', { name: 'Revoke' }).click()
  expect(calls.revoked).toEqual([])
  await page.getByRole('dialog', { name: 'Revoke the key Provisioning' }).getByRole('button', { name: 'Revoke key' }).click()

  await expect(page.getByText('The key Provisioning was revoked.')).toBeVisible()
  expect(calls.revoked).toEqual([PROVISIONING.id])
  await expect(row.getByText('Withdrawn')).toBeVisible()
})

test('a refused revoke shows the panel sentence as received', async ({ page }) => {
  const sentence = 'No such API key.'
  await openOverseenKeys(page, [PROVISIONING])
  await page.route(`**/api/v1/resellers/${ACME_HOSTING.id}/api-keys/*`, async (route) => {
    await route.fulfill({
      status: 404,
      contentType: 'application/problem+json',
      body: JSON.stringify({ code: 'ApiKeyNotFound', title: sentence, detail: sentence, status: 404 }),
    })
  })

  await page
    .getByRole('row')
    .filter({ hasText: 'Provisioning' })
    .getByRole('button', { name: 'Actions for the key Provisioning' })
    .click()
  await page.getByRole('menuitem', { name: 'Revoke' }).click()
  await page.getByRole('dialog', { name: 'Revoke the key Provisioning' }).getByRole('button', { name: 'Revoke key' }).click()

  await expect(page.getByText(sentence)).toBeVisible()
  await expect(page.getByText('The key Provisioning was revoked.')).toHaveCount(0)
})

test('the way back returns to the reseller', async ({ page }) => {
  await openOverseenKeys(page, [PROVISIONING])

  await page.getByRole('link', { name: 'Back to the reseller' }).click()

  await expect(page).toHaveURL(`/resellers/${ACME_HOSTING.id}`)
  await expect(page.getByRole('heading', { level: 1, name: 'acmehosting' })).toBeVisible()
})
