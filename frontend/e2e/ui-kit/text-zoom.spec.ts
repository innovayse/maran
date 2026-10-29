import { expect, test, type Page } from '@playwright/test'
import { stubAccounts } from '../fixtures/stub-accounts-route'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubBackupDestinations } from '../fixtures/stub-backup-destination-routes'
import { stubBackupSchedules } from '../fixtures/stub-backup-schedule-routes'
import { stubBackups } from '../fixtures/stub-backups-routes'
import { stubDashboard } from '../fixtures/stub-dashboard-route'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import { stubPlans, THREE_PLANS } from '../fixtures/stub-plans-route'
import { stubPhpVersions, stubSites } from '../fixtures/stub-sites-routes'
import { stubTasks } from '../fixtures/stub-tasks-routes'
import type { Account } from '../../src/types/account'
import type { Backup } from '../../src/types/backup'
import type { Dashboard } from '../../src/types/dashboard'
import type { PanelModule } from '../../src/types/module'
import type { Site } from '../../src/types/site'

/*
 * Text-only zoom, or a large system font: `html { font-size: 200% }` on a 1280px window.
 *
 * Why this is its own spec: it is the one situation in which the viewport lies. Every rem-sized
 * thing doubles — the sidebar above all — so the content panel is left about 700px wide while the
 * viewport still reports 1280px and every viewport breakpoint (`lg:`) still believes the screen is
 * wide. Layouts chosen by those breakpoints then put a title beside its actions, or two cards side
 * by side, inside a column too narrow for either: the page heading was squeezed to a hundred pixels
 * ("SI / TE / S") and its buttons pushed off the screen. The fix is that layout follows the width
 * of the CONTAINER (Tailwind container queries), and this spec holds it there.
 *
 * Two measurements, both on geometry: the page's `<h1>` is at least 200px wide (a squeezed title
 * is 0-104px, a sensible one is the width of its longest word or more), and `<main>` — which
 * clips sideways rather than scrolling — has nothing wider than its own box.
 */

/** The window the zoom is applied at. */
const WINDOW = { width: 1280, height: 800 }

/** The narrowest an `<h1>` may be, in CSS pixels, before it counts as squeezed. */
const MIN_TITLE_WIDTH = 200

/** Every module, so no screen is gated. */
const MODULES: PanelModule[] = ['accounts', 'sites', 'databases', 'backups', 'monitoring', 'tasks'].map((name) => {
  return { name, displayName: name, tier: 'included', isEnabled: true }
})

const ACCOUNT: Account = {
  id: '00000000-0000-0000-0000-0000000000c1',
  name: 'acme-industrial-holdings',
  primaryDomain: 'storefront.acme-industrial-holdings.example.com',
  planId: THREE_PLANS[1]?.id ?? '',
  status: 'active',
  createdAt: '2026-08-30T09:00:00+00:00',
}

const SITE: Site = {
  id: '11111111-1111-1111-1111-111111111111',
  accountId: ACCOUNT.id,
  domain: 'storefront.acme-industrial-holdings.example.com',
  backendType: 'php',
  phpVersion: '8.3',
  status: 'enabled',
  createdAt: '2026-08-01T10:00:00Z',
}

const BACKUP: Backup = {
  id: '77777777-7777-7777-7777-777777777771',
  accountId: ACCOUNT.id,
  orphanedAccountUsername: '',
  status: 'failed',
  kind: 'manual',
  sizeBytes: 0,
  sha256: '',
  databaseCount: 0,
  startedAt: '2026-09-07T03:00:00Z',
  finishedAt: '2026-09-07T03:01:00Z',
  failureCode: 'DestinationUnreachable',
  failureDisplayName: 'The backup destination could not be reached',
}

const DASHBOARD: Dashboard = {
  isAdministrator: true,
  resources: {
    cpuPercent: 37.4,
    memoryUsedBytes: 3 * 1024 ** 3,
    memoryTotalBytes: 8 * 1024 ** 3,
    diskUsedBytes: 44 * 1024 ** 3,
    diskTotalBytes: 98 * 1024 ** 3,
    networkRxBytes: 0,
    networkTxBytes: 0,
    loadAverage1m: 0.21,
    loadAverage5m: 0.34,
    loadAverage15m: 0.55,
  },
  services: [{ service: 'webServer', name: 'Web server', state: 'running', detail: 'active (running)' }],
  counts: { accounts: 3, sites: 7, databases: 4 },
  attention: { certificatesExpiringSoon: 2, failedBackups: 5, bannedAddresses: 9, failedTasks: 6 },
  recentAudit: [
    {
      id: '22222222-2222-2222-2222-222222222222',
      occurredAt: '2026-10-05T11:00:00+00:00',
      actorUsername: 'administrator-on-call',
      action: 'SignInFailed',
      actionName: 'Sign-in failed',
      subject: 'storefront.acme-industrial-holdings.example.com',
      ipAddress: '203.0.113.9',
      succeeded: false,
    },
  ],
}

/**
 * Installs the shell and the endpoints the three screens read.
 * @param page The Playwright page whose network the routes are installed on.
 * @returns Resolves once every route is installed.
 */
const stubPanel = async (page: Page): Promise<void> => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, MODULES)
  await stubTasks(page, [])
  await stubDashboard(page, DASHBOARD)
  await stubPlans(page, THREE_PLANS)
  await stubAccounts(page, [ACCOUNT])
  await stubSites(page, [SITE])
  await stubPhpVersions(page, [{ version: '8.3', isDefault: true }])
  await stubBackups(page, [BACKUP])
  await stubBackupSchedules(page, {})
  await stubBackupDestinations(page, [])
}

/** One screen under test. */
const SCREENS = [
  { name: 'dashboard', path: '/', marker: 'Web server' },
  { name: 'sites', path: '/sites', marker: SITE.domain },
  { name: 'backups', path: '/backups', marker: 'The backup destination could not be reached' },
]

test.describe('text-only zoom to 200 percent at 1280 px', () => {
  test.use({ viewport: WINDOW })

  for (const screen of SCREENS) {
    test(`${screen.name} keeps its title readable and nothing overflows the content panel`, async ({ page }) => {
      await stubPanel(page)
      await page.goto(screen.path)

      const main = page.getByRole('main')
      await expect(main.getByRole('heading', { level: 1 })).toBeVisible()
      await expect(main.getByText(screen.marker).first()).toBeVisible()

      // Applied after load, as zooming a loaded page is: the layout must re-flow, not merely be
      // right on first paint.
      await page.addStyleTag({ content: 'html { font-size: 200% !important; }' })
      await page.evaluate(async () => {
        await document.fonts.ready
      })

      const measured = await page.evaluate(() => {
        const heading = document.querySelector('main h1')
        const panel = document.querySelector('main')
        if (heading === null || panel === null) {
          throw new Error('The page has no <main> with an <h1> to measure.')
        }
        return {
          titleWidth: heading.getBoundingClientRect().width,
          scrollWidth: panel.scrollWidth,
          clientWidth: panel.clientWidth,
        }
      })

      expect(measured.titleWidth, 'the page title is squeezed into a sliver').toBeGreaterThanOrEqual(MIN_TITLE_WIDTH)
      expect(measured.scrollWidth, 'the content panel overflows its own box').toBeLessThanOrEqual(measured.clientWidth)
    })
  }
})
