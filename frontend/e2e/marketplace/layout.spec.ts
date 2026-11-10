import { expect, test } from '../fixtures/guarded-test'
import { expectNoHorizontalOverflow } from '../fixtures/measure-horizontal-extent'
import { openMarketplace } from '../fixtures/open-marketplace'
import { setPersistedLocale } from '../fixtures/set-locale'
import {
  catalogueModule,
  detailOf,
  enabledWorld,
  installedModule,
  type MarketplaceWorld,
} from '../fixtures/stub-marketplace-routes'
import type { Page } from '@playwright/test'

// Issue #187: the marketplace fits a 390 px screen in every language, on every tab, on the detail
// screen and with the install dialog open. The content is stocked with long names, long server
// sentences and a long unbroken token, because that is what a real catalogue sends and what an
// over-wide card or table would be caught by. `e2e/ui-kit/route-coverage.spec.ts` reads the
// `path:` rows below as this file's two swept screens.

/** The screens this file opens at 390 px. */
const SWEPT_SCREENS = [{ path: '/marketplace' }, { path: '/marketplace/weather' }]

/** What each language prints, so the spec drives the page without English names. */
const LANGUAGES = [
  {
    code: 'en',
    title: 'Marketplace',
    check: 'Check for updates',
    installFor: (name: string): string => {
      return `Install ${name}`
    },
  },
  {
    code: 'ru',
    title: 'Каталог модулей',
    check: 'Проверить обновления',
    installFor: (name: string): string => {
      return `Установить ${name}`
    },
  },
  {
    code: 'hy',
    title: 'Մոդուլների շուկա',
    check: 'Ստուգել թարմացումները',
    installFor: (name: string): string => {
      return `Տեղադրել ${name} մոդուլը`
    },
  },
] as const

/** A name and a sentence long enough to wrap on a phone, with one token that cannot break. */
const LONG_NAME = 'Weather widgets with a deliberately very long module name'
const LONG_TEXT =
  'A licence is required to install this module; ask the publisher: https://cabinet.example.com/buy/weather-widgets-annual-business-licence-0123456789'

/**
 * A world stocked so every tab and the detail screen have content wide enough to wrap.
 * @returns The world, with the marketplace on.
 */
const stockedWorld = (): MarketplaceWorld => {
  const weather = catalogueModule('weather', LONG_NAME)
  const paid = catalogueModule('paid-reports', 'Paid reports', {
    price: { display: '9 EUR a month', purchaseUrl: 'https://cabinet.example.com/buy/paid-reports' },
    entitlement: { state: 'notPurchased', stateDisplayName: 'Licence not purchased', until: null },
    canInstall: false,
    cannotInstallMessage: LONG_TEXT,
  })
  const world = enabledWorld({
    details: { weather: detailOf(weather) },
    installed: [
      installedModule('clock', LONG_NAME, { previousVersion: '1.9.0', canRollback: true }),
      installedModule('notes', 'Notes', { failureMessage: LONG_TEXT, lifecycle: 'failed' }),
    ],
    updates: {
      items: [
        {
          id: 'clock',
          displayName: LONG_NAME,
          installedVersion: '2.0.0',
          availableVersion: '2.1.0',
          revoked: false,
          revokedMessage: null,
          canUpdate: true,
          cannotUpdateMessage: null,
        },
      ],
      verified: true,
      checkedAt: '2026-11-09T09:05:00Z',
      unverifiedMessage: null,
    },
  })
  world.catalogue = { ...world.catalogue, items: [weather, paid] }
  return world
}

/**
 * Opens the marketplace in a language at the phone width.
 * @param page The Playwright page under test.
 * @param code The interface language.
 * @param path The screen to open.
 * @returns Resolves once the page is drawn at 390 px.
 */
const openAtPhoneWidth = async (page: Page, code: 'en' | 'ru' | 'hy', path: string): Promise<void> => {
  await setPersistedLocale(page, code)
  await openMarketplace(page, stockedWorld(), path)
  await page.setViewportSize({ width: 390, height: 844 })
}

for (const language of LANGUAGES) {
  test.describe(`in ${language.code}`, () => {
    test(`the marketplace fits a 390px screen on every tab (${language.code})`, async ({ page }) => {
      await openAtPhoneWidth(page, language.code, SWEPT_SCREENS[0]?.path ?? '/marketplace')
      await expect(page.getByRole('heading', { level: 1, name: language.title })).toBeVisible()

      const tabs = page.getByRole('tab')
      await expect(tabs).toHaveCount(3)
      await expect(page.getByTestId('module-card-weather')).toBeVisible()
      await expectNoHorizontalOverflow(page)

      await tabs.nth(1).click()
      await expect(page.getByRole('table')).toBeVisible()
      await expectNoHorizontalOverflow(page)

      await tabs.nth(2).click()
      await page.getByRole('button', { name: language.check }).click()
      await expect(page.getByRole('table')).toBeVisible()
      await expectNoHorizontalOverflow(page)
    })

    test(`the detail screen fits a 390px screen (${language.code})`, async ({ page }) => {
      await openAtPhoneWidth(page, language.code, SWEPT_SCREENS[1]?.path ?? '/marketplace/weather')
      await expect(page.getByTestId('module-summary')).toBeVisible()
      await expect(page.getByTestId('module-manifest')).toBeVisible()
      await expectNoHorizontalOverflow(page)
    })

    test(`the install dialog fits a 390px screen (${language.code})`, async ({ page }) => {
      await openAtPhoneWidth(page, language.code, SWEPT_SCREENS[0]?.path ?? '/marketplace')
      await page.getByRole('button', { name: language.installFor(LONG_NAME) }).click()

      const dialog = page.getByRole('dialog')
      await expect(dialog.getByTestId('module-manifest')).toBeVisible()
      await expectNoHorizontalOverflow(page)
      const box = await dialog.boundingBox()
      expect(box).not.toBeNull()
      expect((box?.x ?? -1) >= 0 && (box?.x ?? 0) + (box?.width ?? 0) <= 390).toBe(true)
    })
  })
}

test('the overflow probe finds a planted over-wide element on the marketplace', async ({ page }) => {
  // The positive control: without it a probe that had stopped looking would pass every test above.
  await openAtPhoneWidth(page, 'en', '/marketplace')
  await expect(page.getByTestId('module-card-weather')).toBeVisible()
  await expectNoHorizontalOverflow(page)

  await page.evaluate(() => {
    const wide = document.createElement('div')
    wide.style.width = '900px'
    wide.style.height = '4px'
    document.querySelector('main')?.append(wide)
  })

  await expect(expectNoHorizontalOverflow(page)).rejects.toThrow()
})
