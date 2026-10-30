import { expect, test, type Page } from '@playwright/test'
import { stubAccounts } from '../fixtures/stub-accounts-route'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import { stubCreateSftpUserProblem, stubSftpUsers } from '../fixtures/stub-sftp-routes'
import type { Account } from '../../src/types/account'
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

/**
 * Signs in as the administrator with no SFTP login yet.
 * @param page The Playwright page under test.
 * @returns Every list request the page made so far.
 */
const signIn = async (page: Page): Promise<() => readonly URL[]> => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubAccounts(page, [ALICE])
  return await stubSftpUsers(page, [])
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
 * Fills the create form's two fields.
 * @param page The Playwright page under test.
 * @param name The login suffix to type.
 * @returns Resolves once the form is filled.
 */
const fillForm = async (page: Page, name: string): Promise<void> => {
  await page.getByRole('combobox', { name: 'Account' }).click()
  await page.getByRole('option', { name: /alice/ }).click()
  await page.getByRole('textbox', { name: 'Login name' }).fill(name)
}

// The client rules mirror the server's validator. They are advice that saves a round trip, and the
// check that matters is that the round trip really is saved.
test('the create form refuses a login name the panel can reject without asking the server', async ({ page }) => {
  await signIn(page)
  await page.goto('/file-transfer/sftp')
  const posts = recordPosts(page)

  // An underscore is refused on purpose: account names may contain one, so a suffix that could hold
  // one would let `alice` ask for `bob_deploy` and be handed a login that reads as `bob`'s.
  await fillForm(page, 'bob_deploy')
  await page.getByRole('button', { name: 'Create SFTP login' }).click()

  await expect(page.getByText('Use lowercase letters and digits only, up to 30 characters.')).toBeVisible()
  expect(posts).toEqual([])
})

test('the create form refuses an empty login name and never asks the server about it', async ({ page }) => {
  await signIn(page)
  await page.goto('/file-transfer/sftp')
  const posts = recordPosts(page)

  await page.getByRole('button', { name: 'Create SFTP login' }).click()

  await expect(page.getByText('Choose the account that will own the login.')).toBeVisible()
  await expect(page.getByText('Login name is required.')).toBeVisible()
  expect(posts).toEqual([])
})

// The page re-reads its page after a create rather than patching the rows it held, so the new row is
// the panel's answer to a second list request, not a row the SPA added itself.
test('a create posts to the sftp module only, and the re-read page shows the new row', async ({ page }) => {
  const requests = await signIn(page)
  await page.goto('/file-transfer/sftp')
  await expect(page.getByText('No SFTP logins yet')).toBeVisible()
  const readsBefore = requests().length
  const posts = recordPosts(page)

  await fillForm(page, 'web')
  await page.getByRole('button', { name: 'Create SFTP login' }).click()

  await expect(page.getByRole('dialog')).toContainText('alice_web')
  expect(posts).toEqual(['/api/v1/sftp-users'])
  await page.getByRole('button', { name: 'Done' }).click()

  await expect(page.getByRole('row').filter({ hasText: 'alice_web' })).toBeVisible()
  expect(requests().length).toBeGreaterThan(readsBefore)
})

// rules/vue.md: the SPA never invents an error message for a server outcome.
test('a rejected create renders the backend own message rather than frontend copy', async ({ page }) => {
  const backendDetail = 'A login with that name already exists for this account.'
  await signIn(page)
  await stubCreateSftpUserProblem(page, backendDetail)
  await page.goto('/file-transfer/sftp')

  await fillForm(page, 'web')
  await page.getByRole('button', { name: 'Create SFTP login' }).click()

  await expect(page.getByText(backendDetail)).toBeVisible()
  await expect(page.getByRole('dialog')).toHaveCount(0)
})
