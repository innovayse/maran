import { expectNoSeriousViolations } from '../fixtures/expect-no-serious-violations'
import { expect, test } from '../fixtures/guarded-test'
import { FULL_CATALOGUE } from '../fixtures/open-full-shell'
import { setPersistedLocale } from '../fixtures/set-locale'
import { stubAccountResources, stubMyAccountResources } from '../fixtures/stub-account-resources-route'
import { stubAccounts } from '../fixtures/stub-accounts-route'
import { RESELLER_KEY_PATHS, apiKeyRow, stubApiKeys } from '../fixtures/stub-api-keys-routes'
import { stubAudit, stubResellerAudit } from '../fixtures/stub-audit-routes'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { NO_BRANDING, stubResellerBranding } from '../fixtures/stub-branding-routes'
import { CERTIFICATE_SITE_ID } from '../fixtures/stub-certificates-routes'
import { stubDashboard, WITHHELD_DASHBOARD } from '../fixtures/stub-dashboard-route'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubInvitation } from '../fixtures/stub-invitation-routes'
import { stubIdleLiveDashboard } from '../fixtures/stub-live-dashboard-stream'
import { stubMembershipOffers } from '../fixtures/stub-membership-offers-routes'
import { stubModules } from '../fixtures/stub-modules-route'
import { stubMyAccount } from '../fixtures/stub-my-account-route'
import { MANAGED_PLANS, stubManagedPlans, stubPlans, THREE_PLANS } from '../fixtures/stub-plans-route'
import { stubProfile } from '../fixtures/stub-profile-route'
import { RESELLER_MODULES, stubResellerSession } from '../fixtures/stub-reseller-session'
import { ACME_HOSTING, stubResellers } from '../fixtures/stub-resellers-routes'
import { stubSessions } from '../fixtures/stub-sessions-route'
import { stubShellReads } from '../fixtures/stub-shell-reads'
import {
  completePhpVersion,
  stubInstallablePhpVersions,
  stubOpenSiteLogStream,
  stubPhpVersions,
  stubSiteDetail,
  stubSiteLimits,
  stubSites,
  type PhpVersionStub,
} from '../fixtures/stub-sites-routes'
import { storedSmtpSettings, stubSmtpSettings } from '../fixtures/stub-smtp-routes'
import { stubSubUsers, subUserRow } from '../fixtures/stub-sub-users-routes'
import type { Account, MyAccount } from '../../src/types/account'
import type { AuditEvent } from '../../src/types/audit'
import type { AuthenticatedUser, Session } from '../../src/types/auth'
import type { PanelCertificateReading } from '../../src/types/panelCertificate'
import type { SecurityPolicy } from '../../src/types/securityPolicy'
import type { Site, SiteDetail } from '../../src/types/site'
import type { Page } from '@playwright/test'

/*
 * Issue #82: axe on the screens `axe-scan.spec.ts` does not reach, in both themes. That spec stops
 * at the sign-in screen, the dashboard and the main lists; this one scans a detail screen, the
 * forms, every settings page, the customer's own area, the reseller's screens, the phone
 * navigation drawer, the kit's dialogs (a modal, a confirmation, the filters dialog and the
 * filters sheet) and an open row menu. The drawer and two dialogs are also scanned in Armenian,
 * whose long labels and a different script are where a contrast or naming gap hides.
 *
 * The threshold is the one `axe-scan.spec.ts` uses (serious or critical, WCAG 2.2 AA). A screen
 * that fails is a bug to file against the component, never a case to drop from this table.
 *
 * Every screen is scanned with real rows or values in it: an empty form draws fewer rules than a
 * filled one, and a contrast failure lives in the tokens one theme paints a filled control with.
 */

/** The phone viewport the drawer and the filters sheet are scanned at. */
const PHONE = { width: 390, height: 844 }

/** The desktop viewport every other screen is scanned at. */
const DESKTOP = { width: 1440, height: 900 }

/** An administrator's catalogue with every module on, so no screen is gated. */
const MODULES = FULL_CATALOGUE

/** A customer who owns an account. */
const CUSTOMER: AuthenticatedUser = {
  id: '00000000-0000-0000-0000-000000000099',
  username: 'olive',
  email: 'olive@example.com',
  role: 'customer',
  accountId: '11111111-1111-1111-1111-111111111111',
}

/** The customer's own account. */
const OWN_ACCOUNT: MyAccount = {
  id: CUSTOMER.accountId ?? '',
  name: 'olive',
  primaryDomain: 'olive.example.com',
  status: 'active',
  statusName: 'Active',
  plan: {
    displayName: 'Starter',
    diskQuotaMb: 1024,
    maxSites: 5,
    maxDatabases: 5,
    maxSftpUsers: 5,
    maxFtpUsers: 5,
    maxCronEntries: 5,
  },
}

/** An account for the detail screen and the forms to read. */
const ALICE: Account = {
  id: '22222222-2222-2222-2222-222222222222',
  name: 'alice',
  primaryDomain: 'alice.example.com',
  planId: THREE_PLANS[1]?.id ?? '',
  status: 'active',
  statusName: 'Active',
  createdAt: '2026-08-01T10:00:00Z',
  resellerId: null,
  resellerName: null,
}

/** A site, the subject of the site detail screen. */
const SITE: Site = {
  id: CERTIFICATE_SITE_ID,
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

/** The site's detail. */
const SITE_DETAIL: SiteDetail = {
  ...SITE,
  aliases: ['www.alpha.example.com'],
  proxyUpstream: '',
  documentRoot: '/home/alice/sites/alpha.example.com/public',
  hasCertificate: false,
}

/** Two devices for the sessions screen. */
const SESSIONS: Session[] = [
  {
    id: '00000000-0000-0000-0000-0000000000a1',
    issuedAt: '2026-08-30T09:00:00+00:00',
    expiresAt: '2026-09-13T09:00:00+00:00',
    ipAddress: '203.0.113.9',
    userAgent: 'Firefox on Linux',
    isCurrent: true,
  },
  {
    id: '00000000-0000-0000-0000-0000000000a2',
    issuedAt: '2026-08-29T18:30:00+00:00',
    expiresAt: '2026-09-12T18:30:00+00:00',
    ipAddress: '198.51.100.4',
    userAgent: 'Safari on iPhone',
    isCurrent: false,
  },
]

/** The policy the security-policy screen is filled with. */
const POLICY: SecurityPolicy = {
  minimumPasswordLength: 12,
  forceTwoFactorForAdmins: true,
  forceTwoFactorForResellers: true,
  maxFailedLoginAttempts: 5,
  lockoutMinutes: 15,
}

/** Installed PHP runtimes. */
const PHP_VERSIONS: PhpVersionStub[] = [
  { version: '8.4', defaultStatus: 'default' },
  {
    version: '8.3',
    defaultStatus: 'notDefault',
    lifecycle: { status: 'securityOnly', statusName: 'Security fixes only', endOfLife: '2027-12-31', daysLeft: 420 },
    sitesUsing: 2,
  },
  {
    version: '8.0',
    defaultStatus: 'notDefault',
    lifecycle: { status: 'endOfLife', statusName: 'End of life', endOfLife: '2023-11-26', daysLeft: null },
    sitesUsing: 1,
  },
  {
    version: '5.6',
    defaultStatus: 'unknown',
    lifecycle: { status: 'unknown', statusName: 'Not known', endOfLife: null, daysLeft: null },
    sitesUsing: 0,
  },
]

/** One journal entry. */
const EVENT: AuditEvent = {
  id: '00000000-0000-0000-0000-0000000000e1',
  occurredAt: '2026-09-08T18:14:00+00:00',
  actorUsername: 'admin',
  impersonatorUsername: null,
  action: 'AccountCreated',
  actionName: 'Account created',
  subject: 'alice',
  subjectName: 'alice',
  ipAddress: '203.0.113.9',
  succeeded: true,
  outcomeName: 'Succeeded',
}

/** A trusted panel certificate, so the screen draws its renew and restore controls. */
const RECORDED_CERTIFICATE: PanelCertificateReading = {
  hostNameConfigured: true,
  hostNameNotice: null,
  hostName: 'panel.example.net',
  expiresAt: '2027-02-01T00:00:00Z',
  issuedAt: '2026-11-03T00:00:00Z',
}

/** One screen to scan, and how to bring it up. */
interface Case {
  /** Name used in the generated test title. */
  name: string
  /** The path to open. */
  path: string
  /**
   * Installs every stub the screen needs, before navigation.
   * @param page The page whose network is stubbed.
   * @returns Resolves once the stubs are installed.
   */
  prepare: (page: Page) => Promise<void>
  /**
   * Waits until the screen shows its stubbed content, so axe reads the filled screen and not a skeleton.
   * @param page The page, already navigated to {@link Case.path}.
   * @returns Resolves once the content is visible.
   */
  ready: (page: Page) => Promise<void>
}

/**
 * Waits for stubbed text to be on screen in the main region.
 * @param text The literal stubbed text.
 * @returns A readiness function for {@link Case.ready}.
 */
const textReady = (text: string): ((page: Page) => Promise<void>) => {
  return async (page) => {
    await expect(page.getByRole('main').getByText(text).first()).toBeVisible()
  }
}

/**
 * Waits for the first text field of the main region.
 * @param page The page, on a form screen.
 * @returns Resolves once the field is visible.
 */
const fieldReady = async (page: Page): Promise<void> => {
  await expect(page.getByRole('main').getByRole('textbox').first()).toBeVisible()
}

/**
 * Signs an administrator in with every module licensed and the shell's own reads answered.
 * @param page The page whose network is stubbed.
 * @returns Resolves once the stubs are installed.
 */
const administrator = async (page: Page): Promise<void> => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubShellReads(page)
  await stubModules(page, MODULES)
}

/**
 * The administrator plus what the account, plan and site screens read.
 * @param page The page whose network is stubbed.
 * @returns Resolves once the stubs are installed.
 */
const administratorWithData = async (page: Page): Promise<void> => {
  await administrator(page)
  await stubPlans(page, THREE_PLANS)
  await stubManagedPlans(page, MANAGED_PLANS.map((plan) => {
    return { ...plan }
  }))
  await stubAccounts(page, [ALICE])
  await page.route(`**/api/v1/accounts/${ALICE.id}`, async (route) => {
    if (route.request().method() !== 'GET') {
      await route.fallback()
      return
    }
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(ALICE) })
  })
  await stubAccountResources(page, { accountId: ALICE.id, resources: [] })
  await stubInvitation(page, ALICE.id, 'pending')
  await stubSites(page, [SITE])
  await stubSiteLimits(page, { maximumAliases: 20 })
  await stubSiteDetail(page, SITE_DETAIL)
  await stubPhpVersions(page, PHP_VERSIONS)
  await stubOpenSiteLogStream(page)
  await stubResellers(page)
}

/**
 * The administrator plus a stored API key, for the screens and dialogs the keys page carries.
 * @param page The page whose network is stubbed.
 * @returns Resolves once the stubs are installed.
 */
const administratorWithKeys = async (page: Page): Promise<void> => {
  await administrator(page)
  await stubApiKeys(page, { keys: [apiKeyRow()] })
}

/** Every detail, form, settings, customer and reseller screen. */
const CASES: Case[] = [
  { name: 'the account detail', path: `/accounts/${ALICE.id}`, prepare: administratorWithData, ready: textReady('alice') },
  { name: 'the account form', path: '/accounts/new', prepare: administratorWithData, ready: fieldReady },
  { name: 'the plan form', path: '/plans/new', prepare: administratorWithData, ready: fieldReady },
  { name: 'the plan edit form', path: `/plans/${MANAGED_PLANS[0]?.id ?? ''}`, prepare: administratorWithData, ready: fieldReady },
  { name: 'the site form', path: '/sites/new', prepare: administratorWithData, ready: fieldReady },
  { name: 'the site detail', path: `/sites/${SITE.id}`, prepare: administratorWithData, ready: textReady('alpha.example.com') },
  {
    name: 'the reseller detail',
    path: `/resellers/${ACME_HOSTING.id}`,
    prepare: administratorWithData,
    ready: textReady(ACME_HOSTING.contactEmail),
  },
  { name: 'the reseller form', path: '/resellers/new', prepare: administratorWithData, ready: fieldReady },
  {
    name: 'the reseller profile form',
    path: `/resellers/${ACME_HOSTING.id}/profile`,
    prepare: administratorWithData,
    ready: fieldReady,
  },
  { name: 'the reseller edit form', path: `/resellers/${ACME_HOSTING.id}/edit`, prepare: administratorWithData, ready: fieldReady },
  {
    name: 'the security policy',
    path: '/settings/security-policy',
    prepare: async (page): Promise<void> => {
      await administrator(page)
      await page.route('**/api/v1/security-policy', async (route) => {
        await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(POLICY) })
      })
    },
    ready: async (page): Promise<void> => {
      await expect(page.getByRole('main').getByRole('textbox').first()).toHaveValue('12')
    },
  },
  {
    name: 'the mail settings',
    path: '/settings/smtp',
    prepare: async (page): Promise<void> => {
      await administrator(page)
      await stubSmtpSettings(page, storedSmtpSettings)
    },
    ready: async (page): Promise<void> => {
      await expect(page.getByRole('main').getByRole('textbox').first()).toHaveValue(storedSmtpSettings.host)
    },
  },
  {
    name: 'the PHP versions',
    path: '/settings/php',
    prepare: async (page): Promise<void> => {
      await administrator(page)
      await page.route('**/api/v1/sites/php-versions', async (route) => {
        await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(PHP_VERSIONS.map(completePhpVersion)) })
      })
      await stubInstallablePhpVersions(page, () => {
        return []
      })
    },
    ready: textReady('8.4'),
  },
  {
    name: 'the audit journal',
    path: '/settings/audit',
    prepare: async (page): Promise<void> => {
      await administrator(page)
      await stubAudit(page, [EVENT])
    },
    ready: textReady('203.0.113.9'),
  },
  {
    name: 'the sessions',
    path: '/settings/sessions',
    prepare: async (page): Promise<void> => {
      await administrator(page)
      await stubSessions(page, SESSIONS)
    },
    ready: textReady('Safari on iPhone'),
  },
  {
    name: 'the two-factor settings',
    path: '/settings/two-factor',
    prepare: async (page): Promise<void> => {
      await administrator(page)
      await stubProfile(page, {
        username: 'admin',
        email: 'admin@example.com',
        role: 'admin',
        isTwoFactorEnabled: false,
        activeSessionCount: 1,
      })
    },
    ready: async (page): Promise<void> => {
      await expect(page.getByRole('main').getByRole('heading', { level: 1 })).toBeVisible()
      await expect(page.getByRole('main').getByRole('button').first()).toBeVisible()
    },
  },
  {
    name: 'the API keys',
    path: '/settings/api-keys',
    prepare: administratorWithKeys,
    ready: textReady('Billing integration'),
  },
  {
    name: 'the panel certificate',
    path: '/settings/panel-certificate',
    prepare: async (page): Promise<void> => {
      await administrator(page)
      await page.route('**/api/v1/panel-certificate', async (route) => {
        await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(RECORDED_CERTIFICATE) })
      })
    },
    ready: textReady('panel.example.net'),
  },
  {
    name: 'my account (customer)',
    path: '/my-account',
    prepare: async (page): Promise<void> => {
      await stubSignedIn(page, CUSTOMER)
      await stubHealthy(page)
      await stubShellReads(page)
      await stubModules(page, MODULES)
      await stubMyAccount(page, OWN_ACCOUNT)
      await stubMyAccountResources(page, { accountId: OWN_ACCOUNT.id, resources: [] })
      await stubMembershipOffers(page, [])
    },
    ready: textReady(OWN_ACCOUNT.primaryDomain),
  },
  {
    name: 'the sub-users (customer)',
    path: '/my-account/sub-users',
    prepare: async (page): Promise<void> => {
      await stubSignedIn(page, CUSTOMER)
      await stubHealthy(page)
      await stubShellReads(page)
      await stubModules(page, MODULES)
      await stubMyAccount(page, OWN_ACCOUNT)
      await stubSubUsers(page, [subUserRow('deploy', ['databases']), subUserRow('writer', [], 'invited')])
    },
    ready: textReady('deploy'),
  },
  {
    name: 'the reseller branding',
    path: '/settings/branding',
    prepare: async (page): Promise<void> => {
      await stubResellerSession(page, ACME_HOSTING, RESELLER_MODULES)
      await stubResellerBranding(page, { saved: [] }, { ...NO_BRANDING, displayName: 'Acme hosting', accentColor: '#2468dc' })
    },
    ready: fieldReady,
  },
  {
    name: 'the reseller API keys',
    path: '/settings/reseller-api-keys',
    prepare: async (page): Promise<void> => {
      await stubResellerSession(page, ACME_HOSTING, RESELLER_MODULES)
      await stubApiKeys(page, { keys: [apiKeyRow({ name: 'Provisioning', scopes: ['accounts.read'] })] }, RESELLER_KEY_PATHS)
    },
    ready: textReady('Provisioning'),
  },
  {
    name: 'the reseller audit journal',
    path: '/reseller/audit',
    prepare: async (page): Promise<void> => {
      await stubResellerSession(page, ACME_HOSTING, RESELLER_MODULES)
      await stubResellerAudit(page, [EVENT])
    },
    ready: textReady('203.0.113.9'),
  },
]

/**
 * Opens the API keys screen at the desktop width in the given language and waits for its row.
 * @param page The page to drive.
 * @param locale The interface language.
 * @param rowName The key's name as the screen prints it.
 * @returns Resolves once the row is on screen.
 */
const openKeys = async (page: Page, locale: 'en' | 'hy', rowName: string): Promise<void> => {
  await page.setViewportSize(DESKTOP)
  await setPersistedLocale(page, locale)
  await administratorWithKeys(page)
  await page.goto('/settings/api-keys')
  await expect(page.getByRole('main').getByText(rowName).first()).toBeVisible()
}

/** The words the dialogs are opened with, per language (`apiKeys.create` and `apiKeys.rowActions` in the locales). */
const KEY_WORDS = {
  en: { create: 'Create key', rowActions: 'Actions for the key Billing integration' },
  hy: { create: 'Ստեղծել բանալի', rowActions: 'Գործողություններ Billing integration բանալու համար' },
} as const

/** The drawer's openers, per language (`app.shell.openNavigation` in `shell.json`). */
const OPEN_NAVIGATION = { en: 'Open the navigation', hy: 'Բացել նավիգացիան' } as const

for (const theme of ['dark', 'light'] as const) {
  test.describe(`in the ${theme} theme`, () => {
    test.use({ colorScheme: theme })

    for (const scanned of CASES) {
      test(`${scanned.name} has no serious accessibility violation`, async ({ page }) => {
        await page.setViewportSize(DESKTOP)
        await scanned.prepare(page)

        await page.goto(scanned.path)
        await scanned.ready(page)

        await expectNoSeriousViolations(page)
      })
    }

    for (const locale of ['en', 'hy'] as const) {
      test(`the phone navigation drawer, open, has no serious accessibility violation in ${locale}`, async ({ page }) => {
        await page.setViewportSize(PHONE)
        await setPersistedLocale(page, locale)
        await stubSignedIn(page)
        await stubHealthy(page)
        await stubShellReads(page)
        await stubModules(page, MODULES)
        await stubDashboard(page, WITHHELD_DASHBOARD)
        await stubIdleLiveDashboard(page)
        await page.goto('/')
        await expect(page.getByRole('main')).toBeVisible()

        await page.locator(`button[aria-label="${OPEN_NAVIGATION[locale]}"]`).click()
        await expect(page.getByRole('dialog')).toBeVisible()

        await expectNoSeriousViolations(page)
      })

      test(`the create-key dialog has no serious accessibility violation in ${locale}`, async ({ page }) => {
        await openKeys(page, locale, 'Billing integration')

        await page.getByRole('button', { name: KEY_WORDS[locale].create }).first().click()
        await expect(page.getByRole('dialog')).toBeVisible()

        await expectNoSeriousViolations(page)
      })

      test(`the revoke confirmation has no serious accessibility violation in ${locale}`, async ({ page }) => {
        await openKeys(page, locale, 'Billing integration')

        await page.getByRole('button', { name: KEY_WORDS[locale].rowActions }).click()
        await page.getByRole('menuitem').first().click()
        await expect(page.getByRole('dialog')).toBeVisible()

        await expectNoSeriousViolations(page)
      })

      test(`the filters dialog has no serious accessibility violation at 1440px in ${locale}`, async ({ page }) => {
        await page.setViewportSize(DESKTOP)
        await page.goto(`/e2e/fixtures/list-kit-harness.html?locale=${locale}`)
        await page.getByTestId('filter-trigger').click()
        await expect(page.getByRole('dialog')).toBeVisible()

        await expectNoSeriousViolations(page)
      })

      test(`the filters sheet has no serious accessibility violation at 390px in ${locale}`, async ({ page }) => {
        await page.setViewportSize(PHONE)
        await page.goto(`/e2e/fixtures/list-kit-harness.html?locale=${locale}`)
        await page.getByTestId('filter-trigger').click()
        await expect(page.getByTestId('filter-sheet')).toBeVisible()

        await expectNoSeriousViolations(page)
      })
    }

    test('an open row menu has no serious accessibility violation', async ({ page }) => {
      await openKeys(page, 'en', 'Billing integration')

      await page.getByRole('button', { name: KEY_WORDS.en.rowActions }).click()
      await expect(page.getByRole('menuitem').first()).toBeVisible()

      await expectNoSeriousViolations(page)
    })
  })
}
