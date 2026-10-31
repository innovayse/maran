import { expect, test, type Page } from '@playwright/test'
import { stubAccounts } from '../fixtures/stub-accounts-route'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubPhpVersions, stubSiteLimits, stubSites } from '../fixtures/stub-sites-routes'
import type { Account } from '../../src/types/account'
import type { PanelModule } from '../../src/types/module'
import type { SiteLimits } from '../../src/types/site'

const LICENSED_SITES: PanelModule[] = [
  { name: 'sites', displayName: 'Sites', tier: 'included', isEnabled: true },
]

const OWNER: Account = {
  id: '22222222-2222-2222-2222-222222222222',
  name: 'alpha',
  primaryDomain: 'alpha.example.com',
  planId: '44444444-4444-4444-4444-444444444444',
  status: 'active',
  createdAt: '2026-08-01T10:00:00Z',
}

/**
 * Installs everything the create-site form reads, with the given alias limits.
 * @param page The Playwright page whose network the routes are installed on.
 * @param limits The limits the server reports, or null for a server that cannot answer.
 * @returns Resolves once every route is installed.
 */
const stubForm = async (page: Page, limits: SiteLimits | null): Promise<void> => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED_SITES)
  await stubAccounts(page, [OWNER])
  await stubPhpVersions(page, [{ version: '8.3', isDefault: true }])
  await stubSites(page, [])
  await stubSiteLimits(page, limits)
}

/**
 * Distinct aliases, one per line, as the operator would type them.
 * @param count How many aliases.
 * @returns The textarea's text.
 */
const aliasLines = (count: number): string => {
  return Array.from({ length: count }, (_, index) => {
    return `alias${index}.example.com`
  }).join('\n')
}

/**
 * Opens the create form and waits until the limits read has been answered, so an assertion about
 * the count cannot pass merely because nothing had loaded yet.
 * @param page The Playwright page.
 * @returns Resolves once the limits response has arrived.
 */
const openForm = async (page: Page): Promise<void> => {
  await Promise.all([page.waitForResponse('**/api/v1/sites/limits'), page.goto('/sites/new')])
}

// The limit is the server's (GET /api/v1/sites/limits), never a number written into the SPA; a
// small one here keeps the spec readable and proves the form shows whatever the server says.
test('the site form counts aliases against the server limit and accepts exactly the limit', async ({ page }) => {
  await stubForm(page, { maximumAliases: 3 })
  await openForm(page)

  await expect(page.getByText('0 of 3 additional hostnames.')).toBeVisible()

  await page.getByLabel('Aliases').fill(aliasLines(3))

  await expect(page.getByText('3 of 3 additional hostnames.')).toBeVisible()
  await expect(page.getByText('A site can have at most 3 additional hostnames.')).toHaveCount(0)
  await expect(page.getByRole('button', { name: 'Create site' })).toBeEnabled()
})

test('the site form refuses one alias over the server limit and disables creating', async ({ page }) => {
  await stubForm(page, { maximumAliases: 3 })
  await openForm(page)

  await page.getByLabel('Aliases').fill(aliasLines(4))

  await expect(page.getByText('4 of 3 additional hostnames.')).toBeVisible()
  await expect(page.getByText('A site can have at most 3 additional hostnames.')).toBeVisible()
  await expect(page.getByRole('button', { name: 'Create site' })).toBeDisabled()
})

// rules/security.md: a check in the browser is advice and must err permissive. A server that
// cannot state its limit leaves the form open; the server's own refusal is what decides.
test('the site form stays permissive when the server limit cannot be read', async ({ page }) => {
  await stubForm(page, null)
  await openForm(page)

  await page.getByLabel('Aliases').fill(aliasLines(4))

  await expect(page.getByText(/additional hostnames\.$/)).toHaveCount(0)
  await expect(page.getByRole('button', { name: 'Create site' })).toBeEnabled()
})
