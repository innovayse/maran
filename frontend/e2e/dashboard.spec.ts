import { expect, test, type Page } from '@playwright/test'
import { stubSignedIn } from './fixtures/stub-auth-routes'
import {
  loaded,
  stubDashboard,
  stubDashboardProblem,
  SYNCHRONIZED_CLOCK,
  UNAVAILABLE,
  WITHHELD_DASHBOARD,
} from './fixtures/stub-dashboard-route'
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
  counts: {
    accounts: loaded(3),
    sites: loaded(7),
    databases: loaded(4),
  },
  attention: { certificatesExpiringSoon: loaded(2), failedBackups: loaded(5), bannedAddresses: loaded(9), failedTasks: loaded(6), clock: SYNCHRONIZED_CLOCK },
  recentAudit: loaded([
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
  ]),
}

/** A server with one account and nothing else on it — the state right after an install. */
const FRESHLY_INSTALLED: Dashboard = {
  ...FULL,
  counts: {
    accounts: loaded(0),
    sites: loaded(0),
    databases: loaded(0),
  },
  attention: { certificatesExpiringSoon: loaded(0), failedBackups: loaded(0), bannedAddresses: loaded(0), failedTasks: loaded(0), clock: SYNCHRONIZED_CLOCK },
  recentAudit: loaded([]),
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

  test('a count the panel could not take reads unavailable, never zero', async ({ page }) => {
    await openLandingPage(page, {
      ...FULL,
      counts: {
        accounts: loaded(3),
        sites: UNAVAILABLE,
        databases: loaded(4),
      },
    })

    // A zero would claim the server holds no sites; the module only failed to answer.
    await expect(page.getByRole('link', { name: 'Unavailable Sites' })).toBeVisible()
    await expect(page.getByRole('link', { name: '0 Sites' })).toHaveCount(0)
    await expect(page.getByRole('link', { name: '3 Accounts' })).toBeVisible()
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
    // the services could not be read, while the counts — which come from the database — are still
    // there. Each unreadable section says so in its own place instead of vanishing (#248).
    await openLandingPage(page, { ...FULL, resources: UNAVAILABLE, services: UNAVAILABLE })

    await expect(page.getByText('The live reading could not be taken.')).toBeVisible()
    await expect(page.getByText('37.4%')).toHaveCount(0)
    await expect(page.getByText('The services could not be read.')).toBeVisible()
    await expect(page.getByText('On this server')).toBeVisible()
    await expect(page.getByText('Account created')).toBeVisible()
  })

  test('an attention source the panel could not read is named, and is not an all-clear', async ({ page }) => {
    // #248: the backups count could not be taken and every other source holds zero. Counted as zero,
    // this screen would say "Nothing needs attention." about backups nobody checked.
    await openLandingPage(page, {
      ...FRESHLY_INSTALLED,
      attention: {
        certificatesExpiringSoon: loaded(0),
        failedBackups: UNAVAILABLE,
        bannedAddresses: loaded(0),
        failedTasks: loaded(0),
        clock: SYNCHRONIZED_CLOCK,
      },
    })

    await expect(page.getByText('Needs attention')).toBeVisible()
    await expect(page.getByRole('link', { name: 'Backups could not be checked' })).toBeVisible()
    await expect(page.getByText('Nothing needs attention.')).toHaveCount(0)

    // Only the unread source is named: the three that were read and hold zero stay quiet.
    await expect(page.getByText('Certificates could not be checked')).toHaveCount(0)
    await expect(page.getByText('Tasks could not be checked')).toHaveCount(0)
  })

  test('an unread source is named beside the counts that need attention', async ({ page }) => {
    await openLandingPage(page, {
      ...FULL,
      attention: {
        certificatesExpiringSoon: loaded(2),
        failedBackups: loaded(5),
        bannedAddresses: loaded(9),
        failedTasks: UNAVAILABLE,
        clock: SYNCHRONIZED_CLOCK,
      },
    })

    await expect(page.getByText('backups failed')).toBeVisible()
    await expect(page.getByText('Tasks could not be checked')).toBeVisible()
    await expect(page.getByText('tasks failed')).toHaveCount(0)
  })

  test('a journal the panel could not read says so, and not that nothing happened', async ({ page }) => {
    await openLandingPage(page, { ...FULL, recentAudit: UNAVAILABLE })

    await expect(page.getByText('The audit journal could not be read.')).toBeVisible()
    await expect(page.getByText('Nothing has been recorded yet.')).toHaveCount(0)
  })

  test('a clock that is not synchronised needs attention, and is not an all-clear', async ({ page }) => {
    // #150: every count holds zero and only the clock is wrong. Left out of the notice, this screen
    // would say "Nothing needs attention." while two-factor codes fail.
    await openLandingPage(page, {
      ...FRESHLY_INSTALLED,
      attention: {
        certificatesExpiringSoon: loaded(0),
        failedBackups: loaded(0),
        bannedAddresses: loaded(0),
        failedTasks: loaded(0),
        clock: loaded({ state: 'syncServiceDisabled', description: 'No time synchronisation service is enabled.' }),
      },
    })

    await expect(page.getByRole('link', { name: 'Server clock not synchronised' })).toHaveAttribute(
      'href',
      '/monitoring',
    )
    await expect(page.getByText('Nothing needs attention.')).toHaveCount(0)
  })

  test('a clock the panel could not read is named as unchecked, never as synchronised', async ({ page }) => {
    await openLandingPage(page, {
      ...FRESHLY_INSTALLED,
      attention: {
        certificatesExpiringSoon: loaded(0),
        failedBackups: loaded(0),
        bannedAddresses: loaded(0),
        failedTasks: loaded(0),
        clock: UNAVAILABLE,
      },
    })

    await expect(page.getByRole('link', { name: 'The clock could not be checked' })).toBeVisible()
    await expect(page.getByText('Server clock not synchronised')).toHaveCount(0)
    await expect(page.getByText('Nothing needs attention.')).toHaveCount(0)
  })
})
