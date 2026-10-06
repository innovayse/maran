import { expect, test, type Page } from '@playwright/test'
import { stubSignedIn } from './fixtures/stub-auth-routes'
import { stubDashboard, stubDashboardProblem, WITHHELD_DASHBOARD } from './fixtures/stub-dashboard-route'
import { stubHealthy } from './fixtures/stub-health-route'
import { stubModules } from './fixtures/stub-modules-route'
import type { AuthenticatedUser } from '../src/types/auth'
import type { Dashboard } from '../src/types/dashboard'
import type { PanelModule } from '../src/types/module'

const LICENSED: PanelModule[] = [
  { name: 'accounts', displayName: 'Accounts', tier: 'included', isEnabled: true },
  { name: 'sites', displayName: 'Sites', tier: 'included', isEnabled: true },
  { name: 'databases', displayName: 'Databases', tier: 'included', isEnabled: true },
]

/** A customer, who must be shown the health verdict and no part of the server's state. */
const CUSTOMER: AuthenticatedUser = {
  id: '00000000-0000-0000-0000-000000000099',
  username: 'olive',
  email: 'olive@example.com',
  role: 'customer',
  accountId: '00000000-0000-0000-0000-0000000000aa',
}

// Values chosen so that nothing on screen could be a coincidence of a default: the counts are
// distinct from each other and from the attention numbers, the percentage is not round, and every
// attention row is non-zero so that all four are drawn.
const FULL: Dashboard = {
  isAdministrator: true,
  resources: {
    cpuPercent: 37.4,
    memoryUsedBytes: 3 * 1024 * 1024 * 1024,
    memoryTotalBytes: 8 * 1024 * 1024 * 1024,
    diskUsedBytes: 44 * 1024 * 1024 * 1024,
    diskTotalBytes: 98 * 1024 * 1024 * 1024,
    networkRxBytes: 4_200_000_000_000,
    networkTxBytes: 1_100_000_000_000,
    loadAverage1m: 0.21,
    loadAverage5m: 0.34,
    loadAverage15m: 0.55,
  },
  services: [
    { service: 'webServer', name: 'Web server', state: 'running', detail: 'active (running)' },
    { service: 'database', name: 'Database', state: 'stopped', detail: 'inactive (dead)' },
  ],
  counts: { accounts: 3, sites: 7, databases: 4 },
  attention: { certificatesExpiringSoon: 2, failedBackups: 5, bannedAddresses: 9, failedTasks: 6 },
  recentAudit: [
    {
      id: '11111111-1111-1111-1111-111111111111',
      occurredAt: '2026-10-05T12:00:00+00:00',
      actorUsername: 'admin',
      action: 'AccountCreated',
      actionName: 'Account created',
      subject: 'acme',
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
  ],
}

/** A server with one account and nothing else on it — the state right after an install. */
const FRESHLY_INSTALLED: Dashboard = {
  ...FULL,
  counts: { accounts: 0, sites: 0, databases: 0 },
  attention: { certificatesExpiringSoon: 0, failedBackups: 0, bannedAddresses: 0, failedTasks: 0 },
  recentAudit: [],
}

/**
 * Signs the given user in with the panel healthy and the dashboard answering `dashboard`.
 * @param page The page to install the routes on.
 * @param dashboard What `/api/v1/dashboard` answers.
 * @param user Who is signed in; the administrator by default.
 * @returns Resolves once the landing page has settled.
 */
const openLandingPage = async (page: Page, dashboard: Dashboard, user?: AuthenticatedUser): Promise<void> => {
  await stubModules(page, LICENSED)
  await stubSignedIn(page, user)
  await stubHealthy(page)
  await stubDashboard(page, dashboard)
  await page.goto('/')
}

test.describe('the landing page', () => {
  test('an administrator is shown what the server is doing, holds and needs', async ({ page }) => {
    await openLandingPage(page, FULL)

    // The health verdict first and still: this route's original job, and the one thing it has to
    // say when nothing else can.
    await expect(page.getByText('All systems are operational.')).toBeVisible()

    // The live reading. The processor figure is asserted to one decimal exactly as the card formats
    // it, so a card that silently rounded to a whole number would fail here.
    await expect(page.getByText('37.4%')).toBeVisible()
    await expect(page.getByText('0.21 · 0.34 · 0.55')).toBeVisible()

    // The services, through the same component the monitoring screen uses.
    await expect(page.getByText('Web server')).toBeVisible()
    await expect(page.getByText('Database', { exact: true })).toBeVisible()

    // The counts, each a link to the screen that owns the detail — asserted as links, because a
    // number an operator cannot click through is half the feature. The accessible name carries the
    // number as well as the noun, which also distinguishes the tile from the sidebar's own
    // "Accounts" link; asserting on the number alone would have matched either.
    await expect(page.getByRole('link', { name: '3 Accounts' })).toBeVisible()
    await expect(page.getByRole('link', { name: '7 Sites' })).toBeVisible()
    await expect(page.getByRole('link', { name: '4 Databases' })).toBeVisible()

    // The attention rows, and the journal's newest entries.
    await expect(page.getByText('backups failed')).toBeVisible()
    await expect(page.getByText('Account created')).toBeVisible()
    await expect(page.getByText('Sign-in failed')).toBeVisible()
  })

  test('a count of zero is drawn as a zero and not as an empty state', async ({ page }) => {
    // The state of a server somebody installed five minutes ago, which is when this screen is read
    // for the first time. A blank panel here would read as "failed to load" on exactly the screen
    // where it matters most that it does not.
    await openLandingPage(page, FRESHLY_INSTALLED)

    // "0 Accounts", not an empty state and not an absent tile. The sidebar also links to
    // /accounts, so the number is part of what identifies this one.
    await expect(page.getByRole('link', { name: '0 Accounts' })).toBeVisible()
    await expect(page.getByRole('link', { name: '0 Sites' })).toBeVisible()
  })

  test('a server with nothing wrong says so instead of listing four zeros', async ({ page }) => {
    // The deliberate asymmetry with the counts above: a card whose purpose is to be noticed when it
    // changes must not be a permanent row of zeros, or a reader stops looking at it.
    await openLandingPage(page, FRESHLY_INSTALLED)

    await expect(page.getByText('Nothing needs attention.')).toBeVisible()
    await expect(page.getByText('backups failed')).toHaveCount(0)
  })

  test('a customer sees the verdict and no part of the server state', async ({ page }) => {
    await openLandingPage(page, WITHHELD_DASHBOARD, CUSTOMER)

    await expect(page.getByText('All systems are operational.')).toBeVisible()

    // Asserted by absence of the section headings rather than of a number: a count that happened to
    // be zero would satisfy a weaker check while the section was still on screen.
    await expect(page.getByText('On this server')).toHaveCount(0)
    await expect(page.getByText('Right now')).toHaveCount(0)
    await expect(page.getByText('Needs attention')).toHaveCount(0)
    await expect(page.getByText('Latest activity')).toHaveCount(0)
  })

  test('a dashboard the panel refused is said out loud, not left as an absence', async ({ page }) => {
    // An administrator who sees only the health sentence cannot tell a refused read from the defect
    // this page was built to fix, so the panel's own message is rendered.
    await stubModules(page, LICENSED)
    await stubSignedIn(page)
    await stubHealthy(page)
    await stubDashboardProblem(page, 'The agent is not answering on this server.')
    await page.goto('/')

    await expect(page.getByText('All systems are operational.')).toBeVisible()
    await expect(page.getByText('The agent is not answering on this server.')).toBeVisible()
  })

  test('one unreadable section leaves the rest of the screen standing', async ({ page }) => {
    // What the endpoint's per-section isolation is for: the agent is down, so the live reading and
    // the services are gone, while the counts — which come from the database — are still there.
    await openLandingPage(page, { ...FULL, resources: null, services: [] })

    await expect(page.getByText('Right now')).toHaveCount(0)
    await expect(page.getByText('On this server')).toBeVisible()
    await expect(page.getByText('Account created')).toBeVisible()
  })
})
