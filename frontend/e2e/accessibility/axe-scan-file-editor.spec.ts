import { expectNoSeriousViolations } from '../fixtures/expect-no-serious-violations'
import { expectEditorText } from '../fixtures/code-editor-text'
import { expect, test } from '../fixtures/guarded-test'
import { openFileEditor } from '../fixtures/open-file-editor'
import { stubShellReads } from '../fixtures/stub-shell-reads'

// Issue #503: axe runs on the code editor with highlighted text and its search panel open, in both
// themes. The syntax colours are tokens whose contrast was measured on the field fill, and a scan
// with real tokens in the text is what proves the measurement holds in the browser.

const SOURCE = '<?php\n$name = "maran"; // greeting\necho strlen($name) + 42;\n'

for (const theme of ['dark', 'light'] as const) {
  test.describe(`in the ${theme} theme`, () => {
    test.use({ colorScheme: theme })

    test('the code editor with highlighted text has no serious accessibility violation', async ({ page }) => {
      await page.setViewportSize({ width: 1440, height: 900 })
      await stubShellReads(page)
      await openFileEditor(page, { path: 'www/index.php', content: SOURCE })
      const box = page.getByRole('textbox', { name: 'File contents' })
      await expectEditorText(box, SOURCE)
      await expect(box.locator('.tok-keyword').first()).toBeVisible()

      await expectNoSeriousViolations(page)
    })

    test('the editor with its search panel open has no serious accessibility violation', async ({ page }) => {
      await page.setViewportSize({ width: 1440, height: 900 })
      await stubShellReads(page)
      await openFileEditor(page, { path: 'www/index.php', content: SOURCE })
      await expectEditorText(page.getByRole('textbox', { name: 'File contents' }), SOURCE)

      await page.getByRole('button', { name: 'Find and replace' }).click()
      await expect(page.getByRole('textbox', { name: 'Find' })).toBeVisible()

      await expectNoSeriousViolations(page)
    })
  })
}
