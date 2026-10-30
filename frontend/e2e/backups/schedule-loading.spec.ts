import { test, type Page  } from '@playwright/test'
import { describeRegionLoading } from '../fixtures/expect-region-loading'
import { stubAccounts } from '../fixtures/stub-accounts-route'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubBackupSchedules } from '../fixtures/stub-backup-schedule-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import type { BackupSchedule } from '../../src/types/backupSchedule'
import type { PanelModule } from '../../src/types/module'

// The schedule form while the scope's schedule is on the way (#243): real labels over placeholders.

test.use({ timezoneId: 'UTC' })

const SCHEDULES_PATTERN = '**/api/v1/backup-schedules*'

const LICENSED: PanelModule[] = [{ name: 'backups', displayName: 'Backups', tier: 'included', isEnabled: true }]

/**
 * A configured host-wide schedule, so no "nothing is scheduled" warning lands above the form when
 * the answer arrives and the anchor's position is the form's own.
 */
const HOST_SCHEDULE: BackupSchedule = {
  id: '55555555-5555-5555-5555-555555555555',
  accountId: null,
  destinationId: null,
  frequency: 'daily',
  hourUtc: 2,
  dayOfWeekUtc: null,
  retainCount: 4,
  enabled: true,
  lastRunAt: '2026-09-01T02:00:00Z',
  nextRunAt: '2026-09-02T02:00:00Z',
}

/** The server's refusal the failure case answers with. */
const REFUSAL = 'The schedule could not be read.'

const shell = async (page: Page): Promise<void> => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubAccounts(page, [])
}

describeRegionLoading('backup schedule', {
  stub: async (page) => {
    await shell(page)
    await stubBackupSchedules(page, { host: HOST_SCHEDULE })
  },
  stubFailure: async (page) => {
    await shell(page)
    await page.route(SCHEDULES_PATTERN, async (route) => {
      await route.fulfill({
        status: 500,
        contentType: 'application/problem+json',
        body: JSON.stringify({ code: 'HostUnexpectedError', title: 'Unexpected error', detail: REFUSAL }),
      })
    })
  },
  pattern: SCHEDULES_PATTERN,
  path: '/backups/schedule',
  anchor: (page) => {
    return page.getByText('How often', { exact: true })
  },
  // The summary card exists only for a scope that has a schedule, so it appears only once loaded.
  contentText: 'What the server holds',
  failureText: REFUSAL,
})
