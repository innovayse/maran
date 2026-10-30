import { expect, test, type Page, type Request } from '@playwright/test'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubBackups } from '../fixtures/stub-backups-routes'
import { stubCronEntries, stubCronEnvironment } from '../fixtures/stub-cron-routes'
import { stubDatabases } from '../fixtures/stub-databases-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import { forbidAccountsList, stubMyAccount } from '../fixtures/stub-my-account-route'
import { stubSftpUsers } from '../fixtures/stub-sftp-routes'
import { stubPhpVersions, stubSites } from '../fixtures/stub-sites-routes'
import { stubEmptyTasks } from '../fixtures/stub-tasks-routes'
import type { MyAccount } from '../../src/types/account'
import type { AuthenticatedUser } from '../../src/types/auth'
import type { PanelModule } from '../../src/types/module'

/**
 * A customer owns exactly one account, so every create form sends that account's id on its own:
 * no picker is drawn, and the administrator-only accounts list is never requested. Stubbed, like
 * every spec outside `golden-path/`: what is proven is the SPA's request, not a backend's answer.
 */

/** The account the customer owns. */
const OWN_ACCOUNT: MyAccount = {
  id: '11111111-1111-1111-1111-111111111111',
  name: 'olive',
  primaryDomain: 'olive.example.com',
  status: 'active',
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

/** The signed-in customer. */
const CUSTOMER: AuthenticatedUser = {
  id: '00000000-0000-0000-0000-000000000099',
  username: 'olive',
  email: 'olive@example.com',
  role: 'customer',
  accountId: OWN_ACCOUNT.id,
}

/** The modules a customer's account reaches in these specs. */
const MODULES: PanelModule[] = [
  { name: 'sites', displayName: 'Sites', tier: 'included', isEnabled: true },
  { name: 'databases', displayName: 'Databases', tier: 'included', isEnabled: true },
  { name: 'backups', displayName: 'Backups', tier: 'included', isEnabled: true },
  { name: 'cron', displayName: 'Scheduled tasks', tier: 'included', isEnabled: true },
  { name: 'sftp', displayName: 'File transfer', tier: 'included', isEnabled: true },
  { name: 'ftp', displayName: 'FTPS', tier: 'included', isEnabled: true },
]

/**
 * Signs the customer in and installs the shell's routes, with the accounts list counted.
 * @param page The Playwright page under test.
 * @returns How many times the accounts list was requested so far.
 */
const signInCustomer = async (page: Page): Promise<() => number> => {
  await stubSignedIn(page, CUSTOMER)
  await stubHealthy(page)
  await stubModules(page, MODULES)
  await stubEmptyTasks(page)
  await stubMyAccount(page, OWN_ACCOUNT)
  return forbidAccountsList(page)
}

/**
 * Waits for the next POST to a path.
 * @param page The Playwright page under test.
 * @param path The API path the POST goes to.
 * @returns The request once it has been sent.
 */
const nextPost = (page: Page, path: RegExp): Promise<Request> => {
  return page.waitForRequest((request) => {
    return request.method() === 'POST' && path.test(new URL(request.url()).pathname)
  })
}

test('a customer creates a database for their own account without a picker', async ({ page }) => {
  const accountsCalls = await signInCustomer(page)
  await stubDatabases(page, [])

  await page.goto('/databases')
  await expect(page.getByRole('textbox', { name: 'Database name' })).toBeVisible()
  await expect(page.getByRole('combobox', { name: 'Account' })).toHaveCount(0)

  await page.getByRole('textbox', { name: 'Database name' }).fill('shop')
  await page.getByRole('textbox', { name: 'User name' }).fill('shopuser')
  const post = nextPost(page, /\/api\/v1\/databases$/)
  await page.getByRole('button', { name: 'Create database' }).click()

  expect((await post).postDataJSON()).toMatchObject({ accountId: OWN_ACCOUNT.id, name: 'shop' })
  expect(accountsCalls()).toBe(0)
})

test('a customer creates an SFTP login for their own account without a picker', async ({ page }) => {
  const accountsCalls = await signInCustomer(page)
  await stubSftpUsers(page, [])

  await page.goto('/file-transfer/sftp')
  await expect(page.getByRole('textbox', { name: 'Login name' })).toBeVisible()
  await expect(page.getByRole('combobox', { name: 'Account' })).toHaveCount(0)

  await page.getByRole('textbox', { name: 'Login name' }).fill('deploy')
  const post = nextPost(page, /\/api\/v1\/sftp-users$/)
  await page.getByRole('button', { name: 'Create SFTP login' }).click()

  expect((await post).postDataJSON()).toMatchObject({ accountId: OWN_ACCOUNT.id, name: 'deploy' })
  expect(accountsCalls()).toBe(0)
})

test('a customer adds a cron entry to their own crontab without a picker', async ({ page }) => {
  const accountsCalls = await signInCustomer(page)
  await stubCronEntries(page, [])
  await stubCronEnvironment(page, [])

  await page.goto('/cron')
  await expect(page.getByRole('heading', { level: 1, name: 'Scheduled tasks' })).toBeVisible()
  await expect(page.getByRole('combobox', { name: 'Account' })).toHaveCount(0)

  await page.getByRole('textbox', { name: 'Command' }).fill('/usr/bin/php /home/olive/job.php')
  const post = nextPost(page, /\/api\/v1\/cron-entries$/)
  await page.getByRole('button', { name: 'Add entry' }).click()

  expect((await post).postDataJSON()).toMatchObject({ accountId: OWN_ACCOUNT.id })
  expect(accountsCalls()).toBe(0)
})

test('a customer takes a backup of their own account without a picker', async ({ page }) => {
  const accountsCalls = await signInCustomer(page)
  await stubBackups(page, [])

  await page.goto('/backups')
  await expect(page.getByRole('button', { name: 'Take a backup' })).toBeVisible()
  await expect(page.getByRole('combobox', { name: 'Account' })).toHaveCount(0)

  const post = nextPost(page, /\/api\/v1\/backups$/)
  await page.getByRole('button', { name: 'Take a backup' }).click()

  expect((await post).postDataJSON()).toEqual({ accountId: OWN_ACCOUNT.id })
  expect(accountsCalls()).toBe(0)
})

test('a customer creates a site on their own account without a picker', async ({ page }) => {
  const accountsCalls = await signInCustomer(page)
  await stubSites(page, [])
  await stubPhpVersions(page, [{ version: '8.3', isDefault: true }])

  await page.goto('/sites/new')
  await expect(page.getByLabel('Domain')).toBeVisible()
  await expect(page.getByRole('combobox', { name: 'Account' })).toHaveCount(0)

  await page.getByLabel('Domain').fill('olive-shop.example.com')
  const post = nextPost(page, /\/api\/v1\/sites$/)
  await page.getByRole('button', { name: 'Create site' }).click()

  expect((await post).postDataJSON()).toMatchObject({
    accountId: OWN_ACCOUNT.id,
    domain: 'olive-shop.example.com',
  })
  expect(accountsCalls()).toBe(0)
})
