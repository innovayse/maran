import { expect, test } from '@playwright/test'
import { stubAccounts } from '../fixtures/stub-accounts-route'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubCreateDatabaseProblem, stubDatabases } from '../fixtures/stub-databases-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import type { Account } from '../../src/types/account'
import type { PanelModule } from '../../src/types/module'

const LICENSED: PanelModule[] = [
  { name: 'databases', displayName: 'Databases', tier: 'included', isEnabled: true },
]

const ALICE: Account = {
  id: '22222222-2222-2222-2222-222222222222',
  name: 'alice',
  primaryDomain: 'alice.example.com',
  planId: '44444444-4444-4444-4444-444444444444',
  status: 'active',
  createdAt: '2026-08-01T10:00:00Z',
}

// The client rules mirror `CreateDatabaseCommandValidator`. They are advice that saves a round
// trip, and the check that matters is that the round trip really is saved: a form that renders the
// message but posts anyway has bought nothing.
test('the create form refuses a name the panel can reject without asking the server', async ({ page }) => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubAccounts(page, [ALICE])
  await stubDatabases(page, [])

  const posts: string[] = []
  page.on('request', (request) => {
    if (request.method() === 'POST' && request.url().includes('/api/v1/databases')) {
      posts.push(request.url())
    }
  })

  await page.goto('/databases')

  await page.getByRole('combobox', { name: 'Account' }).click()
  await page.getByRole('option', { name: /alice/ }).click()
  await page.getByRole('textbox', { name: 'Database name' }).fill('Shop!')
  await page.getByRole('textbox', { name: 'User name' }).fill('shopuser')
  await page.getByRole('button', { name: 'Create database' }).click()

  await expect(
    page.getByText('Use lowercase letters and digits only, up to 30 characters.').first(),
  ).toBeVisible()
  expect(posts).toEqual([])
})

test('the create form refuses an empty name and never asks the server about it', async ({ page }) => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubAccounts(page, [ALICE])
  await stubDatabases(page, [])

  const posts: string[] = []
  page.on('request', (request) => {
    if (request.method() === 'POST' && request.url().includes('/api/v1/databases')) {
      posts.push(request.url())
    }
  })

  await page.goto('/databases')
  await page.getByRole('button', { name: 'Create database' }).click()

  await expect(page.getByText('Choose the account that will own the database.')).toBeVisible()
  await expect(page.getByText('Database name is required.')).toBeVisible()
  await expect(page.getByText('User name is required.')).toBeVisible()
  expect(posts).toEqual([])
})

test('creating a database adds it to the list under the name MySQL holds', async ({ page }) => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubAccounts(page, [ALICE])
  await stubDatabases(page, [])

  await page.goto('/databases')

  await page.getByRole('combobox', { name: 'Account' }).click()
  await page.getByRole('option', { name: /alice/ }).click()
  await page.getByRole('textbox', { name: 'Database name' }).fill('shop')
  await page.getByRole('textbox', { name: 'User name' }).fill('shopuser')
  await page.getByRole('button', { name: 'Create database' }).click()

  await expect(page.getByRole('row').filter({ hasText: 'alice_shop' })).toBeVisible()
})

test('the form empties its name fields once the panel has accepted them', async ({ page }) => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubAccounts(page, [ALICE])
  await stubDatabases(page, [])

  await page.goto('/databases')

  await page.getByRole('combobox', { name: 'Account' }).click()
  await page.getByRole('option', { name: /alice/ }).click()
  await page.getByRole('textbox', { name: 'Database name' }).fill('shop')
  await page.getByRole('textbox', { name: 'User name' }).fill('shopuser')
  await page.getByRole('button', { name: 'Create database' }).click()

  await expect(page.getByRole('textbox', { name: 'Database name' })).toHaveValue('')
  await expect(page.getByRole('textbox', { name: 'User name' })).toHaveValue('')
})

// rules/vue.md: the SPA never invents an error message for a server outcome.
test('a rejected create renders the backend own message rather than frontend copy', async ({ page }) => {
  const backendDetail = 'A database with that name already exists for this account.'
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubAccounts(page, [ALICE])
  await stubCreateDatabaseProblem(page, backendDetail)

  await page.goto('/databases')

  await page.getByRole('combobox', { name: 'Account' }).click()
  await page.getByRole('option', { name: /alice/ }).click()
  await page.getByRole('textbox', { name: 'Database name' }).fill('shop')
  await page.getByRole('textbox', { name: 'User name' }).fill('shopuser')
  await page.getByRole('button', { name: 'Create database' }).click()

  await expect(page.getByText(backendDetail)).toBeVisible()
})
