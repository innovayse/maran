import { expect, test } from '../fixtures/guarded-test'
import { holdRoute } from '../fixtures/hold-route'
import { PHP_SETTINGS_PATTERN, signInAsPhpCustomer, stubPhpSettings } from '../fixtures/stub-php-settings-route'

// The PHP settings page while its read is on the way, and when the read is refused (#138).

test('while the settings are on the way the page shows placeholders and one polite status, then the form', async ({
  page,
}) => {
  await signInAsPhpCustomer(page)
  await stubPhpSettings(page)
  const held = await holdRoute(page, PHP_SETTINGS_PATTERN)

  await page.goto('/sites/php-settings')
  await held.requested

  await expect(page.getByRole('heading', { level: 1, name: 'PHP settings' })).toBeVisible()
  await expect(page.getByRole('main').locator('[data-skeleton]').first()).toBeAttached()
  await expect(page.getByTestId('loading-status').filter({ hasText: 'Loading…' }).first()).toBeAttached()
  await expect(page.getByRole('button', { name: 'Save PHP settings' })).toHaveCount(0)

  held.release()

  await expect(page.getByLabel('Memory limit (MiB)')).toBeVisible()
  await expect(page.locator('[data-skeleton]')).toHaveCount(0)
  await expect(page.getByTestId('loading-status').filter({ hasText: 'Loading…' })).toHaveCount(0)
})

test('a refused read shows the backend sentence as an alert and no form', async ({ page, guard }) => {
  guard.allow(500, /\/api\/v1\/php-settings$/)
  const detail = 'Your PHP settings could not be read.'
  await signInAsPhpCustomer(page)
  await page.route(PHP_SETTINGS_PATTERN, async (route) => {
    await route.fulfill({
      status: 500,
      contentType: 'application/problem+json',
      body: JSON.stringify({ code: 'HostUnexpectedError', title: 'Unexpected error', detail }),
    })
  })

  await page.goto('/sites/php-settings')

  await expect(page.getByText(detail, { exact: true })).toBeVisible()
  await expect(page.locator('[data-skeleton]')).toHaveCount(0)
  await expect(page.getByRole('button', { name: 'Save PHP settings' })).toHaveCount(0)
})
