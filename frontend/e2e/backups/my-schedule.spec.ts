import { expect, test } from '../fixtures/guarded-test'
import { FULL_CATALOGUE, mainNavigation, openSection } from '../fixtures/open-full-shell'
import { setPersistedLocale } from '../fixtures/set-locale'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubDashboard, WITHHELD_DASHBOARD } from '../fixtures/stub-dashboard-route'
import { stubBackupOptions } from '../fixtures/stub-backup-options-route'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import {
  stubMyBackupSchedule,
  stubMyBackupScheduleRefusal,
} from '../fixtures/stub-my-backup-schedule-route'
import { stubMyAccount } from '../fixtures/stub-my-account-route'
import { OWNER_PROFILE, stubProfile } from '../fixtures/stub-profile-route'
import { stubShellReads } from '../fixtures/stub-shell-reads'
import type { MyAccount } from '../../src/types/account'
import type { AuthenticatedUser } from '../../src/types/auth'
import type { BackupSchedule } from '../../src/types/backupSchedule'
import type { ConsoleMessage, Page } from '@playwright/test'

// Issue #147: a customer sets their own backup schedule. The page names no account and no
// destination (the server takes the account from the login), offers only the customer's shorter lists,
// and renders the backend's own words for a refusal. The summary prints instants; in UTC they read the
// same on every machine running the suite.
test.use({ timezoneId: 'UTC' })

const OWN_ACCOUNT: MyAccount = {
  id: '11111111-1111-1111-1111-111111111111',
  name: 'olive',
  primaryDomain: 'olive.example.com',
  status: 'active',
  statusName: 'Active',
  plan: {
    displayName: 'Starter',
    diskQuotaMb: 1024,
    maxSites: 5,
    maxDatabases: 5,
    maxSftpUsers: 5,
    maxFtpUsers: 5,
    maxCronEntries: 5,
  },
}

const CUSTOMER: AuthenticatedUser = {
  id: '00000000-0000-0000-0000-000000000099',
  username: 'olive',
  email: 'olive@example.com',
  role: 'customer',
  accountId: OWN_ACCOUNT.id,
}

/** The address the 390px checks below open; `e2e/ui-kit/route-coverage.spec.ts` reads it from here. */
const SWEPT_SCREEN = { path: '/backups/my-schedule' }

/** The caller's own schedule: weekly on Tuesday, keeping three copies, and it has actually run. */
const OWN_SCHEDULE: BackupSchedule = {
  id: '55555555-5555-5555-5555-555555555555',
  accountId: OWN_ACCOUNT.id,
  destinationId: null,
  frequency: 'weekly',
  hourUtc: 2,
  dayOfWeekUtc: 'tuesday',
  retainCount: 3,
  enabled: true,
  lastRunAt: '2026-09-01T02:00:00Z',
  nextRunAt: '2026-09-08T02:00:00Z',
}

/**
 * Signs in as the customer with the shell's quiet reads answered.
 * @param page The Playwright page under test.
 * @returns Resolves once the stubs are installed.
 */
const signInAsCustomer = async (page: Page): Promise<void> => {
  await stubSignedIn(page, CUSTOMER)
  await stubHealthy(page)
  await stubShellReads(page)
  await stubModules(page, FULL_CATALOGUE)
  await stubMyAccount(page, OWN_ACCOUNT)
  await stubProfile(page, OWNER_PROFILE)
  await stubBackupOptions(page)
}

// The customer reaches the page by the menu, not by typing the address.
test('the customer opens their own schedule from the Backups menu', async ({ page }) => {
  await signInAsCustomer(page)
  await stubDashboard(page, WITHHELD_DASHBOARD)
  await stubMyBackupSchedule(page, null)

  await page.goto('/')
  await openSection(page, 'Backups')
  await mainNavigation(page).getByRole('link', { name: 'Schedule' }).click()

  await expect(page.getByRole('heading', { level: 1, name: 'My backup schedule' })).toBeVisible()
})

// The panel invents no default: an account that saved nothing is told so, every field is blank, and the
// ordinary first state is not a failed request.
test('an account with no schedule of its own is told so and every field is left blank', async ({ page }) => {
  const consoleErrors: string[] = []
  page.on('console', (message: ConsoleMessage) => {
    if (message.type() === 'error') {
      consoleErrors.push(message.text())
    }
  })
  await signInAsCustomer(page)
  const traffic = await stubMyBackupSchedule(page, null)

  await page.goto('/backups/my-schedule')

  await expect(page.getByText('You have not saved a schedule of your own', { exact: false })).toBeVisible()
  // Positive control on the probe: the form IS rendered and this locator can read its fields.
  await expect(page.getByRole('button', { name: 'Save schedule' })).toBeVisible()
  await expect(page.getByRole('combobox', { name: 'Hour of the day (UTC)' })).toHaveText('Choose an hour')
  await expect(page.getByRole('combobox', { name: 'Copies to keep' })).toHaveText('Choose how many')
  // No scope picker: the page edits one schedule and names no account.
  await expect(page.getByRole('combobox', { name: 'Applies to' })).toHaveCount(0)
  expect(traffic.reads()).toBe(1)
  expect(consoleErrors).toEqual([])
})

test('a saved schedule is shown in the form and in the summary', async ({ page }) => {
  await signInAsCustomer(page)
  await stubMyBackupSchedule(page, OWN_SCHEDULE)

  await page.goto('/backups/my-schedule')

  await expect(page.getByText('You have not saved a schedule of your own', { exact: false })).toHaveCount(0)
  await expect(page.getByRole('combobox', { name: 'Hour of the day (UTC)' })).toHaveText('02:00')
  await expect(page.getByRole('combobox', { name: 'Copies to keep' })).toHaveText('3')
  await expect(page.getByText('Next run')).toBeVisible()
})

// The customer's copies list is the shorter one the panel publishes: it ends at the customer ceiling,
// where the operator's list runs to 365.
test('the copies to keep are offered only up to the customer ceiling', async ({ page }) => {
  await signInAsCustomer(page)
  await stubMyBackupSchedule(page, null)

  await page.goto('/backups/my-schedule')
  await page.getByRole('combobox', { name: 'Copies to keep' }).click()

  await expect(page.getByRole('option', { name: '7', exact: true })).toBeVisible()
  await expect(page.getByRole('option', { name: '8', exact: true })).toHaveCount(0)
})

// `SaveMyBackupScheduleCommand` carries neither an account nor a destination, and `ipAddress` and
// `userAgent` are established by the server. None of that is visible on the page, so the recorded body
// is the only witness that the form sends exactly the members the command binds.
test('saving sends exactly the schedule members, and no account, destination or weekday on a daily schedule', async ({
  page,
}) => {
  await signInAsCustomer(page)
  const traffic = await stubMyBackupSchedule(page, null)

  await page.goto('/backups/my-schedule')
  await page.getByRole('combobox', { name: 'How often' }).click()
  await page.getByRole('option', { name: 'Every day' }).click()
  await page.getByRole('combobox', { name: 'Hour of the day (UTC)' }).click()
  await page.getByRole('option', { name: '03:00' }).click()
  await page.getByRole('combobox', { name: 'Copies to keep' }).click()
  await page.getByRole('option', { name: '5', exact: true }).click()
  await page.getByRole('switch', { name: 'Run this schedule' }).click()
  await page.getByRole('button', { name: 'Save schedule' }).click()

  await expect.poll(traffic.writes).toEqual([
    { frequency: 'daily', hourUtc: 3, dayOfWeekUtc: null, retainCount: 5, enabled: true },
  ])
  await expect(page.getByText('The schedule was saved.')).toBeVisible()
})

// The backend owns the text of a server outcome and the SPA renders it verbatim; the CODE is for
// behaviour only, and here the behaviour is which field is marked invalid.
test('a refused retention count shows the backend sentence on that field', async ({ page, guard }) => {
  guard.allow(400, /\/api\/v1\/me\/backup-schedule$/)
  await signInAsCustomer(page)
  await stubMyBackupSchedule(page, OWN_SCHEDULE)
  await stubMyBackupScheduleRefusal(page, 'BackupScheduleCustomerRetainTooHigh', 'You can keep at most 7 backups.')

  await page.goto('/backups/my-schedule')
  await page.getByRole('button', { name: 'Save schedule' }).click()

  await expect(page.getByText('You can keep at most 7 backups.')).toHaveCount(1)
})

test.describe('at 390px', () => {
  for (const locale of ['en', 'ru', 'hy'] as const) {
    test(`the ${locale} page does not scroll sideways`, async ({ page }) => {
      await page.setViewportSize({ width: 390, height: 800 })
      await setPersistedLocale(page, locale)
      await signInAsCustomer(page)
      await stubMyBackupSchedule(page, OWN_SCHEDULE)

      await page.goto(SWEPT_SCREEN.path)

      // Positive control: the form IS rendered, so the measurement covers the page's real content.
      await expect(page.getByRole('combobox').first()).toBeVisible()
      const widths = await page.evaluate(() => {
        return { document: document.documentElement.scrollWidth, window: window.innerWidth }
      })
      expect(widths.document).toBeLessThanOrEqual(widths.window)
    })
  }
})
