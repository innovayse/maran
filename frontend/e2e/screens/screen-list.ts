import { expect, type Page } from '@playwright/test'
import { stubAccountResources } from '../fixtures/stub-account-resources-route'
import { stubAccounts } from '../fixtures/stub-accounts-route'
import { stubSignedIn, stubSignedOut } from '../fixtures/stub-auth-routes'
import { stubBackupDestinations } from '../fixtures/stub-backup-destination-routes'
import { stubBackupOptions } from '../fixtures/stub-backup-options-route'
import { stubBackupSchedules } from '../fixtures/stub-backup-schedule-routes'
import { stubBackups } from '../fixtures/stub-backups-routes'
import { certificateFor, CERTIFICATE_SITE_ID, stubCertificates } from '../fixtures/stub-certificates-routes'
import { stubCronOptions } from '../fixtures/stub-cron-options-route'
import { stubCronEntries, stubCronEnvironment } from '../fixtures/stub-cron-routes'
import { ALERT_DELIVERY_OK, loaded, stubDashboard, SYNCHRONIZED_CLOCK } from '../fixtures/stub-dashboard-route'
import { stubDatabaseGrants } from '../fixtures/stub-database-grant-routes'
import { stubDatabases } from '../fixtures/stub-databases-routes'
import { stubFirewall } from '../fixtures/stub-firewall-routes'
import { runningFtpsStatus, stubFtpUsers, stubFtpsServer } from '../fixtures/stub-ftp-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubInvitation } from '../fixtures/stub-invitation-routes'
import { DEFAULT_LAYOUT, installLiveStream, stubDashboardLayout } from '../fixtures/stub-live-dashboard-stream'
import { stubEmptyModules, stubModules } from '../fixtures/stub-modules-route'
import { stubMonitoring, stubbedBucket } from '../fixtures/stub-monitoring-routes'
import { MANAGED_PLANS, stubManagedPlans, stubPlans, THREE_PLANS } from '../fixtures/stub-plans-route'
import type { CardActions } from '../fixtures/expect-table-cards'
import { stubSftpUsers } from '../fixtures/stub-sftp-routes'
import { stubShellReads } from '../fixtures/stub-shell-reads'
import {
  stubOpenSiteLogStream,
  stubPhpVersions,
  stubSiteDetail,
  stubSiteLimits,
  stubSites,
} from '../fixtures/stub-sites-routes'
import { stubTasks, stubTaskStream, taskEndFrame, taskFrame } from '../fixtures/stub-tasks-routes'
import type { Account, AccountDetail } from '../../src/types/account'
import type { AccountResourceSummary } from '../../src/types/accountResourceSummary'
import type { Backup } from '../../src/types/backup'
import type { BackupDestination } from '../../src/types/backupDestination'
import type { BackupSchedule } from '../../src/types/backupSchedule'
import type { CronEntry } from '../../src/types/cronEntry'
import type { Dashboard } from '../../src/types/dashboard'
import type { Database } from '../../src/types/database'
import type { Ban, FirewallRule, WhitelistEntry } from '../../src/types/firewall'
import type { FtpUser } from '../../src/types/ftpUser'
import type { GrantRepairReport } from '../../src/types/grantRepair'
import type { ChartRange, MetricBucket, ServiceStatus } from '../../src/types/monitoring'
import type { PanelModule } from '../../src/types/module'
import type { PanelTask } from '../../src/types/panelTask'
import type { SftpUser } from '../../src/types/sftpUser'
import type { Site, SiteDetail } from '../../src/types/site'
import type { SiteTraffic } from '../../src/types/siteTraffic'

// The data, the stub and the table below are the administrator sweep's
// (e2e/ui-kit/no-horizontal-scroll-admin.spec.ts), kept here as the one list the tablet sweep and
// the screenshot script both read.

/** Every module an administrator's panel can license, so nothing is hidden or gated. */
export const MODULES: PanelModule[] = [
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
  return { name, displayName: name, kindDisplayName: 'Core', tier: 'included', state: 'enabled' }
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
  statusName: 'Active',
  createdAt: '2026-08-30T09:00:00+00:00',
  resellerId: null,
  resellerName: null,
}

const SECOND_ACCOUNT: Account = {
  ...ACCOUNT,
  id: '00000000-0000-0000-0000-0000000000c2',
  name: 'bob',
  status: 'suspended',
  statusName: 'Suspended',
}

const SITE_ID = CERTIFICATE_SITE_ID

const SITE: Site = {
  id: SITE_ID,
  accountId: ACCOUNT.id,
  ownerName: ACCOUNT.name,
  domain: LONG_DOMAIN,
  backendType: 'php',
  backendTypeName: 'PHP',
  phpVersion: '8.3',
  status: 'enabled',
  statusName: 'Enabled',
  createdAt: '2026-08-01T10:00:00Z',
  logRetentionDays: 14,
}

const SITE_DETAIL: SiteDetail = {
  id: SITE.id,
  accountId: SITE.accountId,
  domain: SITE.domain,
  backendType: SITE.backendType,
  backendTypeName: SITE.backendTypeName,
  phpVersion: SITE.phpVersion,
  status: SITE.status,
  statusName: SITE.statusName,
  createdAt: SITE.createdAt,
  logRetentionDays: SITE.logRetentionDays,
  aliases: ['www.' + LONG_DOMAIN, 'cdn-assets-for-the-storefront.acme-industrial-holdings.example.com'],
  proxyUpstream: '',
  documentRoot: '/home/acme-industrial-holdings/sites/' + LONG_DOMAIN + '/public',
  hasCertificate: true,
}

/** The services the dashboard and the monitoring screen both report. */
const SERVICES: ServiceStatus[] = [
  { service: 'webServer', name: 'Web server', state: 'running', detail: 'active (running)' },
  { service: 'database', name: 'Database', state: 'stopped', detail: 'inactive (dead)' },
]

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
  services: loaded(SERVICES),
  counts: {
    accounts: loaded(12),
    sites: loaded(27),
    databases: loaded(14),
  },
  attention: { certificatesExpiringSoon: loaded(2), failedBackups: loaded(5), sitesOnEndOfLifePhp: loaded(0), licenceExpiring: loaded(0), alertDelivery: ALERT_DELIVERY_OK, bannedAddresses: loaded(9), failedTasks: loaded(6), clock: SYNCHRONIZED_CLOCK },
  recentAudit: loaded([
    {
      id: '11111111-1111-1111-1111-111111111111',
      occurredAt: '2026-10-05T12:00:00+00:00',
      actorUsername: 'administrator-on-call',
      impersonatorUsername: null,
      action: 'AccountCreated',
      actionName: 'Account created',
      subject: LONG_ACCOUNT,
      subjectName: LONG_ACCOUNT,
      ipAddress: '2001:0db8:85a3:0000:0000:8a2e:0370:7334',
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
      subjectName: 'admin',
      ipAddress: '203.0.113.9',
      succeeded: false,
      outcomeName: 'Failed',
    },
  ]),
}

const DATABASE: Database = {
  id: '33333333-3333-3333-3333-333333333333',
  accountId: ACCOUNT.id,
  ownerName: ACCOUNT.name,
  name: 'storefront_catalogue_production',
  fullName: 'acme_industrial_storefront_catalogue_production',
  dbUserName: 'acme_industrial_storefront_catalogue_user',
  createdAt: '2026-08-02T10:00:00Z',
}

const GRANT_REPORT: GrantRepairReport = {
  examinedGrants: 11,
  alreadyCorrect: 9,
  work: {
    kind: 'preview',
    grants: [
      {
        databaseName: 'acme_industrial_storefront_catalogue',
        dbUsername: 'acme_industrial_storefront_catalogue',
        alsoMatchedDatabases: ['acme_industrial_xstorefront_catalogue'],
      },
    ],
  },
  refused: [
    {
      grantHost: '10.0.0.5',
      databaseName: 'reporting_metrics_warehouse_nightly',
      dbUsername: 'reporting_tool_readonly',
      reason: 'hostIsNotLocalhost',
      reasonDisplayName: 'Granted from another host than this server',
      reasonAdvice:
        'Nothing was changed. Open your database server own grant table, then narrow the row by hand.',
    },
  ],
}

const SFTP_USER: SftpUser = {
  id: '44444444-4444-4444-4444-444444444444',
  accountId: ACCOUNT.id,
  ownerName: ACCOUNT.name,
  name: 'deployment-pipeline-user',
  fullName: 'acme-industrial-holdings_deployment-pipeline-user',
  protocol: 'Sftp',
  createdAt: '2026-08-03T10:00:00Z',
}

const FTP_USER: FtpUser = {
  id: '55555555-5555-5555-5555-555555555555',
  accountId: ACCOUNT.id,
  ownerName: ACCOUNT.name,
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
    ownerName: ACCOUNT.name,
    orphanedAccountUsername: null,
    status: 'completed',
    statusName: 'Completed',
    kind: 'scheduled',
    kindName: 'Scheduled',
    sizeBytes: 3_221_225_472,
    sha256: 'a1b2c3d4e5f60718293a4b5c6d7e8f90a1b2c3d4e5f60718293a4b5c6d7e8f90',
    databaseCount: 2,
    startedAt: '2026-09-06T03:00:00Z',
    finishedAt: '2026-09-06T03:04:00Z',
    failureCode: null,
    failureDisplayName: null,
    verificationState: 'notVerified',
    verificationStateName: 'Not verified',
    verifiedAt: null,
    verificationFailureCode: null,
    verificationFailureDisplayName: null,
  },
  {
    id: '77777777-7777-7777-7777-777777777772',
    accountId: ACCOUNT.id,
    ownerName: ACCOUNT.name,
    orphanedAccountUsername: null,
    status: 'failed',
    statusName: 'Failed',
    kind: 'manual',
    kindName: 'Manual',
    sizeBytes: 0,
    sha256: null,
    databaseCount: 0,
    startedAt: '2026-09-07T03:00:00Z',
    finishedAt: '2026-09-07T03:01:00Z',
    failureCode: 'DestinationUnreachable',
    failureDisplayName: 'The backup destination could not be reached',
    verificationState: 'notVerified',
    verificationStateName: 'Not verified',
    verifiedAt: null,
    verificationFailureCode: null,
    verificationFailureDisplayName: null,
  },
  {
    id: '77777777-7777-7777-7777-777777777773',
    accountId: '00000000-0000-0000-0000-0000000000ff',
    ownerName: '',
    orphanedAccountUsername: 'removed-customer-account-name',
    status: 'completed',
    statusName: 'Completed',
    kind: 'preDeletion',
    kindName: 'Before deletion',
    sizeBytes: 1_572_864,
    sha256: 'b1b2c3d4e5f60718293a4b5c6d7e8f90a1b2c3d4e5f60718293a4b5c6d7e8f90',
    databaseCount: 1,
    startedAt: '2026-09-05T03:00:00Z',
    finishedAt: '2026-09-05T03:02:00Z',
    failureCode: null,
    failureDisplayName: null,
    verificationState: 'notVerified',
    verificationStateName: 'Not verified',
    verifiedAt: null,
    verificationFailureCode: null,
    verificationFailureDisplayName: null,
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
  nextRunAt: '2026-09-02T02:00:00Z',
}

const DESTINATIONS: BackupDestination[] = [
  {
    id: '88888888-8888-8888-8888-888888888881',
    name: 'This server',
    displayName: 'This server',
    kind: 'local',
    path: '/var/backups/maran/very/deeply/nested/directory/of/customer-copies/archive',
    isDefault: true,
    encryptArchives: false,
    createdAt: '2026-09-01T09:00:00Z',
  },
  {
    id: '88888888-8888-8888-8888-888888888882',
    name: 'offsite-object-storage',
    displayName: 'Off-site object storage bucket in another region',
    kind: 's3',
    path: null,
    isDefault: false,
    encryptArchives: false,
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
    statusName: 'Running',
    percent: 42,
    log: 'Compressing the account home directory\n',
    logIncluded: true,
    failure: null,
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
    statusName: 'Failed',
    percent: 100,
    log: 'The agent refused the request\n',
    logIncluded: true,
    failure: {
      code: 'AgentUnavailable',
      message: 'Your server is not answering right now, so this request could not be confirmed.',
    },
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
    statusName: 'Completed',
    percent: 100,
    log: 'Done\n',
    logIncluded: true,
    failure: null,
    startedAt: '2026-09-01T08:00:00Z',
    finishedAt: '2026-09-01T08:01:00Z',
    revision: 7,
  },
]

/** The account detail the account screen reads: the list shape plus its plan name and contact address. */
const ACCOUNT_DETAIL: AccountDetail = {
  ...ACCOUNT,
  planDisplayName: 'Business',
  ownerEmail: 'owner@' + LONG_DOMAIN,
}

/** What the account holds against its plan, as the account screen's resources card reads it. */
const ACCOUNT_RESOURCES: AccountResourceSummary = {
  accountId: ACCOUNT.id,
  resources: [
    { kind: 'sites', count: { state: 'counted', used: 1 }, limit: { kind: 'limited', maximum: 25 } },
    { kind: 'databases', count: { state: 'counted', used: 1 }, limit: { kind: 'unlimited' } },
  ],
}

/** The site's traffic summary: no traffic recorded yet, the ordinary first state of a new site. */
const SITE_TRAFFIC: SiteTraffic = {
  days: 7,
  from: '2026-11-01',
  to: '2026-11-07',
  hasData: false,
  totals: { requests: 0, bytes: 0 },
  perDay: [],
  statusCodes: [],
  topPaths: [],
  incompleteSince: null,
  updatedAt: null,
}

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
export const stubPanel = async (page: Page): Promise<void> => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, MODULES)
  // The reads the shell makes on every screen come first, so a screen's own stub can still win.
  await stubShellReads(page)
  await stubTasks(page, TASKS)
  // The running task is watched by the shell's badge; without this its stream would leave the page.
  await stubTaskStream(page, taskFrame(TASKS[0]) + taskEndFrame('completed'))
  await stubDashboard(page, DASHBOARD)
  // The dashboard's live stream is a stand-in that never leaves the page, and its layout is held here.
  await stubDashboardLayout(page, DEFAULT_LAYOUT)
  await installLiveStream(page)

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
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(ACCOUNT_DETAIL) })
  })
  await stubAccountResources(page, ACCOUNT_RESOURCES)
  await stubInvitation(page, ACCOUNT.id, 'pending')

  await stubSites(page, [SITE])
  await stubSiteLimits(page, { maximumAliases: 20 })
  await stubSiteDetail(page, SITE_DETAIL)
  await page.route('**/api/v1/sites/*/traffic*', async (route) => {
    const days = Number(new URL(route.request().url()).searchParams.get('days'))
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({ ...SITE_TRAFFIC, days }),
    })
  })
  await stubPhpVersions(page, [
    { version: '8.2', defaultStatus: 'notDefault' },
    { version: '8.3', defaultStatus: 'default' },
  ])
  await stubOpenSiteLogStream(page)
  await stubCertificates(page, [certificateFor(LONG_DOMAIN, 'acme')])

  await stubDatabases(page, [DATABASE])
  await stubDatabaseGrants(page, GRANT_REPORT)

  await stubSftpUsers(page, [SFTP_USER])
  await stubFtpUsers(page, [FTP_USER])
  await stubFtpsServer(page, runningFtpsStatus)

  await stubCronOptions(page)
  await stubCronEntries(page, [CRON_ENTRY])
  await stubCronEnvironment(page, [
    { name: 'MAILTO_ADDRESS_FOR_FAILURES', value: 'operations@' + LONG_DOMAIN },
  ])

  await stubFirewall(page, { rules: RULES, bans: BANS, whitelist: WHITELIST })

  await stubBackupOptions(page)
  await stubBackups(page, BACKUPS)
  await stubBackupSchedules(page, { host: BACKUP_SCHEDULE })
  await stubBackupDestinations(page, DESTINATIONS)

  await stubMonitoring(page, BUCKETS, SERVICES, [
    {
      accountId: ACCOUNT.id,
      username: LONG_ACCOUNT,
      usedBytes: 3 * 1024 * 1024 * 1024,
      quotaBytes: 2 * 1024 * 1024 * 1024,
      backing: { state: 'enforced' },
    },
  ])
}

/** One screen under test. */
export interface Screen {
  /** What the screen is called in the test title. */
  name: string
  /** The path to open. */
  path: string
  /** A value from the stub that must be on screen before measuring, or null for a form. */
  marker: string | null
  /** Which of the site detail's tabs to open, counting from zero; absent for every other screen. */
  tab?: number
  /** For a list screen: its tables must be cards, with or without a row-actions column (#217). */
  cards?: CardActions
  /** True for a screen a signed-out visitor sees; absent for every administrator screen. */
  visitor?: boolean
}

/**
 * Every screen the tablet sweep measures and the screenshot script photographs: the administrator
 * screens of the 390 px sweep, plus the signed-out sign-in and first-run screens.
 */
export const SCREENS: Screen[] = [
  { name: 'dashboard', path: '/', marker: 'Web server' },
  { name: 'accounts list', path: '/accounts', marker: LONG_ACCOUNT, cards: 'none' },
  { name: 'account form', path: '/accounts/new', marker: null },
  { name: 'account detail', path: `/accounts/${ACCOUNT.id}`, marker: LONG_ACCOUNT },
  { name: 'plans list', path: '/plans', marker: LONG_PLAN, cards: 'actions' },
  { name: 'plan form', path: '/plans/new', marker: null },
  { name: 'plan edit', path: `/plans/${MANAGED_PLANS[0]?.id ?? ''}`, marker: null },
  { name: 'sites list', path: '/sites', marker: LONG_DOMAIN, cards: 'none' },
  { name: 'site form', path: '/sites/new', marker: null },
  { name: 'site detail, overview', path: `/sites/${SITE_ID}`, marker: LONG_DOMAIN, tab: 0 },
  { name: 'site detail, logs', path: `/sites/${SITE_ID}`, marker: LONG_DOMAIN, tab: 1 },
  { name: 'site detail, ssl', path: `/sites/${SITE_ID}`, marker: LONG_DOMAIN, tab: 2 },
  { name: 'databases', path: '/databases', marker: DATABASE.dbUserName, cards: 'actions' },
  {
    name: 'database grant repair',
    path: '/databases/grant-repair',
    marker: 'reporting_metrics_warehouse_nightly',
  },
  { name: 'sftp logins', path: '/file-transfer/sftp', marker: SFTP_USER.fullName, cards: 'actions' },
  { name: 'ftps logins', path: '/file-transfer/ftps', marker: FTP_USER.fullName, cards: 'actions' },
  { name: 'cron', path: '/cron', marker: '/home/acme-industrial-holdings/public_html', cards: 'actions' },
  { name: 'firewall rules', path: '/firewall', marker: '2001:0db8:85a3:0000:0000:8a2e:0370:7334/128', cards: 'actions' },
  { name: 'firewall bans', path: '/firewall/bans', marker: '203.0.113.9', cards: 'actions' },
  { name: 'firewall whitelist', path: '/firewall/whitelist', marker: '198.51.100.0/24', cards: 'actions' },
  { name: 'backups list', path: '/backups', marker: 'removed-customer-account-name', cards: 'actions' },
  { name: 'backup schedule', path: '/backups/schedule', marker: null },
  { name: 'backup destinations', path: '/backups/destinations', marker: 'Off-site object storage bucket', cards: 'none' },
  { name: 'monitoring', path: '/monitoring', marker: LONG_ACCOUNT, cards: 'none' },
  { name: 'tasks', path: '/tasks', marker: 'backup.create', cards: 'actions' },
  { name: 'sign-in', path: '/login', marker: null, visitor: true },
]

/**
 * Installs every stub the screen needs: the signed-out panel for a visitor's screen, the full
 * administrator panel for any other.
 * @param page The Playwright page whose network the routes are installed on.
 * @param screen The screen about to be opened.
 * @returns Resolves once every route is installed.
 */
export const prepareScreen = async (page: Page, screen: Screen): Promise<void> => {
  if (screen.visitor === true) {
    await stubSignedOut(page)
    await stubHealthy(page)
    await stubEmptyModules(page)
    return
  }
  await stubPanel(page)
}

/**
 * Opens the screen and waits until its stubbed content is drawn and the web fonts are loaded, so
 * what is measured or photographed is the filled screen and not its skeleton.
 * @param page The page, with {@link prepareScreen} already applied and the locale seeded.
 * @param screen The screen to open.
 * @returns Resolves once the screen is settled.
 */
export const openScreen = async (page: Page, screen: Screen): Promise<void> => {
  await page.goto(screen.path)

  const main = page.getByRole('main')
  if (screen.visitor === true) {
    await expect(main.getByRole('heading').first()).toBeVisible()
  } else {
    await expect(main.getByRole('heading', { level: 1 })).toBeVisible()
  }

  if (screen.tab !== undefined) {
    // The tabs are a group of buttons whose labels are translated, so they are found by
    // position: overview, logs, ssl, in that order in every language.
    await main.getByRole('group').first().getByRole('button').nth(screen.tab).click()
  }

  // A form has no stubbed value; its first control is the proof it has drawn.
  if (screen.visitor === true) {
    await expect(main.locator('input').first()).toBeVisible()
  } else if (screen.marker === null) {
    await expect(main.locator('input, textarea, select, [role="combobox"]').first()).toBeVisible()
  } else {
    await expect(main.getByText(screen.marker).first()).toBeVisible()
  }

  // Web fonts change glyph widths; settle with the ones the screen will really use.
  await page.evaluate(async () => {
    await document.fonts.ready
  })
  await page.waitForLoadState('networkidle')
}
