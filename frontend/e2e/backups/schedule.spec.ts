import { expect, test } from '@playwright/test'
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
}

// The panel invents no default schedule, so a server nobody has configured answers 404 for ever.
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
  await expect.poll(traffic.reads).toEqual([
    '/api/v1/backup-schedules',
    `/api/v1/backup-schedules?accountId=${ALICE.id}`,
  ])
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
  // endpoint that would record another refuses every request today.
  for (const forbidden of ['S3', 'Bucket', 'bucket', 'Region', 'Destination', 'destination']) {
    await expect(page.locator('body')).not.toContainText(forbidden)
  }
})

// A route nothing links to is a feature nobody has. This screen already existed once with no route
// and no way in, and every spec it had passed, because a spec navigates by URL where an operator
// navigates by clicking. So this one clicks: sidebar, then the backups screen, then the schedule.
test('an operator reaches the schedule through the shell, without typing a URL', async ({ page }) => {
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
