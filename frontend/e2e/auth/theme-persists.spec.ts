import { expect, test } from '@playwright/test'

/**
 * Signed-out pages must honour the persisted theme. The theme store used to be created only by the
 * authenticated shell, so login and its siblings rendered the baseline dark whatever was stored.
 * No API stub is needed: `/login` renders before any request that matters to this check.
 */
for (const theme of ['light', 'dark'] as const) {
  test(`/login renders in the stored ${theme} theme`, async ({ page }) => {
    await page.addInitScript((stored: string) => {
      window.localStorage.setItem('maran.theme', stored)
    }, theme)

    await page.goto('/login')

    await expect(page.locator('html')).toHaveAttribute('data-theme', theme)
  })
}

test('/login follows the system preference when nothing is stored', async ({ page }) => {
  await page.emulateMedia({ colorScheme: 'light' })

  await page.goto('/login')

  await expect(page.locator('html')).toHaveAttribute('data-theme', 'light')
})
