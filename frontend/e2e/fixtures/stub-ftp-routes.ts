import type { Page } from '@playwright/test'
import type { CreatedFtpUser, FtpUser, FtpUserPassword } from '../../src/types/ftpUser'
import type { FtpsStatus } from '../../src/types/ftpsStatus'

/** The collection endpoint the FTPS list and the create form both talk to. */
const FTP_USERS_PATTERN = '**/api/v1/ftp-users'

/** The single-login endpoint, which answers the removal. */
const FTP_USER_PATTERN = '**/api/v1/ftp-users/*'

/** The password-reset endpoint. A `*` never spans a `/`, so this is narrower than the one above. */
const FTP_USER_PASSWORD_PATTERN = '**/api/v1/ftp-users/*/password'

/** The administrator's FTPS daemon endpoint, and the two switches under it. */
const FTPS_SERVER_PATTERN = '**/api/v1/ftps-server'

/** The enable and disable switches, both narrower than the collection pattern above. */
const FTPS_SERVER_SWITCH_PATTERN = '**/api/v1/ftps-server/*'

/** The account prefix the stubs build a full login with, mirroring what the host would hold. */
const ACCOUNT_PREFIX = 'alice'

/** The password a stubbed FTPS create answers with — a value no other fixture in the suite uses. */
export const stubbedCreatedFtpPassword = 'Cr34ted-FTPS-P4ss-Once'

/** The password a stubbed FTPS reset answers with, distinct from the created one. */
export const stubbedResetFtpPassword = 'R3set-FTPS-P4ss-Once'

/**
 * A daemon that is on, running, answering, and serving a real certificate.
 *
 * Every number is the SERVER's: the SPA holds no port, so a spec that asserted a port it wrote
 * itself would pass against a screen that had stopped reading the response.
 */
export const runningFtpsStatus: FtpsStatus = {
  hostname: 'panel.example.net',
  enabled: true,
  running: true,
  controlPortAnswered: true,
  forcedTls: true,
  certificatePresent: true,
  certificateIsSelfSigned: false,
  certificatePath: '/etc/maran/certificates/panel.example.net/fullchain.pem',
  controlPort: 21,
  passivePortMin: 30000,
  passivePortMax: 30099,
  ipv4Only: false,
}

/**
 * A daemon that ships installed and turned off — the normal state of a fresh server, not an error.
 *
 * It reports no live configuration, so the passive range is zero on both ends: that is what the
 * agent answers when there is no configuration to read a range out of.
 */
export const disabledFtpsStatus: FtpsStatus = {
  hostname: null,
  enabled: false,
  running: false,
  controlPortAnswered: false,
  forcedTls: false,
  certificatePresent: false,
  certificateIsSelfSigned: false,
  certificatePath: '/etc/maran/certificates',
  controlPort: 21,
  passivePortMin: 0,
  passivePortMax: 0,
  ipv4Only: false,
}

/**
 * Fulfils `GET /api/v1/ftp-users` with the given list and answers a `POST` by echoing the submitted
 * body back as a created login, prefixed the way the host prefixes it and carrying a password.
 *
 * The prefix and the protocol are both applied HERE, in the stub, because both are the server's:
 * the SPA under test must render what it was sent rather than assembling a name or assuming a
 * protocol, and a stub that answered neither would let a page that invents them pass.
 * @param page The Playwright page whose network the route is installed on.
 * @param users The logins the stub reports for the list request.
 * @returns Resolves once the route is installed.
 */
export const stubFtpUsers = async (page: Page, users: FtpUser[]): Promise<void> => {
  await page.route(FTP_USERS_PATTERN, async (route) => {
    if (route.request().method() === 'POST') {
      const submitted = route.request().postDataJSON() as { accountId: string; name: string }
      const created: CreatedFtpUser = {
        id: '88888888-8888-8888-8888-888888888888',
        accountId: submitted.accountId,
        name: submitted.name,
        fullName: `${ACCOUNT_PREFIX}_${submitted.name}`,
        protocol: 'Ftps',
        password: stubbedCreatedFtpPassword,
        createdAt: '2026-09-01T10:00:00Z',
      }
      users.push({
        id: created.id,
        accountId: created.accountId,
        name: created.name,
        fullName: created.fullName,
        protocol: created.protocol,
        createdAt: created.createdAt,
      })
      await route.fulfill({
        status: 201,
        contentType: 'application/json',
        body: JSON.stringify(created),
      })
      return
    }

    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(users),
    })
  })
}

/**
 * Fulfils `GET /api/v1/ftp-users` with an RFC 7807 problem body, so a spec can assert the screen
 * renders the backend's own already-localized message verbatim.
 *
 * This is the shape an agent REFUSAL arrives in as well as the shape a host failure does — the wire
 * carries nothing that tells them apart, which is exactly what the spec using this asserts the
 * screen does not pretend to know.
 * @param page The Playwright page whose network the route is installed on.
 * @param detail The backend-localized message the stub reports in `detail`.
 * @param status The HTTP status the stub answers with.
 * @returns Resolves once the route is installed.
 */
export const stubFtpUsersProblem = async (
  page: Page,
  detail: string,
  status = 500,
): Promise<void> => {
  await page.route(FTP_USERS_PATTERN, async (route) => {
    await route.fulfill({
      status,
      contentType: 'application/problem+json',
      body: JSON.stringify({ code: 'HostUnexpectedError', title: 'Unexpected error', detail }),
    })
  })
}

/**
 * Serves an empty `GET /api/v1/ftp-users` list but rejects `POST` with an RFC 7807 problem body.
 * @param page The Playwright page whose network the route is installed on.
 * @param detail The backend-localized message the stub reports in `detail`.
 * @returns Resolves once the route is installed.
 */
export const stubCreateFtpUserProblem = async (page: Page, detail: string): Promise<void> => {
  await page.route(FTP_USERS_PATTERN, async (route) => {
    if (route.request().method() === 'POST') {
      await route.fulfill({
        status: 409,
        contentType: 'application/problem+json',
        body: JSON.stringify({ code: 'FtpUserNameTaken', title: 'Conflict', detail }),
      })
      return
    }
    await route.fulfill({ status: 200, contentType: 'application/json', body: '[]' })
  })
}

/**
 * Fulfils `POST /api/v1/ftp-users/{id}/password` with a new password, and `DELETE
 * /api/v1/ftp-users/{id}` with a success.
 *
 * Installed AFTER {@link stubFtpUsers} in a spec: Playwright gives priority to the most recently
 * registered route, and these two patterns are the narrower ones.
 * @param page The Playwright page whose network the routes are installed on.
 * @param fullName The system login the reset reports the new password for.
 * @returns Resolves once both routes are installed.
 */
export const stubFtpUserActions = async (page: Page, fullName: string): Promise<void> => {
  await page.route(FTP_USER_PATTERN, async (route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: 'true' })
  })

  await page.route(FTP_USER_PASSWORD_PATTERN, async (route) => {
    const id = new URL(route.request().url()).pathname.split('/').slice(-2)[0] ?? ''
    const reset: FtpUserPassword = { id, fullName, password: stubbedResetFtpPassword }
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(reset),
    })
  })
}

/**
 * Fulfils `GET /api/v1/ftps-server` with the given status, and answers the two switches with it too.
 * @param page The Playwright page whose network the routes are installed on.
 * @param status The daemon state the stub reports.
 * @returns Resolves once both routes are installed.
 */
export const stubFtpsServer = async (page: Page, status: FtpsStatus): Promise<void> => {
  await page.route(FTPS_SERVER_PATTERN, async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(status),
    })
  })

  await page.route(FTPS_SERVER_SWITCH_PATTERN, async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(status),
    })
  })
}

/**
 * Refuses `GET /api/v1/ftps-server` with a 403, which is what the panel answers a customer: the
 * status carries a certificate path on the host, and that is operator-facing text.
 * @param page The Playwright page whose network the route is installed on.
 * @returns Resolves once the route is installed.
 */
export const stubFtpsServerForbidden = async (page: Page): Promise<void> => {
  await page.route(FTPS_SERVER_PATTERN, async (route) => {
    await route.fulfill({
      status: 403,
      contentType: 'application/problem+json',
      body: JSON.stringify({ code: 'Forbidden', title: 'Forbidden', detail: 'Administrators only.' }),
    })
  })
}
