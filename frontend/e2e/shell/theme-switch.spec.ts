import { expect, test, type Locator, type Page } from '@playwright/test'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubEmptyModules } from '../fixtures/stub-modules-route'

// Issue #217, point 3: one labelled theme control — the mode in force with its icon, named
// "Theme: <mode>" — opening a menu of Light, Dark and System, the same in the app bar on the
// desktop and in the navigation drawer on a phone.

/**
 * Opens the signed-in shell with a stored theme preference.
 * @param page The page under test.
 * @param stored The preference persisted before the app boots.
 * @returns Resolves once the shell has loaded.
 */
const openShell = async (page: Page, stored: string): Promise<void> => {
  await page.addInitScript((value: string): void => {
    window.localStorage.setItem('maran.theme', value)
  }, stored)
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubEmptyModules(page)
  await page.goto('/')
}

/**
 * The theme menu inside a container, by its accessible name.
 * @param scope Where the control lives (the banner or the drawer).
 * @param mode The mode the trigger is expected to name.
 * @returns The trigger button.
 */
const themeTrigger = (scope: Locator, mode: string): Locator => {
  return scope.getByRole('button', { name: `Theme: ${mode}`, exact: true })
}

test('on the desktop the app bar holds one labelled theme menu, usable by mouse', async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 })
  await openShell(page, 'dark')

  const banner = page.getByRole('banner')
  const trigger = themeTrigger(banner, 'Dark')
  await expect(trigger).toBeVisible()
  await expect(trigger).toHaveAttribute('aria-haspopup', 'menu')
  await expect(trigger).toHaveAttribute('aria-expanded', 'false')
  await expect(trigger.getByText('Dark', { exact: true })).toBeVisible()
  // Exactly one theme control: the old 0-width switch must be gone.
  await expect(banner.getByRole('button', { name: /^Theme: / })).toHaveCount(1)
  await expect(banner.getByRole('button', { name: /^Switch to / })).toHaveCount(0)

  await trigger.click()
  await expect(trigger).toHaveAttribute('aria-expanded', 'true')
  const menu = page.getByRole('menu')
  await expect(menu.getByRole('menuitemradio')).toHaveText(['Light', 'Dark', 'System'])
  await expect(menu.getByRole('menuitemradio', { name: 'Dark' })).toHaveAttribute('aria-checked', 'true')

  await menu.getByRole('menuitemradio', { name: 'Light' }).click()
  await expect(page.locator('html')).toHaveAttribute('data-theme', 'light')
  await expect(themeTrigger(banner, 'Light')).toBeVisible()
  expect(await page.evaluate(() => {return window.localStorage.getItem('maran.theme')})).toBe('light')
})

test('the desktop theme menu works by keyboard and lands on the mode in force', async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 })
  await openShell(page, 'light')

  const trigger = themeTrigger(page.getByRole('banner'), 'Light')
  await trigger.focus()
  await page.keyboard.press('Enter')
  await expect(page.getByRole('menuitemradio', { name: 'Light' })).toBeFocused()

  await page.keyboard.press('ArrowDown')
  await page.keyboard.press('ArrowDown')
  await expect(page.getByRole('menuitemradio', { name: 'System' })).toBeFocused()
  await page.keyboard.press('Enter')

  await expect(themeTrigger(page.getByRole('banner'), 'System')).toBeFocused()
  expect(await page.evaluate(() => {return window.localStorage.getItem('maran.theme')})).toBe('system')

  await page.keyboard.press('Enter')
  await page.keyboard.press('Escape')
  await expect(page.getByRole('menu')).toHaveCount(0)
  await expect(themeTrigger(page.getByRole('banner'), 'System')).toBeFocused()
})

test('System follows the operating system preference, and keeps following it', async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 })
  await page.emulateMedia({ colorScheme: 'light' })
  await openShell(page, 'system')

  await expect(page.locator('html')).toHaveAttribute('data-theme', 'light')
  await page.emulateMedia({ colorScheme: 'dark' })
  await expect(page.locator('html')).toHaveAttribute('data-theme', 'dark')
})

test('at 390px the drawer holds the same labelled theme menu, usable by keyboard', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 })
  await openShell(page, 'dark')

  await expect(page.getByRole('banner').getByRole('button', { name: /^Theme: / })).toHaveCount(0)

  await page.getByRole('button', { name: 'Open the navigation' }).click()
  const drawer = page.getByRole('dialog', { name: 'Navigation' })
  const trigger = themeTrigger(drawer, 'Dark')
  await expect(trigger).toBeVisible()
  await expect(trigger.getByText('Dark', { exact: true })).toBeVisible()
  // The drawer's head carries no unlabelled sun or moon beside it.
  await expect(drawer.getByRole('button', { name: /^Switch to / })).toHaveCount(0)

  await trigger.focus()
  await page.keyboard.press('Enter')
  await expect(page.getByRole('menuitemradio', { name: 'Dark' })).toBeFocused()
  await page.keyboard.press('ArrowUp')
  await expect(page.getByRole('menuitemradio', { name: 'Light' })).toBeFocused()
  await page.keyboard.press('Enter')

  await expect(page.locator('html')).toHaveAttribute('data-theme', 'light')
  await expect(themeTrigger(drawer, 'Light')).toBeFocused()

  await themeTrigger(drawer, 'Light').click()
  await page.getByRole('menuitemradio', { name: 'Dark' }).click()
  await expect(page.locator('html')).toHaveAttribute('data-theme', 'dark')
})
