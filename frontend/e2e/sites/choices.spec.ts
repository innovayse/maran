import { expect, test, type Page } from '@playwright/test'
import { stubAccounts } from '../fixtures/stub-accounts-route'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubPhpVersions, stubSiteChoices, stubSites } from '../fixtures/stub-sites-routes'
import type { Account } from '../../src/types/account'
import type { PanelModule } from '../../src/types/module'
import type { Site, SiteChoices } from '../../src/types/site'

const LICENSED_SITES: PanelModule[] = [
  { name: 'sites', displayName: 'Sites', tier: 'included', state: 'enabled' },
]

const OWNER: Account = {
  id: '22222222-2222-2222-2222-222222222222',
  name: 'alpha',
  primaryDomain: 'alpha.example.com',
  planId: '44444444-4444-4444-4444-444444444444',
  status: 'active',
  statusName: 'Active',
  createdAt: '2026-08-01T10:00:00Z',
}

/**
 * Labels no bundle contains, so an option carrying one can only have come from the panel's answer.
 * The wire values stay the ones the server accepts.
 */
const PANEL_CHOICES: SiteChoices = {
  backendTypes: [
    { value: 'static', label: 'Panel-labelled static' },
    { value: 'php', label: 'Panel-labelled PHP' },
    { value: 'reverseProxy', label: 'Panel-labelled proxy' },
  ],
  statuses: [
    { value: 'enabled', label: 'Panel-labelled serving' },
    { value: 'disabled', label: 'Panel-labelled paused' },
  ],
}

/** One site, so the list shows its toolbar: with no site at all the list is the empty state, which has no filters. */
const ONE_SITE: Site = {
  id: '33333333-3333-3333-3333-333333333333',
  accountId: OWNER.id,
  ownerName: OWNER.name,
  domain: 'alpha.example.com',
  backendType: 'static',
  backendTypeName: 'Panel-labelled static',
  phpVersion: '',
  status: 'enabled',
  statusName: 'Panel-labelled serving',
  createdAt: '2026-08-30T10:00:00Z',
}

/**
 * Installs everything the site screens read, with the panel answering the given choices.
 * @param page The Playwright page whose network the routes are installed on.
 * @returns Resolves once every route is installed.
 */
const stubScreens = async (page: Page): Promise<void> => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED_SITES)
  await stubAccounts(page, [OWNER])
  await stubPhpVersions(page, [{ version: '8.3', defaultStatus: 'default' }])
  await stubSites(page, [ONE_SITE])
  // After stubSites, whose own default answer this replaces.
  await stubSiteChoices(page, PANEL_CHOICES)
}

test('the site form offers the backends the panel lists, under the panel\'s own labels', async ({ page }) => {
  await stubScreens(page)
  await Promise.all([page.waitForResponse('**/api/v1/sites/choices'), page.goto('/sites/new')])

  await page.getByRole('combobox', { name: 'Backend' }).click()

  await expect(page.getByRole('option', { name: 'Panel-labelled static' })).toBeVisible()
  await expect(page.getByRole('option', { name: 'Panel-labelled PHP' })).toBeVisible()
  await expect(page.getByRole('option', { name: 'Panel-labelled proxy' })).toBeVisible()
  // Control: the bundle's own English names are gone, so the options really came from the answer.
  await expect(page.getByRole('option', { name: 'Reverse proxy' })).toHaveCount(0)

  await page.getByRole('option', { name: 'Panel-labelled proxy' }).click()
  await expect(page.getByRole('textbox', { name: 'Upstream' })).toBeVisible()
})

test('the sites filter sheet offers the backends and states the panel lists', async ({ page }) => {
  await stubScreens(page)
  await Promise.all([page.waitForResponse('**/api/v1/sites/choices'), page.goto('/sites')])

  await page.getByTestId('filter-trigger').click()
  const dialog = page.getByRole('dialog', { name: 'Filters' })

  await dialog.getByRole('combobox', { name: 'Backend' }).click()
  await expect(page.getByRole('option', { name: 'Panel-labelled proxy' })).toBeVisible()
  await page.getByRole('option', { name: 'Panel-labelled static' }).click()

  await dialog.getByRole('combobox', { name: 'Status' }).click()
  await page.getByRole('option', { name: 'Panel-labelled paused' }).click()
  await dialog.getByRole('button', { name: 'Apply' }).click()

  const chips = page.getByTestId('active-filter-chips')
  await expect(chips).toContainText('Backend: Panel-labelled static')
  await expect(chips).toContainText('Status: Panel-labelled paused')
})
