import { expect, test } from '../fixtures/guarded-test'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubLandingReads } from '../fixtures/stub-landing-reads'
import { stubModules } from '../fixtures/stub-modules-route'
import { stubMyAccount } from '../fixtures/stub-my-account-route'
import { stubShellReads } from '../fixtures/stub-shell-reads'
import { stubTaskStream, stubTasks, taskFrame } from '../fixtures/stub-tasks-routes'
import type { MyAccount } from '../../src/types/account'
import type { AuthenticatedUser } from '../../src/types/auth'
import type { PanelModule } from '../../src/types/module'
import type { PanelTask } from '../../src/types/panelTask'
import type { Locator, Page } from '@playwright/test'

// Issue #201: `GET /api/v1/tasks` answers a customer with the tasks started for their own account,
// and every one arrives with `log: ""` and `logIncluded: false` because the log is written for the
// operator. The stubs below are that shape; the SPA must count them, list them, and show the
// withheld-log note in place of a log.

const CUSTOMER: AuthenticatedUser = {
  id: '00000000-0000-0000-0000-000000000099',
  username: 'olive',
  email: 'olive@example.com',
  role: 'customer',
  accountId: '11111111-1111-1111-1111-111111111111',
}

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

const MODULES: PanelModule[] = [
  { name: 'backups', displayName: 'Backups', kindDisplayName: 'Core', tier: 'included', state: 'enabled' },
  { name: 'tasks', displayName: 'Background tasks', kindDisplayName: 'Core', tier: 'included', state: 'enabled' },
]

/** A backup of the customer's own account, still running, as the panel sends it to a customer. */
const OWN_BACKUP: PanelTask = {
  id: '44444444-4444-4444-4444-444444444444',
  kind: 'BackupCreate',
  subject: 'olive',
  correlationId: null,
  status: 'running',
  statusName: 'Running',
  percent: 30,
  log: '',
  logIncluded: false,
  failure: null,
  startedAt: '2026-10-30T09:00:00Z',
  finishedAt: null,
  revision: 2,
}

const badge = (page: Page): Locator => {
  return page.getByRole('link', { name: /background tasks? running/i })
}

test('the badge counts the customer running task', async ({ page }) => {
  await stubSignedIn(page, CUSTOMER)
  await stubHealthy(page)
  await stubShellReads(page)
  await stubModules(page, MODULES)
  await stubMyAccount(page, OWN_ACCOUNT)
  await stubLandingReads(page, 'customer')
  await stubTasks(page, [OWN_BACKUP])
  await stubTaskStream(page, taskFrame(OWN_BACKUP))

  await page.goto('/')

  await expect(badge(page)).toHaveText('1')
})

test('the tasks page lists the customer task and offers no log pane', async ({ page }) => {
  await stubSignedIn(page, CUSTOMER)
  await stubHealthy(page)
  await stubShellReads(page)
  await stubModules(page, MODULES)
  await stubMyAccount(page, OWN_ACCOUNT)
  await stubLandingReads(page, 'customer')
  await stubTasks(page, [OWN_BACKUP])
  await stubTaskStream(page, taskFrame(OWN_BACKUP))

  await page.goto('/tasks')

  await expect(page.getByRole('heading', { level: 1, name: 'Background tasks' })).toBeVisible()
  await expect(page.getByRole('cell', { name: 'Taking a backup' })).toBeVisible()
  // The listing is not an error any more: no refusal is rendered in place of the table.
  await expect(page.getByText('could not be found')).toHaveCount(0)

  await page.getByRole('button', { name: `Actions for ${OWN_BACKUP.subject}` }).click()
  await page.getByRole('menuitem', { name: 'Live view' }).click()

  await expect(page.getByText("The detailed log of a task is kept for the server's administrators.")).toBeVisible()
  // The withheld note replaces the empty-log sentence: an empty log here is not "nothing reported".
  await expect(page.getByText('Nothing has been reported yet.')).toHaveCount(0)
  await expect(page.getByLabel('Task log')).toHaveCount(0)
})

test('a failed listing is shown as the listing failure, verbatim', async ({ page, guard }) => {
  guard.allow(500, /\/api\/v1\/tasks(\?.*)?$/)
  await stubSignedIn(page, CUSTOMER)
  await stubHealthy(page)
  await stubShellReads(page)
  await stubModules(page, MODULES)
  await stubMyAccount(page, OWN_ACCOUNT)
  await stubLandingReads(page, 'customer')
  await page.route(/\/api\/v1\/tasks(\?.*)?$/, async (route) => {
    await route.fulfill({
      status: 500,
      contentType: 'application/problem+json',
      body: JSON.stringify({ code: 'Unexpected', title: 'Server error', detail: 'The task list could not be read.' }),
    })
  })

  await page.goto('/tasks')

  await expect(page.getByText('The task list could not be read.')).toBeVisible()
})
