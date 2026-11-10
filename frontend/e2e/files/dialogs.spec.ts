import { expect, test } from '../fixtures/guarded-test'
import { stubAccountNames, stubAccounts } from '../fixtures/stub-accounts-route'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { fileEntry, stubFileEntries, stubFilesOptions } from '../fixtures/stub-file-browser-routes'
import { stubFileOperations, type RecordedOperation } from '../fixtures/stub-file-operation-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import { stubShellReads } from '../fixtures/stub-shell-reads'
import type { Account } from '../../src/types/account'
import type { FileEntry } from '../../src/types/file'
import type { PanelModule } from '../../src/types/module'
import type { Page } from '@playwright/test'

// The file manager's dialogs: create, rename, move, copy, permissions, compress, extract, trash and
// permanent delete. Each is asked through the row menu or the bulk bar, and each sends exactly the
// request the panel's contract names; the SPA decides nothing about what is allowed.

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

const HOME: FileEntry[] = [
  fileEntry('', 'public_html', { kind: 'directory', sizeBytes: 4096, mode: '755' }),
  fileEntry('', 'notes.txt'),
  fileEntry('', 'site.zip'),
]

/**
 * Opens the browser on a small home with every change endpoint stubbed.
 * @param page The Playwright page.
 * @param failing Subjects the stub refuses, by reason.
 * @param home The entries of the home folder.
 * @param inside The entries of the `public_html` folder inside it.
 * @returns A reader of the change requests made so far.
 */
const openBrowser = async (
  page: Page,
  failing: Record<string, string> = {},
  home: FileEntry[] = HOME,
  inside: FileEntry[] = [],
): Promise<() => readonly RecordedOperation[]> => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED_FILES)
  await stubShellReads(page)
  await stubAccounts(page, [ALICE])
  await stubAccountNames(page, [ALICE])
  await stubFilesOptions(page)
  await stubFileEntries(page, { '': home, public_html: inside })
  const calls = await stubFileOperations(page, failing)
  await page.goto('/files')
  await expect(page.getByRole('row').filter({ hasText: 'notes.txt' })).toBeVisible()
  return calls
}

/**
 * Opens a row's actions menu and chooses an item.
 * @param page The Playwright page.
 * @param name The entry's name.
 * @param item The menu item's name.
 * @returns Resolves once the item was pressed.
 */
const chooseFromMenu = async (page: Page, name: string, item: string): Promise<void> => {
  await page.getByRole('button', { name: `Actions for ${name}` }).click()
  await page.getByRole('menuitem', { name: item, exact: true }).click()
}

test('a new folder is created inside the folder being browsed', async ({ page }) => {
  const calls = await openBrowser(page)

  await page.getByRole('button', { name: 'New folder' }).click()
  const dialog = page.getByRole('dialog', { name: 'New folder' })
  await dialog.getByLabel('Name').fill('assets')
  await dialog.getByRole('button', { name: 'Create' }).click()

  await expect(dialog).toHaveCount(0)
  expect(calls()).toEqual([
    { method: 'POST', endpoint: 'entries', body: { accountId: ALICE.id, path: 'assets', kind: 'directory' } },
  ])
})

test('a name with a slash is refused before anything is sent', async ({ page }) => {
  const calls = await openBrowser(page)

  await page.getByRole('button', { name: 'New file' }).click()
  const dialog = page.getByRole('dialog', { name: 'New file' })
  await dialog.getByLabel('Name').fill('a/b.txt')
  await dialog.getByRole('button', { name: 'Create' }).click()

  await expect(dialog.getByText('A name cannot contain a slash or a backslash.')).toBeVisible()
  expect(calls()).toHaveLength(0)
})

test('the destination picker pages and searches a crowded folder on the server', async ({ page }) => {
  // 30 sub-folders inside public_html: more than one page of the picker, so showing them all would mean
  // truncating or loading all. They sit one folder down so the browser's own listing stays on one page.
  const crowded = Array.from({ length: 30 }, (_, index) => {
    return fileEntry('public_html', `site${String(index + 1).padStart(2, '0')}`, {
      kind: 'directory',
      sizeBytes: 4096,
      mode: '755',
    })
  })
  await openBrowser(page, {}, HOME, crowded)

  await chooseFromMenu(page, 'notes.txt', 'Move')
  const dialog = page.getByRole('dialog', { name: 'Move 1 item' })
  await dialog.getByRole('button', { name: 'public_html' }).click()
  await expect(dialog.getByRole('button', { name: 'site25' })).toBeVisible()
  await expect(dialog.getByRole('button', { name: 'site26' })).toHaveCount(0)

  await dialog.getByRole('button', { name: 'Show more folders' }).click()
  await expect(dialog.getByRole('button', { name: 'site30' })).toBeVisible()
  await expect(dialog.getByRole('button', { name: 'Show more folders' })).toHaveCount(0)

  await dialog.getByRole('searchbox', { name: 'Search folders' }).fill('site07')
  await expect(dialog.getByRole('button', { name: 'site07' })).toBeVisible()
  await expect(dialog.getByRole('button', { name: 'site08' })).toHaveCount(0)
})

test('renaming sends the new name and reads the folder again', async ({ page }) => {
  const calls = await openBrowser(page)

  await chooseFromMenu(page, 'notes.txt', 'Rename')
  const dialog = page.getByRole('dialog', { name: 'Rename notes.txt' })
  await dialog.getByLabel('New name').fill('todo.txt')
  await dialog.getByRole('button', { name: 'Rename' }).click()

  await expect(dialog).toHaveCount(0)
  expect(calls()).toEqual([
    { method: 'POST', endpoint: 'rename', body: { accountId: ALICE.id, path: 'notes.txt', newName: 'todo.txt' } },
  ])
})

test('moving walks into a folder and sends that folder as the destination', async ({ page }) => {
  const calls = await openBrowser(page)

  await chooseFromMenu(page, 'notes.txt', 'Move')
  const dialog = page.getByRole('dialog', { name: 'Move 1 item' })
  await dialog.getByRole('button', { name: 'public_html' }).click()
  await expect(dialog.getByTestId('picker-current')).toContainText('public_html')
  await dialog.getByRole('button', { name: 'Move here' }).click()

  await expect(dialog).toHaveCount(0)
  expect(calls()).toEqual([
    {
      method: 'POST',
      endpoint: 'move',
      body: { accountId: ALICE.id, sourcePaths: ['notes.txt'], destinationDirectory: 'public_html' },
    },
  ])
})

test('the permissions grid and the octal field are one value', async ({ page }) => {
  const calls = await openBrowser(page)

  await chooseFromMenu(page, 'notes.txt', 'Permissions')
  const dialog = page.getByRole('dialog', { name: 'Permissions of notes.txt' })
  await expect(dialog.getByLabel('Mode (octal)')).toHaveValue('644')
  await dialog.getByRole('group', { name: 'Group' }).getByRole('checkbox', { name: 'Write' }).check({ force: true })
  await expect(dialog.getByLabel('Mode (octal)')).toHaveValue('664')
  await dialog.getByRole('button', { name: 'Apply' }).click()

  await expect(dialog).toHaveCount(0)
  expect(calls()).toEqual([
    { method: 'POST', endpoint: 'permissions', body: { accountId: ALICE.id, path: 'notes.txt', mode: '664', recursive: false } },
  ])
})

test('a recursive change of a folder asks for the folders\' mode, prefilled with execute where read is set', async ({
  page,
}) => {
  const calls = await openBrowser(page)

  await chooseFromMenu(page, 'public_html', 'Permissions')
  const dialog = page.getByRole('dialog', { name: 'Permissions of public_html' })
  await expect(dialog.getByLabel('Mode for folders inside (octal)')).toHaveCount(0)
  await dialog.getByLabel('Mode (octal)').fill('644')
  await dialog.getByRole('switch', { name: 'Apply to everything inside this folder' }).check({ force: true })
  const folderMode = dialog.getByLabel('Mode for folders inside (octal)')
  await expect(folderMode).toHaveValue('755')

  await dialog.getByLabel('Mode (octal)').fill('640')
  await expect(folderMode).toHaveValue('750')
  await folderMode.fill('770')
  await dialog.getByLabel('Mode (octal)').fill('600')
  await expect(folderMode).toHaveValue('770')
  await dialog.getByRole('button', { name: 'Apply' }).click()

  await expect(dialog).toHaveCount(0)
  expect(calls()).toEqual([
    {
      method: 'POST',
      endpoint: 'permissions',
      body: { accountId: ALICE.id, path: 'public_html', mode: '600', recursive: true, directoryMode: '770' },
    },
  ])
})

test('compressing sends the chosen name with its extension and the format', async ({ page }) => {
  const calls = await openBrowser(page)

  await chooseFromMenu(page, 'notes.txt', 'Compress')
  const dialog = page.getByRole('dialog', { name: 'Compress 1 item' })
  await dialog.getByLabel('Archive name').fill('bundle')
  await dialog.getByRole('button', { name: 'Compress' }).click()

  await expect(dialog).toHaveCount(0)
  expect(calls()).toEqual([
    {
      method: 'POST',
      endpoint: 'archive',
      body: { accountId: ALICE.id, sourcePaths: ['notes.txt'], destinationPath: 'bundle.zip', format: 'zip' },
    },
  ])
})

test('extracting sends the archive and the folder being browsed', async ({ page }) => {
  const calls = await openBrowser(page)

  await chooseFromMenu(page, 'site.zip', 'Extract')
  const dialog = page.getByRole('dialog', { name: 'Extract site.zip' })
  await dialog.getByRole('button', { name: 'Extract here' }).click()

  await expect(dialog).toHaveCount(0)
  expect(calls()).toEqual([
    { method: 'POST', endpoint: 'extract', body: { accountId: ALICE.id, archivePath: 'site.zip', destinationDirectory: '' } },
  ])
})

test('moving to the trash asks first and then sends the paths', async ({ page }) => {
  const calls = await openBrowser(page)

  await chooseFromMenu(page, 'notes.txt', 'Move to trash')
  const dialog = page.getByRole('dialog', { name: 'Move 1 item to the trash' })
  await expect(dialog).toContainText('30 days')
  expect(calls()).toHaveLength(0)
  await dialog.getByRole('button', { name: 'Move to trash' }).click()

  await expect(dialog).toHaveCount(0)
  expect(calls()).toEqual([{ method: 'POST', endpoint: 'trash', body: { accountId: ALICE.id, paths: ['notes.txt'] } }])
})

test('a permanent delete stays disabled until the acknowledgement is ticked', async ({ page }) => {
  const calls = await openBrowser(page)

  await chooseFromMenu(page, 'notes.txt', 'Delete permanently')
  const dialog = page.getByRole('dialog', { name: 'Delete 1 item permanently' })
  const confirm = dialog.getByRole('button', { name: 'Delete permanently' })
  await expect(confirm).toBeDisabled()
  await dialog.getByRole('checkbox', { name: 'I understand this cannot be undone' }).check({ force: true })
  await confirm.click()

  await expect(dialog).toHaveCount(0)
  expect(calls()).toEqual([
    { method: 'POST', endpoint: 'delete', body: { accountId: ALICE.id, paths: ['notes.txt'], confirm: true } },
  ])
})

test('a batch where one item fails lists the failure in the server own words', async ({ page }) => {
  await openBrowser(page, { 'site.zip': 'The file is in use.' })

  await page.getByRole('checkbox', { name: 'Select notes.txt' }).check({ force: true })
  await page.getByRole('checkbox', { name: 'Select site.zip' }).check({ force: true })
  await page.getByRole('group', { name: 'Actions for the selected items' }).getByRole('button', { name: 'Move to trash' }).click()
  await page.getByRole('dialog').getByRole('button', { name: 'Move to trash' }).click()

  const notice = page.getByTestId('batch-result')
  await expect(notice).toContainText('1 done, 1 failed.')
  await expect(notice).toContainText('site.zip')
  await expect(notice).toContainText('The file is in use.')
})
