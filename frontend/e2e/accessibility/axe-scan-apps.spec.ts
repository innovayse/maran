import { expectNoSeriousViolations } from '../fixtures/expect-no-serious-violations'
import { expect, test } from '../fixtures/guarded-test'
import { openApps } from '../fixtures/open-apps'
import { appsWorld, hostApp, installableApp } from '../fixtures/stub-apps-routes'
import { EMPTY_STACK } from '../fixtures/stub-stack-routes'
import type { AuthenticatedUser } from '../../src/types/auth'

// Issue #517 for the app store and the first-run screens (rules/vue.md: a new top-level list joins
// the axe scan): the app store with rows, the filter sheet, every step of the wizard and the "being
// set up" screen, in both themes, with real content in them. A contrast failure lives in one
// theme's tokens and an empty list draws no table at all.

/** A customer, who is held on the "being set up" screen. */
const CUSTOMER: AuthenticatedUser = {
  id: '00000000-0000-0000-0000-0000000000c1',
  username: 'carol',
  email: 'carol@example.com',
  role: 'customer',
  accountId: '22222222-2222-2222-2222-222222222222',
}

for (const theme of ['dark', 'light'] as const) {
  test.describe(`in the ${theme} theme`, () => {
    test.use({ colorScheme: theme })

    test('the app store has no serious accessibility violation', async ({ page }) => {
      await openApps(
        page,
        appsWorld({
          installed: [hostApp('nginx', 'nginx'), hostApp('php-8.3', 'PHP 8.3', { state: 'failed', stateName: 'Failed', serviceStatus: 'failed', serviceStatusName: 'Failed' })],
          installable: [installableApp('nodejs-22', 'Node.js 22')],
        }),
      )
      await expect(page.getByRole('table', { name: 'Apps installed on this server' }).getByRole('row', { name: /nginx/ })).toBeVisible()
      await expect(page.getByRole('table', { name: 'Apps this server can install' })).toBeVisible()

      await expectNoSeriousViolations(page)
    })

    test('the app store\'s filter sheet has no serious accessibility violation', async ({ page }) => {
      await openApps(page, appsWorld({ installed: [hostApp('nginx', 'nginx')] }))
      await page.getByRole('button', { name: 'Filters' }).click()
      await expect(page.getByRole('dialog')).toBeVisible()

      await expectNoSeriousViolations(page)
    })

    test('every step of the setup wizard has no serious accessibility violation', async ({ page }) => {
      await openApps(page, appsWorld(), { path: '/', stack: EMPTY_STACK })
      await expect(page.getByRole('radio', { name: 'nginx' })).toBeVisible()
      await expectNoSeriousViolations(page)

      await page.getByRole('radio', { name: 'nginx' }).check({ force: true })
      await page.getByRole('button', { name: 'Next' }).click()
      await expect(page.getByRole('checkbox', { name: 'PHP 8.3' })).toBeVisible()
      await expectNoSeriousViolations(page)

      await page.getByRole('checkbox', { name: 'PHP 8.3' }).check({ force: true })
      await page.getByRole('button', { name: 'Next' }).click()
      await expect(page.getByRole('button', { name: 'Install' })).toBeVisible()
      await expectNoSeriousViolations(page)
    })

    test('the "being set up" screen has no serious accessibility violation', async ({ page }) => {
      await openApps(page, appsWorld(), { path: '/', stack: EMPTY_STACK, user: CUSTOMER })
      await expect(page.getByText('This panel is being set up')).toBeVisible()

      await expectNoSeriousViolations(page)
    })
  })
}
