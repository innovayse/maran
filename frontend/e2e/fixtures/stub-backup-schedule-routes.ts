import type { Page } from '@playwright/test'
import type { BackupSchedule, BackupScheduleScope } from '../../src/types/backupSchedule'

/** The one endpoint the schedule screen reads and writes; the scope travels in query or body. */
const SCHEDULES_PATTERN = '**/api/v1/backup-schedules*'

/** What the stub recorded about the traffic the screen produced. */
export interface BackupScheduleTraffic {
  /** Every URL a GET reached the stub on, oldest first — the scope is in the query string. */
  reads: () => string[]
  /** Every body a PUT carried, oldest first. */
  writes: () => unknown[]
}

/**
 * Answers `GET /api/v1/backup-schedules` from a map of scope to schedule, and `PUT` with what was
 * sent, recording both.
 *
 * A scope with no entry in the map is answered `200` with `schedule: null` — the panel's own answer
 * for a scope nobody has configured, and the state the screen exists to render honestly.
 * The recorder is the point rather than a convenience: what has to be proved about this form is
 * which SCOPE it read and exactly which members it sent, and neither is visible on the rendered
 * page.
 * @param page The Playwright page whose network the route is installed on.
 * @param schedules The schedule per scope, keyed by account id and by `'host'` for the host policy.
 * @returns The traffic recorded so far.
 */
export const stubBackupSchedules = async (
  page: Page,
  schedules: Readonly<Record<string, BackupSchedule>>,
): Promise<BackupScheduleTraffic> => {
  const reads: string[] = []
  const writes: unknown[] = []

  await page.route(SCHEDULES_PATTERN, async (route) => {
    const url = new URL(route.request().url())

    if (route.request().method() === 'PUT') {
      writes.push(route.request().postDataJSON())
      const sent = route.request().postDataJSON() as BackupSchedule
      // Saving runs nothing, so no run is reported. The stub does not restate the server's
      // next-run rule, so it reports none: a spec about the next run stubs the read instead.
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          ...sent,
          id: 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
          lastRunAt: null,
          nextRunAt: null,
        }),
      })
      return
    }

    reads.push(url.pathname + url.search)
    const scope = url.searchParams.get('accountId') ?? 'host'
    // A scope with no entry is the ordinary first state of this endpoint, not a fault: no schedule
    // has ever been saved for it, and the panel says so in a successful answer.
    const answer: BackupScheduleScope = { schedule: schedules[scope] ?? null }
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(answer),
    })
  })

  return {
    reads: (): string[] => {
      return reads
    },
    writes: (): unknown[] => {
      return writes
    },
  }
}

/**
 * Answers the read as configured but refuses every `PUT` with one RFC 7807 problem, so a spec can
 * assert the screen branches on the code and renders the backend's own message verbatim.
 *
 * Installed AFTER {@link stubBackupSchedules} in a spec: Playwright gives priority to the most
 * recently registered route.
 * @param page The Playwright page whose network the route is installed on.
 * @param code The machine-stable problem code the stubbed panel refuses with.
 * @param detail The backend-localized message the stub reports in `detail`.
 * @returns Resolves once the route is installed.
 */
export const stubBackupScheduleRefusal = async (
  page: Page,
  code: string,
  detail: string,
): Promise<void> => {
  await page.route(SCHEDULES_PATTERN, async (route) => {
    if (route.request().method() !== 'PUT') {
      await route.fallback()
      return
    }

    await route.fulfill({
      status: 400,
      contentType: 'application/problem+json',
      body: JSON.stringify({ code, title: 'Invalid', detail }),
    })
  })
}
