import { expect, test, type Page } from '@playwright/test'
import { stubAccounts } from '../fixtures/stub-accounts-route'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import { stubCronEntries, stubCronEnvironment } from '../fixtures/stub-cron-routes'
import { stubCronRuns } from '../fixtures/stub-cron-runs-routes'
import type { Account } from '../../src/types/account'
import type { CronEntry } from '../../src/types/cronEntry'
import type { CronRun, CronRunSummary } from '../../src/types/cronRun'
import type { PanelModule } from '../../src/types/module'

const LICENSED: PanelModule[] = [{ name: 'cron', displayName: 'Scheduled tasks', tier: 'included', state: 'enabled' }]

const ALICE: Account = {
  id: '22222222-2222-2222-2222-222222222222',
  name: 'alice',
  primaryDomain: 'alice.example.com',
  planId: '44444444-4444-4444-4444-444444444444',
  status: 'active',
  createdAt: '2026-08-01T10:00:00Z',
}

const NIGHTLY: CronEntry = {
  entryId: '11111111-1111-1111-1111-111111111111',
  accountId: ALICE.id,
  schedule: { minute: '30', hour: '3', dayOfMonth: '*', month: '*', dayOfWeek: '*' },
  command: '/usr/bin/php /home/alice/cleanup.php',
  enabled: true,
}

const FAILED: CronRun = {
  id: '33333333-3333-4333-8333-333333333333',
  entryId: NIGHTLY.entryId,
  startedAt: '2026-11-01T09:00:00Z',
  durationMilliseconds: 1500,
  end: { kind: 'exited', exitCode: 2 },
  outcome: 'failed',
  stdout: 'cleaning\n',
  stderr: 'permission denied\n',
}

const SUCCEEDED: CronRunSummary = {
  id: '44444444-4444-4444-8444-444444444444',
  entryId: NIGHTLY.entryId,
  startedAt: '2026-11-01T08:00:00Z',
  durationMilliseconds: 900,
  end: { kind: 'exited', exitCode: 0 },
  outcome: 'succeeded',
}

const STOPPED: CronRun = {
  id: '55555555-5555-4555-8555-555555555555',
  entryId: NIGHTLY.entryId,
  startedAt: '2026-11-01T10:00:00Z',
  durationMilliseconds: 120000,
  end: { kind: 'stopped', limit: 'runtime' },
  outcome: 'stopped',
  stdout: '',
  stderr: '',
}

const openCronPage = async (page: Page, run: CronRun = FAILED): Promise<void> => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubAccounts(page, [ALICE])
  await stubCronEntries(page, [NIGHTLY])
  await stubCronEnvironment(page, [])
  await stubCronRuns(page, run, [FAILED, SUCCEEDED])
  await page.goto('/cron')
  await expect(page.getByRole('heading', { level: 1, name: 'Scheduled tasks' })).toBeVisible()
}

test('Run now shows the run that failed, with both streams and its exit code', async ({ page }) => {
  await openCronPage(page)

  await page.getByRole('button', { name: `Actions for ${NIGHTLY.command}` }).click()
  await page.getByRole('menuitem', { name: 'Run now' }).click()

  const dialog = page.getByRole('dialog', { name: 'Run result' })
  await expect(dialog.getByText('Failed')).toBeVisible()
  await expect(dialog.getByTestId('cron-run-exit-code')).toHaveText('2')
  await expect(dialog.getByRole('region', { name: 'Standard error' })).toHaveText('permission denied')
})

test('Run now shows a stopped run with its reason and no exit status', async ({ page }) => {
  await openCronPage(page, STOPPED)

  await page.getByRole('button', { name: `Actions for ${NIGHTLY.command}` }).click()
  await page.getByRole('menuitem', { name: 'Run now' }).click()

  const dialog = page.getByRole('dialog', { name: 'Run result' })
  await expect(dialog.getByText('The run reached the two-minute limit and was stopped.')).toBeVisible()
  await expect(dialog.getByTestId('cron-run-exit-code')).toHaveText('Not reported')
})

test('Run history lists the runs and narrows them by outcome through the filter sheet', async ({ page }) => {
  await openCronPage(page)

  await page.getByRole('button', { name: `Actions for ${NIGHTLY.command}` }).click()
  await page.getByRole('menuitem', { name: 'Run history' }).click()

  await expect(page.getByRole('heading', { level: 1, name: 'Run history' })).toBeVisible()
  await expect(page.getByRole('row')).toHaveCount(3)

  await page.getByTestId('filter-trigger').click()
  const sheet = page.getByRole('dialog', { name: 'Filters' })
  await sheet.getByRole('combobox', { name: 'Outcome' }).click()
  await page.getByRole('option', { name: 'Succeeded' }).click()
  await sheet.getByRole('button', { name: 'Apply' }).click()

  await expect(page.getByRole('row')).toHaveCount(2)
})
