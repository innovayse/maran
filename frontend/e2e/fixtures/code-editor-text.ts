import { expect, type Locator } from '@playwright/test'

/**
 * Reads the text of a code editor the way a person sees it: one line per `.cm-line`, joined with
 * newlines. A content-editable surface has no `value`, so `toHaveValue` cannot be used on it.
 * @param box The editor's text box (role textbox).
 * @returns The whole document.
 */
export const readEditorText = async (box: Locator): Promise<string> => {
  return await box.evaluate((element) => {
    return Array.from(element.querySelectorAll('.cm-line'))
      .map((line) => {
        return line.textContent ?? ''
      })
      .join('\n')
  })
}

/**
 * Waits until the editor shows exactly this text.
 * @param box The editor's text box (role textbox).
 * @param expected The document it must hold.
 * @returns Resolves once the text matches, or fails after the expect timeout.
 */
export const expectEditorText = async (box: Locator, expected: string): Promise<void> => {
  await expect.poll(async () => {
    return await readEditorText(box)
  }).toBe(expected)
}
