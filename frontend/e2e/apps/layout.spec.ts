import { expect, test } from '../fixtures/guarded-test'
import { expectNoHorizontalOverflow } from '../fixtures/measure-horizontal-extent'
import { openApps } from '../fixtures/open-apps'
import { setPersistedLocale } from '../fixtures/set-locale'
import { appsWorld, hostApp, installableApp } from '../fixtures/stub-apps-routes'
import { EMPTY_STACK } from '../fixtures/stub-stack-routes'
import type { AuthenticatedUser } from '../../src/types/auth'

// Issue #517: the app store, the setup wizard and the "being set up" screen fit a 390 px screen in
// every language. Armenian labels are the long ones. The content is stocked with a long app name
// because that is what makes an over-wide table or card show. `e2e/ui-kit/route-coverage.spec.ts`
// reads the `path:` rows below as this file's three swept screens.

/** The screens this file opens at 390 px. */
const SWEPT_SCREENS = [{ path: '/apps' }, { path: '/setup/stack' }, { path: '/setup/pending' }]

/** The languages the panel ships. */
const LANGUAGES = ['en', 'ru', 'hy'] as const

/** An app name long enough to wrap on a phone. */
const LONG_NAME = 'PHP 8.3 with a deliberately very long application name for the narrow screen'

/** A customer, who is held on the "being set up" screen. */
const CUSTOMER: AuthenticatedUser = {
  id: '00000000-0000-0000-0000-0000000000c1',
  username: 'carol',
  email: 'carol@example.com',
  role: 'customer',
  accountId: '22222222-2222-2222-2222-222222222222',
}

for (const code of LANGUAGES) {
  test.describe(`in ${code}`, () => {
    test(`the app store fits a 390px screen (${code})`, async ({ page }) => {
      await setPersistedLocale(page, code)
      await openApps(
        page,
        appsWorld({
          installed: [hostApp('php-8.3', LONG_NAME), hostApp('nginx', 'nginx')],
          installable: [installableApp('nodejs-22', LONG_NAME)],
        }),
        { path: SWEPT_SCREENS[0]?.path },
      )
      await page.setViewportSize({ width: 390, height: 844 })

      await expect(page.getByRole('table').first()).toBeVisible()
      await expectNoHorizontalOverflow(page)
    })

    test(`the setup wizard fits a 390px screen (${code})`, async ({ page }) => {
      await setPersistedLocale(page, code)
      await openApps(page, appsWorld(), { path: SWEPT_SCREENS[1]?.path, stack: EMPTY_STACK })
      await page.setViewportSize({ width: 390, height: 844 })

      await expect(page.getByRole('radio').first()).toBeVisible()
      await expectNoHorizontalOverflow(page)
    })

    test(`the "being set up" screen fits a 390px screen (${code})`, async ({ page }) => {
      await setPersistedLocale(page, code)
      await openApps(page, appsWorld(), { path: SWEPT_SCREENS[2]?.path, stack: EMPTY_STACK, user: CUSTOMER })
      await page.setViewportSize({ width: 390, height: 844 })

      await expect(page.getByRole('heading', { level: 1 })).toBeVisible()
      await expectNoHorizontalOverflow(page)
    })
  })
}
