import { expect, test } from '@playwright/test'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubSites, stubSitesProblem } from '../fixtures/stub-sites-routes'
import type { PanelModule } from '../../src/types/module'
import type { Site } from '../../src/types/site'

const LICENSED_SITES: PanelModule[] = [
  { name: 'sites', displayName: 'Sites', tier: 'included', isEnabled: true },
]

const ALPHA: Site = {
  id: '11111111-1111-1111-1111-111111111111',
  accountId: '22222222-2222-2222-2222-222222222222',
  domain: 'alpha.example.com',
  backendType: 'php',
  phpVersion: '8.3',
  status: 'enabled',
  createdAt: '2026-08-01T10:00:00Z',
}

const BETA: Site = {
  id: '33333333-3333-3333-3333-333333333333',
  accountId: '22222222-2222-2222-2222-222222222222',
  domain: 'beta.example.com',
  backendType: 'static',
  phpVersion: '',
  status: 'disabled',
  createdAt: '2026-08-02T10:00:00Z',
}

test('sites list shows the empty state when the panel reports no sites', async ({ page }) => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED_SITES)
  await stubSites(page, [])

  await page.goto('/sites')

  await expect(page.getByText('No sites yet')).toBeVisible()
  await expect(page.getByRole('table')).toHaveCount(0)
})

test('sites list renders a row per site with its backend and serving status', async ({ page }) => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED_SITES)
  await stubSites(page, [ALPHA, BETA])

  await page.goto('/sites')

  const rows = page.getByRole('row')
  await expect(rows.filter({ hasText: 'alpha.example.com' })).toContainText('Enabled')
  await expect(rows.filter({ hasText: 'alpha.example.com' })).toContainText('8.3')
  await expect(rows.filter({ hasText: 'beta.example.com' })).toContainText('Disabled')
  await expect(rows.filter({ hasText: 'beta.example.com' })).toContainText('Static')
})

// A screen nothing links to is a screen nobody has: the domain is the only way into the detail
// page, and every action a site has lives there.
test('the site domain links to that site detail page', async ({ page }) => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED_SITES)
  await stubSites(page, [ALPHA])

  await page.goto('/sites')
  await page.getByRole('link', { name: 'alpha.example.com' }).click()

  await expect(page).toHaveURL(new RegExp(`/sites/${ALPHA.id}$`))
})

// The other way in: the sidebar entry the module catalogue drives.
test('the sidebar links to the sites list when the panel licenses the sites module', async ({ page }) => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED_SITES)
  await stubSites(page, [])

  await page.goto('/')
  await page.getByRole('navigation').getByRole('link', { name: 'Sites' }).click()

  await expect(page).toHaveURL(/\/sites$/)
  await expect(page.getByRole('heading', { name: 'Sites' })).toBeVisible()
})

// rules/vue.md: "Error messages are produced by the backend, already localized, and rendered as-is."
test('sites list renders the backend RFC 7807 detail verbatim when the list request fails', async ({
  page,
}) => {
  const backendDetail = 'The sites store is temporarily unavailable. Try again in a moment.'
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED_SITES)
  await stubSitesProblem(page, backendDetail)

  await page.goto('/sites')

  await expect(page.getByRole('status')).toHaveText(backendDetail)
  await expect(page.getByRole('table')).toHaveCount(0)
  await expect(page.getByText('No sites yet')).toHaveCount(0)
})
