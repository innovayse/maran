import { expect, test } from '../fixtures/guarded-test'
import { GOOD_SCRIPT, MALFORMED_SCRIPT } from '../fixtures/module-frame-scripts'
import { expectNoHorizontalOverflow } from '../fixtures/measure-horizontal-extent'
import { openPackagedShell } from '../fixtures/open-packaged-module'
import { PACKAGED_ID, PACKAGED_TEXT, type SpecLocale } from '../fixtures/stub-packaged-module'

// The phone-width contract (rules/vue.md "Accessibility"): no screen scrolls sideways at 390px, in
// any of the three languages. A module's screen has two shapes the shell draws, the running frame
// and the unavailable panel, and the Armenian and Russian texts are the long ones.

/** The phone viewport the criterion names. */
const PHONE = { width: 390, height: 844 }

/** The interface languages the panel ships. */
const LOCALES: readonly SpecLocale[] = ['en', 'ru', 'hy']

/** The retry button's name per language (`app.modules.retry`). */
const RETRY: Readonly<Record<SpecLocale, string>> = { en: 'Try again', ru: 'Повторить', hy: 'Կրկին փորձել' }

for (const locale of LOCALES) {
  test(`a running module screen does not scroll sideways at 390px in ${locale}`, async ({ page }) => {
    await openPackagedShell(page, {
      script: GOOD_SCRIPT,
      locale,
      viewport: PHONE,
      path: `/m/${PACKAGED_ID}/hello`,
      stubs: async (stubbed): Promise<void> => {
        await stubbed.route('**/api/v1/hello/greeting', async (route) => {
          await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ greeting: 'Hello' }) })
        })
      },
    })

    await expect(page.getByRole('heading', { level: 1, name: PACKAGED_TEXT[locale].title })).toBeVisible()
    await expect(page.frameLocator('iframe').getByText(`init locale:${locale}`)).toBeVisible()
    await expectNoHorizontalOverflow(page)
  })

  test(`the unavailable panel does not scroll sideways at 390px in ${locale}`, async ({ page }) => {
    await openPackagedShell(page, {
      script: MALFORMED_SCRIPT,
      locale,
      viewport: PHONE,
      path: `/m/${PACKAGED_ID}/hello`,
    })

    await expect(page.getByRole('button', { name: RETRY[locale] })).toBeVisible()
    await expect(page.locator('iframe')).toHaveCount(0)
    await expectNoHorizontalOverflow(page)
  })
}
