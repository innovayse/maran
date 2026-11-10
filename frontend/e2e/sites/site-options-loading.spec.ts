import { delayRoute } from '../fixtures/delay-route'
import { expect, test } from '../fixtures/guarded-test'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import { stubShellReads } from '../fixtures/stub-shell-reads'
import { OPTIONS_SITE_ID, SITE_OPTIONS_PATTERN, stubSiteOptions } from '../fixtures/stub-site-options-routes'
import { stubPhpVersions, stubSiteDetail, stubSites } from '../fixtures/stub-sites-routes'
import type { PanelModule } from '../../src/types/module'
import type { SiteDetail } from '../../src/types/site'
import type { Page } from '@playwright/test'

// The Options tab while its read is on the way (#243): a slow answer holds the form's place under one
// polite status, and a refusal is an alert with the server's own words, never a skeleton. The shared
// region checks open the screen's own address, and the tab is one click deeper, so the steps are
// written out here.

/** How long the delayed read is held, long enough for the placeholders (150 ms) to show. */
const SLOW_MS = 600

const LICENSED_SITES: PanelModule[] = [{ name: 'sites', displayName: 'Sites', kindDisplayName: 'Core', tier: 'included', state: 'enabled' }]

const SITE: SiteDetail = {
  id: OPTIONS_SITE_ID,
  accountId: '22222222-2222-2222-2222-222222222222',
  domain: 'example.com',
  aliases: [],
  backendType: 'static',
  backendTypeName: 'Static',
  phpVersion: '',
  proxyUpstream: '',
  documentRoot: '/home/alpha/sites/example.com/public',
  hasCertificate: false,
  status: 'enabled',
  statusName: 'Enabled',
  createdAt: '2026-08-01T10:00:00Z',
  logRetentionDays: 14,
}

/** The server's refusal the failure case answers with. */
const REFUSAL = 'The server agent is not answering.'

/**
 * Signs in and stubs the shell and the site's neighbours.
 * @param page The Playwright page.
 * @returns Resolves once the routes are installed.
 */
const stubShell = async (page: Page): Promise<void> => {
  await stubSignedIn(page)
  await stubShellReads(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED_SITES)
  await stubSites(page, [])
  await stubSiteDetail(page, SITE)
  await stubPhpVersions(page, [])
}

test('options tab: a slow answer shows a placeholder under one status, then the form', async ({ page }) => {
  await stubShell(page)
  await stubSiteOptions(page)
  await delayRoute(page, SITE_OPTIONS_PATTERN, SLOW_MS)
  await page.goto(`/sites/${SITE.id}`)
  await page.getByRole('button', { name: 'Options' }).click()

  const statuses = page.getByTestId('loading-status')
  await expect(page.getByRole('main').locator('[data-skeleton]').first()).toBeVisible()
  await expect(statuses.filter({ hasText: 'Loading…' }).first()).toBeAttached()

  await expect(page.getByLabel('Largest upload (MiB)')).toBeVisible()
  await expect(statuses.filter({ hasText: 'Loading…' })).toHaveCount(0)
  await expect(page.locator('[data-skeleton]')).toHaveCount(0)
})

test('options tab: a slow refusal is an alert with the server words, never a skeleton', async ({ page, guard }) => {
  guard.allow(503, /\/api\/v1\/sites\/[^/]+\/options$/)
  await stubShell(page)
  await page.route(SITE_OPTIONS_PATTERN, async (route) => {
    await route.fulfill({
      status: 503,
      contentType: 'application/problem+json',
      body: JSON.stringify({ code: 'AgentUnavailable', detail: REFUSAL, status: 503 }),
    })
  })
  await delayRoute(page, SITE_OPTIONS_PATTERN, SLOW_MS)
  await page.goto(`/sites/${SITE.id}`)
  await page.getByRole('button', { name: 'Options' }).click()

  await expect(page.getByText(REFUSAL)).toBeVisible()
  await expect(page.getByLabel('Largest upload (MiB)')).toHaveCount(0)
  await expect(page.locator('[data-skeleton]')).toHaveCount(0)
})
