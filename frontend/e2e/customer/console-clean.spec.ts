import { expect, test, type ConsoleMessage } from '@playwright/test'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import { stubDashboard, WITHHELD_DASHBOARD } from '../fixtures/stub-dashboard-route'
import { stubSites } from '../fixtures/stub-sites-routes'
import { stubDatabases } from '../fixtures/stub-databases-routes'
import { stubBackups } from '../fixtures/stub-backups-routes'
import { stubCronEntries, stubCronEnvironment } from '../fixtures/stub-cron-routes'
import { stubTasks } from '../fixtures/stub-tasks-routes'
import { forbidAccountsList, stubMyAccount } from '../fixtures/stub-my-account-route'
import type { AuthenticatedUser } from '../../src/types/auth'
import type { MyAccount } from '../../src/types/account'
import type { PanelModule } from '../../src/types/module'

// Issues #201 and #202: a customer walks every screen of their own area and the browser stays
// quiet. No console error, no 4xx from `/api/v1/tasks` (the badge reads it on every screen), and no
// request to the administrator-only accounts list, which the panel answers with `HostForbidden`.

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
  primaryDomain: 'olive-shop.example.com',
  status: 'active',
  plan: {
    displayName: 'Starter',
    diskQuotaMb: 1024,
    maxSites: 1,
    maxDatabases: 1,
    maxSftpUsers: 1,
    maxFtpUsers: 1,
    maxCronEntries: 1,
  },
}

const MODULES: PanelModule[] = [
  { name: 'sites', displayName: 'Sites', tier: 'included', isEnabled: true },
  { name: 'databases', displayName: 'Databases', tier: 'included', isEnabled: true },
  { name: 'backups', displayName: 'Backups', tier: 'included', isEnabled: true },
  { name: 'cron', displayName: 'Cron', tier: 'included', isEnabled: true },
  { name: 'tasks', displayName: 'Background tasks', tier: 'included', isEnabled: true },
]

const CUSTOMER_PAGES = ['/', '/sites', '/databases', '/backups', '/cron', '/tasks', '/my-account', '/accounts']

test('a customer walks their own area with a clean console', async ({ page }) => {
  const consoleErrors: string[] = []
  const taskFailures: number[] = []
  page.on('console', (message: ConsoleMessage) => {
    if (message.type() === 'error') {
      consoleErrors.push(message.text())
    }
  })
  page.on('response', (response) => {
    if (/\/api\/v1\/tasks(\?.*)?$/.test(response.url()) && response.status() >= 400) {
      taskFailures.push(response.status())
    }
  })

  await stubSignedIn(page, CUSTOMER)
  await stubHealthy(page)
  await stubModules(page, MODULES)
  await stubDashboard(page, WITHHELD_DASHBOARD)
  await stubSites(page, [])
  await stubDatabases(page, [])
  await stubBackups(page, [])
  await stubCronEntries(page, [])
  await stubCronEnvironment(page, [])
  await stubTasks(page, [])
  await stubMyAccount(page, OWN_ACCOUNT)
  const accountsListCalls = await forbidAccountsList(page)

  for (const path of CUSTOMER_PAGES) {
    await page.goto(path)
    await expect(page.getByRole('heading', { level: 1 })).toBeVisible()
  }

  expect(consoleErrors).toEqual([])
  expect(taskFailures).toEqual([])
  expect(accountsListCalls()).toBe(0)
})
