import { expect, test, type Page } from '@playwright/test'
import { stubAccounts } from '../fixtures/stub-accounts-route'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubCreateSiteProblem, stubPhpVersions, stubSites } from '../fixtures/stub-sites-routes'
import type { Account } from '../../src/types/account'
import type { PanelModule } from '../../src/types/module'

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
 * Installs everything the create-site form reads: the signed-in session, the shell's health and
 * module catalogue, the account picker's accounts and the runtime picker's installed versions.
 * @param page The Playwright page whose network the routes are installed on.
 * @returns Resolves once every route is installed.
 */
const stubFormDependencies = async (page: Page): Promise<void> => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED_SITES)
  await stubAccounts(page, [OWNER])
  await stubPhpVersions(page, [
    { version: '8.2', isDefault: false },
    { version: '8.3', isDefault: true },
  ])
}

test('the site form offers a PHP version only once the backend is PHP', async ({ page }) => {
  await stubFormDependencies(page)
  await stubSites(page, [])

  await Promise.all([
    // Bounded: without waiting for the runtimes to arrive, "no PHP field" would pass simply
    // because nothing had loaded yet, and the mutation that always shows it would survive.
    page.waitForResponse('**/php-versions**'),
    page.goto('/sites/new'),
  ])

  await expect(page.getByRole('combobox', { name: 'Backend' })).toBeVisible()
  await expect(page.getByRole('combobox', { name: 'PHP version' })).toHaveCount(0)

  await page.getByRole('combobox', { name: 'Backend' }).click()
  await page.getByRole('option', { name: 'PHP', exact: true }).click()

  await expect(page.getByText('Only versions installed on this server can be selected.')).toBeVisible()
  await expect(page.getByText('Upstream requests are forwarded')).toHaveCount(0)
})

test('the site form offers an upstream only once the backend is a reverse proxy', async ({ page }) => {
  await stubFormDependencies(page)
  await stubSites(page, [])

  await page.goto('/sites/new')
  await page.getByRole('combobox', { name: 'Backend' }).click()
  await page.getByRole('option', { name: 'Reverse proxy' }).click()

  await expect(page.getByLabel('Upstream')).toBeVisible()
  await expect(page.getByText('Only versions installed on this server can be selected.')).toHaveCount(0)
})

// The server's ProxyUpstream rule is a host or host:port with no scheme. The client mirrors it, so
// a pasted URL is refused here rather than after a round trip.
test('the site form refuses an upstream carrying a scheme, as the server would', async ({ page }) => {
  await stubFormDependencies(page)
  await stubSites(page, [])

  await page.goto('/sites/new')
  await page.getByLabel('Domain').fill('proxied.example.com')
  await page.getByRole('combobox', { name: 'Account' }).click()
  await page.getByRole('option', { name: /alpha/ }).click()
  await page.getByRole('combobox', { name: 'Backend' }).click()
  await page.getByRole('option', { name: 'Reverse proxy' }).click()
  await page.getByLabel('Upstream').fill('http://127.0.0.1:3000')
  await page.getByRole('button', { name: 'Create site' }).click()

  await expect(page.getByText('Upstream must be a host or host:port, with no scheme or path.')).toBeVisible()
  await expect(page).toHaveURL(/\/sites\/new$/)
})

// The browser must never validate: `UiForm` renders `novalidate`, so an empty required field
// reaches the page's own validation instead of a browser bubble in the browser's language.
test('the site form validates an empty domain itself rather than letting the browser do it', async ({
  page,
}) => {
  await stubFormDependencies(page)
  await stubSites(page, [])

  await page.goto('/sites/new')
  await page.getByRole('button', { name: 'Create site' }).click()

  await expect(page.getByText('Domain is required.')).toBeVisible()
  await expect(page.getByText('Choose the account that will own the site.')).toBeVisible()
})

// rules/vue.md: the backend owns the text of its own refusals.
test('the site form renders the server rejection verbatim', async ({ page }) => {
  const backendDetail = 'A site already serves alpha.example.com on this server.'
  await stubFormDependencies(page)
  await stubCreateSiteProblem(page, backendDetail)

  await page.goto('/sites/new')
  await page.getByRole('combobox', { name: 'Account' }).click()
  await page.getByRole('option', { name: /alpha/ }).click()
  await page.getByLabel('Domain').fill('alpha.example.com')
  await page.getByRole('button', { name: 'Create site' }).click()

  await expect(page.getByText(backendDetail)).toBeVisible()
  await expect(page).toHaveURL(/\/sites\/new$/)
})
