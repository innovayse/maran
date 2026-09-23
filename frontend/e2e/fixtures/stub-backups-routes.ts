import type { Page } from '@playwright/test'
import type { Backup } from '../../src/types/backup'

/** The collection endpoint the backups list and the create form both talk to. */
const BACKUPS_PATTERN = '**/api/v1/backups'

/** The single-backup endpoint, which answers the delete. A `*` never spans a `/`. */
const BACKUP_PATTERN = '**/api/v1/backups/*'

/**
 * Fulfils `GET /api/v1/backups` with the given list and answers a `POST` with a finished record.
 *
 * The stub answers the create with a COMPLETED backup carrying a real size and digest, because
 * that is what the panel's synchronous create endpoint does — it does not return until the archive
 * is written. A stub that answered `running` would let a screen that cannot render a finished run
 * pass.
 * @param page The Playwright page whose network the route is installed on.
 * @param backups The backups the stub reports for the list request.
 * @returns Resolves once the route is installed.
 */
export const stubBackups = async (page: Page, backups: Backup[]): Promise<void> => {
  await page.route(BACKUPS_PATTERN, async (route) => {
    if (route.request().method() === 'POST') {
      const submitted = route.request().postDataJSON() as { accountId: string }
      const created: Backup = {
        id: '99999999-9999-9999-9999-999999999999',
        accountId: submitted.accountId,
        orphanedAccountUsername: '',
        status: 'completed',
        kind: 'manual',
        sizeBytes: 5_242_880,
        sha256: 'e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855',
        databaseCount: 1,
        startedAt: '2026-09-07T09:00:00Z',
        finishedAt: '2026-09-07T09:02:00Z',
        failureCode: '',
        failureDisplayName: '',
      }
      backups.unshift(created)
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
      body: JSON.stringify(backups),
    })
  })
}

/**
 * Fulfils `GET /api/v1/backups` with an RFC 7807 problem body, so a spec can assert the page
 * renders the backend's own already-localized message verbatim.
 * @param page The Playwright page whose network the route is installed on.
 * @param detail The backend-localized message the stub reports in `detail`.
 * @returns Resolves once the route is installed.
 */
export const stubBackupsProblem = async (page: Page, detail: string): Promise<void> => {
  await page.route(BACKUPS_PATTERN, async (route) => {
    await route.fulfill({
      status: 500,
      contentType: 'application/problem+json',
      body: JSON.stringify({ code: 'HostUnexpectedError', title: 'Unexpected error', detail }),
    })
  })
}

/**
 * Fulfils `DELETE /api/v1/backups/{id}` with a success, and counts every request that reaches it.
 *
 * The counter is the point rather than a convenience: a confirmation that a spec drives by clicking
 * proves only that a second click exists, where the number of requests that left the page proves
 * that the first click sent nothing.
 *
 * Installed AFTER {@link stubBackups} in a spec: Playwright gives priority to the most recently
 * registered route, and this pattern is the narrower one.
 * @param page The Playwright page whose network the route is installed on.
 * @returns A function answering how many delete requests have been made so far.
 */
export const stubBackupDeletion = async (page: Page): Promise<() => number> => {
  let deletions = 0

  await page.route(BACKUP_PATTERN, async (route) => {
    if (route.request().method() !== 'DELETE') {
      await route.fallback()
      return
    }

    deletions += 1
    await route.fulfill({ status: 200, contentType: 'application/json', body: 'true' })
  })

  return (): number => {
    return deletions
  }
}

/** The restore endpoint. `*` never spans a `/`, so this cannot collide with the single-backup one. */
const RESTORE_PATTERN = '**/api/v1/backups/*/restore'

/**
 * Fulfils `POST /api/v1/backups/{id}/restore` with one prepared answer, and records every request
 * body that reached it.
 *
 * The recorder is the point rather than a convenience, for the same reason the deletion counter is:
 * a confirmation a spec drives by typing and clicking proves only that a field and a button exist.
 * What has to be proved is that NOTHING left the browser until the account's name was typed, and
 * the only witness to that is the number of bodies that arrived.
 *
 * The answer is given whole — status and body — because this endpoint's two failure families are
 * the thing the screen branches on, and a fixture that could only produce a success would let a
 * screen that renders every failure identically pass.
 * @param page The Playwright page whose network the route is installed on.
 * @param status The HTTP status the stubbed panel answers with.
 * @param body The response body: a `RestoreOutcome` for 200, an RFC 7807 problem otherwise.
 * @returns A function answering the request bodies recorded so far, oldest first.
 */
export const stubBackupRestore = async (
  page: Page,
  status: number,
  body: unknown,
): Promise<() => unknown[]> => {
  const submitted: unknown[] = []

  await page.route(RESTORE_PATTERN, async (route) => {
    submitted.push(route.request().postDataJSON())
    await route.fulfill({
      status,
      contentType: status === 200 ? 'application/json' : 'application/problem+json',
      body: JSON.stringify(body),
    })
  })

  return (): unknown[] => {
    return submitted
  }
}
