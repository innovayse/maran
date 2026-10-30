import { expect, test, type Page } from '@playwright/test'
import { stubAccounts } from '../fixtures/stub-accounts-route'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import { stubCronEntries, stubCronEnvironment } from '../fixtures/stub-cron-routes'
import type { Account } from '../../src/types/account'
import type { CronEntry } from '../../src/types/cronEntry'
import type { PanelModule } from '../../src/types/module'

// #222: an entry reads in words next to its expression, the builder asks for the hour before the
// minute, and adding an entry is confirmed by a toast.

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

const DAILY: CronEntry = {
  entryId: '11111111-1111-1111-1111-111111111111',
  accountId: ALICE.id,
  schedule: { minute: '5', hour: '3', dayOfMonth: '*', month: '*', dayOfWeek: '*' },
  command: '/usr/bin/php /home/alice/daily.php',
  enabled: true,
}

const EVERY_FIVE: CronEntry = {
  entryId: '33333333-3333-3333-3333-333333333333',
  accountId: ALICE.id,
  schedule: { minute: '*/5', hour: '*', dayOfMonth: '*', month: '*', dayOfWeek: '*' },
  command: '/usr/bin/php /home/alice/queue.php',
  enabled: true,
}

/** A schedule no sentence can carry exactly: the expression alone describes it. */
const IRREGULAR: CronEntry = {
  entryId: '44444444-4444-4444-4444-444444444444',
  accountId: ALICE.id,
  schedule: { minute: '0', hour: '1-5', dayOfMonth: '*', month: '6', dayOfWeek: '*' },
  command: '/usr/bin/php /home/alice/summer.php',
  enabled: true,
}

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

test('each entry shows when it runs in words beside its expression', async ({ page }) => {
  await openCronPage(page, [DAILY, EVERY_FIVE, IRREGULAR])

  const daily = page.getByRole('row').filter({ hasText: DAILY.command })
  await expect(daily).toContainText('5 3 * * *')
  await expect(daily).toContainText('Every day at 03:05')

  await expect(page.getByRole('row').filter({ hasText: EVERY_FIVE.command })).toContainText('Every 5 minutes')

  // No half-true sentence for a schedule the description cannot carry.
  const irregular = page.getByRole('row').filter({ hasText: IRREGULAR.command })
  await expect(irregular).toContainText('0 1-5 * 6 *')
  await expect(irregular).not.toContainText('Every')
})

test('the builder asks for the hour before the minute, on one row', async ({ page }) => {
  await openCronPage(page, [])

  await page.getByRole('combobox', { name: 'How often' }).click()
  await page.getByRole('option', { name: 'Every day' }).click()

  const hour = await page.getByRole('textbox', { name: 'Hour' }).boundingBox()
  const minute = await page.getByRole('textbox', { name: 'Minute' }).boundingBox()
  expect(hour).not.toBeNull()
  expect(minute).not.toBeNull()
  if (hour === null || minute === null) {
    return
  }
  expect(hour.x).toBeLessThan(minute.x)
  expect(Math.abs(hour.y - minute.y)).toBeLessThan(2)
})

test('adding an entry is confirmed by a toast', async ({ page }) => {
  await openCronPage(page, [])

  await page.getByRole('textbox', { name: 'Command' }).fill('/usr/bin/php /home/alice/new.php')
  await page.getByRole('button', { name: 'Add entry' }).click()

  await expect(page.getByRole('status').filter({ hasText: 'Scheduled task added.' })).toBeVisible()
})
