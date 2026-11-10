import { expect, test } from '../fixtures/guarded-test'
import { stubAccounts } from '../fixtures/stub-accounts-route'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import { stubShellReads } from '../fixtures/stub-shell-reads'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubPhpVersions, stubSiteDetail, stubSites } from '../fixtures/stub-sites-routes'
import type { PanelModule } from '../../src/types/module'
import type { Site, SiteDetail } from '../../src/types/site'
import type { Page } from '@playwright/test'

// The backend and serving-state words on a site's screens are the panel's own: the rows carry
// them, so the bundle holds no copy of the vocabulary (issue #330).

const LICENSED_SITES: PanelModule[] = [
  { name: 'sites', displayName: 'Sites', kindDisplayName: 'Core', tier: 'included', state: 'enabled' },
]

/** Names no bundle contains, so a screen printing one can only have read it off the panel's answer. */
const SITE: Site = {
  id: '11111111-1111-1111-1111-111111111111',
  accountId: '22222222-2222-2222-2222-222222222222',
  ownerName: 'alpha',
  domain: 'alpha.example.com',
  backendType: 'php',
  backendTypeName: 'Panel-named runtime',
  phpVersion: '8.3',
  status: 'disabled',
  statusName: 'Panel-named paused',
  createdAt: '2026-08-01T10:00:00Z',
  logRetentionDays: 14,
}

const DETAIL: SiteDetail = {
  id: SITE.id,
  accountId: SITE.accountId,
  domain: SITE.domain,
  aliases: [],
  backendType: SITE.backendType,
  backendTypeName: SITE.backendTypeName,
  phpVersion: SITE.phpVersion,
  proxyUpstream: '',
  documentRoot: '/home/alpha/sites/alpha.example.com/public',
  hasCertificate: false,
  status: SITE.status,
  statusName: SITE.statusName,
  createdAt: SITE.createdAt,
  logRetentionDays: 14,
}

/**
 * Installs everything the sites list and a site's detail read.
 * @param page The Playwright page whose network the routes are installed on.
 * @returns Resolves once every route is installed.
 */
const stubScreens = async (page: Page): Promise<void> => {
  await stubSignedIn(page)
  await stubShellReads(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED_SITES)
  await stubAccounts(page, [])
  await stubPhpVersions(page, [{ version: '8.3', defaultStatus: 'default' }])
  await stubSites(page, [SITE])
  await stubSiteDetail(page, DETAIL)
}

test('the sites table prints the backend and the serving state under the names the rows carry', async ({ page }) => {
  await stubScreens(page)

  await page.goto('/sites')

  const row = page.getByRole('row').filter({ hasText: SITE.domain })
  await expect(row.getByText('Panel-named runtime', { exact: true })).toBeVisible()
  await expect(row.getByText('Panel-named paused', { exact: true })).toBeVisible()
})

test('a site opened from the list prints the same panel names in its header and overview', async ({ page }) => {
  await stubScreens(page)
  await page.goto('/sites')

  await page.getByRole('link', { name: SITE.domain }).click()

  await expect(page.getByRole('heading', { name: SITE.domain })).toBeVisible()
  await expect(page.getByText('Panel-named runtime').first()).toBeVisible()
  await expect(page.getByText('Panel-named paused').first()).toBeVisible()
})

test('a link naming a backend or a state the panel does not list is not applied once the lists arrive', async ({
  page,
}) => {
  await stubScreens(page)

  await page.goto('/sites?backend=bogus&status=bogus')

  await expect(page.getByRole('row').filter({ hasText: SITE.domain })).toBeVisible()
  await expect(page.getByTestId('active-filter-chips')).toHaveCount(0)
})

test('a link naming a listed backend is kept as a filter chip', async ({ page }) => {
  await stubScreens(page)

  await page.goto('/sites?backend=php')

  await expect(page.getByTestId('active-filter-chips')).toContainText('Backend')
})
