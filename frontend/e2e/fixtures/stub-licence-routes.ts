import type { Page } from '@playwright/test'
import type { InstallLicenceRequest, LicenceStatus } from '../../src/types/licence'

/** The ID of the stubbed server, as the marketplace spells one: 32 lower-case hexadecimal digits. */
export const SERVER_ID = '0123456789abcdef0123456789abcdef'

/** The claim page the stubbed backend builds for {@link SERVER_ID}. */
export const CLAIM_URL = `https://plugins-maran.innovayse.com/claim?server=${SERVER_ID}`

/** A panel holding a valid licence with a fresh lease; the texts are the backend's, already localized. */
export const VALID_LICENCE: LicenceStatus = {
  state: 'valid',
  licenceId: 'LIC-2026-0042',
  tier: 'business',
  modules: ['mail', 'dns', 'reseller'],
  expiry: '2027-03-01T00:00:00Z',
  refusalReason: null,
  sentence: 'The licence is valid and covers 3 paid modules.',
  advice: null,
  stateDisplayName: 'Valid',
  refusalReasonDisplayName: null,
  isOffline: false,
  lease: 'fresh',
  leaseDisplayName: 'Fresh',
  leaseValidUntil: '2026-12-09T00:00:00Z',
  serverId: SERVER_ID,
  claimUrl: CLAIM_URL,
  leaseRequest: null,
  leaseRequestDisplayName: null,
  expiryWarningDaysLeft: null,
  expiryWarning: null,
  expiryRenewalAdvice: null,
}

/** A panel with no licence installed. */
export const ABSENT_LICENCE: LicenceStatus = {
  state: 'absent',
  licenceId: null,
  tier: null,
  modules: null,
  expiry: null,
  refusalReason: null,
  sentence: 'No licence is installed.',
  advice: 'Paste a licence below to unlock the paid modules.',
  stateDisplayName: 'Not installed',
  refusalReasonDisplayName: null,
  isOffline: false,
  lease: 'missing',
  leaseDisplayName: 'No lease',
  leaseValidUntil: null,
  serverId: SERVER_ID,
  claimUrl: CLAIM_URL,
  leaseRequest: null,
  leaseRequestDisplayName: null,
  expiryWarningDaysLeft: null,
  expiryWarning: null,
  expiryRenewalAdvice: null,
}

/** A panel that refused the installed licence because it expired. */
export const REFUSED_LICENCE: LicenceStatus = {
  ...ABSENT_LICENCE,
  state: 'refused',
  licenceId: 'LIC-2025-0007',
  tier: 'business',
  modules: ['mail'],
  expiry: '2026-01-01T00:00:00Z',
  refusalReason: 'expired',
  sentence: 'The installed licence was refused.',
  advice: 'Install a current licence.',
  stateDisplayName: 'Refused',
  refusalReasonDisplayName: 'The licence has expired',
}

/** A valid licence the backend has flagged as 14 days from its expiry; the texts are the backend's, already localized. */
export const EXPIRING_LICENCE: LicenceStatus = {
  ...VALID_LICENCE,
  expiry: '2026-12-26T00:00:00Z',
  expiryWarningDaysLeft: 14,
  expiryWarning: 'The installed licence expires on 2026-12-26 (days left: 14).',
  expiryRenewalAdvice: 'To renew: claim the licence again for this server on the marketplace, or install a new licence file.',
}

/** What the install answers when the lease it asked for was stored. */
export const INSTALLED_WITH_LEASE: LicenceStatus = {
  ...VALID_LICENCE,
  leaseRequest: 'renewed',
  leaseRequestDisplayName: 'A lease was requested from the licence service and stored.',
}

/** What the install answers when the licensing service could not be reached: installed, no lease yet. */
export const INSTALLED_WITHOUT_LEASE: LicenceStatus = {
  ...VALID_LICENCE,
  lease: 'missing',
  leaseDisplayName: 'No lease',
  leaseValidUntil: null,
  leaseRequest: 'unreachable',
  leaseRequestDisplayName: 'The licence service could not be reached. The panel will try again later.',
}

/**
 * What the install answers when a lease that still stands as fresh was due again and the licensing service could
 * not be reached: the standing is good, the request just made is not.
 */
export const INSTALLED_FRESH_BUT_UNREACHABLE: LicenceStatus = {
  ...VALID_LICENCE,
  leaseRequest: 'unreachable',
  leaseRequestDisplayName: 'The licence service could not be reached. The panel will try again later.',
}

/** A panel whose agent could not tell the server's ID: no ID and no link. */
export const NO_SERVER_ID: LicenceStatus = { ...ABSENT_LICENCE, serverId: null, claimUrl: null }

/** What the stubbed licence endpoints saw, so a spec can assert on the exact bodies sent. */
export interface LicenceCalls {
  /** The body of every `POST .../licence-status/install`, in order. */
  installs: InstallLicenceRequest[]
  /** How many times the status was read. */
  reads: number
}

/** How the stubbed install endpoint answers. */
export interface LicenceInstallAnswer {
  /** The status a successful install answers with; ignored when `refusal` is set. */
  status?: LicenceStatus
  /** The backend's own sentence for a refusal (HTTP 400); when set, the install is refused with it. */
  refusal?: string
}

/**
 * Fulfils both licence endpoints: GET answers the current status, and POST install either stores the
 * answer as the new status or refuses with the backend's sentence, as the real panel does.
 * @param page The Playwright page whose network the routes are installed on.
 * @param initial The status the panel starts with.
 * @param install How the install endpoint answers.
 * @returns The record of what the panel saw.
 */
export const stubLicenceRoutes = async (
  page: Page,
  initial: LicenceStatus,
  install: LicenceInstallAnswer = { status: VALID_LICENCE },
): Promise<LicenceCalls> => {
  const calls: LicenceCalls = { installs: [], reads: 0 }
  let current = initial

  await page.route('**/api/v1/licence-status', async (route) => {
    calls.reads += 1
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(current) })
  })

  await page.route('**/api/v1/licence-status/install', async (route) => {
    calls.installs.push(route.request().postDataJSON() as InstallLicenceRequest)
    if (install.refusal !== undefined) {
      await route.fulfill({
        status: 400,
        contentType: 'application/problem+json',
        body: JSON.stringify({ code: 'LicenceRefused', title: 'Licence refused', detail: install.refusal }),
      })
      return
    }
    current = install.status ?? VALID_LICENCE
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(current) })
  })

  return calls
}
