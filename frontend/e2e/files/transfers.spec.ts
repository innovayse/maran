import { expect, test, type Page } from '@playwright/test'
import { Buffer } from 'node:buffer'
import { stubAccounts } from '../fixtures/stub-accounts-route'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubFileEditorRoutes, type FileEditorStub } from '../fixtures/stub-file-editor-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import type { Account } from '../../src/types/account'
import type { PanelModule } from '../../src/types/module'

const LICENSED: PanelModule[] = [{ name: 'files', displayName: 'File manager', kindDisplayName: 'Core', tier: 'included', state: 'enabled' }]

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
 * Opens the file manager on an empty home folder, where the drop zone and the upload queue live.
 * @param page The page to drive.
 * @returns The stub, to read uploads from and to steer refusals with.
 */
const openManager = async (page: Page): Promise<FileEditorStub> => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubAccounts(page, [ALICE])
  await page.route(/\/api\/v1\/files\/entries(\?.*)?$/, async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({ items: [], total: 0, page: 1, pageSize: 25 }),
    })
  })
  const stub = await stubFileEditorRoutes(page, { content: '', modifiedAt: '2026-11-07T09:00:00Z', mode: '644' })
  await page.goto(`/files?account=${ALICE.id}`)
  await expect(page.getByTestId('drop-zone')).toBeVisible()
  return stub
}

/**
 * Hands files to the drop zone's picker the way the platform's file dialog does.
 * @param page The page to drive.
 * @param names The names of the files to choose, each with a few bytes of content.
 * @returns Resolves once the files are chosen.
 */
const choose = async (page: Page, names: string[]): Promise<void> => {
  await page
    .getByTestId('drop-zone')
    .locator('input[type=file]')
    .setInputFiles(names.map((name) => {return { name, mimeType: 'text/plain', buffer: Buffer.from(`content of ${name}`) }}))
}

test('Several chosen files each upload into the folder and are marked uploaded', async ({ page }) => {
  const stub = await openManager(page)

  await choose(page, ['a.txt', 'b.txt'])

  const queue = page.getByRole('region', { name: 'Upload queue' })
  await expect(queue.getByText('Uploaded', { exact: true })).toHaveCount(2)
  expect(stub.uploads.map((upload) => {return upload.path}).sort()).toEqual(['a.txt', 'b.txt'])
  expect(stub.uploads.every((upload) => {return !upload.overwrite})).toBe(true)
  // The request really carried the bytes; a stub that counted zero would hide an empty body.
  expect(stub.uploads.every((upload) => {return upload.bytes > 0})).toBe(true)
})

test('A file that already exists is refused with the server sentence, and Replace sends it over the old one', async ({
  page,
}) => {
  const stub = await openManager(page)
  stub.existing.add('taken.txt')

  await choose(page, ['taken.txt'])

  const queue = page.getByRole('region', { name: 'Upload queue' })
  await expect(queue.getByText('A file with that name already exists.')).toBeVisible()
  await queue.getByRole('button', { name: 'Replace the existing taken.txt with this upload' }).click()

  await expect(queue.getByText('Uploaded', { exact: true })).toBeVisible()
  expect(stub.uploads.map((upload) => {return upload.overwrite})).toEqual([false, true])
})

test('A running upload can be cancelled, and a cancelled one can be removed', async ({ page }) => {
  const stub = await openManager(page)
  stub.held.add('big.bin')

  await choose(page, ['big.bin'])

  const queue = page.getByRole('region', { name: 'Upload queue' })
  await expect(queue.getByText('Uploading', { exact: true })).toBeVisible()
  await queue.getByRole('button', { name: 'Cancel the upload of big.bin' }).click()
  await expect(queue.getByText('Cancelled', { exact: true })).toBeVisible()

  await queue.getByRole('button', { name: 'Remove big.bin from the list' }).click()
  await expect(page.getByRole('region', { name: 'Upload queue' })).toHaveCount(0)
  stub.releaseUploads()
})

test('Clear finished empties the queue of everything that is over', async ({ page }) => {
  await openManager(page)

  await choose(page, ['one.txt', 'two.txt'])
  const queue = page.getByRole('region', { name: 'Upload queue' })
  await expect(queue.getByText('Uploaded', { exact: true })).toHaveCount(2)

  await queue.getByRole('button', { name: 'Clear finished' }).click()

  await expect(page.getByRole('region', { name: 'Upload queue' })).toHaveCount(0)
})
