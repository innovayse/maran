import { expect, test, type Page } from '@playwright/test'
import { stubAccounts } from '../fixtures/stub-accounts-route'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubBackupDestinations } from '../fixtures/stub-backup-destination-routes'
import { stubBackupSchedules } from '../fixtures/stub-backup-schedule-routes'
import { stubBackups } from '../fixtures/stub-backups-routes'
import { certificateFor, CERTIFICATE_SITE_ID, stubCertificates } from '../fixtures/stub-certificates-routes'
import { stubCronEntries, stubCronEnvironment } from '../fixtures/stub-cron-routes'
import { stubDashboard } from '../fixtures/stub-dashboard-route'
import { stubDatabaseGrants } from '../fixtures/stub-database-grant-routes'
import { stubDatabases } from '../fixtures/stub-databases-routes'
import { stubFirewall } from '../fixtures/stub-firewall-routes'
import { runningFtpsStatus, stubFtpUsers, stubFtpsServer } from '../fixtures/stub-ftp-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import { stubMonitoring, stubbedBucket } from '../fixtures/stub-monitoring-routes'
import { MANAGED_PLANS, stubManagedPlans, stubPlans, THREE_PLANS } from '../fixtures/stub-plans-route'
import { setPersistedLocale } from '../fixtures/set-locale'
import { stubSftpUsers } from '../fixtures/stub-sftp-routes'
import {
  stubOpenSiteLogStream,
  stubPhpVersions,
  stubSiteDetail,
  stubSites,
} from '../fixtures/stub-sites-routes'
import { stubTasks, stubTaskStream, taskEndFrame, taskFrame } from '../fixtures/stub-tasks-routes'
import type { Account } from '../../src/types/account'
import type { Backup } from '../../src/types/backup'
import type { BackupSchedule } from '../../src/types/backupSchedule'
import type { CronEntry } from '../../src/types/cronEntry'
import type { Dashboard } from '../../src/types/dashboard'
import type { Database } from '../../src/types/database'
import type { Ban, FirewallRule, WhitelistEntry } from '../../src/types/firewall'
import type { FtpUser } from '../../src/types/ftpUser'
import type { GrantRepairReport } from '../../src/types/grantRepair'
import type { ChartRange, MetricBucket } from '../../src/types/monitoring'
import type { PanelModule } from '../../src/types/module'
import type { PanelTask } from '../../src/types/panelTask'
import type { SftpUser } from '../../src/types/sftpUser'
import type { Site, SiteDetail } from '../../src/types/site'

/*
 * Acceptance criterion of issue #82: "no horizontal scroll at 390 px on any screen, in all three
 * languages". This file protects it for every ADMINISTRATOR screen.
 *
 * Why it needs a spec of its own: the layout's `<main>` carries `overflow-x-hidden` as a backstop,
 * so a screen that overflows does not scroll — it silently CLIPS, and the page looks fine in a
 * screenshot while a button, a badge or a heading is cut off at the right edge. Russian and
 * Armenian labels are routinely 30-60 % longer than English ones, and a customer-supplied name has
 * no length limit the layout can rely on, so the proof has to be measured, per screen and per
 * language, with long realistic values on screen.
 *
 * Two measurements per screen, both on geometry and never on a class name:
 *   1. the document does not scroll sideways: `scrollWidth <= innerWidth`;
 *   2. the main content panel does not overflow its own box: `scrollWidth <= clientWidth`. The
 *      panel clips, so this is the check that actually catches the silent case. A table scrolling
 *      INSIDE its own container is allowed — that container is a descendant, and clips its own
 *      overflow, so it never widens the panel.
 *
 * One named test per screen x language, generated from the table below.
 */

/** The phone width the criterion is stated for. */
const PHONE = { width: 390, height: 844 }

/** The three interface languages the panel ships. */
const LOCALES = ['en', 'ru', 'hy'] as const

/** Every module an administrator's panel can license, so nothing is hidden or gated. */
const MODULES: PanelModule[] = [
  'accounts',
  'sites',
  'databases',
  'sftp',
  'ftp',
  'cron',
  'firewall',
  'backups',
  'monitoring',
  'tasks',
].map((name) => {
  return { name, displayName: name, tier: 'included', isEnabled: true }
})

// Long on purpose: the longest value a customer is plausibly to type, with no break opportunity
// inside the name part, which is what makes a flex or grid cell refuse to shrink.
const LONG_ACCOUNT = 'acme-industrial-holdings'
const LONG_DOMAIN = 'storefront-staging-environment.acme-industrial-holdings.example.com'
const LONG_COMMAND =
  '/usr/bin/php /home/acme-industrial-holdings/public_html/maintenance/rebuild-search-index.php --verbose'

// A plan name has no length limit the layout can rely on short of the server's 64 characters, so
// the plans screen is measured with one at that limit and with no break opportunity in it.
const LONG_PLAN = 'Agency-reseller-bundle-with-dedicated-resources-and-priority-supp'

const ACCOUNT: Account = {
  id: '00000000-0000-0000-0000-0000000000c1',
  name: LONG_ACCOUNT,
  primaryDomain: LONG_DOMAIN,
  planId: THREE_PLANS[1]?.id ?? '',
  status: 'active',
  createdAt: '2026-08-30T09:00:00+00:00',
}

const SECOND_ACCOUNT: Account = {
  ...ACCOUNT,
  id: '00000000-0000-0000-0000-0000000000c2',
  name: 'bob',
  status: 'suspended',
}

const SITE_ID = CERTIFICATE_SITE_ID

const SITE: Site = {
  id: SITE_ID,
  accountId: ACCOUNT.id,
  domain: LONG_DOMAIN,
  backendType: 'php',
  phpVersion: '8.3',
  status: 'enabled',
  createdAt: '2026-08-01T10:00:00Z',
}

const SITE_DETAIL: SiteDetail = {
  ...SITE,
  aliases: ['www.' + LONG_DOMAIN, 'cdn-assets-for-the-storefront.acme-industrial-holdings.example.com'],
  proxyUpstream: '',
  documentRoot: '/home/acme-industrial-holdings/sites/' + LONG_DOMAIN + '/public',
  hasCertificate: true,
}

const DASHBOARD: Dashboard = {
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
  counts: { accounts: 12, sites: 27, databases: 14 },
  attention: { certificatesExpiringSoon: 2, failedBackups: 5, bannedAddresses: 9, failedTasks: 6 },
  recentAudit: [
    {
      id: '11111111-1111-1111-1111-111111111111',
      occurredAt: '2026-10-05T12:00:00+00:00',
      actorUsername: 'administrator-on-call',
      action: 'AccountCreated',
      actionName: 'Account created',
      subject: LONG_ACCOUNT,
      ipAddress: '2001:0db8:85a3:0000:0000:8a2e:0370:7334',
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

const DATABASE: Database = {
  id: '33333333-3333-3333-3333-333333333333',
  accountId: ACCOUNT.id,
  name: 'storefront_catalogue_production',
  fullName: 'acme_industrial_storefront_catalogue_production',
  dbUserName: 'acme_industrial_storefront_catalogue_user',
  createdAt: '2026-08-02T10:00:00Z',
}

const GRANT_REPORT: GrantRepairReport = {
  isReportOnly: true,
  examinedGrants: 11,
  alreadyCorrect: 9,
  repaired: [],
  wouldRepair: [
    {
      databaseName: 'acme_industrial_storefront_catalogue',
      dbUsername: 'acme_industrial_storefront_catalogue',
      alsoMatchedDatabases: ['acme_industrial_xstorefront_catalogue'],
    },
  ],
  refused: [
    {
      grantHost: '10.0.0.5',
      databaseName: 'reporting_metrics_warehouse_nightly',
      dbUsername: 'reporting_tool_readonly',
      reason: 'HostIsNotLocalhost',
      reasonDisplayName: 'Granted from another host than this server',
      reasonAdvice:
        'Nothing was changed. Open your database server own grant table, then narrow the row by hand.',
    },
  ],
}

const SFTP_USER: SftpUser = {
  id: '44444444-4444-4444-4444-444444444444',
  accountId: ACCOUNT.id,
  name: 'deployment-pipeline-user',
  fullName: 'acme-industrial-holdings_deployment-pipeline-user',
  createdAt: '2026-08-03T10:00:00Z',
}

const FTP_USER: FtpUser = {
  id: '55555555-5555-5555-5555-555555555555',
  accountId: ACCOUNT.id,
  name: 'legacy-importer',
  fullName: 'acme-industrial-holdings_legacy-importer',
  protocol: 'ftps',
  createdAt: '2026-08-04T10:00:00Z',
}

const CRON_ENTRY: CronEntry = {
  entryId: '66666666-6666-6666-6666-666666666666',
  accountId: ACCOUNT.id,
  schedule: { minute: '30', hour: '3', dayOfMonth: '*', month: '*', dayOfWeek: '2' },
  command: LONG_COMMAND,
  enabled: true,
}

const RULES: FirewallRule[] = [
  { port: 443, portTo: null, protocol: 'tcp', sourceCidr: '0.0.0.0/0' },
  { port: 30000, portTo: 30099, protocol: 'tcp', sourceCidr: '2001:0db8:85a3:0000:0000:8a2e:0370:7334/128' },
]

const BANS: Ban[] = [
  {
    id: '00000000-0000-0000-0000-0000000000b1',
    ipAddress: '2001:0db8:85a3:0000:0000:8a2e:0370:7334',
    reason: 'bruteForce',
    failures: 12,
    bannedAt: '2026-09-01T11:45:00+00:00',
    expiresAt: '2026-09-01T12:30:00+00:00',
  },
  {
    id: '00000000-0000-0000-0000-0000000000b2',
    ipAddress: '203.0.113.9',
    reason: 'manual',
    failures: 0,
    bannedAt: '2026-09-01T09:00:00+00:00',
    expiresAt: null,
  },
]

const WHITELIST: WhitelistEntry[] = [
  {
    id: '00000000-0000-0000-0000-0000000000a1',
    cidr: '198.51.100.0/24',
    note: 'The office network of the agency that administers this server on our behalf',
    createdAt: '2026-08-01T10:00:00+00:00',
  },
]

const BACKUPS: Backup[] = [
  {
    id: '77777777-7777-7777-7777-777777777771',
    accountId: ACCOUNT.id,
    orphanedAccountUsername: '',
    status: 'completed',
    kind: 'scheduled',
    sizeBytes: 3_221_225_472,
    sha256: 'a1b2c3d4e5f60718293a4b5c6d7e8f90a1b2c3d4e5f60718293a4b5c6d7e8f90',
    databaseCount: 2,
    startedAt: '2026-09-06T03:00:00Z',
    finishedAt: '2026-09-06T03:04:00Z',
    failureCode: '',
    failureDisplayName: '',
  },
  {
    id: '77777777-7777-7777-7777-777777777772',
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
  },
  {
    id: '77777777-7777-7777-7777-777777777773',
    accountId: '00000000-0000-0000-0000-0000000000ff',
    orphanedAccountUsername: 'removed-customer-account-name',
    status: 'completed',
    kind: 'preDeletion',
    sizeBytes: 1_572_864,
    sha256: 'b1b2c3d4e5f60718293a4b5c6d7e8f90a1b2c3d4e5f60718293a4b5c6d7e8f90',
    databaseCount: 1,
    startedAt: '2026-09-05T03:00:00Z',
    finishedAt: '2026-09-05T03:02:00Z',
    failureCode: '',
    failureDisplayName: '',
  },
]

const BACKUP_SCHEDULE: BackupSchedule = {
  id: '55555555-5555-5555-5555-555555555555',
  accountId: null,
  destinationId: null,
  frequency: 'weekly',
  hourUtc: 2,
  dayOfWeekUtc: 'wednesday',
  retainCount: 4,
  enabled: true,
  lastRunAt: '2026-09-01T02:00:00Z',
}

const DESTINATIONS = [
  {
    id: '88888888-8888-8888-8888-888888888881',
    name: 'This server',
    displayName: 'This server',
    kind: 'local',
    path: '/var/backups/maran/very/deeply/nested/directory/of/customer-copies/archive',
    isDefault: true,
    createdAt: '2026-09-01T09:00:00Z',
  },
  {
    id: '88888888-8888-8888-8888-888888888882',
    name: 'offsite-object-storage',
    displayName: 'Off-site object storage bucket in another region',
    kind: 's3',
    path: null,
    isDefault: false,
    createdAt: '2026-09-02T09:00:00Z',
  },
]

const BUCKETS: Record<ChartRange, MetricBucket[]> = {
  lastDay: [stubbedBucket(0, 12.5), stubbedBucket(1, 33.5), stubbedBucket(2, 91.5), stubbedBucket(3, 45.5)],
  lastWeek: [stubbedBucket(0, 12.5), stubbedBucket(1, 33.5)],
}

const TASKS: PanelTask[] = [
  {
    id: '99999999-9999-9999-9999-999999999991',
    kind: 'backup.create',
    subject: LONG_ACCOUNT + '-' + LONG_DOMAIN,
    correlationId: null,
    status: 'running',
    percent: 42,
    log: 'Compressing the account home directory\n',
    errorCode: null,
    startedAt: '2026-09-01T10:00:00Z',
    finishedAt: null,
    revision: 3,
  },
  {
    id: '99999999-9999-9999-9999-999999999992',
    kind: 'account.delete',
    subject: 'bob',
    correlationId: null,
    status: 'failed',
    percent: 100,
    log: 'The agent refused the request\n',
    errorCode: 'AgentUnavailable',
    startedAt: '2026-09-01T09:00:00Z',
    finishedAt: '2026-09-01T09:01:00Z',
    revision: 5,
  },
  {
    id: '99999999-9999-9999-9999-999999999993',
    kind: 'site.create',
    subject: LONG_DOMAIN,
    correlationId: null,
    status: 'completed',
    percent: 100,
    log: 'Done\n',
    errorCode: null,
    startedAt: '2026-09-01T08:00:00Z',
    finishedAt: '2026-09-01T08:01:00Z',
    revision: 7,
  },
]

/**
 * Installs the shell and every endpoint any administrator screen reads, with realistic and
 * deliberately long values. One superset rather than one set per screen: the screens differ in
 * what they ask for, never in what the stubbed panel is able to answer.
 *
 * Order matters in two places. Playwright matches routes newest-first, so the php-versions stub is
 * installed AFTER the site-detail stub whose `sites/*` pattern would otherwise swallow it; and the
 * tasks stub after the sign-in, which installs an empty one of its own.
 * @param page The Playwright page whose network the routes are installed on.
 * @returns Resolves once every route is installed.
 */
const stubPanel = async (page: Page): Promise<void> => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, MODULES)
  await stubTasks(page, TASKS)
  // The running task is watched by the shell's badge; without this its stream would leave the page.
  await stubTaskStream(page, taskFrame(TASKS[0]) + taskEndFrame('completed'))
  await stubDashboard(page, DASHBOARD)

  await stubPlans(page, THREE_PLANS)
  await stubManagedPlans(page, [
    ...MANAGED_PLANS.map((plan) => {
      return { ...plan }
    }),
    { ...MANAGED_PLANS[0], id: '66666666-6666-6666-6666-666666666666', displayName: LONG_PLAN },
  ])
  await stubAccounts(page, [ACCOUNT, SECOND_ACCOUNT])
  await page.route(`**/api/v1/accounts/${ACCOUNT.id}`, async (route) => {
    if (route.request().method() !== 'GET') {
      await route.fallback()
      return
    }
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(ACCOUNT) })
  })

  await stubSites(page, [SITE])
  await stubSiteDetail(page, SITE_DETAIL)
  await stubPhpVersions(page, [
    { version: '8.2', isDefault: false },
    { version: '8.3', isDefault: true },
  ])
  await stubOpenSiteLogStream(page)
  await stubCertificates(page, [certificateFor(LONG_DOMAIN, 'acme')])

  await stubDatabases(page, [DATABASE])
  await stubDatabaseGrants(page, GRANT_REPORT)

  await stubSftpUsers(page, [SFTP_USER])
  await stubFtpUsers(page, [FTP_USER])
  await stubFtpsServer(page, runningFtpsStatus)

  await stubCronEntries(page, [CRON_ENTRY])
  await stubCronEnvironment(page, [
    { name: 'MAILTO_ADDRESS_FOR_FAILURES', value: 'operations@' + LONG_DOMAIN },
  ])

  await stubFirewall(page, { rules: RULES, bans: BANS, whitelist: WHITELIST })

  await stubBackups(page, BACKUPS)
  await stubBackupSchedules(page, { host: BACKUP_SCHEDULE })
  await stubBackupDestinations(page, DESTINATIONS)

  await stubMonitoring(page, BUCKETS, DASHBOARD.services, [
    {
      accountId: ACCOUNT.id,
      username: LONG_ACCOUNT,
      usedBytes: 3 * 1024 * 1024 * 1024,
      quotaBytes: 2 * 1024 * 1024 * 1024,
      enforceable: true,
      note: null,
    },
  ])
}

/** One screen under test. */
interface Screen {
  /** What the screen is called in the test title. */
  name: string
  /** The path to open. */
  path: string
  /** A value from the stub that must be on screen before measuring, or null for a form. */
  marker: string | null
  /** Which of the site detail's tabs to open, counting from zero; absent for every other screen. */
  tab?: number
}

const SCREENS: Screen[] = [
  { name: 'dashboard', path: '/', marker: 'Web server' },
  { name: 'accounts list', path: '/accounts', marker: LONG_ACCOUNT },
  { name: 'account form', path: '/accounts/new', marker: null },
  { name: 'account detail', path: `/accounts/${ACCOUNT.id}`, marker: LONG_ACCOUNT },
  { name: 'plans list', path: '/plans', marker: LONG_PLAN },
  { name: 'plan form', path: '/plans/new', marker: null },
  { name: 'plan edit', path: `/plans/${MANAGED_PLANS[0]?.id ?? ''}`, marker: null },
  { name: 'sites list', path: '/sites', marker: LONG_DOMAIN },
  { name: 'site form', path: '/sites/new', marker: null },
  { name: 'site detail, overview', path: `/sites/${SITE_ID}`, marker: LONG_DOMAIN, tab: 0 },
  { name: 'site detail, logs', path: `/sites/${SITE_ID}`, marker: LONG_DOMAIN, tab: 1 },
  { name: 'site detail, ssl', path: `/sites/${SITE_ID}`, marker: LONG_DOMAIN, tab: 2 },
  { name: 'databases', path: '/databases', marker: DATABASE.dbUserName },
  {
    name: 'database grant repair',
    path: '/databases/grant-repair',
    marker: 'reporting_metrics_warehouse_nightly',
  },
  { name: 'file transfer', path: '/file-transfer', marker: SFTP_USER.fullName },
  { name: 'cron', path: '/cron', marker: '/home/acme-industrial-holdings/public_html' },
  { name: 'firewall', path: '/firewall', marker: '198.51.100.0/24' },
  { name: 'backups list', path: '/backups', marker: 'removed-customer-account-name' },
  { name: 'backup schedule', path: '/backups/schedule', marker: null },
  { name: 'backup destinations', path: '/backups/destinations', marker: 'Off-site object storage bucket' },
  { name: 'monitoring', path: '/monitoring', marker: LONG_ACCOUNT },
  { name: 'tasks', path: '/tasks', marker: 'backup.create' },
]

/** What a measurement reports about one page. */
interface Measurement {
  documentWidth: number
  viewportWidth: number
  mainScrollWidth: number
  mainClientWidth: number
  /** The elements poking past the panel's right edge, for the failure message. */
  offenders: string[]
}

/**
 * Measures the page's horizontal extent.
 * @param page The Playwright page, already loaded.
 * @returns The document's and the main panel's widths, and a short list of what overflows the panel.
 */
const measure = async (page: Page): Promise<Measurement> => {
  return page.evaluate(() => {
    const main = document.querySelector('main')
    if (main === null) {
      throw new Error('The page has no <main> landmark to measure.')
    }
    const edge = main.getBoundingClientRect().right
    const pageEdge = window.innerWidth
    const offenders: string[] = []

    /**
     * Whether an ancestor between the element and the panel clips sideways and itself fits.
     * @param element The element to test.
     * @returns True when its overflow is contained inside its own scroll container.
     */
    const isContained = (element: Element): boolean => {
      for (
        let parent = element.parentElement;
        parent !== null && parent !== main;
        parent = parent.parentElement
      ) {
        const style = getComputedStyle(parent)
        const clips = ['auto', 'scroll', 'hidden', 'clip'].includes(style.overflowX)
        if (clips && parent.getBoundingClientRect().right <= edge + 1) {
          return true
        }
      }
      return false
    }

    for (const element of main.querySelectorAll('*')) {
      const box = element.getBoundingClientRect()
      if (box.width > 0 && box.right > edge + 1 && !isContained(element) && offenders.length < 6) {
        offenders.push(
          `<${element.tagName.toLowerCase()}> right=${Math.round(box.right)} "${(element.textContent ?? '').trim().slice(0, 40)}"`,
        )
      }
    }

    for (const element of document.querySelectorAll('body *')) {
      const box = element.getBoundingClientRect()
      if (box.width > 0 && box.right > pageEdge + 1 && !main.contains(element) && offenders.length < 6) {
        offenders.push(
          `outside main: <${element.tagName.toLowerCase()}> right=${Math.round(box.right)} "${(element.textContent ?? '').trim().slice(0, 40)}"`,
        )
      }
    }

    return {
      documentWidth: document.documentElement.scrollWidth,
      viewportWidth: window.innerWidth,
      mainScrollWidth: main.scrollWidth,
      mainClientWidth: main.clientWidth,
      offenders,
    }
  })
}

test.describe('no horizontal scroll at 390 px on an administrator screen', () => {
  test.use({ viewport: PHONE })

  for (const screen of SCREENS) {
    for (const locale of LOCALES) {
      test(`${screen.name} in ${locale}`, async ({ page }) => {
        await setPersistedLocale(page, locale)
        await stubPanel(page)

        await page.goto(screen.path)

        const main = page.getByRole('main')
        await expect(main.getByRole('heading', { level: 1 })).toBeVisible()

        if (screen.tab !== undefined) {
          // The tabs are a group of buttons whose labels are translated, so they are found by
          // position: overview, logs, ssl, in that order in every language.
          await main.getByRole('group').first().getByRole('button').nth(screen.tab).click()
        }

        // Wait for the stubbed data, so the measurement is of the filled screen and not of its
        // skeleton. A form has no stubbed value; its first control is the proof it has drawn.
        if (screen.marker === null) {
          await expect(main.locator('input, textarea, select, [role="combobox"]').first()).toBeVisible()
        } else {
          await expect(main.getByText(screen.marker).first()).toBeVisible()
        }

        // Web fonts change glyph widths; measure with the ones the screen will really use.
        await page.evaluate(async () => {
          await document.fonts.ready
        })
        await page.waitForLoadState('networkidle')

        const widths = await measure(page)

        expect(
          widths.documentWidth,
          `the document scrolls sideways; wide elements: ${widths.offenders.join(' | ')}`,
        ).toBeLessThanOrEqual(widths.viewportWidth)
        expect(
          widths.mainScrollWidth,
          `the main panel overflows its box; elements past its right edge: ${widths.offenders.join(' | ')}`,
        ).toBeLessThanOrEqual(widths.mainClientWidth)
      })
    }
  }
})
