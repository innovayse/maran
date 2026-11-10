import { expect, test } from '../fixtures/guarded-test'
import { stubAccounts } from '../fixtures/stub-accounts-route'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import { stubShellReads } from '../fixtures/stub-shell-reads'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubPhpVersions, stubSiteLimits, stubSites } from '../fixtures/stub-sites-routes'
import type { Account } from '../../src/types/account'
import type { PanelModule } from '../../src/types/module'
import type { Page } from '@playwright/test'

// Issue #499: an invalid line in the aliases box was answered with the domain's sentence ("Domain must
// be a valid hostname."), naming the wrong field and not the line. The message now names the alias.

const LICENSED_SITES: PanelModule[] = [
  { name: 'sites', displayName: 'Sites', kindDisplayName: 'Core', tier: 'included', state: 'enabled' },
]

const OWNER: Account = {
  id: '22222222-2222-2222-2222-222222222222',
  name: 'alpha',
  primaryDomain: 'alpha.example.com',
  planId: '44444444-4444-4444-4444-444444444444',
  status: 'active',
  statusName: 'Active',
  createdAt: '2026-08-01T10:00:00Z',
  resellerId: null,
  resellerName: null,
}

/**
 * Opens the create form with a valid domain and account and the given alias lines, then submits.
 * @param page The Playwright page.
 * @param aliases The textarea's text.
 * @returns Resolves once the form was submitted.
 */
const submitWithAliases = async (page: Page, aliases: string): Promise<void> => {
  await stubSignedIn(page)
  await stubShellReads(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED_SITES)
  await stubAccounts(page, [OWNER])
  await stubPhpVersions(page, [{ version: '8.3', defaultStatus: 'default' }])
  await stubSites(page, [])
  await stubSiteLimits(page, { maximumAliases: 10 })
  await page.goto('/sites/new')
  await page.getByLabel('Domain', { exact: true }).fill('example.com')
  await page.getByRole('combobox', { name: 'Account' }).click()
  await page.getByRole('option', { name: /alpha/ }).click()
  await page.getByLabel('Aliases').fill(aliases)
  await page.getByRole('button', { name: 'Create site' }).click()
}

test('an invalid alias line is named in the message, not answered with the domain rule', async ({ page }) => {
  await submitWithAliases(page, 'www.example.com\na b')

  await expect(page.getByText('Alias "a b" must be a valid hostname.')).toBeVisible()
  await expect(page.getByText('Domain must be a valid hostname.')).toHaveCount(0)
})

test('a list of valid aliases raises no alias message', async ({ page }) => {
  // Bounded: the create request leaving proves the form accepted the list, so the absence below is a
  // finding and not the result of asserting before the form had answered.
  const created = page.waitForRequest((request) => {
    return request.method() === 'POST' && new URL(request.url()).pathname === '/api/v1/sites'
  })
  await submitWithAliases(page, 'www.example.com\nshop.example.com')
  await created

  await expect(page.getByText(/must be a valid hostname/)).toHaveCount(0)
})
