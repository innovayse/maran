import { expect, test, type Page } from '@playwright/test'
import { expectNoHorizontalOverflow } from '../fixtures/measure-horizontal-extent'
import { setPersistedLocale } from '../fixtures/set-locale'
import { stubAccountNames, stubAccounts } from '../fixtures/stub-accounts-route'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubFilesOptions } from '../fixtures/stub-file-browser-routes'
import {
  stubFileOperations,
  stubTrashEntries,
  trashEntry,
  type RecordedOperation,
} from '../fixtures/stub-file-operation-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import type { Account } from '../../src/types/account'
import type { TrashEntry } from '../../src/types/file'
import type { PanelModule } from '../../src/types/module'

// The trash: what the account moved there, restored by trash id, emptied for good after a question.

/** The address the 390px check below opens; `e2e/ui-kit/route-coverage.spec.ts` reads it from here. */
const SWEPT_SCREEN = { path: '/files/trash' }

const LICENSED_FILES: PanelModule[] = [{ name: 'files', displayName: 'Files', kindDisplayName: 'Core', tier: 'included', state: 'enabled' }]

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

/**
 * Opens the trash page with a mutable trash and every change endpoint stubbed.
 * @param page The Playwright page.
 * @param trash The trashed entries; a restore or an empty edits it.
 * @param failing Trash ids the stub refuses, by reason.
 * @returns A reader of the change requests made so far.
 */
const openTrash = async (
  page: Page,
  trash: TrashEntry[],
  failing: Record<string, string> = {},
): Promise<() => readonly RecordedOperation[]> => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED_FILES)
  await stubAccounts(page, [ALICE])
  await stubAccountNames(page, [ALICE])
  await stubFilesOptions(page)
  await stubTrashEntries(page, trash)
  const calls = await stubFileOperations(page, failing, trash)
  await page.goto(SWEPT_SCREEN.path)
  return calls
}

test('the trash lists what was trashed with its original location and retention', async ({ page }) => {
  await openTrash(page, [trashEntry('t1', 'report.pdf', { originalPath: 'docs/report.pdf', sizeBytes: 1536 })])

  const row = page.getByRole('row').filter({ hasText: 'report.pdf' })
  await expect(row).toContainText('docs/report.pdf')
  await expect(row).toContainText('1.5 KiB')
  await expect(page.getByText('Entries are removed for good after 30 days')).toBeVisible()
})

test('an empty trash says so', async ({ page }) => {
  await openTrash(page, [])

  await expect(page.getByText('The trash is empty')).toBeVisible()
  await expect(page.getByRole('button', { name: 'Empty trash' })).toBeDisabled()
})

test('restoring ticked entries sends their trash ids and reads the page again', async ({ page }) => {
  const trash = [trashEntry('t1', 'report.pdf'), trashEntry('t2', 'old.log')]
  const calls = await openTrash(page, trash)

  await page.getByRole('checkbox', { name: 'Select report.pdf' }).check({ force: true })
  await page.getByRole('group', { name: 'Actions for the selected entries' }).getByRole('button', { name: 'Restore' }).click()

  await expect(page.getByRole('row').filter({ hasText: 'report.pdf' })).toHaveCount(0)
  await expect(page.getByRole('row').filter({ hasText: 'old.log' })).toBeVisible()
  expect(calls()).toEqual([{ method: 'POST', endpoint: 'trash/restore', body: { accountId: ALICE.id, trashIds: ['t1'] } }])
})

test('a restore whose target exists again is refused for that entry alone', async ({ page }) => {
  const trash = [trashEntry('t1', 'report.pdf'), trashEntry('t2', 'old.log')]
  await openTrash(page, trash, { t1: 'A file with that name already exists.' })

  await page.getByRole('checkbox', { name: 'Select all' }).check({ force: true })
  await page.getByRole('group', { name: 'Actions for the selected entries' }).getByRole('button', { name: 'Restore' }).click()

  await expect(page.getByTestId('batch-result')).toContainText('A file with that name already exists.')
  await expect(page.getByRole('row').filter({ hasText: 'old.log' })).toHaveCount(0)
  await expect(page.getByRole('row').filter({ hasText: 'report.pdf' })).toBeVisible()
})

test('emptying the trash asks first and removes everything after the answer', async ({ page }) => {
  const trash = [trashEntry('t1', 'report.pdf'), trashEntry('t2', 'old.log')]
  const calls = await openTrash(page, trash)

  await page.getByRole('button', { name: 'Empty trash' }).click()
  const dialog = page.getByRole('dialog', { name: 'Empty the trash' })
  await expect(dialog.getByTestId('confirm-message')).toHaveText('This removes 2 entries for good. It cannot be undone.')
  expect(calls()).toHaveLength(0)
  await dialog.getByRole('button', { name: 'Empty trash' }).click()

  await expect(page.getByText('The trash is empty')).toBeVisible()
  expect(calls()).toEqual([{ method: 'DELETE', endpoint: 'trash', body: null }])
})

test('the trash fits a phone in every language without scrolling sideways', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 })
  for (const locale of ['en', 'ru', 'hy'] as const) {
    await setPersistedLocale(page, locale)
    await openTrash(page, [trashEntry('t1', 'a-rather-long-file-name-that-wraps.tar.gz')])

    await expect(page.getByRole('heading', { level: 1 })).toBeVisible()
    await expectNoHorizontalOverflow(page)
  }
})
