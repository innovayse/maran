import { expect, test } from '../fixtures/guarded-test'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { ALERT_DELIVERY_OK, loaded, stubDashboard, SYNCHRONIZED_CLOCK, UNAVAILABLE } from '../fixtures/stub-dashboard-route'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubLandingReads } from '../fixtures/stub-landing-reads'
import { stubModules } from '../fixtures/stub-modules-route'
import { stubShellReads } from '../fixtures/stub-shell-reads'
import {
  stubPhpVersions,
  type PhpVersionStub,
} from '../fixtures/stub-sites-routes'
import type { Dashboard } from '../../src/types/dashboard'
import type { PanelModule } from '../../src/types/module'
import type { Locator, Page } from '@playwright/test'

// Issue #157: the PHP versions screen shows where each version stands in its support lifecycle as a
// word and a glyph (the word is the panel's, printed as received), counts the sites on each, and
// warns an administrator when sites run on a version that no longer receives security fixes. The
// dashboard's attention block carries the same fact as a row that links to that screen.

const MODULES: PanelModule[] = [
  { name: 'accounts', displayName: 'Accounts', kindDisplayName: 'Core', tier: 'included', state: 'enabled' },
  { name: 'sites', displayName: 'Sites', kindDisplayName: 'Core', tier: 'included', state: 'enabled' },
  { name: 'databases', displayName: 'Databases', kindDisplayName: 'Core', tier: 'included', state: 'enabled' },
]

/** One version in each phase the panel can name, with a status word no locale file of ours holds. */
const VERSIONS: PhpVersionStub[] = [
  {
    version: '8.4',
    defaultStatus: 'default',
    lifecycle: { status: 'supported', statusName: 'Actively supported', endOfLife: '2028-12-31', daysLeft: 784 },
    sitesUsing: 4,
  },
  {
    version: '8.2',
    defaultStatus: 'notDefault',
    lifecycle: { status: 'securityOnly', statusName: 'Security fixes only', endOfLife: '2026-12-31', daysLeft: 54 },
    sitesUsing: 2,
  },
  {
    version: '8.0',
    defaultStatus: 'notDefault',
    lifecycle: { status: 'endOfLife', statusName: 'Support ended', endOfLife: '2023-11-26', daysLeft: null },
    sitesUsing: 3,
  },
  {
    version: '5.6',
    defaultStatus: 'unknown',
    lifecycle: { status: 'unknown', statusName: 'Not in the support table', endOfLife: null, daysLeft: null },
    sitesUsing: 0,
  },
]

/** An administrator's dashboard on which only the end-of-life row needs attention. */
const DASHBOARD: Dashboard = {
  audience: 'administrator',
  resources: UNAVAILABLE,
  services: UNAVAILABLE,
  counts: { accounts: loaded(1), sites: loaded(9), databases: loaded(1) },
  attention: {
    certificatesExpiringSoon: loaded(0),
    failedBackups: loaded(0),
    sitesOnEndOfLifePhp: loaded(3),
    licenceExpiring: loaded(0),
    alertDelivery: ALERT_DELIVERY_OK,
    bannedAddresses: loaded(0),
    failedTasks: loaded(0),
    clock: SYNCHRONIZED_CLOCK,
  },
  recentAudit: loaded([]),
}

/**
 * Signs in as an administrator and stubs the PHP versions the screen reads.
 * @param page The Playwright page under test.
 * @param versions The runtimes the stubbed server reports.
 * @returns Resolves once the routes are installed.
 */
const prepare = async (page: Page, versions: PhpVersionStub[]): Promise<void> => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubShellReads(page)
  // The landing screen's layout and stream; the dashboard read below replaces its default one-shot answer.
  await stubLandingReads(page)
  await stubModules(page, MODULES)
  await stubDashboard(page, DASHBOARD)
  await stubPhpVersions(page, versions)
}

/**
 * Opens the PHP versions screen through the sidebar, as an operator does.
 * @param page The Playwright page under test.
 * @returns Resolves once the screen's heading is on screen.
 */
const openPhpVersions = async (page: Page): Promise<void> => {
  await page.goto('/')
  const nav = page.getByRole('navigation', { name: 'Main navigation' })
  await nav.getByRole('button', { name: 'Show the Sites and domains pages' }).click()
  await nav.getByRole('link', { name: 'PHP versions' }).click()
  await expect(page.getByRole('heading', { level: 1, name: 'PHP versions' })).toBeVisible()
}

test('each version shows the panel status word, and days left only while still supported', async ({ page }) => {
  await prepare(page, VERSIONS)
  await openPhpVersions(page)

  const row = (version: string): Locator => {
    return page.getByRole('row').filter({ hasText: version })
  }

  await expect(row('8.4').getByText('Actively supported')).toBeVisible()
  await expect(row('8.4').getByText('(784 days left)')).toBeVisible()
  await expect(row('8.2').getByText('Security fixes only')).toBeVisible()
  await expect(row('8.2').getByText('(54 days left)')).toBeVisible()
  await expect(row('8.0').getByText('Support ended')).toBeVisible()
  await expect(row('8.0').getByText('days left')).toHaveCount(0)
  await expect(row('5.6').getByText('Not in the support table')).toBeVisible()
})

test('the end of the support is a date, and each version states how many sites run on it', async ({ page }) => {
  await prepare(page, VERSIONS)
  await openPhpVersions(page)

  const row = page.getByRole('row').filter({ hasText: '8.0' })
  await expect(row.getByText('26 Nov 2023')).toBeVisible()
  await expect(row.getByRole('cell', { name: '3', exact: true })).toBeVisible()
})

test('sites on an end-of-life version raise a warning that names the version and links to the sites', async ({
  page,
}) => {
  await prepare(page, VERSIONS)
  await openPhpVersions(page)

  await expect(
    page.getByText('3 sites run on end-of-life PHP versions (8.0) that no longer receive security fixes.', {
      exact: false,
    }),
  ).toBeVisible()
  await expect(page.getByRole('link', { name: 'Open the sites list' })).toBeVisible()
})

test('an end-of-life version nobody uses raises no warning', async ({ page }) => {
  await prepare(page, [
    VERSIONS[0],
    {
      version: '8.0',
      defaultStatus: 'notDefault',
      lifecycle: { status: 'endOfLife', statusName: 'Support ended', endOfLife: '2023-11-26', daysLeft: null },
      sitesUsing: 0,
    },
  ])
  await openPhpVersions(page)

  await expect(page.getByRole('row').filter({ hasText: '8.0' }).getByText('Support ended')).toBeVisible()
  await expect(page.getByText('no longer receive security fixes', { exact: false })).toHaveCount(0)
})

test('the dashboard lists sites on end-of-life PHP and its link opens the PHP versions screen', async ({ page }) => {
  await prepare(page, VERSIONS)
  await page.goto('/')

  await expect(page.getByText('Needs attention')).toBeVisible()
  await page.getByRole('link', { name: '3 sites on end-of-life PHP' }).click()

  await expect(page.getByRole('heading', { level: 1, name: 'PHP versions' })).toBeVisible()
})
