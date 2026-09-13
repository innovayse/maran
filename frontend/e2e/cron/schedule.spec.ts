import { expect, test, type Page } from '@playwright/test'
import { stubAccounts } from '../fixtures/stub-accounts-route'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import { stubCronEntries, stubCronEnvironment } from '../fixtures/stub-cron-routes'
import type { Account } from '../../src/types/account'
import type { CronEntry, CronSchedule } from '../../src/types/cronEntry'
import type { PanelModule } from '../../src/types/module'

const LICENSED: PanelModule[] = [
  { name: 'cron', displayName: 'Scheduled tasks', tier: 'included', isEnabled: true },
]

const ALICE: Account = {
  id: '22222222-2222-2222-2222-222222222222',
  name: 'alice',
  primaryDomain: 'alice.example.com',
  planId: '44444444-4444-4444-4444-444444444444',
  status: 'active',
  createdAt: '2026-08-01T10:00:00Z',
}

const COMMAND = '/usr/bin/php /home/alice/cleanup.php'

const openCronPage = async (page: Page, entries: CronEntry[]): Promise<void> => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubAccounts(page, [ALICE])
  await stubCronEntries(page, entries)
  await stubCronEnvironment(page, [])
  await page.goto('/cron')
  await expect(page.getByRole('heading', { level: 1, name: 'Scheduled tasks' })).toBeVisible()
}

// The builder's whole job is this mapping, and it is the half a stubbed network cannot check for
// itself: the assertion is on the body the panel POSTs, against the field names and the five-field
// shape `CronScheduleDto` actually declares. It fails if the weekday lands in `dayOfMonth`, if a
// field the frequency does not use keeps a stale value instead of becoming `*`, or if the panel
// ever starts sending one joined expression instead of the module's five separate fields.
test('the builder maps a weekly pattern onto the five fields the module declares', async ({ page }) => {
  await openCronPage(page, [])

  await page.getByRole('combobox', { name: 'How often' }).click()
  await page.getByRole('option', { name: 'Every week' }).click()

  await page.getByRole('textbox', { name: 'Minute' }).fill('30')
  await page.getByRole('textbox', { name: 'Hour' }).fill('3')
  await page.getByRole('combobox', { name: 'Day of the week' }).click()
  await page.getByRole('option', { name: 'Tuesday' }).click()

  // Shown before anything is sent, so the operator can check a builder pattern against the cron
  // they already know. Tuesday is 2 because cron counts Sunday as 0.
  await expect(page.getByTestId('cron-schedule-preview')).toHaveText('30 3 * * 2')

  await page.getByRole('textbox', { name: 'Command' }).fill(COMMAND)

  const posted = page.waitForRequest(
    (request) => {
      return request.method() === 'POST' && request.url().includes('/api/v1/cron-entries')
    },
  )
  await page.getByRole('button', { name: 'Add entry' }).click()

  const body = (await posted).postDataJSON() as { schedule: CronSchedule; command: string }
  const expected: CronSchedule = {
    minute: '30',
    hour: '3',
    dayOfMonth: '*',
    month: '*',
    dayOfWeek: '2',
  }
  expect(body.schedule).toEqual(expected)
  expect(body.command).toBe(COMMAND)
})

// The frequency decides which parts reach the five fields. Without this, a "daily" pattern that
// quietly kept a weekday from an earlier "weekly" choice would run once a week while its own form
// said daily — and the previous assertion could not tell the two apart, because it never switches
// back.
test('switching the builder back to daily drops the weekday it no longer uses', async ({ page }) => {
  await openCronPage(page, [])

  await page.getByRole('combobox', { name: 'How often' }).click()
  await page.getByRole('option', { name: 'Every week' }).click()
  await page.getByRole('combobox', { name: 'Day of the week' }).click()
  await page.getByRole('option', { name: 'Tuesday' }).click()
  await expect(page.getByTestId('cron-schedule-preview')).toHaveText('0 3 * * 2')

  await page.getByRole('combobox', { name: 'How often' }).click()
  await page.getByRole('option', { name: 'Every day' }).click()

  await expect(page.getByTestId('cron-schedule-preview')).toHaveText('0 3 * * *')
  await expect(page.getByRole('combobox', { name: 'Day of the week' })).toHaveCount(0)
})
