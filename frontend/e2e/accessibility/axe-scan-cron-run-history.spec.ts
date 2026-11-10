import { expectNoSeriousViolations } from '../fixtures/expect-no-serious-violations'
import { expect, test } from '../fixtures/guarded-test'
import { stubAccounts } from '../fixtures/stub-accounts-route'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubCronOptions } from '../fixtures/stub-cron-options-route'
import { stubCronEntries, stubCronEnvironment } from '../fixtures/stub-cron-routes'
import { stubCronRuns } from '../fixtures/stub-cron-runs-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import { stubShellReads } from '../fixtures/stub-shell-reads'
import type { Account } from '../../src/types/account'
import type { CronEntry } from '../../src/types/cronEntry'
import type { CronRun, CronRunSummary } from '../../src/types/cronRun'
import type { PanelModule } from '../../src/types/module'
import type { Page } from '@playwright/test'

// Issue #359: axe on the cron run history, a screen reached from a row's menu. It is scanned with
// one run of each outcome in the list, in both themes, because the outcome badges are the part
// whose colours differ per outcome and per theme: a list with a single succeeded run would draw
// one badge and leave the other two tokens unmeasured.

const LICENSED: PanelModule[] = [
  { name: 'cron', displayName: 'Scheduled tasks', kindDisplayName: 'Core', tier: 'included', state: 'enabled' },
]

const ALICE: Account = {
  id: '22222222-2222-2222-2222-222222222222',
  name: 'alice',
  primaryDomain: 'alice.example.com',
  planId: '44444444-4444-4444-4444-444444444444',
  status: 'active',
  statusName: 'Active',
  createdAt: '2026-08-01T10:00:00Z',
  resellerId: null,
  resellerName: null,
}

const NIGHTLY: CronEntry = {
  entryId: '11111111-1111-1111-1111-111111111111',
  accountId: ALICE.id,
  schedule: { minute: '30', hour: '3', dayOfMonth: '*', month: '*', dayOfWeek: '*' },
  command: '/usr/bin/php /home/alice/cleanup.php',
  enabled: true,
}

/** The run a single read answers with; the scan never opens it. */
const FAILED_RUN: CronRun = {
  id: '33333333-3333-4333-8333-333333333333',
  entryId: NIGHTLY.entryId,
  startedAt: '2026-11-01T09:00:00Z',
  durationMilliseconds: 1500,
  end: { kind: 'exited', exitCode: 2 },
  outcome: 'failed',
  outcomeName: 'Failed',
  stdout: '',
  stderr: 'permission denied\n',
}

/** One history row: the failed run, one that succeeded and one a ceiling stopped. */
const HISTORY: CronRunSummary[] = [
  {
    id: FAILED_RUN.id,
    entryId: NIGHTLY.entryId,
    startedAt: FAILED_RUN.startedAt,
    durationMilliseconds: FAILED_RUN.durationMilliseconds,
    end: FAILED_RUN.end,
    outcome: 'failed',
    outcomeName: 'Failed',
  },
  {
    id: '44444444-4444-4444-8444-444444444444',
    entryId: NIGHTLY.entryId,
    startedAt: '2026-11-02T09:00:00Z',
    durationMilliseconds: 800,
    end: { kind: 'exited', exitCode: 0 },
    outcome: 'succeeded',
    outcomeName: 'Succeeded',
  },
  {
    id: '55555555-5555-4555-8555-555555555555',
    entryId: NIGHTLY.entryId,
    startedAt: '2026-11-03T09:00:00Z',
    durationMilliseconds: 60000,
    end: { kind: 'stopped', limit: 'runtime' },
    outcome: 'stopped',
    outcomeName: 'Stopped',
  },
]

/**
 * Signs in, then moves to the run history by clicks, the way an operator reaches it: the
 * scheduled-tasks list, a row's actions menu, Run history.
 * @param page The page to drive.
 * @returns Resolves once the run history shows its rows.
 */
const openRunHistory = async (page: Page): Promise<void> => {
  await page.setViewportSize({ width: 1440, height: 900 })
  await stubSignedIn(page)
  await stubShellReads(page)
  await stubHealthy(page)
  await stubCronOptions(page)
  await stubModules(page, LICENSED)
  await stubAccounts(page, [ALICE])
  await stubCronEntries(page, [NIGHTLY])
  await stubCronEnvironment(page, [])
  await stubCronRuns(page, FAILED_RUN, HISTORY)

  await page.goto('/cron')
  await expect(page.getByRole('heading', { level: 1, name: 'Scheduled tasks' })).toBeVisible()
  await page.getByRole('button', { name: `Actions for ${NIGHTLY.command}` }).click()
  await page.getByRole('menuitem', { name: 'Run history' }).click()
  await expect(page.getByRole('heading', { level: 1, name: 'Run history' })).toBeVisible()
  // The scan must read the rows, not the loading state.
  await expect(page.getByRole('table').getByText('Failed', { exact: true })).toBeVisible()
  await expect(page.getByRole('table').getByText('Succeeded', { exact: true })).toBeVisible()
  await expect(page.getByRole('table').getByText('Stopped', { exact: true })).toBeVisible()
}

for (const theme of ['dark', 'light'] as const) {
  test.describe(`in the ${theme} theme`, () => {
    test.use({ colorScheme: theme })

    test('the cron run history has no serious accessibility violation', async ({ page }) => {
      await openRunHistory(page)

      await expectNoSeriousViolations(page)
    })
  })
}
