import { expect, test } from '@playwright/test'
import { expectNoHorizontalOverflow } from '../fixtures/measure-horizontal-extent'
import { setPersistedLocale } from '../fixtures/set-locale'
import { openScreen, prepareScreen, SCREENS } from '../screens/screen-list'

/*
 * Acceptance criterion of issue #82, tablet row: no screen extends sideways at 768 px, in any of
 * the three languages. The 390 px sweeps (`no-horizontal-scroll-admin.spec.ts`,
 * `no-horizontal-scroll-other.spec.ts`) cover the phone; the band from 640 to 1023 px is where the
 * drawer, the rail and the card tables are all in play at once, and until this spec only
 * `table-narrow.spec.ts` looked at it.
 *
 * It takes the same three measurements as the phone sweeps (`expectNoHorizontalOverflow`: the
 * document, the clipped main region and every element's box), over the screen table the screenshot
 * script also reads (`e2e/screens/screen-list.ts`), one named test per screen x language.
 */

/** The tablet width the row is stated for (a portrait tablet). */
const TABLET = { width: 768, height: 1024 }

/** The three interface languages the panel ships. */
const LOCALES = ['en', 'ru', 'hy'] as const

test.describe('no horizontal scroll at 768 px', () => {
  test.use({ viewport: TABLET })

  for (const screen of SCREENS) {
    for (const locale of LOCALES) {
      test(`${screen.name} does not scroll sideways at 768px in ${locale}`, async ({ page }) => {
        await setPersistedLocale(page, locale)
        await prepareScreen(page, screen)
        await openScreen(page, screen)

        await expectNoHorizontalOverflow(page)
      })
    }
  }

  // Positive control: a measurement that never fails on anything would pass every row above, so
  // plant an element wider than the viewport and require the very same check to refuse it.
  test('the overflow check refuses a planted element wider than the tablet viewport', async ({ page }) => {
    const screen = SCREENS[0]
    expect(screen, 'the screen table is not empty').toBeDefined()
    if (screen === undefined) {
      return
    }
    await setPersistedLocale(page, 'en')
    await prepareScreen(page, screen)
    await openScreen(page, screen)
    await expectNoHorizontalOverflow(page)

    await page.getByRole('main').evaluate((main) => {
      const planted = document.createElement('div')
      planted.style.width = '2000px'
      planted.style.height = '10px'
      main.appendChild(planted)
    })

    await expect(expectNoHorizontalOverflow(page)).rejects.toThrow()
  })
})
