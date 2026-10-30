import { expect, test, type ConsoleMessage, type Locator } from '@playwright/test'
import { stubAccounts } from '../fixtures/stub-accounts-route'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubBackups } from '../fixtures/stub-backups-routes'
import {
  stubBackupScheduleRefusal,
  stubBackupSchedules,
} from '../fixtures/stub-backup-schedule-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import type { Account } from '../../src/types/account'
import type { BackupSchedule } from '../../src/types/backupSchedule'
import type { PanelModule } from '../../src/types/module'

// The summary card prints instants; in UTC they read the same on every machine running the suite.
test.use({ timezoneId: 'UTC' })

const LICENSED: PanelModule[] = [
  { name: 'backups', displayName: 'Backups', tier: 'included', isEnabled: true },
]

const ALICE: Account = {
  id: '22222222-2222-2222-2222-222222222222',
  name: 'alice',
  primaryDomain: 'alice.example.com',
  planId: '44444444-4444-4444-4444-444444444444',
  status: 'active',
  createdAt: '2026-08-01T10:00:00Z',
}

/** One account's own schedule: weekly, and it has actually run. */
const ALICE_SCHEDULE: BackupSchedule = {
  id: '55555555-5555-5555-5555-555555555555',
  accountId: ALICE.id,
  destinationId: null,
  frequency: 'weekly',
  hourUtc: 2,
  dayOfWeekUtc: 'tuesday',
  retainCount: 4,
  enabled: true,
  lastRunAt: '2026-09-01T02:00:00Z',
  nextRunAt: '2026-09-08T02:00:00Z',
}

// The panel invents no default schedule, so a server nobody has configured answers `schedule: null`.
// A screen that met that and drew a filled form would be stating a cadence the server will not
// keep tonight; one that drew an error would be calling the ordinary first state a fault.
test('a server with no schedule is told so, and every field is left blank', async ({ page }) => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubAccounts(page, [ALICE])
  await stubBackupSchedules(page, {})

  await page.goto('/backups/schedule')

  await expect(page.getByText('Nothing is scheduled here', { exact: false })).toBeVisible()
  // Positive control on the probe: the form IS rendered and this locator can read its fields, so
  // their emptiness is a fact about the screen rather than about the probe.
  await expect(page.getByRole('button', { name: 'Save schedule' })).toBeVisible()
  await expect(page.getByLabel('Hour of the day (UTC)')).toHaveValue('')
  await expect(page.getByLabel('Copies to keep')).toHaveValue('')
  // The cadence picker shows its placeholder, not a value nobody chose.
  await expect(page.getByText('Choose how often')).toBeVisible()
})

// Issue #211: "nothing is scheduled here" is an answer, not a failure. The screen used to read a
// 404 for it on every load and the browser logged the failed request. Both scopes the screen offers
// are walked — the host-wide policy and an account's override — because each is its own read.
test('a server with no schedule loads with a clean console and no failed request, in every scope', async ({
  page,
}) => {
  const consoleErrors: string[] = []
  const failedReads: string[] = []
  page.on('console', (message: ConsoleMessage) => {
    if (message.type() === 'error') {
      consoleErrors.push(message.text())
    }
  })
  page.on('response', (response) => {
    if (response.url().includes('/api/v1/backup-schedules') && response.status() >= 400) {
      failedReads.push(`${String(response.status())} ${response.url()}`)
    }
  })

  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubAccounts(page, [ALICE])
  const traffic = await stubBackupSchedules(page, {})

  await page.goto('/backups/schedule')
  await expect(page.getByText('Nothing is scheduled here', { exact: false })).toBeVisible()

  await page.getByRole('combobox', { name: 'Applies to' }).click()
  await page.getByRole('option', { name: 'alice' }).click()

  // Positive control on the probes: both reads reached the stub, so the listeners had traffic to see.
  await expect
    .poll(traffic.reads)
    .toEqual(['/api/v1/backup-schedules', `/api/v1/backup-schedules?accountId=${ALICE.id}`])
  await expect(page.getByText('Nothing is scheduled here', { exact: false })).toBeVisible()
  expect(failedReads).toEqual([])
  expect(consoleErrors).toEqual([])
})

// A null `accountId` is the host-wide policy, not "no account chosen": it is the schedule every
// account without an override is actually backed up by. An unselected picker would say the
// opposite, and the read it produced would be indistinguishable from an unmade choice.
test('the host-wide policy is named as a scope of its own and is read with no account at all', async ({
  page,
}) => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubAccounts(page, [ALICE])
  const traffic = await stubBackupSchedules(page, {})

  await page.goto('/backups/schedule')

  await expect(page.getByText('Every account — the host-wide policy')).toBeVisible()
  await expect(
    page.getByText('every account without a schedule of its own', { exact: false }),
  ).toBeVisible()
  await expect.poll(traffic.reads).toEqual(['/api/v1/backup-schedules'])
})

// The other half of the same distinction: an account's override is a different schedule, read from
// a different scope, and the screen must not show one account's cadence under another's name.
test("choosing an account reads that account's own schedule and shows it", async ({ page }) => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubAccounts(page, [ALICE])
  const traffic = await stubBackupSchedules(page, { [ALICE.id]: ALICE_SCHEDULE })

  await page.goto('/backups/schedule')
  await expect(page.getByText('Nothing is scheduled here', { exact: false })).toBeVisible()

  await page.getByRole('combobox', { name: 'Applies to' }).click()
  await page.getByRole('option', { name: 'alice' }).click()

  await expect(page.getByText('Nothing is scheduled here', { exact: false })).toHaveCount(0)
  await expect(page.getByLabel('Hour of the day (UTC)')).toHaveValue('2')
  await expect(page.getByLabel('Copies to keep')).toHaveValue('4')
  await expect
    .poll(traffic.reads)
    .toEqual(['/api/v1/backup-schedules', `/api/v1/backup-schedules?accountId=${ALICE.id}`])
})

// `SaveBackupScheduleCommand` establishes `ipAddress` and `userAgent` itself — they are
// `[property: JsonIgnore]` and `[BindNever]`, and the audit record of a request must not be
// written by the request. `destinationId` is the opposite case: the command declares it with no
// default, so a body that omits it would arrive with the member at its default. Neither fact is
// visible on the rendered page, so the recorded body is the only witness to either.
test('saving sends exactly the members the command binds, and no weekday on a daily schedule', async ({
  page,
}) => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubAccounts(page, [ALICE])
  const traffic = await stubBackupSchedules(page, {})

  await page.goto('/backups/schedule')
  await page.getByRole('combobox', { name: 'How often' }).click()
  await page.getByRole('option', { name: 'Every day' }).click()
  await page.getByLabel('Hour of the day (UTC)').fill('3')
  await page.getByLabel('Copies to keep').fill('7')
  await page.getByRole('switch', { name: 'Run this schedule' }).click()
  await page.getByRole('button', { name: 'Save schedule' }).click()

  await expect.poll(traffic.writes).toEqual([
    {
      accountId: null,
      destinationId: null,
      frequency: 'daily',
      hourUtc: 3,
      dayOfWeekUtc: null,
      retainCount: 7,
      enabled: true,
    },
  ])
})

// The backend owns the text of a server outcome and the SPA renders it verbatim (rules/vue.md);
// the CODE is for behaviour only, and here the behaviour is which field is marked invalid. One
// refusal must read as one refusal — the message on the field it names, and not also in the
// banner above it.
test('a refusal about the retained count is shown on that field, once', async ({ page }) => {
  const backendDetail = 'Keep between 1 and 365 copies.'
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubAccounts(page, [ALICE])
  await stubBackupSchedules(page, {})
  await stubBackupScheduleRefusal(page, 'BackupScheduleRetainOutOfRange', backendDetail)

  await page.goto('/backups/schedule')
  await page.getByRole('combobox', { name: 'How often' }).click()
  await page.getByRole('option', { name: 'Every day' }).click()
  await page.getByLabel('Hour of the day (UTC)').fill('3')
  await page.getByLabel('Copies to keep').fill('900')
  await page.getByRole('button', { name: 'Save schedule' }).click()

  await expect(page.getByText(backendDetail)).toHaveCount(1)
  await expect(page.getByLabel('Copies to keep')).toHaveAttribute('aria-invalid', 'true')
  await expect(page.getByLabel('Hour of the day (UTC)')).toHaveAttribute('aria-invalid', 'false')
})

// Four things a number of copies to keep would otherwise be read as promising. Retention prunes on
// its own five-minute cadence, it never forgets a row whose archive is still there, a copy taken
// before an account was deleted is exempt from it entirely, and nothing here is encrypted at rest.
test('the screen says what retention does not cover', async ({ page }) => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubAccounts(page, [ALICE])
  await stubBackupSchedules(page, {})

  await page.goto('/backups/schedule')

  await expect(page.getByText('every five minutes', { exact: false })).toBeVisible()
  await expect(page.getByText('is never pruned by this number', { exact: false })).toBeVisible()
  await expect(page.getByText('not encrypted at rest', { exact: false })).toBeVisible()
  // And it advertises no choice that does not exist: the panel records one destination and the
  // endpoint that would record another refuses every request today. The page body is checked, not
  // the sidebar, whose Backups submenu names the Destinations screen.
  for (const forbidden of ['S3', 'Bucket', 'bucket', 'Region', 'Destination', 'destination']) {
    await expect(page.locator('#main-content')).not.toContainText(forbidden)
  }
})

// A route nothing links to is a feature nobody has. This screen already existed once with no route
// and no way in, and every spec it had passed, because a spec navigates by URL where an operator
// navigates by clicking. So this one clicks: sidebar, then the backups screen, then the schedule.
test('an operator reaches the schedule through the shell, without typing a URL', async ({
  page,
}) => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubAccounts(page, [ALICE])
  await stubBackups(page, [])
  await stubBackupSchedules(page, {})

  await page.goto('/')
  await page.getByRole('navigation').getByRole('link', { name: 'Backups' }).click()
  // Positive control: the backups screen itself IS reached by clicking, so a failure below is
  // about the schedule's way in rather than about the shell or the locator.
  await expect(page).toHaveURL(/\/backups$/)

  await page.getByRole('link', { name: 'Backup schedule' }).click()

  await expect(page).toHaveURL(/\/backups\/schedule$/)
  await expect(
    page.getByRole('heading', { level: 1, name: 'Backup schedule', exact: true }),
  ).toBeVisible()
})

/** A host-wide schedule just switched on: it has a next run and has never run. */
const JUST_ENABLED: BackupSchedule = {
  id: '66666666-6666-6666-6666-666666666666',
  accountId: null,
  destinationId: null,
  frequency: 'daily',
  hourUtc: 2,
  dayOfWeekUtc: null,
  retainCount: 7,
  enabled: true,
  lastRunAt: null,
  nextRunAt: '2026-10-31T02:00:00Z',
}

// Issue #224: switching a schedule on runs nothing, and the card used to print the save as "Last
// run started". The card now names the next run the server computed and says the schedule has
// never run. The switched-off case is the inverse control: no next run is shown for a schedule
// that will not act.
test('the summary card shows the next run and that a just-enabled schedule has never run', async ({
  page,
}) => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubAccounts(page, [ALICE])
  await stubBackupSchedules(page, {
    host: JUST_ENABLED,
    [ALICE.id]: { ...ALICE_SCHEDULE, enabled: false, lastRunAt: null, nextRunAt: null },
  })

  await page.goto('/backups/schedule')

  const summary = page.locator('dl')
  const valueOf = (term: string): Locator => {
    return summary.locator('div').filter({ has: page.getByText(term, { exact: true }) }).locator('dd')
  }

  await expect(page.getByRole('heading', { name: 'What the server holds' })).toBeVisible()
  await expect(valueOf('Next run')).toHaveText('31 Oct 2026 02:00')
  await expect(valueOf('Last run started')).toHaveText('Never run')

  await page.getByRole('combobox', { name: 'Applies to' }).click()
  await page.getByRole('option', { name: 'alice' }).click()

  await expect(page.getByText('Saved, but switched off')).toBeVisible()
  await expect(valueOf('Last run started')).toHaveText('Never run')
  await expect(summary.getByText('Next run', { exact: true })).toHaveCount(0)
})
