import { expect, test } from '../fixtures/guarded-test'
import { openApps } from '../fixtures/open-apps'
import { appsWorld } from '../fixtures/stub-apps-routes'
import { stubPhpVersions } from '../fixtures/stub-sites-routes'

// Issue #517: a PHP version is an app. The PHP versions page reports what is installed and sends the
// administrator to the app store instead of installing on its own.

test('the PHP versions page has no install control of its own and points to the app store', async ({ page }) => {
  await openApps(page, appsWorld(), {
    path: '/settings/php',
    prepare: async (target): Promise<void> => {
      await stubPhpVersions(target, [{ version: '8.3', defaultStatus: 'default' }])
    },
  })

  await expect(page.getByRole('row', { name: /8\.3/ })).toBeVisible()
  await expect(page.getByRole('button', { name: /Install/ })).toHaveCount(0)

  await page.getByRole('link', { name: 'Open the app store' }).click()
  await expect(page).toHaveURL(/\/apps$/)
})
