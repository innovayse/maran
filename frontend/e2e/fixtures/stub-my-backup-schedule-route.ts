import type { Page } from '@playwright/test'
import type { BackupSchedule, BackupScheduleScopeView } from '../../src/types/backupSchedule'

/** The one endpoint the customer's own schedule screen reads and writes; it names no account. */
const MY_SCHEDULE_PATTERN = '**/api/v1/me/backup-schedule'

/** What the stub recorded about the traffic the screen produced. */
export interface MyBackupScheduleTraffic {
  /** How many GETs reached the stub. */
  reads: () => number
  /** Every body a PUT carried, oldest first. */
  writes: () => unknown[]
}

/**
 * Answers `GET /api/v1/me/backup-schedule` with the schedule given (or `schedule: null` for an
 * account that never saved one), and `PUT` with what was sent, recording both.
 *
 * The recorder is the point: what has to be proved about this form is exactly which members it sends
 * (no account, no destination), and neither is visible on the rendered page.
 * @param page The Playwright page whose network the route is installed on.
 * @param schedule The caller's schedule, or `null` for none.
 * @returns The traffic recorded so far.
 */
export const stubMyBackupSchedule = async (
  page: Page,
  schedule: BackupSchedule | null,
): Promise<MyBackupScheduleTraffic> => {
  let reads = 0
  const writes: unknown[] = []

  await page.route(MY_SCHEDULE_PATTERN, async (route) => {
    if (route.request().method() === 'PUT') {
      const sent = route.request().postDataJSON() as Record<string, unknown>
      writes.push(sent)
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          ...sent,
          id: 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb',
          accountId: '11111111-1111-1111-1111-111111111111',
          destinationId: null,
          lastRunAt: null,
          nextRunAt: null,
        }),
      })
      return
    }

    reads += 1
    const answer: BackupScheduleScopeView = { schedule }
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(answer) })
  })

  return {
    reads: (): number => {
      return reads
    },
    writes: (): unknown[] => {
      return writes
    },
  }
}

/**
 * Refuses every `PUT` of the customer's own schedule with one RFC 7807 problem, so a spec can assert
 * the screen marks the field the code names and renders the backend's own message verbatim.
 *
 * Installed AFTER {@link stubMyBackupSchedule} in a spec: Playwright gives priority to the most
 * recently registered route.
 * @param page The Playwright page whose network the route is installed on.
 * @param code The machine-stable problem code the stubbed panel refuses with.
 * @param detail The backend-localized message the stub reports in `detail`.
 * @returns Resolves once the route is installed.
 */
export const stubMyBackupScheduleRefusal = async (
  page: Page,
  code: string,
  detail: string,
): Promise<void> => {
  await page.route(MY_SCHEDULE_PATTERN, async (route) => {
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
