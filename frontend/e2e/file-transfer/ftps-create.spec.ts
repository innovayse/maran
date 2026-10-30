import { expect, test, type Page } from '@playwright/test'
import { stubAccounts } from '../fixtures/stub-accounts-route'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubFirewall } from '../fixtures/stub-firewall-routes'
import {
  disabledFtpsStatus,
  neverConfiguredFtpsStatus,
  runningFtpsStatus,
  stubCreateFtpUserProblem,
  stubFtpUsers,
  stubFtpsServer,
  stubFtpsServerForbidden,
} from '../fixtures/stub-ftp-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import { stubMyAccount } from '../fixtures/stub-my-account-route'
import type { Account, MyAccount } from '../../src/types/account'
import type { AuthenticatedUser } from '../../src/types/auth'
import type { FtpsStatus } from '../../src/types/ftpsStatus'
import type { PanelModule } from '../../src/types/module'

const LICENSED: PanelModule[] = [
  { name: 'sftp', displayName: 'File transfer', tier: 'included', isEnabled: true },
  { name: 'ftp', displayName: 'FTPS', tier: 'included', isEnabled: true },
]

const ALICE: Account = {
  id: '22222222-2222-2222-2222-222222222222',
  name: 'alice',
  primaryDomain: 'alice.example.com',
  planId: '44444444-4444-4444-4444-444444444444',
  status: 'active',
  createdAt: '2026-08-01T10:00:00Z',
}

const OWN_ACCOUNT: MyAccount = {
  id: '66666666-6666-6666-6666-666666666666',
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

const CUSTOMER: AuthenticatedUser = {
  id: '00000000-0000-0000-0000-000000000099',
  username: 'olive',
  email: 'olive@example.com',
  role: 'customer',
  accountId: OWN_ACCOUNT.id,
}

/** The sentence the form shows when the panel has said the daemon is not on. */
const FTPS_OFF_NOTE =
  'FTPS is turned off on this server, so no FTPS login can be created yet. An administrator turns the service on; SFTP logins are unaffected.'

/**
 * Signs in with no FTPS login yet and the daemon in the given state.
 * @param page The Playwright page under test.
 * @param status The FTPS daemon state the panel reports.
 * @returns Every list request the page made so far.
 */
const signIn = async (page: Page, status: FtpsStatus = runningFtpsStatus): Promise<() => readonly URL[]> => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubAccounts(page, [ALICE])
  await stubFirewall(page, { rules: [], bans: [], whitelist: [] })
  await stubFtpsServer(page, status)
  return await stubFtpUsers(page, [])
}

/**
 * Records the path of every POST to either login endpoint.
 * @param page The Playwright page under test.
 * @returns The paths posted to so far.
 */
const recordPosts = (page: Page): string[] => {
  const posts: string[] = []
  page.on('request', (request) => {
    if (request.method() === 'POST' && /\/api\/v1\/s?ftp-users/.test(request.url())) {
      posts.push(new URL(request.url()).pathname)
    }
  })
  return posts
}

/**
 * Fills the administrator's create form.
 * @param page The Playwright page under test.
 * @param name The login suffix to type.
 * @returns Resolves once the form is filled.
 */
const fillForm = async (page: Page, name: string): Promise<void> => {
  await page.getByRole('combobox', { name: 'Account' }).click()
  await page.getByRole('option', { name: /alice/ }).click()
  await page.getByRole('textbox', { name: 'Login name' }).fill(name)
}

test('the create form refuses a login name the panel can reject without asking the server', async ({ page }) => {
  await signIn(page)
  await page.goto('/file-transfer/ftps')
  const posts = recordPosts(page)

  await fillForm(page, 'bob_deploy')
  await page.getByRole('button', { name: 'Create FTPS login' }).click()

  await expect(page.getByText('Use lowercase letters and digits only, up to 30 characters.')).toBeVisible()
  expect(posts).toEqual([])
})

test('a create posts to the ftp module only, and the re-read page shows the new row', async ({ page }) => {
  const requests = await signIn(page)
  await page.goto('/file-transfer/ftps')
  await expect(page.getByText('No FTPS logins yet')).toBeVisible()
  const readsBefore = requests().length
  const posts = recordPosts(page)

  await fillForm(page, 'upload')
  await page.getByRole('button', { name: 'Create FTPS login' }).click()

  await expect(page.getByRole('dialog')).toContainText('alice_upload')
  expect(posts).toEqual(['/api/v1/ftp-users'])
  await page.getByRole('button', { name: 'Done' }).click()

  await expect(page.getByRole('row').filter({ hasText: 'alice_upload' })).toBeVisible()
  expect(requests().length).toBeGreaterThan(readsBefore)
})

// A daemon that is off is a normal state, not an error. The form stays visible and says why it
// cannot be used; nothing on the screen reports a failure.
test('the create button is disabled while the server has FTPS turned off, and says why', async ({ page }) => {
  await signIn(page, disabledFtpsStatus)
  await page.goto('/file-transfer/ftps')

  await expect(page.getByTestId('ftps-off-note')).toHaveText(FTPS_OFF_NOTE)
  await expect(page.getByRole('button', { name: 'Create FTPS login' })).toBeDisabled()
})

// Nobody chose "off" on a fresh server, but nothing is on either: the same note, the same refusal.
test('the create button is disabled on a server where FTPS was never configured', async ({ page }) => {
  await signIn(page, neverConfiguredFtpsStatus)
  await page.goto('/file-transfer/ftps')

  await expect(page.getByTestId('ftps-off-note')).toHaveText(FTPS_OFF_NOTE)
  await expect(page.getByRole('button', { name: 'Create FTPS login' })).toBeDisabled()
})

// The other direction, because a note that is always shown is not a note.
test('the create button is usable and unexplained while the server has FTPS turned on', async ({ page }) => {
  await signIn(page, runningFtpsStatus)
  await page.goto('/file-transfer/ftps')

  await expect(page.getByRole('button', { name: 'Create FTPS login' })).toBeEnabled()
  await expect(page.getByTestId('ftps-off-note')).toHaveCount(0)
})

// A customer may not read the status, so the SPA does not know whether FTPS is on. It errs
// permissive (rules/security.md: enforcement is the backend's) — a form disabled on a guess would
// lock a customer out of a service that is running.
test('a customer who may not read the status keeps a usable form that posts their own account', async ({ page }) => {
  await stubSignedIn(page, CUSTOMER)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubMyAccount(page, OWN_ACCOUNT)
  await stubFtpsServerForbidden(page)
  await stubFtpUsers(page, [])
  await page.goto('/file-transfer/ftps')
  const posts = recordPosts(page)

  await expect(page.getByText('No FTPS logins yet')).toBeVisible()
  await expect(page.getByTestId('ftps-off-note')).toHaveCount(0)
  await expect(page.getByRole('combobox', { name: 'Account' })).toHaveCount(0)
  await page.getByRole('textbox', { name: 'Login name' }).fill('upload')
  const post = page.waitForRequest((request) => {
    return request.method() === 'POST' && new URL(request.url()).pathname === '/api/v1/ftp-users'
  })
  await page.getByRole('button', { name: 'Create FTPS login' }).click()

  expect((await post).postDataJSON()).toMatchObject({ accountId: OWN_ACCOUNT.id, name: 'upload' })
  expect(posts).toEqual(['/api/v1/ftp-users'])
})

// The refusal case. When the agent refuses because the account is busy, the wire carries nothing
// that distinguishes it from a host failure — so the screen renders the server's sentence and
// invents no verdict of its own.
test('a refused create renders the backend own sentence and no verdict of its own', async ({ page }) => {
  const backendDetail = 'The account is busy with another operation. Try again in a moment.'
  await signIn(page)
  await stubCreateFtpUserProblem(page, backendDetail)
  await page.goto('/file-transfer/ftps')

  await fillForm(page, 'upload')
  await page.getByRole('button', { name: 'Create FTPS login' }).click()

  await expect(page.getByText(backendDetail)).toBeVisible()
  await expect(page.getByRole('dialog')).toHaveCount(0)
})
