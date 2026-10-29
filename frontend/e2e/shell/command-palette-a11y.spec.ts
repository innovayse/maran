import { expect, test, type Page } from '@playwright/test'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import type { PanelModule } from '../../src/types/module'

/**
 * Protects the command palette's keyboard and screen-reader contract (issue #82): the search field
 * is a combobox that owns the result list, the arrow keys move `aria-activedescendant` along the
 * options while focus never leaves the field, and the active option stays visible inside the
 * scrolling list.
 *
 * Why it exists: the palette's selection is invisible to the browser's own focus handling, so
 * everything a screen reader hears and everything a keyboard user sees is carried by a handful of
 * attributes and one `scrollIntoView` call. A regression in any of them leaves the palette looking
 * fine to a mouse user while a keyboard user selects rows they cannot see, and was found only by
 * looking at a screenshot.
 */

/** Many modules, so the palette's result list is longer than its own height and must scroll. */
const MODULES: PanelModule[] = [
  'accounts',
  'sites',
  'databases',
  'sftp',
  'ftp',
  'cron',
  'firewall',
  'backups',
  'monitoring',
  'tasks',
  'mail',
  'dns',
  'analytics',
  'reports',
  'security',
  'billing',
].map((name) => {
  return { name, displayName: `Module ${name}`, tier: 'included', isEnabled: true }
})

/**
 * Opens the palette on a signed-in administrator's panel.
 * @param page The Playwright page to drive.
 * @returns Resolves once the palette dialog is on screen.
 */
const openPalette = async (page: Page): Promise<void> => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, MODULES)

  await page.goto('/')
  await page.getByRole('button', { name: 'Search or jump to…' }).click()
  await expect(page.getByRole('dialog')).toBeVisible()
}

test('the palette search field is a combobox that controls and announces its result list', async ({
  page,
}) => {
  await openPalette(page)

  const field = page.getByRole('dialog').getByRole('combobox')
  await field.fill('Module')

  const listbox = page.getByRole('dialog').getByRole('listbox')
  await expect(listbox).toBeVisible()
  expect(await page.getByRole('dialog').getByRole('option').count()).toBeGreaterThan(3)

  await expect(field).toHaveAttribute('aria-expanded', 'true')
  const listId = await listbox.getAttribute('id')
  expect(listId).toBeTruthy()
  await expect(field).toHaveAttribute('aria-controls', listId ?? '')
})

test('ArrowDown moves the active descendant to the next option and marks it selected', async ({ page }) => {
  await openPalette(page)

  const field = page.getByRole('dialog').getByRole('combobox')
  await field.fill('Module')
  const options = page.getByRole('dialog').getByRole('option')
  await expect(options.first()).toHaveAttribute('aria-selected', 'true')

  const firstId = await options.nth(0).getAttribute('id')
  const secondId = await options.nth(1).getAttribute('id')
  expect(firstId).toBeTruthy()
  expect(secondId).toBeTruthy()
  expect(secondId).not.toBe(firstId)
  await expect(field).toHaveAttribute('aria-activedescendant', firstId ?? '')

  await page.keyboard.press('ArrowDown')

  await expect(field).toHaveAttribute('aria-activedescendant', secondId ?? '')
  await expect(options.nth(1)).toHaveAttribute('aria-selected', 'true')
  await expect(options.nth(0)).toHaveAttribute('aria-selected', 'false')
  await expect(field).toBeFocused()
})

test('the active option is scrolled into view inside the list as ArrowDown walks past its end', async ({
  page,
}) => {
  await openPalette(page)

  const field = page.getByRole('dialog').getByRole('combobox')
  await field.fill('Module')
  const listbox = page.getByRole('dialog').getByRole('listbox')
  const options = page.getByRole('dialog').getByRole('option')

  // The premise: the list really overflows, otherwise "scrolled into view" proves nothing.
  const overflows = await listbox.evaluate((element) => {
    return element.scrollHeight > element.clientHeight
  })
  expect(overflows, 'the result list does not overflow its box').toBe(true)

  const total = await options.count()
  for (let press = 0; press < total - 1; press += 1) {
    await page.keyboard.press('ArrowDown')
  }

  const active = options.nth(total - 1)
  await expect(active).toHaveAttribute('aria-selected', 'true')
  await expect(field).toHaveAttribute('aria-activedescendant', (await active.getAttribute('id')) ?? '')

  const box = await listbox.boundingBox()
  const optionBox = await active.boundingBox()
  expect(box).not.toBeNull()
  expect(optionBox).not.toBeNull()
  expect(optionBox?.y ?? 0).toBeGreaterThanOrEqual((box?.y ?? 0) - 1)
  expect((optionBox?.y ?? 0) + (optionBox?.height ?? 0)).toBeLessThanOrEqual(
    (box?.y ?? 0) + (box?.height ?? 0) + 1,
  )
})
