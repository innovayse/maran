import { expect, test, type Page } from '@playwright/test'
import { expectNoSeriousViolations, seriousViolations } from '../fixtures/expect-no-serious-violations'
import { stubAccounts } from '../fixtures/stub-accounts-route'
import { stubAlertSettings } from '../fixtures/stub-alert-routes'
import { stubSignedIn, stubSignedOut } from '../fixtures/stub-auth-routes'
import { ALERT_DELIVERY_OK, loaded, stubDashboard, SYNCHRONIZED_CLOCK } from '../fixtures/stub-dashboard-route'
import { stubDatabases } from '../fixtures/stub-databases-routes'
import { stubDatabaseUsers } from '../fixtures/stub-database-users-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubEmptyModules, stubModules } from '../fixtures/stub-modules-route'
import { feedEntry, feedState, stubFirstRun, stubNotificationFeed } from '../fixtures/stub-notification-routes'
import { stubPendingUpdates } from '../fixtures/stub-monitoring-routes'
import { stubPlans } from '../fixtures/stub-plans-route'
import { RESELLER_USER, stubMyReseller, stubResellerSession, stubViewAsChain } from '../fixtures/stub-reseller-session'
import { ACME_HOSTING, stubResellers } from '../fixtures/stub-resellers-routes'
import { stubShellReads } from '../fixtures/stub-shell-reads'
import { stubPhpVersions, stubSites } from '../fixtures/stub-sites-routes'
import type { Account } from '../../src/types/account'
import type { Dashboard } from '../../src/types/dashboard'
import type { FirstRunChecklist } from '../../src/types/firstRun'
import type { Database } from '../../src/types/database'
import type { PanelModule } from '../../src/types/module'
import type { Site } from '../../src/types/site'
import type { ViewingAs } from '../../src/types/viewAs'

// Issue #149 (and #179 for the reseller screens): axe runs on the sign-in screen, the dashboard and the main lists, and finds no
// serious or critical violation. Each screen is scanned with real rows in it, in both themes,
// because a contrast failure lives in one theme's tokens and an empty list draws no table at all.

const LICENSED: PanelModule[] = [
  { name: 'accounts', displayName: 'Accounts', kindDisplayName: 'Core', tier: 'included', state: 'enabled' },
  { name: 'sites', displayName: 'Sites', kindDisplayName: 'Core', tier: 'included', state: 'enabled' },
  { name: 'databases', displayName: 'Databases', kindDisplayName: 'Core', tier: 'included', state: 'enabled' },
  { name: 'monitoring', displayName: 'Monitoring', kindDisplayName: 'Core', tier: 'included', state: 'enabled' },
]

const ALICE: Account = {
  id: '22222222-2222-2222-2222-222222222222',
  name: 'alice',
  primaryDomain: 'alice.example.com',
  planId: '44444444-4444-4444-4444-444444444444',
  status: 'active',
  statusName: 'Active',
  createdAt: '2026-08-01T10:00:00Z',
  resellerId: null,
  resellerName: null,
}

const SUSPENDED: Account = {
  ...ALICE,
  id: '55555555-5555-5555-5555-555555555555',
  name: 'bob',
  primaryDomain: 'bob.example.com',
  status: 'suspended',
  statusName: 'Suspended',
}

const ALPHA: Site = {
  id: '11111111-1111-1111-1111-111111111111',
  accountId: ALICE.id,
  ownerName: ALICE.name,
  domain: 'alpha.example.com',
  backendType: 'php',
  backendTypeName: 'PHP',
  phpVersion: '8.3',
  status: 'enabled',
  statusName: 'Enabled',
  createdAt: '2026-08-01T10:00:00Z',
  logRetentionDays: 14,
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
  audience: 'administrator',
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
    sitesOnEndOfLifePhp: loaded(0),
    alertDelivery: ALERT_DELIVERY_OK,
    bannedAddresses: loaded(3),
    failedTasks: loaded(1),
    clock: SYNCHRONIZED_CLOCK,
  },
  recentAudit: loaded([
    {
      id: '11111111-1111-1111-1111-111111111111',
      occurredAt: '2026-10-05T12:00:00+00:00',
      actorUsername: 'admin',
      impersonatorUsername: null,
      action: 'AccountCreated',
      actionName: 'Account created',
      subject: 'alice',
      ipAddress: '203.0.113.9',
      succeeded: true,
      outcomeName: 'Succeeded',
    },
    {
      id: '22222222-2222-2222-2222-222222222222',
      occurredAt: '2026-10-05T11:00:00+00:00',
      actorUsername: 'admin',
      impersonatorUsername: null,
      action: 'SignInFailed',
      actionName: 'Sign-in failed',
      subject: 'admin',
      ipAddress: '203.0.113.9',
      succeeded: false,
      outcomeName: 'Failed',
    },
  ]),
}

/** An unfinished first-run checklist, so the dashboard draws its card and axe has it to scan. */
const FIRST_RUN: FirstRunChecklist = {
  complete: false,
  steps: [
    { key: 'plan', done: loaded(true), routeName: 'plans' },
    { key: 'account', done: loaded(false), routeName: 'accounts' },
    { key: 'site', done: loaded(false), routeName: 'sites' },
    { key: 'backupDestination', done: loaded(false), routeName: 'backupDestinations' },
    { key: 'certificate', done: { state: 'unavailable' }, routeName: 'certificates' },
  ],
}

/** What the panel reports while an administrator views as the reseller. */
const VIEWING_RESELLER: ViewingAs = {
  kind: 'reseller',
  subjectId: ACME_HOSTING.id,
  subjectName: ACME_HOSTING.name,
  username: RESELLER_USER.username,
  operatorUsername: 'admin',
  expiresAt: '2099-01-01T00:15:00+00:00',
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
  await stubResellers(page)
  await stubSites(page, [ALPHA])
  await stubPhpVersions(page, [])
  await stubDatabases(page, [SHOP])
  await stubDatabaseUsers(page, [
    {
      id: 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
      accountId: ALICE.id,
      ownerName: ALICE.name,
      nameSuffix: 'reports',
      fullName: 'alice_reports',
      isDedicated: false,
      grants: [],
      createdAt: '2026-08-02T10:00:00Z',
    },
  ])
  await stubPendingUpdates(page, [
    { packageName: 'openssl', availableVersion: '3.0.13-0ubuntu3.5', installedVersion: '3.0.13-0ubuntu3.4', source: 'noble-security', classification: 'security' },
    { packageName: 'bash', availableVersion: '5.2.21-2ubuntu4', installedVersion: null, source: '', classification: 'unmeasured' },
  ], null, { securityCount: 1, reboot: true })
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

    test('the dashboard with its first-run checklist has no serious accessibility violation', async ({ page }) => {
      await signInWithData(page)
      await stubFirstRun(page, FIRST_RUN)

      await page.goto('/')
      await expect(page.getByTestId('first-run-checklist')).toBeVisible()

      await expectNoSeriousViolations(page)
    })

    test('the open notification feed has no serious accessibility violation', async ({ page }) => {
      await signInWithData(page)
      await stubNotificationFeed(
        page,
        feedState([
          feedEntry({ id: '00000000-0000-0000-0000-000000000001', title: 'The scheduled backup of alice failed', detail: 'The destination refused the upload.', deepLink: 'backups' }),
          feedEntry({ id: '00000000-0000-0000-0000-000000000002', severity: 'warning', source: 'alert', title: 'Disk usage is above 90%', detail: '93% of 98 GB is in use.' }),
          feedEntry({ id: '00000000-0000-0000-0000-000000000003', severity: 'info', source: 'alert', title: 'Disk usage is back below 90%' }),
        ]),
      )

      await page.goto('/')
      await page.getByRole('banner').getByRole('button', { name: /^Notifications/ }).click()
      await expect(page.getByRole('dialog', { name: 'Notifications' }).getByTestId('notification-row')).toHaveCount(3)

      await expectNoSeriousViolations(page)
    })

    test('the alert settings have no serious accessibility violation', async ({ page }) => {
      await signInWithData(page)
      await stubAlertSettings(page)

      await page.goto('/settings/alerts')
      await expect(page.getByRole('heading', { level: 1, name: 'Alert settings' })).toBeVisible()

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

    test('the database users list has no serious accessibility violation', async ({ page }) => {
      await signInWithData(page)

      await page.goto('/databases/users')
      await expect(page.getByRole('cell', { name: 'alice_reports', exact: true })).toBeVisible()

      await expectNoSeriousViolations(page)
    })

    test('the resellers list has no serious accessibility violation', async ({ page }) => {
      await signInWithData(page)

      await page.goto('/resellers')
      await expect(page.getByRole('link', { name: 'acmehosting', exact: true })).toBeVisible()
      await expect(page.getByRole('link', { name: 'bravohosting', exact: true })).toBeVisible()

      await expectNoSeriousViolations(page)
    })

    test('the reseller overview has no serious accessibility violation', async ({ page }) => {
      await stubResellerSession(page, ACME_HOSTING, LICENSED)

      await page.goto('/reseller')
      await expect(page.getByRole('heading', { level: 1, name: 'Your reseller account' })).toBeVisible()
      await expect(page.getByText('Disk per account (MB)')).toBeVisible()

      await expectNoSeriousViolations(page)
    })

    test('a page viewed as a reseller, with its banner, has no serious accessibility violation', async ({ page }) => {
      await signInWithData(page)
      await stubShellReads(page)
      await stubMyReseller(page)
      await stubViewAsChain(page, { viewedAs: RESELLER_USER, viewing: VIEWING_RESELLER })

      await page.goto('/resellers')
      await page.getByRole('link', { name: 'acmehosting', exact: true }).click()
      await page.getByRole('button', { name: 'View as this reseller' }).click()
      await expect(page.getByRole('region', { name: 'Viewing as another reseller' })).toBeVisible()
      await expect(page.getByRole('heading', { level: 1, name: 'Your reseller account' })).toBeVisible()

      await expectNoSeriousViolations(page)
    })

    test('the pending updates list has no serious accessibility violation', async ({ page }) => {
      await signInWithData(page)

      await page.goto('/servers/updates')
      await expect(page.getByRole('cell', { name: 'openssl', exact: true })).toBeVisible()

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
