import { expect, test } from '../fixtures/guarded-test'
import { stubAccounts } from '../fixtures/stub-accounts-route'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import {
  stubBackupRestore,
  stubBackupRestoreChoices,
  stubBackups,
} from '../fixtures/stub-backups-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubBackupOptions } from '../fixtures/stub-backup-options-route'
import { stubModules } from '../fixtures/stub-modules-route'
import { stubShellReads } from '../fixtures/stub-shell-reads'
import type { Account } from '../../src/types/account'
import type { Backup } from '../../src/types/backup'
import type { PanelModule } from '../../src/types/module'
import type { Page } from '@playwright/test'

const LICENSED: PanelModule[] = [
  { name: 'backups', displayName: 'Backups', kindDisplayName: 'Core', tier: 'included', state: 'enabled' },
]

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

const COMPLETED: Backup = {
  id: '11111111-1111-1111-1111-111111111111',
  accountId: ALICE.id,
  ownerName: ALICE.name,
  orphanedAccountUsername: null,
  status: 'completed',
  statusName: 'Completed',
  kind: 'manual',
  kindName: 'Manual',
  sizeBytes: 1_572_864,
  sha256: 'a1b2c3d4e5f60718293a4b5c6d7e8f90a1b2c3d4e5f60718293a4b5c6d7e8f90',
  databaseCount: 2,
  startedAt: '2026-09-06T03:00:00Z',
  finishedAt: '2026-09-06T03:04:00Z',
  failureCode: null,
  failureDisplayName: null,
  verificationState: 'notVerified',
  verificationStateName: 'Not verified',
  verifiedAt: null,
  verificationFailureCode: null,
  verificationFailureDisplayName: null,
}

/** What the stubbed panel says alice's account holds. */
const CHOICES = {
  databases: ['alice_blog', 'alice_shop'],
  sites: ['blog.example.com', 'shop.example.com'],
}

/**
 * Puts the backups screen in front of a signed-in caller and opens the restore dialog on alice's
 * copy, with the panel naming the given parts the restore can be narrowed to.
 * @param page The Playwright page under test.
 * @param choices The lists the stubbed panel answers, or `null` for a panel that cannot list them.
 * @returns Resolves once the dialog is on screen.
 */
const openDialog = async (
  page: Page,
  choices: { databases: string[]; sites: string[] } | null = CHOICES,
): Promise<void> => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubBackupOptions(page)
  await stubModules(page, LICENSED)
  await stubShellReads(page)
  await stubAccounts(page, [ALICE])
  await stubBackups(page, [COMPLETED])
  await stubBackupRestoreChoices(page, choices)
  await page.goto('/backups')
  await page.getByRole('button', { name: 'Actions for the backup of alice' }).click()
  await page.getByRole('menuitem', { name: 'Restore', exact: true }).click()
}

// The default is what the dialog always was: the whole account. Positive control for every case
// below, which would pass just as well against a dialog that no longer offered it.
test('the whole account is the default and its request carries no narrowing field', async ({
  page,
}) => {
  await openDialog(page)
  const submitted = await stubBackupRestore(page, 200, {
    backupId: COMPLETED.id,
    accountId: ALICE.id,
    databasesRestored: 2,
  })

  await expect(page.getByRole('radio', { name: 'The whole account' })).toBeChecked()
  await page.getByRole('textbox', { name: 'Account name' }).fill('alice')
  await page.getByRole('button', { name: 'Restore this account' }).click()

  await expect.poll(submitted).toHaveLength(1)
  expect(submitted()[0]).toEqual({ confirmAccountUsername: 'alice' })
})

test('a database restore sends that database and no site, and says only it was replaced', async ({
  page,
}) => {
  await openDialog(page)
  const submitted = await stubBackupRestore(page, 200, {
    backupId: COMPLETED.id,
    accountId: ALICE.id,
    databasesRestored: 1,
  })

  await page.getByRole('radio', { name: 'One database' }).check({ force: true })
  await page.getByRole('combobox', { name: 'Database' }).click()
  await page.getByRole('option', { name: 'alice_shop' }).click()
  await expect(
    page.getByText('The database alice_shop is dropped, re-created and loaded again from the copy.'),
  ).toBeVisible()

  await page.getByRole('textbox', { name: 'Account name' }).fill('alice')
  await page.getByRole('button', { name: 'Restore this part' }).click()

  await expect.poll(submitted).toHaveLength(1)
  expect(submitted()[0]).toEqual({ confirmAccountUsername: 'alice', databaseName: 'alice_shop' })
  await expect(page.getByText('alice_shop: replaced')).toBeVisible()
  await expect(page.getByText('Only this part was replaced.')).toBeVisible()
})

test('a site restore sends that domain and no database', async ({ page }) => {
  await openDialog(page)
  const submitted = await stubBackupRestore(page, 200, {
    backupId: COMPLETED.id,
    accountId: ALICE.id,
    databasesRestored: 0,
  })

  await page.getByRole('radio', { name: 'One site' }).check({ force: true })
  await page.getByRole('combobox', { name: 'Site' }).click()
  await page.getByRole('option', { name: 'shop.example.com' }).click()

  await page.getByRole('textbox', { name: 'Account name' }).fill('alice')
  await page.getByRole('button', { name: 'Restore this part' }).click()

  await expect.poll(submitted).toHaveLength(1)
  expect(submitted()[0]).toEqual({ confirmAccountUsername: 'alice', siteDomain: 'shop.example.com' })
})

// A narrowed restore is still a replacement: choosing a kind without choosing the part must not be
// sendable, and the typed account name is still the whole of the confirmation.
test('a narrowed restore sends nothing until the part is chosen and the name is typed', async ({
  page,
}) => {
  await openDialog(page)
  const submitted = await stubBackupRestore(page, 200, {
    backupId: COMPLETED.id,
    accountId: ALICE.id,
    databasesRestored: 1,
  })

  await page.getByRole('radio', { name: 'One database' }).check({ force: true })
  await page.getByRole('textbox', { name: 'Account name' }).fill('alice')
  const submit = page.getByRole('button', { name: 'Restore this part' })
  await expect(submit).toBeVisible()

  await submit.click({ force: true })
  expect(submitted()).toEqual([])

  await page.getByRole('combobox', { name: 'Database' }).click()
  await page.getByRole('option', { name: 'alice_blog' }).click()
  await page.getByRole('textbox', { name: 'Account name' }).fill('Alice')
  await submit.click({ force: true })
  expect(submitted()).toEqual([])
})

// Switching kind forgets the name chosen under the other, so a database picked and abandoned is
// never sent with a site restore.
test('changing the kind forgets the part chosen under the other kind', async ({ page }) => {
  await openDialog(page)
  const submitted = await stubBackupRestore(page, 200, {
    backupId: COMPLETED.id,
    accountId: ALICE.id,
    databasesRestored: 0,
  })

  await page.getByRole('radio', { name: 'One database' }).check({ force: true })
  await page.getByRole('combobox', { name: 'Database' }).click()
  await page.getByRole('option', { name: 'alice_shop' }).click()

  // Chosen through its label, as a person clicks it: the database list has just closed over the row.
  await page.locator('label').filter({ hasText: 'One site' }).click()
  await expect(page.getByRole('radio', { name: 'One site' })).toBeChecked()
  await page.getByRole('combobox', { name: 'Site' }).click()
  await page.getByRole('option', { name: 'blog.example.com' }).click()
  await page.getByRole('textbox', { name: 'Account name' }).fill('alice')
  await page.getByRole('button', { name: 'Restore this part' }).click()

  await expect.poll(submitted).toHaveLength(1)
  expect(submitted()[0]).toEqual({ confirmAccountUsername: 'alice', siteDomain: 'blog.example.com' })
})

// The lists only widen what the dialog offers. A panel that cannot list them leaves the
// whole-account restore possible and says why nothing narrower is offered.
test('when the lists cannot be had only the whole account is offered, and it still works', async ({
  page,
  guard,
}) => {
  guard.allow(500, /\/api\/v1\/backups\/[^/]+\/restore-choices$/)
  await openDialog(page, null)

  await expect(
    page.getByText('The account\'s databases and sites could not be listed'),
  ).toBeVisible()
  await expect(page.getByRole('radio', { name: 'One database' })).toHaveCount(0)
  await expect(page.getByRole('button', { name: 'Restore this account' })).toBeVisible()
})

// An account with no database and no site has nothing to narrow to; the dialog is the one it always was.
test('an account with nothing to narrow to is offered no choice', async ({ page }) => {
  await openDialog(page, { databases: [], sites: [] })

  await expect(page.getByRole('button', { name: 'Restore this account' })).toBeVisible()
  await expect(page.getByRole('radio')).toHaveCount(0)
})

// The kinds are offered individually: an account with databases but no site is not offered a site.
test('only the kinds the account can offer are shown', async ({ page }) => {
  await openDialog(page, { databases: ['alice_shop'], sites: [] })

  await expect(page.getByRole('radio', { name: 'One database' })).toBeVisible()
  await expect(page.getByRole('radio', { name: 'One site' })).toHaveCount(0)
})
