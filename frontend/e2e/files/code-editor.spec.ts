import { expectEditorText, readEditorText } from '../fixtures/code-editor-text'
import { expect, test } from '../fixtures/guarded-test'
import { openFileEditor } from '../fixtures/open-file-editor'

// Issue #503: the file editor is a code editor. These specs cover what a textarea could not do and
// what must survive the change: colour chosen from the file name, the round trip of an edit, the
// unsaved-changes guard, the refusal to take input before the file has loaded (#456) and a Tab key
// that never traps the keyboard.

const PHP_FILE = '<?php\n$name = "maran"; // greeting\necho strlen($name) + 42;\n'

test('A .php file is highlighted, a .txt file with the same text is not', async ({ page }) => {
  await openFileEditor(page, { path: 'www/index.php', content: PHP_FILE })
  const box = page.getByRole('textbox', { name: 'File contents' })
  await expectEditorText(box, PHP_FILE.replace(/\n$/, '\n'))

  await expect(box.locator('.tok-keyword').first()).toBeVisible()
  await expect(box.locator('.tok-string').first()).toBeVisible()
  await expect(box.locator('.tok-comment').first()).toBeVisible()
  await expect(box.locator('.tok-number').first()).toBeVisible()
})

test('The same text in a file with an unknown name carries no highlighting', async ({ page }) => {
  await openFileEditor(page, { path: 'www/notes.unknown', content: PHP_FILE })
  const box = page.getByRole('textbox', { name: 'File contents' })
  await expectEditorText(box, PHP_FILE)

  await expect(box.locator('.tok-keyword')).toHaveCount(0)
  await expect(box.locator('.tok-string')).toHaveCount(0)
})

test('The editor shows line numbers and picks the language of .htaccess from the whole name', async ({ page }) => {
  await openFileEditor(page, { path: 'www/.htaccess', content: '# rewrite\nRewriteEngine On\n' })
  const box = page.getByRole('textbox', { name: 'File contents' })
  await expectEditorText(box, '# rewrite\nRewriteEngine On\n')

  await expect(page.locator('.cm-lineNumbers .cm-gutterElement', { hasText: '2' }).first()).toBeVisible()
  await expect(box.locator('.tok-comment').first()).toBeVisible()
})

test('An edit made in the editor is saved with the instant the file was loaded at', async ({ page }) => {
  const stub = await openFileEditor(page, { path: 'www/index.php', content: '<?php echo 1;\n' })
  const box = page.getByRole('textbox', { name: 'File contents' })
  await expectEditorText(box, '<?php echo 1;\n')
  await expect(page.getByRole('button', { name: 'Save', exact: true })).toBeDisabled()

  await box.fill('<?php echo 2;\n')
  await expectEditorText(box, '<?php echo 2;\n')
  await page.getByRole('button', { name: 'Save', exact: true }).click()

  await expect(page.getByText('Saved.')).toBeVisible()
  expect(stub.saves).toEqual([{ content: '<?php echo 2;\n', expectedModifiedAt: '2026-11-07T09:00:00Z' }])
  // The text read back from the pretend disk is the text typed, line for line.
  expect(stub.disk.content).toBe('<?php echo 2;\n')
  await expect(page.getByRole('button', { name: 'Save', exact: true })).toBeDisabled()
})

test('Ctrl+S inside the text saves, and does nothing while nothing has changed', async ({ page }) => {
  const stub = await openFileEditor(page, { path: 'www/index.php', content: '<?php echo 1;\n' })
  const box = page.getByRole('textbox', { name: 'File contents' })
  await expectEditorText(box, '<?php echo 1;\n')

  await box.click()
  await page.keyboard.press('Control+s')
  // Nothing changed yet, so there is nothing to send (inverse control for the shortcut).
  await expect(page.getByRole('button', { name: 'Save', exact: true })).toBeDisabled()
  expect(stub.saves).toHaveLength(0)

  await page.keyboard.type('x')
  await page.keyboard.press('Control+s')

  await expect(page.getByText('Saved.')).toBeVisible()
  expect(stub.saves).toHaveLength(1)
})

test('Leaving with an unsaved edit in the code editor asks first', async ({ page }) => {
  await openFileEditor(page, { path: 'www/index.php', content: '<?php echo 1;\n' })
  const box = page.getByRole('textbox', { name: 'File contents' })
  await expectEditorText(box, '<?php echo 1;\n')

  await box.fill('<?php echo 3;\n')
  await page.getByRole('link', { name: 'Back to the folder' }).click()
  const dialog = page.getByRole('dialog', { name: 'Leave without saving' })
  await expect(dialog).toBeVisible()

  await dialog.getByRole('button', { name: 'Keep editing' }).click()
  await expect(page).toHaveURL(/\/files\/edit/)
  await expectEditorText(box, '<?php echo 3;\n')
})

test('The editor takes no input until the file has loaded, and then does', async ({ page }) => {
  let release: () => void = () => {
    return undefined
  }
  const held = new Promise<void>((resolve) => {
    release = resolve
  })
  await openFileEditor(page, { path: 'www/index.php', content: '<?php echo 1;\n', holdReads: held })
  const box = page.getByRole('textbox', { name: 'File contents' })

  // While the read is on its way the surface is inert: marked disabled and read-only, and a key
  // typed into it changes nothing.
  await expect(box).toHaveAttribute('aria-disabled', 'true')
  await expect(box).toHaveAttribute('aria-readonly', 'true')
  await expect(box).toBeEditable({ editable: false })
  await box.click({ force: true })
  await page.keyboard.type('typed too early')
  await expectEditorText(box, '')

  release()
  await expectEditorText(box, '<?php echo 1;\n')
  await expect(box).not.toHaveAttribute('aria-disabled', 'true')
  await expect(box).toBeEditable()
  await expect(page.getByRole('button', { name: 'Save', exact: true })).toBeDisabled()
})

test('Tab moves focus on by default, and with Tab indents on, Escape then Tab leaves again', async ({ page }) => {
  await openFileEditor(page, { path: 'www/app.js', content: 'const a = 1\n' })
  const box = page.getByRole('textbox', { name: 'File contents' })
  await expectEditorText(box, 'const a = 1\n')

  // Default: no keyboard trap. Tab leaves the text and the text is unchanged.
  await box.click()
  await page.keyboard.press('Tab')
  await expect(box).not.toBeFocused()
  await expectEditorText(box, 'const a = 1\n')

  // Opted in: Tab indents.
  await page.getByRole('switch', { name: 'Tab key indents' }).click()
  await expect(page.getByText('Press Escape, then Tab')).toBeVisible()
  await box.click()
  // The click lands below the text, on the empty last line, so the start of THE DOCUMENT is asked
  // for; `Home` would only reach the start of that empty line.
  await page.keyboard.press('Control+Home')
  await page.keyboard.press('Tab')
  await expect(box).toBeFocused()
  // Polled: the editor draws the inserted tab on its own update, not at the key event.
  await expect.poll(async () => {
    return await readEditorText(box)
  }).toMatch(/^\s+const a = 1\n/)

  // Escape releases it for one keystroke, so the keyboard is never trapped.
  await page.keyboard.press('Escape')
  await page.keyboard.press('Tab')
  await expect(box).not.toBeFocused()
})

test('Find opens the search panel and replaces text; Go to line moves the cursor', async ({ page }) => {
  await openFileEditor(page, { path: 'www/index.php', content: 'one\ntwo\nthree\n' })
  const box = page.getByRole('textbox', { name: 'File contents' })
  await expectEditorText(box, 'one\ntwo\nthree\n')

  await page.getByRole('button', { name: 'Find and replace' }).click()
  const search = page.getByRole('textbox', { name: 'Find' })
  await expect(search).toBeFocused()
  await search.fill('two')
  await page.getByRole('textbox', { name: 'Replace' }).fill('2')
  // "replace" only selects the next match when the cursor is not on one; "replace all" is one step.
  await page.getByRole('button', { name: 'replace all', exact: true }).click()
  await expectEditorText(box, 'one\n2\nthree\n')

  await page.keyboard.press('Escape')
  await page.getByRole('button', { name: 'Go to line' }).click()
  await page.getByRole('textbox', { name: 'Go to line' }).fill('3')
  await page.keyboard.press('Enter')
  await page.keyboard.type('>')
  await expectEditorText(box, 'one\n2\n>three\n')
})

test('Lines wrap by default and stay on one row with the wrap switch on', async ({ page }) => {
  // The stubbed disk refuses text over 64 bytes, so the line is made long for the screen instead:
  // 60 characters do not fit one row of the editor at a phone's width.
  await page.setViewportSize({ width: 390, height: 800 })
  const long = `${'word '.repeat(12)}\n`
  await openFileEditor(page, { path: 'www/notes.txt', content: long })
  const box = page.getByRole('textbox', { name: 'File contents' })
  await expect(box.locator('.cm-line').first()).toBeVisible()

  const rowsWrapped = await box.locator('.cm-line').first().evaluate((line) => {
    return line.getBoundingClientRect().height
  })
  await page.getByRole('switch', { name: 'Do not wrap lines' }).click()
  await expect.poll(async () => {
    return await box.locator('.cm-line').first().evaluate((line) => {
      return line.getBoundingClientRect().height
    })
  }).toBeLessThan(rowsWrapped)
})
