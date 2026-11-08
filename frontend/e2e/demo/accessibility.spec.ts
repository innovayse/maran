import { expect, test } from '@playwright/test'
import { expectNoSeriousViolations } from '../fixtures/expect-no-serious-violations'
import { signInAs } from './sign-in-as'

// axe over the two screens the demo adds chrome to (issue #95, rules/vue.md "Accessibility"): the
// sign-in screen with the banner and the published logins, and a populated page under the banner.
// It lives here and not in e2e/accessibility/ because the everyday suite builds the normal bundle,
// which has neither. Run only through playwright.demo.config.ts against a running demo.

for (const theme of ['dark', 'light'] as const) {
  test.describe(`in the ${theme} theme`, () => {
    test.use({ colorScheme: theme })

    test('the sign-in screen with the banner and the logins has no serious violation', async ({ page }) => {
      await page.goto('/login')
      await expect(page.getByTestId('demo-credentials')).toBeVisible()
      await expect(page.getByTestId('demo-banner')).toBeVisible()

      await expectNoSeriousViolations(page)
    })

    test('a populated page under the banner has no serious violation', async ({ page }) => {
      await signInAs(page, 'administrator')
      await page.goto('/sites')
      await expect(page.getByRole('main').getByRole('row').nth(1)).toBeVisible()
      await expect(page.getByTestId('demo-banner')).toBeVisible()

      await expectNoSeriousViolations(page)
    })
  })
}
