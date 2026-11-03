import { expect, test, type Page } from '@playwright/test'
import { FULL_CATALOGUE, mainNavigation, openSection } from '../fixtures/open-full-shell'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import { stubMyAccount } from '../fixtures/stub-my-account-route'
import { stubEmptyTasks } from '../fixtures/stub-tasks-routes'
import { stubSubUsers, stubSubUsersForbidden, subUserRow } from '../fixtures/stub-sub-users-routes'
import type { MyAccount } from '../../src/types/account'
import type { AuthenticatedUser } from '../../src/types/auth'

// Issue #315: an account's owner invites, limits and removes team members from My account > Sub-users.
// Reached by clicks only: the sidebar's My account section, then its Sub-users link. The endpoints
// are the boundary (they refuse everyone but the owner); these specs prove what the screen does with
// their answers.

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

/** Signs the owner in with the shell's own routes stubbed, then opens the team page from the sidebar. */
const openSubUsers = async (page: Page): Promise<void> => {
  await stubSignedIn(page, CUSTOMER)
  await stubHealthy(page)
  await stubModules(page, FULL_CATALOGUE)
  await stubEmptyTasks(page)
  await stubMyAccount(page, OWN_ACCOUNT)
  await page.goto('/')
  await openSection(page, 'My account')
  await mainNavigation(page).getByRole('link', { name: 'Sub-users', exact: true }).click()
}

test('an owner opens the team page from My account and sees the people they added', async ({ page }) => {
  await stubSubUsers(page, [subUserRow('deploy', ['databases']), subUserRow('writer', [], 'invited')])
  await openSubUsers(page)

  await expect(page.getByRole('heading', { level: 1, name: 'Sub-users' })).toBeVisible()
  const deploy = page.getByRole('row', { name: /deploy/ })
  await expect(deploy).toContainText('deploy@example.com')
  await expect(deploy).toContainText('Active')
  await expect(deploy).toContainText('Databases')
  const writer = page.getByRole('row', { name: /writer/ })
  await expect(writer).toContainText('Invited')
  await expect(writer).toContainText('None')
})

test('an owner invites a person with chosen features and sees them invited', async ({ page }) => {
  const sent = await stubSubUsers(page, [])
  await openSubUsers(page)

  await page.getByLabel('Login name').fill('deploy')
  await page.getByLabel('E-mail address').fill('deploy@example.com')
  await page.getByRole('group', { name: 'Features this person may use' }).getByLabel('Databases').check({ force: true })
  await page.getByRole('group', { name: 'Features this person may use' }).getByLabel('Backups').check({ force: true })
  await page.getByRole('button', { name: 'Send invitation' }).click()

  await expect(page.getByText(/Invitation requested for deploy\./)).toBeVisible()
  expect(sent.invitations).toEqual([
    { username: 'deploy', email: 'deploy@example.com', features: ['databases', 'backups'] },
  ])
  const row = page.getByRole('row', { name: /deploy/ })
  await expect(row).toContainText('Invited')
  await expect(row).toContainText('Databases, Backups')
})

test('the invite form refuses a malformed login name and address without calling the panel', async ({ page }) => {
  const sent = await stubSubUsers(page, [])
  await openSubUsers(page)

  await page.getByLabel('Login name').fill('two words')
  await page.getByLabel('E-mail address').fill('nobody')
  await page.getByRole('button', { name: 'Send invitation' }).click()

  await expect(page.getByText('Use letters, digits, dots, underscores and dashes, up to 64 characters.')).toBeVisible()
  await expect(page.getByText('Enter a valid e-mail address.')).toBeVisible()
  expect(sent.invitations).toEqual([])
})

test('an owner changes the features of a person and the row follows', async ({ page }) => {
  const sent = await stubSubUsers(page, [subUserRow('deploy', ['databases'])])
  await openSubUsers(page)

  await page.getByRole('button', { name: 'Actions for deploy' }).click()
  await page.getByRole('menuitem', { name: 'Change features' }).click()
  const dialog = page.getByRole('dialog', { name: 'Features of deploy' })
  await expect(dialog.getByLabel('Databases')).toBeChecked()
  await dialog.getByText('Databases', { exact: true }).click()
  await dialog.getByText('Cron jobs', { exact: true }).click()
  await dialog.getByRole('button', { name: 'Save features' }).click()

  await expect(page.getByText('Features of deploy saved.')).toBeVisible()
  expect(sent.changes).toEqual([{ features: ['cron'] }])
  await expect(page.getByRole('row', { name: /deploy/ })).toContainText('Cron jobs')
})

test('an owner removes a person only after confirming', async ({ page }) => {
  const sent = await stubSubUsers(page, [subUserRow('deploy', ['databases']), subUserRow('writer', [])])
  await openSubUsers(page)

  await page.getByRole('button', { name: 'Actions for deploy' }).click()
  await page.getByRole('menuitem', { name: 'Remove' }).click()
  await page.getByRole('dialog').getByRole('button', { name: 'Cancel' }).click()
  expect(sent.removals).toEqual([])
  await expect(page.getByRole('row', { name: /deploy/ })).toBeVisible()

  await page.getByRole('button', { name: 'Actions for deploy' }).click()
  await page.getByRole('menuitem', { name: 'Remove' }).click()
  await page.getByRole('dialog').getByRole('button', { name: 'Remove sub-user' }).click()

  await expect(page.getByText('deploy removed.')).toBeVisible()
  expect(sent.removals).toHaveLength(1)
  await expect(page.getByRole('row', { name: /deploy/ })).toHaveCount(0)
  await expect(page.getByRole('row', { name: /writer/ })).toBeVisible()
})

test('a team member who opens the page reads the panel refusal and is offered no form', async ({ page }) => {
  await stubSubUsersForbidden(page, 'Only the owner of the account can manage its sub-users.')
  await openSubUsers(page)

  await expect(page.getByText('Only the owner of the account can manage its sub-users.')).toBeVisible()
  await expect(page.getByRole('button', { name: 'Send invitation' })).toHaveCount(0)
})

test('an owner with no team sees the empty state and the invite form', async ({ page }) => {
  await stubSubUsers(page, [])
  await openSubUsers(page)

  await expect(page.getByText('No sub-users yet')).toBeVisible()
  await expect(page.getByRole('button', { name: 'Send invitation' })).toBeVisible()
})
