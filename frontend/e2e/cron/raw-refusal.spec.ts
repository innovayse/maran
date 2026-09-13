import { expect, test, type Page } from '@playwright/test'
import { stubAccounts } from '../fixtures/stub-accounts-route'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import { stubCronEntries, stubCronEnvironment } from '../fixtures/stub-cron-routes'
import type { Account } from '../../src/types/account'
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

/**
 * Counts every POST the page makes to the cron collection.
 *
 * A counter rather than a route that refuses to answer: the proposition is that NOTHING is sent,
 * and a route can only observe requests that were made. The listener is installed before the page
 * is opened so nothing can slip past it.
 */
const countCreateRequests = (page: Page): { value: () => number } => {
  let count = 0
  page.on('request', (request) => {
    if (request.method() === 'POST' && request.url().includes('/api/v1/cron-entries')) {
      count += 1
    }
  })
  return {
    value: (): number => {
      return count
    },
  }
}

const openCronPage = async (page: Page): Promise<void> => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubAccounts(page, [ALICE])
  await stubCronEntries(page, [])
  await stubCronEnvironment(page, [])
  await page.goto('/cron')
  await expect(page.getByRole('heading', { level: 1, name: 'Scheduled tasks' })).toBeVisible()
}

// The refusal is the client's mirror of `CronScheduleValidator` doing its job: an expression that
// is not five whitespace-separated fields cannot be a schedule, so the panel says so instead of
// spending a round trip to be told the same thing. What would break it: deleting the check in
// `CronEntryForm.submit`, or `parseCronExpression` accepting a field count other than five — either
// way a POST goes out and the count is 1. The visible-refusal assertion is what stops the spec from
// also passing on a form whose button silently does nothing.
test('a raw expression this panel would refuse is not sent at all', async ({ page }) => {
  const creates = countCreateRequests(page)
  await openCronPage(page)

  await page.getByRole('button', { name: 'Expression' }).click()
  // Four fields, not five: the command has been left attached, or a field was dropped.
  await page.getByRole('textbox', { name: 'Cron expression' }).fill('* * * *')
  await page.getByRole('textbox', { name: 'Command' }).fill(COMMAND)
  await page.getByRole('button', { name: 'Add entry' }).click()

  await expect(
    page.getByText('That is not a schedule this server accepts', { exact: false }),
  ).toBeVisible()
  // A second awaited assertion after the refusal: a request made in the same click would have been
  // issued long before this settles.
  await expect(page.getByText('Nothing scheduled yet')).toBeVisible()
  expect(creates.value()).toBe(0)
})

// The same for a five-field expression whose values are out of range. This one parses, so it walks
// a different path through the mirror than the test above, and it is the one that would survive if
// `isValidCronSchedule` were dropped while `parseCronExpression` stayed.
test('a raw expression with an out-of-range field is not sent either', async ({ page }) => {
  const creates = countCreateRequests(page)
  await openCronPage(page)

  await page.getByRole('button', { name: 'Expression' }).click()
  // 61 is not a minute. Five fields, so the split succeeds and only the grammar can refuse it.
  await page.getByRole('textbox', { name: 'Cron expression' }).fill('61 * * * *')
  await page.getByRole('textbox', { name: 'Command' }).fill(COMMAND)
  await page.getByRole('button', { name: 'Add entry' }).click()

  await expect(
    page.getByText('That is not a schedule this server accepts', { exact: false }),
  ).toBeVisible()
  await expect(page.getByText('Nothing scheduled yet')).toBeVisible()
  expect(creates.value()).toBe(0)
})

// The control for both tests above. Without it, a form that never posted anything at all — a broken
// button, a missing handler — would satisfy them perfectly, and the zero would be measuring nothing.
test('a raw expression this panel accepts IS sent, with the five fields split out of it', async ({
  page,
}) => {
  const creates = countCreateRequests(page)
  await openCronPage(page)

  await page.getByRole('button', { name: 'Expression' }).click()
  await page.getByRole('textbox', { name: 'Cron expression' }).fill('*/5 1-4 * * 0')
  await page.getByRole('textbox', { name: 'Command' }).fill(COMMAND)

  const posted = page.waitForRequest((request) => {
    return request.method() === 'POST' && request.url().includes('/api/v1/cron-entries')
  })
  await page.getByRole('button', { name: 'Add entry' }).click()

  const body = (await posted).postDataJSON() as { schedule: Record<string, string> }
  expect(body.schedule).toEqual({
    minute: '*/5',
    hour: '1-4',
    dayOfMonth: '*',
    month: '*',
    dayOfWeek: '0',
  })
  expect(creates.value()).toBe(1)
})

// The command has its own mirror (`CronCommandRule`), and it refuses on its own terms: surrounding
// whitespace is refused rather than trimmed, because the agent compares commands verbatim when it
// decides whether an entry duplicates one already installed. A schedule that is perfectly good must
// not carry a bad command past the form.
test('a command with surrounding whitespace is refused here rather than trimmed and sent', async ({
  page,
}) => {
  const creates = countCreateRequests(page)
  await openCronPage(page)

  await page.getByRole('textbox', { name: 'Command' }).fill(`  ${COMMAND}`)
  await page.getByRole('button', { name: 'Add entry' }).click()

  await expect(page.getByText('A command is one line', { exact: false })).toBeVisible()
  await expect(page.getByText('Nothing scheduled yet')).toBeVisible()
  expect(creates.value()).toBe(0)
})
