import type { Page } from '@playwright/test'
import type { CreatedSftpUser, SftpUser, SftpUserPassword } from '../../src/types/sftpUser'

/** The collection endpoint the SFTP list and the create form both talk to. */
const SFTP_USERS_PATTERN = '**/api/v1/sftp-users'

/** The single-login endpoint, which answers the removal. */
const SFTP_USER_PATTERN = '**/api/v1/sftp-users/*'

/** The password-reset endpoint. A `*` never spans a `/`, so this is narrower than the one above. */
const SFTP_USER_PASSWORD_PATTERN = '**/api/v1/sftp-users/*/password'

/** The account prefix the stubs build a full login with, mirroring what the host would hold. */
const ACCOUNT_PREFIX = 'alice'

/** The password a stubbed create answers with — a value no other fixture in the suite uses. */
export const stubbedCreatedSftpPassword = 'Cr34ted-SFTP-P4ss-Once'

/** The password a stubbed reset answers with, distinct from the created one so a spec can tell them apart. */
export const stubbedResetSftpPassword = 'R3set-SFTP-P4ss-Once'

/**
 * Fulfils `GET /api/v1/sftp-users` with the given list and answers a `POST` by echoing the
 * submitted body back as a created login, prefixed the way the host prefixes it and carrying a
 * password.
 *
 * The prefix is applied HERE, in the stub, because it is the server's job: the SPA under test must
 * render the `fullName` it was sent rather than assembling one, and a stub that echoed the bare
 * suffix would let a page that assembles its own names pass.
 * @param page The Playwright page whose network the route is installed on.
 * @param users The logins the stub reports for the list request.
 * @returns Resolves once the route is installed.
 */
export const stubSftpUsers = async (page: Page, users: SftpUser[]): Promise<void> => {
  await page.route(SFTP_USERS_PATTERN, async (route) => {
    if (route.request().method() === 'POST') {
      const submitted = route.request().postDataJSON() as { accountId: string; name: string }
      const created: CreatedSftpUser = {
        id: '99999999-9999-9999-9999-999999999999',
        accountId: submitted.accountId,
        name: submitted.name,
        fullName: `${ACCOUNT_PREFIX}_${submitted.name}`,
        password: stubbedCreatedSftpPassword,
        createdAt: '2026-09-01T10:00:00Z',
      }
      users.push({
        id: created.id,
        accountId: created.accountId,
        name: created.name,
        fullName: created.fullName,
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
 * Fulfils `GET /api/v1/sftp-users` with an RFC 7807 problem body, so a spec can assert the page
 * renders the backend's own already-localized message verbatim.
 * @param page The Playwright page whose network the route is installed on.
 * @param detail The backend-localized message the stub reports in `detail`.
 * @returns Resolves once the route is installed.
 */
export const stubSftpUsersProblem = async (page: Page, detail: string): Promise<void> => {
  await page.route(SFTP_USERS_PATTERN, async (route) => {
    await route.fulfill({
      status: 500,
      contentType: 'application/problem+json',
      body: JSON.stringify({ code: 'HostUnexpectedError', title: 'Unexpected error', detail }),
    })
  })
}

/**
 * Serves an empty `GET /api/v1/sftp-users` list but rejects `POST` with an RFC 7807 problem body,
 * so a spec can assert the create form surfaces the server's own message rather than invented copy.
 * @param page The Playwright page whose network the route is installed on.
 * @param detail The backend-localized message the stub reports in `detail`.
 * @returns Resolves once the route is installed.
 */
export const stubCreateSftpUserProblem = async (page: Page, detail: string): Promise<void> => {
  await page.route(SFTP_USERS_PATTERN, async (route) => {
    if (route.request().method() === 'POST') {
      await route.fulfill({
        status: 409,
        contentType: 'application/problem+json',
        body: JSON.stringify({ code: 'SftpUserNameTaken', title: 'Conflict', detail }),
      })
      return
    }
    await route.fulfill({ status: 200, contentType: 'application/json', body: '[]' })
  })
}

/**
 * Fulfils `POST /api/v1/sftp-users/{id}/password` with a new password, and `DELETE
 * /api/v1/sftp-users/{id}` with a success.
 *
 * Installed AFTER {@link stubSftpUsers} in a spec: Playwright gives priority to the most recently
 * registered route, and these two patterns are the narrower ones.
 * @param page The Playwright page whose network the routes are installed on.
 * @param fullName The system login the reset reports the new password for.
 * @returns Resolves once both routes are installed.
 */
export const stubSftpUserActions = async (page: Page, fullName: string): Promise<void> => {
  await page.route(SFTP_USER_PATTERN, async (route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: 'true' })
  })

  await page.route(SFTP_USER_PASSWORD_PATTERN, async (route) => {
    const id = new URL(route.request().url()).pathname.split('/').slice(-2)[0] ?? ''
    const reset: SftpUserPassword = { id, fullName, password: stubbedResetSftpPassword }
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(reset),
    })
  })
}
