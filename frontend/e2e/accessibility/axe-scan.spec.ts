import { expect, test, type Page } from '@playwright/test'
import { expectNoSeriousViolations, seriousViolations } from '../fixtures/expect-no-serious-violations'
import { stubAccounts } from '../fixtures/stub-accounts-route'
import { stubSignedIn, stubSignedOut } from '../fixtures/stub-auth-routes'
import { loaded, stubDashboard, SYNCHRONIZED_CLOCK } from '../fixtures/stub-dashboard-route'
import { stubDatabases } from '../fixtures/stub-databases-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubEmptyModules, stubModules } from '../fixtures/stub-modules-route'
import { stubPlans } from '../fixtures/stub-plans-route'
import { stubPhpVersions, stubSites } from '../fixtures/stub-sites-routes'
import type { Account } from '../../src/types/account'
import type { Dashboard } from '../../src/types/dashboard'
import type { Database } from '../../src/types/database'
import type { PanelModule } from '../../src/types/module'
import type { Site } from '../../src/types/site'

// Issue #149: axe runs on the sign-in screen, the dashboard and the main lists, and finds no
// serious or critical violation. Each screen is scanned with real rows in it, in both themes,
// because a contrast failure lives in one theme's tokens and an empty list draws no table at all.

const LICENSED: PanelModule[] = [
  { name: 'accounts', displayName: 'Accounts', tier: 'included', isEnabled: true },
  { name: 'sites', displayName: 'Sites', tier: 'included', isEnabled: true },
  { name: 'databases', displayName: 'Databases', tier: 'included', isEnabled: true },
]

const ALICE: Account = {
  id: '22222222-2222-2222-2222-222222222222',
  name: 'alice',
  primaryDomain: 'alice.example.com',
  planId: '44444444-4444-4444-4444-444444444444',
  status: 'active',
  createdAt: '2026-08-01T10:00:00Z',
}

const SUSPENDED: Account = {
  ...ALICE,
  id: '55555555-5555-5555-5555-555555555555',
  name: 'bob',
  primaryDomain: 'bob.example.com',
  status: 'suspended',
}

const ALPHA: Site = {
  id: '11111111-1111-1111-1111-111111111111',
  accountId: ALICE.id,
  ownerName: ALICE.name,
  domain: 'alpha.example.com',
  backendType: 'php',
  phpVersion: '8.3',
  status: 'enabled',
  createdAt: '2026-08-01T10:00:00Z',
}

const SHOP: Database = {
  id: '11111111-1111-1111-1111-111111111111',
  accountId: ALICE.id,
  ownerName: ALICE.name,
  name: 'shop',
  fullName: 'alice_shop',
  dbUserName: 'alice_shopuser',
  createdAt: '2026-08-01T10:00:00Z',
}

const DASHBOARD: Dashboard = {
  isAdministrator: true,
  resources: loaded({
    cpuPercent: 37.4,
    memoryUsedBytes: 3 * 1024 * 1024 * 1024,
    memoryTotalBytes: 8 * 1024 * 1024 * 1024,
    diskUsedBytes: 44 * 1024 * 1024 * 1024,
    diskReservedBytes: 5 * 1024 * 1024 * 1024,
    diskAvailableBytes: 49 * 1024 * 1024 * 1024,
    diskTotalBytes: 98 * 1024 * 1024 * 1024,
    networkRxBytes: 4_200_000_000_000,
    networkTxBytes: 1_100_000_000_000,
    loadAverage1m: 0.21,
    loadAverage5m: 0.34,
    loadAverage15m: 0.55,
  }),
  services: loaded([
    { service: 'webServer', name: 'Web server', state: 'running', detail: 'active (running)' },
    { service: 'database', name: 'Database', state: 'stopped', detail: 'inactive (dead)' },
  ]),
  counts: { accounts: loaded(2), sites: loaded(1), databases: loaded(1) },
  attention: {
    certificatesExpiringSoon: loaded(2),
    failedBackups: loaded(1),
    bannedAddresses: loaded(3),
    failedTasks: loaded(1),
    clock: SYNCHRONIZED_CLOCK,
  },
  recentAudit: loaded([
    {
      id: '11111111-1111-1111-1111-111111111111',
      occurredAt: '2026-10-05T12:00:00+00:00',
      actorUsername: 'admin',
      action: 'AccountCreated',
      actionName: 'Account created',
      subject: 'alice',
      ipAddress: '203.0.113.9',
      succeeded: true,
    },
    {
      id: '22222222-2222-2222-2222-222222222222',
      occurredAt: '2026-10-05T11:00:00+00:00',
      actorUsername: 'admin',
      action: 'SignInFailed',
      actionName: 'Sign-in failed',
      subject: 'admin',
      ipAddress: '203.0.113.9',
      succeeded: false,
    },
  ]),
}

/**
 * Signs an administrator in with the three list modules licensed and every screen's data stubbed.
 * @param page The Playwright page under test.
 * @returns Resolves once the routes are installed.
 */
const signInWithData = async (page: Page): Promise<void> => {
  await page.setViewportSize({ width: 1440, height: 900 })
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubDashboard(page, DASHBOARD)
  await stubPlans(page)
  await stubAccounts(page, [ALICE, SUSPENDED])
  await stubSites(page, [ALPHA])
  await stubPhpVersions(page, [])
  await stubDatabases(page, [SHOP])
}

for (const theme of ['dark', 'light'] as const) {
  test.describe(`in the ${theme} theme`, () => {
    test.use({ colorScheme: theme })

    test('the sign-in screen has no serious accessibility violation', async ({ page }) => {
      await stubSignedOut(page)
      await stubHealthy(page)
      await stubEmptyModules(page)

      await page.goto('/login')
      await expect(page.getByRole('heading', { level: 2, name: 'Sign in' })).toBeVisible()

      await expectNoSeriousViolations(page)
    })

    test('the dashboard has no serious accessibility violation', async ({ page }) => {
      await signInWithData(page)

      await page.goto('/')
      await expect(page.getByText('Web server')).toBeVisible()

      await expectNoSeriousViolations(page)
    })

    test('the accounts list has no serious accessibility violation', async ({ page }) => {
      await signInWithData(page)

      await page.goto('/accounts')
      await expect(page.getByRole('cell', { name: 'alice', exact: true })).toBeVisible()

      await expectNoSeriousViolations(page)
    })

    test('the sites list has no serious accessibility violation', async ({ page }) => {
      await signInWithData(page)

      await page.goto('/sites')
      await expect(page.getByRole('table').getByText('alpha.example.com')).toBeVisible()

      await expectNoSeriousViolations(page)
    })

    test('the databases list has no serious accessibility violation', async ({ page }) => {
      await signInWithData(page)

      await page.goto('/databases')
      await expect(page.getByRole('cell', { name: 'alice_shop', exact: true })).toBeVisible()

      await expectNoSeriousViolations(page)
    })
  })
}

test('the scan finds a violation planted on a clean screen', async ({ page }) => {
  // The positive control: without it, a scan that had stopped looking would pass every test above.
  await stubSignedOut(page)
  await stubHealthy(page)
  await stubEmptyModules(page)
  await page.goto('/login')
  await expect(page.getByRole('heading', { level: 2, name: 'Sign in' })).toBeVisible()
  expect(await seriousViolations(page)).toEqual([])

  // An image with no text alternative is a `critical` finding under WCAG 1.1.1.
  await page.evaluate(() => {
    const image = document.createElement('img')
    image.src = '/favicon.svg'
    document.querySelector('main')?.append(image)
  })

  const found = await seriousViolations(page)
  expect(found.some((line) => {return line.startsWith('image-alt')})).toBe(true)
})
