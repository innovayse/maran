import { expect, test } from '@playwright/test'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubEmptyModules } from '../fixtures/stub-modules-route'

// Issue #217, point 3: on a phone the theme switch was an unlabelled sun in the drawer, and on the
// desktop the button named the theme in force, so nobody could tell what clicking would do.

test('at 390px the drawer holds a visible, labelled theme switch that works', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 })
  await page.addInitScript((): void => {
    window.localStorage.setItem('maran.theme', 'dark')
  })
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubEmptyModules(page)
  await page.goto('/')

  await page.getByRole('button', { name: 'Open the navigation' }).click()
  const drawer = page.getByRole('dialog', { name: 'Navigation' })
  const toggle = drawer.getByRole('button', { name: 'Switch to light' })
  await expect(toggle).toBeVisible()
  await expect(toggle.getByText('Switch to light')).toBeVisible()

  await toggle.click()
  await expect(page.locator('html')).toHaveAttribute('data-theme', 'light')
  await expect(drawer.getByRole('button', { name: 'Switch to dark' })).toBeVisible()
})

test('on the desktop the theme button says what clicking will do', async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 })
  await page.addInitScript((): void => {
    window.localStorage.setItem('maran.theme', 'dark')
  })
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubEmptyModules(page)
  await page.goto('/')

  const banner = page.getByRole('banner')
  const toggle = banner.getByRole('button', { name: 'Switch to light' })
  await expect(toggle).toBeVisible()
  await expect(toggle.getByText('Switch to light')).toBeVisible()

  await toggle.click()
  await expect(page.locator('html')).toHaveAttribute('data-theme', 'light')
  await expect(banner.getByRole('button', { name: 'Switch to dark' })).toBeVisible()
})
