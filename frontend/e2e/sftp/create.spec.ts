import { expect, test } from '@playwright/test'
import { stubAccounts } from '../fixtures/stub-accounts-route'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import { stubCreateSftpUserProblem, stubSftpUsers } from '../fixtures/stub-sftp-routes'
import type { Account } from '../../src/types/account'
import type { PanelModule } from '../../src/types/module'

const LICENSED: PanelModule[] = [{ name: 'sftp', displayName: 'SFTP', tier: 'included', isEnabled: true }]

const ALICE: Account = {
  id: '22222222-2222-2222-2222-222222222222',
  name: 'alice',
  primaryDomain: 'alice.example.com',
  planId: '44444444-4444-4444-4444-444444444444',
  status: 'active',
  createdAt: '2026-08-01T10:00:00Z',
}

// The client rules mirror `CreateSftpUserCommandValidator`. They are advice that saves a round
// trip, and the check that matters is that the round trip really is saved: a form that renders the
// message but posts anyway has bought nothing.
test('the create form refuses a login name the panel can reject without asking the server', async ({ page }) => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubAccounts(page, [ALICE])
  await stubSftpUsers(page, [])

  const posts: string[] = []
  page.on('request', (request) => {
    if (request.method() === 'POST' && request.url().includes('/api/v1/sftp-users')) {
      posts.push(request.url())
    }
  })

  await page.goto('/sftp-users')

  await page.getByRole('combobox', { name: 'Account' }).click()
  await page.getByRole('option', { name: /alice/ }).click()
  // An underscore is refused on purpose: account names may contain one, so a suffix that could
  // hold one would let `alice` ask for `bob_deploy` and be handed a login that reads as `bob`'s.
  await page.getByRole('textbox', { name: 'Login name' }).fill('bob_deploy')
  await page.getByRole('button', { name: 'Create login' }).click()

  await expect(page.getByText('Use lowercase letters and digits only, up to 30 characters.')).toBeVisible()
  expect(posts).toEqual([])
})

test('the create form refuses an empty login name and never asks the server about it', async ({ page }) => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubAccounts(page, [ALICE])
  await stubSftpUsers(page, [])

  const posts: string[] = []
  page.on('request', (request) => {
    if (request.method() === 'POST' && request.url().includes('/api/v1/sftp-users')) {
      posts.push(request.url())
    }
  })

  await page.goto('/sftp-users')
  await page.getByRole('button', { name: 'Create login' }).click()

  await expect(page.getByText('Choose the account that will own the login.')).toBeVisible()
  await expect(page.getByText('Login name is required.')).toBeVisible()
  expect(posts).toEqual([])
})

test('creating a login adds it to the list under the name the host holds', async ({ page }) => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubAccounts(page, [ALICE])
  await stubSftpUsers(page, [])

  await page.goto('/sftp-users')

  await page.getByRole('combobox', { name: 'Account' }).click()
  await page.getByRole('option', { name: /alice/ }).click()
  await page.getByRole('textbox', { name: 'Login name' }).fill('web')
  await page.getByRole('button', { name: 'Create login' }).click()

  await expect(page.getByRole('row').filter({ hasText: 'alice_web' })).toBeVisible()
})

// rules/vue.md: the SPA never invents an error message for a server outcome.
test('a rejected create renders the backend own message rather than frontend copy', async ({ page }) => {
  const backendDetail = 'A login with that name already exists for this account.'
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubAccounts(page, [ALICE])
  await stubCreateSftpUserProblem(page, backendDetail)

  await page.goto('/sftp-users')

  await page.getByRole('combobox', { name: 'Account' }).click()
  await page.getByRole('option', { name: /alice/ }).click()
  await page.getByRole('textbox', { name: 'Login name' }).fill('web')
  await page.getByRole('button', { name: 'Create login' }).click()

  await expect(page.getByText(backendDetail)).toBeVisible()
})
