import { expect, test } from '../fixtures/guarded-test'
import { stubAccounts } from '../fixtures/stub-accounts-route'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubCronOptions } from '../fixtures/stub-cron-options-route'
import { stubModules } from '../fixtures/stub-modules-route'
import { stubCronEntries, stubCronEnvironment } from '../fixtures/stub-cron-routes'
import { stubCronRuns } from '../fixtures/stub-cron-runs-routes'
import { stubShellReads } from '../fixtures/stub-shell-reads'
import type { Account } from '../../src/types/account'
import type { CronEntry } from '../../src/types/cronEntry'
import type { CronRun, CronRunSummary } from '../../src/types/cronRun'
import type { PanelModule } from '../../src/types/module'
import type { Page } from '@playwright/test'

// The outcome word on a run is the panel's own: the run carries it, so the bundle holds no copy of
// the vocabulary (issue #330).

const LICENSED: PanelModule[] = [{ name: 'cron', displayName: 'Scheduled tasks', kindDisplayName: 'Core', tier: 'included', state: 'enabled' }]

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

/** A run whose outcome name no bundle contains, so a badge printing it can only have read it off the run. */
const RUN: CronRun = {
  id: '33333333-3333-4333-8333-333333333333',
  entryId: NIGHTLY.entryId,
  startedAt: '2026-11-01T09:00:00Z',
  durationMilliseconds: 1500,
  end: { kind: 'exited', exitCode: 2 },
  outcome: 'failed',
  outcomeName: 'Panel-named run failure',
  stdout: '',
  stderr: 'permission denied\n',
}

/** The same run as a history row. */
const ROW: CronRunSummary = {
  id: RUN.id,
  entryId: RUN.entryId,
  startedAt: RUN.startedAt,
  durationMilliseconds: RUN.durationMilliseconds,
  end: RUN.end,
  outcome: RUN.outcome,
  outcomeName: RUN.outcomeName,
}

/**
 * Opens the scheduled-tasks screen with the run stubbed.
 * @param page The page to drive.
 * @returns Resolves once the screen is shown.
 */
const openCronPage = async (page: Page): Promise<void> => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubShellReads(page)
  await stubCronOptions(page)
  await stubModules(page, LICENSED)
  await stubAccounts(page, [ALICE])
  await stubCronEntries(page, [NIGHTLY])
  await stubCronEnvironment(page, [])
  await stubCronRuns(page, RUN, [ROW])
  await page.goto('/cron')
  await expect(page.getByRole('heading', { level: 1, name: 'Scheduled tasks' })).toBeVisible()
}

test('the result dialog prints the outcome name the run carries', async ({ page }) => {
  await openCronPage(page)

  await page.getByRole('button', { name: `Actions for ${NIGHTLY.command}` }).click()
  await page.getByRole('menuitem', { name: 'Run now' }).click()

  const dialog = page.getByRole('dialog', { name: 'Run result' })
  await expect(dialog.getByText('Panel-named run failure', { exact: true })).toBeVisible()
})

test('a row of the run history prints the outcome name the row carries', async ({ page }) => {
  await openCronPage(page)

  await page.getByRole('button', { name: `Actions for ${NIGHTLY.command}` }).click()
  await page.getByRole('menuitem', { name: 'Run history' }).click()

  await expect(page.getByRole('heading', { level: 1, name: 'Run history' })).toBeVisible()
  await expect(page.getByText('Panel-named run failure', { exact: true })).toBeVisible()
})
