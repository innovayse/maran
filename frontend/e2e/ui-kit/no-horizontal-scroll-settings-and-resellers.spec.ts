import { expect, test, type Page } from '@playwright/test'
import { expectNoHorizontalOverflow } from '../fixtures/measure-horizontal-extent'
import { FULL_CATALOGUE } from '../fixtures/open-full-shell'
import { setPersistedLocale } from '../fixtures/set-locale'
import { stubAccounts } from '../fixtures/stub-accounts-route'
import { RESELLER_KEY_PATHS, apiKeyRow, overseenKeyPaths, stubApiKeys } from '../fixtures/stub-api-keys-routes'
import { stubResellerAudit } from '../fixtures/stub-audit-routes'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { NO_BRANDING, stubResellerBranding } from '../fixtures/stub-branding-routes'
import { stubCronOptions } from '../fixtures/stub-cron-options-route'
import { stubCronEntries, stubCronEnvironment } from '../fixtures/stub-cron-routes'
import { stubCronRuns } from '../fixtures/stub-cron-runs-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import { stubPendingUpdates } from '../fixtures/stub-monitoring-routes'
import { stubMyAccount } from '../fixtures/stub-my-account-route'
import { stubPlans } from '../fixtures/stub-plans-route'
import { stubProfile } from '../fixtures/stub-profile-route'
import { RESELLER_MODULES, stubResellerSession } from '../fixtures/stub-reseller-session'
import { ACME_HOSTING, BRAVO_HOSTING, stubResellers } from '../fixtures/stub-resellers-routes'
import { stubShellReads } from '../fixtures/stub-shell-reads'
import { stubSubUsers, subUserRow } from '../fixtures/stub-sub-users-routes'
import type { Account, MyAccount } from '../../src/types/account'
import type { AuditEvent } from '../../src/types/audit'
import type { AuthenticatedUser } from '../../src/types/auth'
import type { CronEntry } from '../../src/types/cronEntry'
import type { CronRun, CronRunSummary } from '../../src/types/cronRun'
import type { PanelCertificateReading } from '../../src/types/panelCertificate'
import type { ResellerDetail } from '../../src/types/reseller'

/*
 * Issue #82's acceptance criterion, "no horizontal scroll at 390 px on any screen, in all three
 * languages", for the screens the administrator and "other" sweeps do not open: the settings
 * screens added after them (API keys, the panel certificate, the reseller's branding), the
 * customer's team page, the profile, the cron run history, the pending updates, the not-available
 * screen, and the whole reseller area (the administrator's resellers screens and a reseller's own
 * overview and journal).
 *
 * The three measurements and the reason there are three are in
 * `e2e/fixtures/measure-horizontal-extent.ts`. The census in `route-coverage.spec.ts` is what
 * keeps this table honest: a route with no row here, in the other two sweeps or in its exemption
 * list fails there by name.
 *
 * Values are long and unbreakable on purpose, as real names, hosts and commands are. One test per
 * screen and language, generated from the table, so a failure names the pair.
 */

/** The phone viewport the criterion names. */
const PHONE = { width: 390, height: 844 }

/** The interface languages the panel ships. */
const LOCALES = ['en', 'ru', 'hy'] as const

/** A name with no break opportunity, as long as a reseller or a key is plausibly given. */
const LONG_NAME = 'Agency-reseller-bundle-with-dedicated-resources-and-priority-support'

/** A host name with no break opportunity inside its labels. */
const LONG_HOST = 'panel-for-the-storefront-staging-environment.acme-industrial-holdings.example.com'

/** An e-mail address with a long unbreakable local part. */
const LONG_EMAIL = 'billing-and-operations-contact-for-the-agency@acme-industrial-holdings.example.com'

/** A command with long path segments, as a cron entry's real command is. */
const LONG_COMMAND =
  '/usr/bin/php /home/acme-industrial-holdings/public_html/maintenance/rebuild-search-index.php --verbose'

/** One screen to measure, and how to bring it up with stubbed data. */
interface Screen {
  /** Name used in the generated test title. */
  name: string
  /** The path (with query) to open. */
  path: string
  /**
   * Installs every stub the screen needs, before navigation. The locale is already seeded and the
   * viewport already set to the phone's; a stub that resizes the window must set it back.
   * @param page The page whose network is stubbed.
   * @returns Resolves once the stubs are installed.
   */
  prepare: (page: Page) => Promise<void>
  /**
   * Waits until the screen shows the stubbed content, so the measurement is of the filled screen
   * and not of its skeleton.
   * @param page The page, already navigated to {@link Screen.path}.
   * @returns Resolves once the content is visible.
   */
  ready: (page: Page) => Promise<void>
}

/** A customer who owns an account and reads their team. */
const CUSTOMER: AuthenticatedUser = {
  id: '00000000-0000-0000-0000-000000000099',
  username: 'olive',
  email: 'olive@example.com',
  role: 'customer',
  accountId: '11111111-1111-1111-1111-111111111111',
}

/** The customer's own account, which the shell reads on every customer screen. */
const OWN_ACCOUNT: MyAccount = {
  id: CUSTOMER.accountId ?? '',
  name: 'olive-shop-with-a-long-account-name',
  primaryDomain: LONG_HOST,
  status: 'active',
  statusName: 'Active',
  plan: {
    displayName: 'Professional hosting plan with extended limits',
    diskQuotaMb: 1048576,
    maxSites: 100,
    maxDatabases: 100,
    maxSftpUsers: 100,
    maxFtpUsers: 100,
    maxCronEntries: 100,
  },
}

/** An account for the cron screens to belong to. */
const ACCOUNT: Account = {
  id: '22222222-2222-2222-2222-222222222222',
  name: 'acme-industrial-holdings',
  primaryDomain: LONG_HOST,
  planId: '44444444-4444-4444-4444-444444444444',
  status: 'active',
  statusName: 'Active',
  createdAt: '2026-08-01T10:00:00Z',
  resellerId: null,
  resellerName: null,
}

/** A reseller whose name and address are as long as the layout has to survive. */
const LONG_RESELLER: ResellerDetail = {
  ...ACME_HOSTING,
  name: LONG_NAME,
  contactEmail: LONG_EMAIL,
}

/** The scheduled entry whose run history is shown. */
const ENTRY: CronEntry = {
  entryId: '11111111-1111-1111-1111-111111111111',
  accountId: ACCOUNT.id,
  schedule: { minute: '30', hour: '3', dayOfMonth: '*', month: '*', dayOfWeek: '*' },
  command: LONG_COMMAND,
  enabled: true,
}

/** A run whose outcome name is long, as the panel's own localized words are in Russian and Armenian. */
const RUN: CronRun = {
  id: '33333333-3333-4333-8333-333333333333',
  entryId: ENTRY.entryId,
  startedAt: '2026-11-01T09:00:00Z',
  durationMilliseconds: 1500,
  end: { kind: 'exited', exitCode: 2 },
  outcome: 'failed',
  outcomeName: 'Failed because the command exited with an error',
  stdout: '',
  stderr: 'permission denied\n',
}

/** The same run as a history row. */
const RUN_ROW: CronRunSummary = {
  id: RUN.id,
  entryId: RUN.entryId,
  startedAt: RUN.startedAt,
  durationMilliseconds: RUN.durationMilliseconds,
  end: RUN.end,
  outcome: RUN.outcome,
  outcomeName: RUN.outcomeName,
}

/** A journal entry with unbreakable cells, the shape of a reseller's own part of the journal. */
const RESELLER_EVENT: AuditEvent = {
  id: '00000000-0000-0000-0000-0000000000e1',
  occurredAt: '2026-09-08T18:14:00+00:00',
  actorUsername: 'reseller-owner-with-a-long-login-name',
  impersonatorUsername: null,
  action: 'AccountCreated',
  actionName: 'AccountCreated',
  subject: 'customer-account-with-a-rather-long-name',
  ipAddress: '2001:0db8:85a3:0000:0000:8a2e:0370:7334',
  succeeded: true,
  outcomeName: 'Succeeded',
}

/** The trusted certificate the panel certificate screen shows, on a host name that cannot wrap. */
const RECORDED_CERTIFICATE: PanelCertificateReading = {
  hostNameConfigured: true,
  hostNameNotice: null,
  hostName: LONG_HOST,
  expiresAt: '2027-02-01T00:00:00Z',
  issuedAt: '2026-11-03T00:00:00Z',
}

/**
 * Waits for a piece of stubbed text to be on screen inside the main region.
 * @param text The literal stubbed text to wait for.
 * @returns A readiness function for {@link Screen.ready}.
 */
const textReady = (text: string): ((page: Page) => Promise<void>) => {
  return async (page) => {
    await expect(page.getByRole('main').getByText(text).first()).toBeVisible()
  }
}

/**
 * Waits for the first text field of the main region, which a form draws only once it holds its data.
 * @param page The page, on a form screen.
 * @returns Resolves once the field is visible.
 */
const fieldReady = async (page: Page): Promise<void> => {
  await expect(page.getByRole('main').getByRole('textbox').first()).toBeVisible()
}

/**
 * The stubs every administrator screen shares, with the catalogue that lets every module's screen open.
 * @param page The page whose network is stubbed.
 * @returns Resolves once the stubs are installed.
 */
const administrator = async (page: Page): Promise<void> => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubShellReads(page)
  await stubModules(page, FULL_CATALOGUE)
}

/**
 * The administrator's stubs plus what the reseller screens read.
 * @param page The page whose network is stubbed.
 * @returns Resolves once the stubs are installed.
 */
const administratorWithResellers = async (page: Page): Promise<void> => {
  await administrator(page)
  await stubAccounts(page, [])
  await stubPlans(page)
  await stubResellers(page, [LONG_RESELLER, BRAVO_HOSTING].map((reseller) => {
    return { ...reseller }
  }))
}

/**
 * A reseller signed in, back at the phone's width (the shared stub opens a desktop window).
 * @param page The page whose network is stubbed.
 * @returns Resolves once the stubs are installed.
 */
const reseller = async (page: Page): Promise<void> => {
  await stubResellerSession(page, LONG_RESELLER, RESELLER_MODULES)
  await page.setViewportSize(PHONE)
}

/** Every screen under test. */
const SCREENS: Screen[] = [
  {
    name: 'profile',
    path: '/profile',
    prepare: async (page): Promise<void> => {
      await administrator(page)
      await stubProfile(page, {
        username: 'administrator-with-a-long-login-name',
        email: LONG_EMAIL,
        role: 'admin',
        isTwoFactorEnabled: false,
        activeSessionCount: 1,
      })
    },
    ready: textReady(LONG_EMAIL),
  },
  {
    name: 'not available',
    path: '/not-available',
    prepare: administrator,
    ready: async (page): Promise<void> => {
      await expect(page.getByRole('main').getByRole('heading', { level: 1 })).toBeVisible()
    },
  },
  {
    name: 'API keys',
    path: '/settings/api-keys',
    prepare: async (page): Promise<void> => {
      await administrator(page)
      await stubApiKeys(page, {
        keys: [
          apiKeyRow({
            name: LONG_NAME,
            scopes: ['sites.read', 'sites.write', 'databases.read', 'databases.write'],
            allowedCidrs: ['2001:0db8:85a3:0000:0000:8a2e:0370:7334/128', '198.51.100.0/24'],
          }),
        ],
      })
    },
    ready: textReady(LONG_NAME),
  },
  {
    name: 'reseller API keys',
    path: '/settings/reseller-api-keys',
    prepare: async (page): Promise<void> => {
      await reseller(page)
      await stubApiKeys(page, { keys: [apiKeyRow({ name: LONG_NAME, scopes: ['accounts.read', 'plans.read'] })] }, RESELLER_KEY_PATHS)
    },
    ready: textReady(LONG_NAME),
  },
  {
    name: 'panel certificate',
    path: '/settings/panel-certificate',
    prepare: async (page): Promise<void> => {
      await administrator(page)
      await page.route('**/api/v1/panel-certificate', async (route) => {
        await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(RECORDED_CERTIFICATE) })
      })
    },
    ready: textReady(LONG_HOST),
  },
  {
    name: 'reseller branding',
    path: '/settings/branding',
    prepare: async (page): Promise<void> => {
      await reseller(page)
      await stubResellerBranding(page, { saved: [] }, { ...NO_BRANDING, displayName: LONG_NAME, accentColor: '#2e7bff' })
    },
    ready: fieldReady,
  },
  {
    name: 'sub-users',
    path: '/my-account/sub-users',
    prepare: async (page): Promise<void> => {
      await stubSignedIn(page, CUSTOMER)
      await stubHealthy(page)
      await stubModules(page, FULL_CATALOGUE)
      await stubMyAccount(page, OWN_ACCOUNT)
      await stubSubUsers(page, [
        subUserRow('deployment-pipeline-service-user', ['databases']),
        subUserRow('content-editor-with-a-long-name', [], 'invited'),
      ])
    },
    ready: textReady('deployment-pipeline-service-user'),
  },
  {
    name: 'cron run history',
    path: `/cron/${ENTRY.entryId}/runs?account=${ACCOUNT.id}`,
    prepare: async (page): Promise<void> => {
      await administrator(page)
      await stubCronOptions(page)
      await stubAccounts(page, [ACCOUNT])
      await stubCronEntries(page, [ENTRY])
      await stubCronEnvironment(page, [])
      await stubCronRuns(page, RUN, [RUN_ROW])
    },
    ready: textReady(RUN.outcomeName),
  },
  {
    name: 'pending updates',
    path: '/servers/updates',
    prepare: async (page): Promise<void> => {
      await administrator(page)
      await stubPendingUpdates(page, [
        {
          packageName: 'libreoffice-writer-extension-with-a-long-name',
          availableVersion: '3.0.13-0ubuntu3.5+esm1+really.long.version.string',
          installedVersion: '3.0.13-0ubuntu3.4',
          source: 'noble-security',
          classification: 'security',
        },
      ])
    },
    ready: textReady('libreoffice-writer-extension-with-a-long-name'),
  },
  {
    name: 'resellers list',
    path: '/resellers',
    prepare: administratorWithResellers,
    ready: textReady(LONG_NAME),
  },
  {
    name: 'reseller form',
    path: '/resellers/new',
    prepare: administratorWithResellers,
    ready: fieldReady,
  },
  {
    name: 'reseller detail',
    path: `/resellers/${LONG_RESELLER.id}`,
    prepare: administratorWithResellers,
    ready: textReady(LONG_EMAIL),
  },
  {
    name: 'reseller API keys overseen',
    path: `/resellers/${LONG_RESELLER.id}/api-keys`,
    prepare: async (page): Promise<void> => {
      await administratorWithResellers(page)
      await stubApiKeys(page, { keys: [apiKeyRow({ name: LONG_NAME })] }, overseenKeyPaths(LONG_RESELLER.id))
    },
    ready: textReady(LONG_NAME),
  },
  {
    name: 'reseller profile form',
    path: `/resellers/${LONG_RESELLER.id}/profile`,
    prepare: administratorWithResellers,
    ready: fieldReady,
  },
  {
    name: 'reseller edit',
    path: `/resellers/${LONG_RESELLER.id}/edit`,
    prepare: administratorWithResellers,
    ready: fieldReady,
  },
  {
    name: 'reseller overview',
    path: '/reseller',
    prepare: reseller,
    ready: textReady(LONG_NAME),
  },
  {
    name: 'reseller audit journal',
    path: '/reseller/audit',
    prepare: async (page): Promise<void> => {
      await reseller(page)
      await stubResellerAudit(page, [RESELLER_EVENT])
    },
    ready: textReady('reseller-owner-with-a-long-login-name'),
  },
]

for (const screen of SCREENS) {
  for (const locale of LOCALES) {
    test(`${screen.name} does not scroll sideways at 390px in ${locale}`, async ({ page }) => {
      await page.setViewportSize(PHONE)
      await setPersistedLocale(page, locale)
      await screen.prepare(page)

      await page.goto(screen.path)
      await screen.ready(page)

      await expectNoHorizontalOverflow(page)
    })
  }
}
