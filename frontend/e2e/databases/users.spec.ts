import { expect, test, type Locator, type Page } from '@playwright/test'
import { stubAccounts } from '../fixtures/stub-accounts-route'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import {
  stubDatabaseUsers,
  stubbedUserPassword,
  stubbedUserResetPassword,
  type DatabaseUsersRecording,
} from '../fixtures/stub-database-users-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import type { Account } from '../../src/types/account'
import type { DatabaseUser, GrantableDatabase } from '../../src/types/databaseUser'
import type { PanelModule } from '../../src/types/module'

// Database users (#156): local MySQL users and their per-database privileges. The list is
// server-paged, the privilege set and the presets are the panel's, and the password is shown once.

const LICENSED: PanelModule[] = [{ name: 'databases', displayName: 'Databases', kindDisplayName: 'Core', tier: 'included', state: 'enabled' }]

const ALICE: Account = {
  id: '22222222-2222-2222-2222-222222222222',
  name: 'alice',
  primaryDomain: 'alice.example.com',
  planId: '44444444-4444-4444-4444-444444444444',
  status: 'active',
  statusName: 'Active',
  createdAt: '2026-08-01T10:00:00Z',
  resellerId: null,
  resellerName: null,
}

const SHOP_DATABASE: GrantableDatabase = { id: '11111111-1111-1111-1111-111111111111', fullName: 'alice_shop' }
const BLOG_DATABASE: GrantableDatabase = { id: '33333333-3333-3333-3333-333333333333', fullName: 'alice_blog' }

/** A database's own user: it holds every privilege on its database and cannot be dropped alone. */
const dedicatedUser = (): DatabaseUser => {return {
  id: 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
  accountId: ALICE.id,
  ownerName: ALICE.name,
  nameSuffix: 'shopuser',
  fullName: 'alice_shopuser',
  isDedicated: true,
  grants: [{ databaseId: SHOP_DATABASE.id, databaseFullName: SHOP_DATABASE.fullName, privileges: ['select', 'insert'] }],
  createdAt: '2026-08-01T10:00:00Z',
}}

/** An extra user the customer created: no privileges yet, and it may be dropped. */
const extraUser = (): DatabaseUser => {return {
  id: 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb',
  accountId: ALICE.id,
  ownerName: ALICE.name,
  nameSuffix: 'reports',
  fullName: 'alice_reports',
  isDedicated: false,
  grants: [],
  createdAt: '2026-08-02T10:00:00Z',
}}

/** The address the 390px check below opens; `e2e/ui-kit/route-coverage.spec.ts` reads it from here. */
const SWEPT_SCREEN = { path: '/databases/users' }

/** Opens the users screen with the given users already on the server. */
const openScreen = async (page: Page, users: DatabaseUser[]): Promise<DatabaseUsersRecording> => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubAccounts(page, [ALICE])
  const recording = await stubDatabaseUsers(page, users, [SHOP_DATABASE, BLOG_DATABASE])
  await page.goto(SWEPT_SCREEN.path)
  return recording
}

/** Opens a row's actions menu and chooses one of its commands. */
const chooseRowAction = async (page: Page, row: Locator, name: string, command: string): Promise<void> => {
  await row.getByRole('button', { name: `Actions for ${name}` }).click()
  // The menu is rendered into `body`, so it is queried from the page and not from the row.
  await page.getByRole('menuitem', { name: command }).click()
}

test('the users screen shows the empty state when the panel reports no users', async ({ page }) => {
  await openScreen(page, [])

  await expect(page.getByText('No database users yet')).toBeVisible()
  await expect(page.getByRole('table')).toHaveCount(0)
})

test('the list shows the prefixed login and how many databases each user holds privileges on', async ({ page }) => {
  await openScreen(page, [dedicatedUser(), extraUser()])

  const dedicated = page.getByRole('row').filter({ hasText: 'alice_shopuser' })
  await expect(dedicated).toContainText('Database user')
  await expect(dedicated).toContainText('1')
  const extra = page.getByRole('row').filter({ hasText: 'alice_reports' })
  await expect(extra).toContainText('Extra user')
  // The bare suffix is not a name MySQL holds, so it is not offered as one.
  await expect(page.getByRole('cell', { name: 'reports', exact: true })).toHaveCount(0)
})

test('the search is sent to the panel, which narrows the list', async ({ page }) => {
  const recording = await openScreen(page, [dedicatedUser(), extraUser()])

  await page.getByRole('searchbox', { name: 'Search users' }).fill('report')

  await expect(page.getByRole('row').filter({ hasText: 'alice_reports' })).toBeVisible()
  await expect(page.getByRole('row').filter({ hasText: 'alice_shopuser' })).toHaveCount(0)
  expect(recording.listRequests().some((url) => {return url.searchParams.get('search') === 'report'})).toBe(true)
})

test('an administrator narrows the list by account through the filter sheet', async ({ page }) => {
  const recording = await openScreen(page, [dedicatedUser(), extraUser()])

  await page.getByTestId('filter-trigger').click()
  // Scoped to the sheet: the create form behind it has an account picker of the same name.
  const sheet = page.getByRole('dialog', { name: 'Filters' })
  await sheet.getByRole('combobox', { name: 'Account' }).click()
  await page.getByRole('option', { name: /alice/ }).click()
  await sheet.getByRole('button', { name: 'Apply' }).click()

  await expect(page.getByRole('row').filter({ hasText: 'alice_reports' })).toBeVisible()
  expect(recording.listRequests().some((url) => {return url.searchParams.get('accountId') === ALICE.id})).toBe(true)
})

test('creating a user shows its generated password once and lists the new user', async ({ page }) => {
  await openScreen(page, [])

  await page.getByRole('combobox', { name: 'Account' }).click()
  await page.getByRole('option', { name: /alice/ }).click()
  await page.getByRole('textbox', { name: 'User name' }).fill('reports')
  await page.getByRole('button', { name: 'Create user' }).click()

  await expect(page.getByTestId('database-password')).toHaveText(stubbedUserPassword)
  await page.getByRole('button', { name: 'Done' }).click()
  await expect(page.getByTestId('database-password')).toHaveCount(0)
  await expect(page.getByRole('row').filter({ hasText: 'alice_reports' })).toBeVisible()
})

test('a name outside the server alphabet is refused before any request is sent', async ({ page }) => {
  const recording = await openScreen(page, [])
  // The screen's own first read is not what is being counted: wait for it before taking the baseline.
  await expect
    .poll(() => {
      return recording.listRequests().length
    })
    .toBeGreaterThan(0)
  const before = recording.listRequests().length

  await page.getByRole('combobox', { name: 'Account' }).click()
  await page.getByRole('option', { name: /alice/ }).click()
  await page.getByRole('textbox', { name: 'User name' }).fill('Bad_Name')
  await page.getByRole('button', { name: 'Create user' }).click()

  await expect(page.getByText('Use lowercase letters and digits only, up to 30 characters.')).toBeVisible()
  await expect(page.getByTestId('database-password')).toHaveCount(0)
  expect(recording.listRequests().length).toBe(before)
})

test('resetting a password shows the new one once', async ({ page }) => {
  await openScreen(page, [extraUser()])

  await chooseRowAction(page, page.getByRole('row').filter({ hasText: 'alice_reports' }), 'alice_reports', 'Reset password')
  await page.getByRole('button', { name: 'Reset password' }).click()

  await expect(page.getByTestId('database-password')).toHaveText(stubbedUserResetPassword)
})

test('an extra user can be dropped after confirmation', async ({ page }) => {
  const recording = await openScreen(page, [dedicatedUser(), extraUser()])

  await chooseRowAction(page, page.getByRole('row').filter({ hasText: 'alice_reports' }), 'alice_reports', 'Drop')
  await page.getByRole('button', { name: 'Drop user' }).click()

  await expect(page.getByRole('row').filter({ hasText: 'alice_reports' })).toHaveCount(0)
  expect(recording.drops()).toEqual([extraUser().id])
})

// The backend refuses a lone drop of a database's own user; the menu does not offer it at all.
test('a database user has no drop in its menu', async ({ page }) => {
  await openScreen(page, [dedicatedUser()])

  await page.getByRole('button', { name: 'Actions for alice_shopuser' }).click()

  await expect(page.getByRole('menuitem', { name: 'Edit privileges' })).toBeVisible()
  await expect(page.getByRole('menuitem', { name: 'Reset password' })).toBeVisible()
  await expect(page.getByRole('menuitem', { name: 'Drop' })).toHaveCount(0)
})

test('the privileges editor offers the panel-served privileges and fills them from a preset', async ({ page }) => {
  const recording = await openScreen(page, [extraUser()])

  await chooseRowAction(page, page.getByRole('row').filter({ hasText: 'alice_reports' }), 'alice_reports', 'Edit privileges')
  const dialog = page.getByRole('dialog')
  await dialog.getByRole('combobox', { name: 'Database' }).click()
  await page.getByRole('option', { name: 'alice_shop' }).click()
  await dialog.getByRole('button', { name: 'Read and write' }).click()

  for (const name of ['SELECT', 'INSERT', 'UPDATE', 'DELETE']) {
    await expect(dialog.getByRole('checkbox', { name })).toBeChecked()
  }
  await dialog.getByRole('checkbox', { name: 'DELETE' }).uncheck({ force: true })
  await dialog.getByRole('button', { name: 'Save privileges' }).click()

  await expect(page.getByText('Privileges saved.')).toBeVisible()
  expect(recording.grantWrites()).toEqual([
    { databaseId: SHOP_DATABASE.id, privileges: ['select', 'insert', 'update'] },
  ])
  // The editor re-read the user, so the grant now appears among the current privileges.
  await expect(dialog).toContainText('alice_shop')
})

test('unticking everything removes the grant, and the editor says so', async ({ page }) => {
  const recording = await openScreen(page, [dedicatedUser()])

  await chooseRowAction(page, page.getByRole('row').filter({ hasText: 'alice_shopuser' }), 'alice_shopuser', 'Edit privileges')
  const dialog = page.getByRole('dialog')
  await dialog.getByRole('combobox', { name: 'Database' }).click()
  await page.getByRole('option', { name: 'alice_shop' }).click()
  await dialog.getByRole('checkbox', { name: 'SELECT' }).uncheck({ force: true })
  await dialog.getByRole('checkbox', { name: 'INSERT' }).uncheck({ force: true })

  await expect(dialog).toContainText('saving removes')
  await dialog.getByRole('button', { name: 'Save privileges' }).click()

  await expect(page.getByText('Privileges saved.')).toBeVisible()
  expect(recording.grantWrites()).toEqual([{ databaseId: SHOP_DATABASE.id, privileges: [] }])
})

test('Save is unavailable until the ticked set differs from what the panel holds', async ({ page }) => {
  await openScreen(page, [dedicatedUser()])

  await chooseRowAction(page, page.getByRole('row').filter({ hasText: 'alice_shopuser' }), 'alice_shopuser', 'Edit privileges')
  const dialog = page.getByRole('dialog')
  await dialog.getByRole('combobox', { name: 'Database' }).click()
  await page.getByRole('option', { name: 'alice_shop' }).click()

  await expect(dialog.getByRole('button', { name: 'Save privileges' })).toBeDisabled()
  await dialog.getByRole('checkbox', { name: 'UPDATE' }).check({ force: true })
  await expect(dialog.getByRole('button', { name: 'Save privileges' })).toBeEnabled()
})

// rules/vue.md: the backend's localized refusal is rendered verbatim.
test('a refused list read renders the panel message verbatim', async ({ page }) => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubAccounts(page, [ALICE])
  await page.route(/\/api\/v1\/database-users(\?.*)?$/, async (route) => {
    await route.fulfill({
      status: 500,
      contentType: 'application/problem+json',
      body: JSON.stringify({ code: 'HostUnexpectedError', title: 'Unexpected error', detail: 'The panel could not list users.' }),
    })
  })

  await page.goto('/databases/users')

  await expect(page.getByText('The panel could not list users.')).toBeVisible()
})

test('the users screen does not scroll horizontally at 390px', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 800 })
  await openScreen(page, [dedicatedUser(), extraUser()])

  await expect(page.getByRole('heading', { name: 'Database users', level: 1 })).toBeVisible()
  const overflow = await page.evaluate(() => {return document.documentElement.scrollWidth - document.documentElement.clientWidth})
  expect(overflow).toBeLessThanOrEqual(0)
})
