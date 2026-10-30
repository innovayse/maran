import type { Page } from '@playwright/test'
import { pagedAnswer, type PagedRules } from './paged-answer'
import type { Backup, BackupAttention } from '../../src/types/backup'

/** The collection endpoint the backups list and the create form both talk to, with or without a query. */
const BACKUPS_PATTERN = /\/api\/v1\/backups(\?.*)?$/

/** The attention endpoint, with or without its `accountId`. */
const ATTENTION_PATTERN = /\/api\/v1\/backups\/attention(\?.*)?$/

/** The single-backup endpoint, which answers the delete. A `*` never spans a `/`. */
const BACKUP_PATTERN = '**/api/v1/backups/*'

/**
 * How the backups endpoint searches, sorts and filters, as the panel does: the search reads the
 * owner's name and a deleted account's recorded username, and the date window is inclusive.
 */
const BACKUP_RULES: PagedRules<Backup> = {
  matches: (backup, term) => {
    return backup.ownerName.toLowerCase().includes(term) || backup.orphanedAccountUsername.toLowerCase().includes(term)
  },
  sorts: {
    startedAt: (backup) => {
      return backup.startedAt
    },
    // Zero-padded so the stub's text comparison orders sizes as numbers.
    size: (backup) => {
      return String(backup.sizeBytes).padStart(16, '0')
    },
  },
  filters: {
    accountId: (backup, value) => {
      return backup.accountId === value
    },
    status: (backup, value) => {
      return backup.status === value
    },
    kind: (backup, value) => {
      return backup.kind === value
    },
    from: (backup, value) => {
      return Date.parse(backup.startedAt) >= Date.parse(value)
    },
    to: (backup, value) => {
      return Date.parse(backup.startedAt) <= Date.parse(value)
    },
  },
}

/**
 * The attention answer the panel gives for these backups: the accounts whose most recent FINISHED
 * run failed, and the newest such failure. The same rule the panel's handler states — a running
 * run neither raises nor clears a failure.
 * @param backups The backups the stub knows.
 * @param accountId The account the question is narrowed to, or null for every account.
 * @returns The answer.
 */
const attentionOf = (backups: readonly Backup[], accountId: string | null): BackupAttention => {
  const latest = new Map<string, Backup>()
  for (const backup of backups) {
    if (backup.status === 'running' || (accountId !== null && backup.accountId !== accountId)) {
      continue
    }
    const seen = latest.get(backup.accountId)
    if (seen === undefined || Date.parse(backup.startedAt) > Date.parse(seen.startedAt)) {
      latest.set(backup.accountId, backup)
    }
  }
  const failures = [...latest.values()]
    .filter((backup) => {
      return backup.status === 'failed'
    })
    .sort((left, right) => {
      return Date.parse(right.startedAt) - Date.parse(left.startedAt)
    })
  return { unrecoveredAccounts: failures.length, newest: failures[0] ?? null }
}

/**
 * Fulfils `GET /api/v1/backups/attention` from the given backups, as the panel would answer it.
 * @param page The Playwright page whose network the route is installed on.
 * @param backups The backups the answer is computed from; read on every request, so a spec that
 * changes the array changes the next answer.
 * @returns Resolves once the route is installed.
 */
const stubBackupAttention = async (page: Page, backups: readonly Backup[]): Promise<void> => {
  await page.route(ATTENTION_PATTERN, async (route) => {
    const accountId = new URL(route.request().url()).searchParams.get('accountId')
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(attentionOf(backups, accountId)),
    })
  })
}

/**
 * Fulfils `GET /api/v1/backups` as a page honouring `page`, `pageSize`, `search`, `sort`,
 * `accountId`, `status`, `kind`, `from` and `to`, answers `GET /api/v1/backups/attention` from the
 * same backups, and answers a `POST` with a finished record that joins them.
 *
 * The stub answers the create with a COMPLETED backup carrying a real size and digest, because
 * that is what the panel's synchronous create endpoint does — it does not return until the archive
 * is written. A stub that answered `running` would let a screen that cannot render a finished run
 * pass.
 * @param page The Playwright page whose network the route is installed on.
 * @param backups The backups the stub knows, newest first — the panel's default order.
 * @returns A function reporting every list request so far, oldest first, so a spec can read what
 * the page asked the panel for.
 */
export const stubBackups = async (page: Page, backups: Backup[]): Promise<() => readonly URL[]> => {
  const listRequests: URL[] = []

  await page.route(BACKUPS_PATTERN, async (route) => {
    if (route.request().method() === 'POST') {
      const submitted = route.request().postDataJSON() as { accountId: string }
      const created: Backup = {
        id: '99999999-9999-9999-9999-999999999999',
        accountId: submitted.accountId,
        // This stub holds no account catalogue to name the owner from, so it answers as the panel does
        // for an account its directory does not name.
        ownerName: '',
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

    listRequests.push(new URL(route.request().url()))
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(pagedAnswer(backups, route.request().url(), BACKUP_RULES)),
    })
  })
  await stubBackupAttention(page, backups)

  return () => {
    return listRequests
  }
}

/**
 * Fulfils `GET /api/v1/backups` with an RFC 7807 problem body, so a spec can assert the page
 * renders the backend's own already-localized message verbatim.
 * @param page The Playwright page whose network the route is installed on.
 * @param detail The backend-localized message the stub reports in `detail`.
 * @returns Resolves once the route is installed.
 */
export const stubBackupsProblem = async (page: Page, detail: string): Promise<void> => {
  // The attention question is answered, with nothing to attend to, so the only failure on the
  // screen is the list's own.
  await stubBackupAttention(page, [])
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
 * registered route, and this pattern is the narrower one. Anything but a DELETE falls back, so the
 * attention read still reaches its own route.
 * @param page The Playwright page whose network the route is installed on.
 * @param backups The same array handed to {@link stubBackups}; a deleted row is removed from it.
 * @returns A function answering how many delete requests have been made so far.
 */
export const stubBackupDeletion = async (page: Page, backups: Backup[]): Promise<() => number> => {
  let deletions = 0

  await page.route(BACKUP_PATTERN, async (route) => {
    if (route.request().method() !== 'DELETE') {
      await route.fallback()
      return
    }

    deletions += 1
    // The row leaves the catalogue as it leaves the panel, so the page's reread no longer lists it.
    const id = new URL(route.request().url()).pathname.split('/').at(-1)
    const index = backups.findIndex((backup) => {
      return backup.id === id
    })
    if (index >= 0) {
      backups.splice(index, 1)
    }
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
