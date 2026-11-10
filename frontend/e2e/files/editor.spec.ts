import { expectEditorText } from '../fixtures/code-editor-text'
import { expect, test } from '../fixtures/guarded-test'
import { setPersistedLocale } from '../fixtures/set-locale'
import { expectNoHorizontalOverflow } from '../fixtures/measure-horizontal-extent'
import { INITIAL_FILE, openFileEditor } from '../fixtures/open-file-editor'
import type { FileEditorStub } from '../fixtures/stub-file-editor-routes'
import type { Page } from '@playwright/test'

const FILE_PATH = 'www/config.txt'

/** The address the 390px check below opens; `e2e/ui-kit/route-coverage.spec.ts` reads it from here. */
export const SWEPT_SCREEN = { path: '/files/edit?account=22222222-2222-2222-2222-222222222222&path=www%2Fconfig.txt' }

const INITIAL = INITIAL_FILE

/**
 * Opens the editor on the stubbed file the way a link from the file manager does.
 * @param page The page to drive.
 * @param content The text the pretend disk holds at the start.
 * @returns The stub, to read recorded saves from and to steer the disk with.
 */
const openEditor = async (page: Page, content: string = INITIAL.content): Promise<FileEditorStub> => {
  return await openFileEditor(page, { content, path: FILE_PATH })
}

test('The editor opens the file, keeps Save off until something changes, and sends the loaded instant on save', async ({
  page,
}) => {
  const stub = await openEditor(page)

  await expect(page.getByRole('heading', { level: 1, name: 'config.txt' })).toBeVisible()
  const text = page.getByRole('textbox', { name: 'File contents' })
  await expectEditorText(text, INITIAL.content)
  await expect(page.getByRole('button', { name: 'Save' })).toBeDisabled()

  await text.fill('debug = true\n')
  await page.getByRole('button', { name: 'Save' }).click()

  await expect(page.getByText('Saved.')).toBeVisible()
  expect(stub.saves).toEqual([{ content: 'debug = true\n', expectedModifiedAt: INITIAL.modifiedAt }])
  await expect(page.getByRole('button', { name: 'Save' })).toBeDisabled()
})

test('A save over a file that changed on disk is refused and the person chooses how to go on', async ({
  page,
  guard,
}) => {
  guard.allow(409, /\/api\/v1\/files\/content(\?.*)?$/)
  const stub = await openEditor(page)
  const text = page.getByRole('textbox', { name: 'File contents' })
  await expectEditorText(text, INITIAL.content)

  // Someone else saves the file while this tab is open.
  stub.disk = { ...stub.disk, content: 'theirs\n', modifiedAt: '2026-11-07T09:30:00Z' }

  await text.fill('mine\n')
  await page.getByRole('button', { name: 'Save', exact: true }).click()
  await expect(page.getByText('The file changed on disk', { exact: false }).first()).toBeVisible()
  // Nothing was written: the disk still holds the other person's text.
  expect(stub.disk.content).toBe('theirs\n')

  await page.getByRole('button', { name: 'Save mine anyway' }).click()
  await expect(page.getByText('Saved.')).toBeVisible()
  expect(stub.disk.content).toBe('mine\n')
  expect(stub.saves[1].expectedModifiedAt).toBeUndefined()
})

test('Reloading after a conflict shows the version on disk and drops the edit', async ({ page, guard }) => {
  guard.allow(409, /\/api\/v1\/files\/content(\?.*)?$/)
  const stub = await openEditor(page)
  const text = page.getByRole('textbox', { name: 'File contents' })
  await expectEditorText(text, INITIAL.content)
  stub.disk = { ...stub.disk, content: 'theirs\n', modifiedAt: '2026-11-07T09:30:00Z' }

  await text.fill('mine\n')
  await page.getByRole('button', { name: 'Save', exact: true }).click()
  await page.getByRole('button', { name: 'Reload and discard my edits' }).click()

  await expectEditorText(text, 'theirs\n')
  await expect(page.getByRole('button', { name: 'Save', exact: true })).toBeDisabled()
})

test('Text over the server limit cannot be saved and says why', async ({ page }) => {
  const stub = await openEditor(page)
  const text = page.getByRole('textbox', { name: 'File contents' })
  await expectEditorText(text, INITIAL.content)

  await text.fill('x'.repeat(200))

  await expect(page.getByText('The text is over the 64 B limit.', { exact: false })).toBeVisible()
  await expect(page.getByRole('button', { name: 'Save', exact: true })).toBeDisabled()
  expect(stub.saves).toHaveLength(0)
})

test('A file the server will not open shows its sentence and offers the download instead', async ({ page, guard }) => {
  guard.allow(422, /\/api\/v1\/files\/content(\?.*)?$/)
  await openEditor(page, 'x'.repeat(200))

  await expect(page.getByText('This file is too large to edit here.')).toBeVisible()
  await expect(page.getByRole('textbox', { name: 'File contents' })).toHaveCount(0)
  await expect(page.getByRole('button', { name: 'Download config.txt' })).toBeVisible()
})

test('Download hands the stored file to the browser under the name the panel proposed', async ({ page }) => {
  const stub = await openEditor(page)
  await expectEditorText(page.getByRole('textbox', { name: 'File contents' }), INITIAL.content)

  const downloading = page.waitForEvent('download')
  await page.getByRole('button', { name: 'Download config.txt' }).click()
  const download = await downloading

  expect(download.suggestedFilename()).toBe('config.txt')
  expect(stub.downloads).toEqual([FILE_PATH])
})

test('Leaving with unsaved edits asks first, and Keep editing stays on the file', async ({ page }) => {
  await openEditor(page)
  const text = page.getByRole('textbox', { name: 'File contents' })
  await text.fill('changed\n')

  await page.getByRole('link', { name: 'Back to the folder' }).click()
  const dialog = page.getByRole('dialog', { name: 'Leave without saving' })
  await expect(dialog).toBeVisible()

  await dialog.getByRole('button', { name: 'Keep editing' }).click()
  await expect(dialog).toBeHidden()
  await expectEditorText(text, 'changed\n')
  await expect(page).toHaveURL(/\/files\/edit/)

  await page.getByRole('link', { name: 'Back to the folder' }).click()
  await page.getByRole('dialog', { name: 'Leave without saving' }).getByRole('button', { name: 'Leave without saving' }).click()
  await expect(page).not.toHaveURL(/\/files\/edit/)
})

test('Leaving a file with nothing unsaved asks nothing', async ({ page }) => {
  await openEditor(page)
  await expectEditorText(page.getByRole('textbox', { name: 'File contents' }), INITIAL.content)

  await page.getByRole('link', { name: 'Back to the folder' }).click()

  await expect(page).not.toHaveURL(/\/files\/edit/)
})

for (const [locale, save, label] of [
  ['ru', 'Сохранить', 'Содержимое файла'],
  ['hy', 'Պահպանել', 'Ֆայլի պարունակությունը'],
] as const) {
  test(`The editor speaks ${locale}, and nothing scrolls sideways at 390px`, async ({ page }) => {
    await setPersistedLocale(page, locale)
    await page.setViewportSize({ width: 390, height: 800 })
    await openEditor(page)

    await expectEditorText(page.getByRole('textbox', { name: label }), INITIAL.content)
    await expect(page.getByRole('button', { name: save, exact: true })).toBeVisible()
    await expectNoHorizontalOverflow(page)
  })
}

test('The editor fits a 390px screen in English', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 800 })
  await openEditor(page)
  await expectEditorText(page.getByRole('textbox', { name: 'File contents' }), INITIAL.content)

  await expectNoHorizontalOverflow(page)
})
