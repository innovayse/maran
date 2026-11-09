import { expect, test, type Page } from '@playwright/test'
import { mainNavigation, openSection } from '../fixtures/open-full-shell'
import {
  RESELLER_KEY_PATHS,
  apiKeyRow,
  numberedKeys,
  stubApiKeys,
  stubbedPlaintext,
  type ApiKeyCalls,
} from '../fixtures/stub-api-keys-routes'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import { ADMIN_MODULES, RESELLER_MODULES, stubResellerSession } from '../fixtures/stub-reseller-session'
import type { ApiKey } from '../../src/types/apiKey'

// Issue #435: a reseller manages the API keys it owns from Settings, API keys. It is the same screen
// the administrator has, asking the reseller's own endpoint (`/api/v1/reseller/api-keys`): the panel
// pages, searches and filters, offers the reseller its own scopes and names the states. Each test
// enters through one `goto`; the rest is clicks.

const PROVISIONING = apiKeyRow({ id: '30000000-0000-0000-0000-000000000001', name: 'Provisioning', prefix: '5a6b7c8d', scopes: ['accounts.read'] })
const OLD_EXPORT = apiKeyRow({
  id: '30000000-0000-0000-0000-000000000002',
  name: 'Old export',
  prefix: 'e1f2a3b4',
  revokedAt: '2026-08-20T00:00:00+00:00',
  status: 'revoked',
})

/**
 * Signs the reseller in, stubs its keys endpoint and opens Settings, API keys from the sidebar.
 * @param page The Playwright page under test.
 * @param keys The keys the reseller holds.
 * @returns The calls the stub will record.
 */
const openResellerKeys = async (page: Page, keys: ApiKey[]): Promise<ApiKeyCalls> => {
  await stubResellerSession(page, undefined, RESELLER_MODULES)
  const calls = await stubApiKeys(
    page,
    {
      keys,
      scopes: [
        { value: 'plans.read', name: 'Look at plans' },
        { value: 'accounts.read', name: 'Look at accounts' },
      ],
    },
    RESELLER_KEY_PATHS,
  )
  await page.goto('/')
  await openSection(page, 'Settings')
  await mainNavigation(page).getByRole('link', { name: 'API keys', exact: true }).click()
  await expect(page.getByRole('heading', { level: 1, name: 'API keys' })).toBeVisible()
  return calls
}

test('a reseller reaches its keys from Settings and the list is read from the reseller endpoint', async ({ page }) => {
  const calls = await openResellerKeys(page, [PROVISIONING, OLD_EXPORT])

  await expect(page).toHaveURL('/settings/reseller-api-keys')
  await expect(page.getByRole('row').filter({ hasText: 'Provisioning' }).getByText('In force')).toBeVisible()
  await expect(page.getByRole('row').filter({ hasText: 'Old export' }).getByText('Withdrawn')).toBeVisible()
  expect(calls.lists.length).toBeGreaterThan(0)
  expect(
    calls.lists.every((url) => {
      return url.pathname === '/api/v1/reseller/api-keys'
    }),
  ).toBe(true)
})

test('a reseller with no keys sees the empty state written for it', async ({ page }) => {
  await openResellerKeys(page, [])

  await expect(page.getByText('No API keys yet')).toBeVisible()
  await expect(page.getByText('Create a key to let one of your integrations read your plans and accounts.')).toBeVisible()
  await expect(page.getByRole('table')).toHaveCount(0)
})

test('the search and the page are sent to the panel, which does the narrowing', async ({ page }) => {
  const calls = await openResellerKeys(page, numberedKeys(30))
  await expect(page.getByRole('row', { name: /Key \d\d/ })).toHaveCount(25)

  await page.getByRole('navigation', { name: 'Pagination' }).getByRole('button', { name: 'Next page' }).click()
  await expect(page).toHaveURL(/\?page=2$/)
  await expect(page.getByRole('row', { name: /Key \d\d/ })).toHaveCount(5)
  await page.getByRole('searchbox', { name: 'Search API keys' }).fill('Key 2')
  await expect(page.getByRole('row', { name: /Key \d\d/ })).toHaveCount(10)

  expect(
    calls.lists.some((url) => {
      return url.searchParams.get('page') === '2'
    }),
  ).toBe(true)
  expect(
    calls.lists.some((url) => {
      return url.searchParams.get('search') === 'Key 2'
    }),
  ).toBe(true)
})

test('the create dialog offers the reseller scopes the panel lists and creates the key for the reseller endpoint', async ({
  page,
}) => {
  const calls = await openResellerKeys(page, [PROVISIONING])

  await page.getByRole('button', { name: 'Create key' }).click()
  const dialog = page.getByRole('dialog', { name: 'Create an API key' })
  await expect(dialog.getByRole('checkbox')).toHaveCount(2)
  await expect(dialog.getByRole('checkbox', { name: 'Look at plans' })).toBeVisible()
  await dialog.getByRole('checkbox', { name: 'Look at accounts' }).check({ force: true })
  await dialog.getByRole('textbox', { name: 'Name' }).fill('Billing sync')
  await dialog.getByRole('button', { name: 'Create key' }).click()

  const secret = page.getByRole('dialog', { name: 'Your new API key' })
  await expect(secret.getByTestId('api-key-secret')).toHaveText(stubbedPlaintext)
  expect(calls.created).toEqual([{ name: 'Billing sync', scopes: ['accounts.read'], allowedCidrs: null, expiresAt: null }])
  expect(calls.scopeReads).toBeGreaterThan(0)
})

test('revoking a key asks first, then sends the DELETE and shows the key withdrawn', async ({ page }) => {
  const calls = await openResellerKeys(page, [PROVISIONING])

  const row = page.getByRole('row').filter({ hasText: 'Provisioning' })
  await row.getByRole('button', { name: 'Actions for the key Provisioning' }).click()
  await page.getByRole('menuitem', { name: 'Revoke' }).click()
  expect(calls.revoked).toEqual([])
  await page.getByRole('dialog', { name: 'Revoke the key Provisioning' }).getByRole('button', { name: 'Revoke key' }).click()

  await expect(page.getByText('The key Provisioning was revoked.')).toBeVisible()
  expect(calls.revoked).toEqual([PROVISIONING.id])
  await expect(row.getByText('Withdrawn')).toBeVisible()
})

test('the screen is a reseller screen: an administrator is sent to the not-available screen', async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 })
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, ADMIN_MODULES)
  await stubApiKeys(page, { keys: [PROVISIONING] }, RESELLER_KEY_PATHS)

  await page.goto('/settings/reseller-api-keys')

  await expect(page.getByRole('heading', { level: 1, name: 'API keys' })).toHaveCount(0)
  await expect(page).toHaveURL('/not-available')
})
