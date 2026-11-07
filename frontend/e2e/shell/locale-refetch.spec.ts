import { expect, test, type Locator, type Page } from '@playwright/test'
import { mainNavigation, openSection } from '../fixtures/open-full-shell'
import { pagedAnswer } from '../fixtures/paged-answer'
import { setPersistedLocale } from '../fixtures/set-locale'
import { stubAccounts } from '../fixtures/stub-accounts-route'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import { stubPlans } from '../fixtures/stub-plans-route'
import { ACME_HOSTING, stubResellers } from '../fixtures/stub-resellers-routes'
import { stubShellReads } from '../fixtures/stub-shell-reads'
import type { ResellerDetail } from '../../src/types/reseller'

// Issue #414: text the backend localizes per request (here the reseller status name) must be
// requested again when the interface language is switched live, without navigating away and back.
// The stub answers each request in the language of its Accept-Language header, as the panel does,
// so a page that kept the earlier answer would still read the old language.

/** The reseller status name per language, as the panel would localize it. */
const ACTIVE_BY_LANGUAGE: Record<string, string> = { en: 'Active', ru: 'Активен', hy: 'Ակտիվ' }

/** One reseller detail endpoint, matched by the stub below. */
const DETAIL_PATTERN = /\/api\/v1\/resellers\/00000000-0000-0000-0000-0000000000e1$/

/** The resellers list endpoint, with or without a query. */
const LIST_PATTERN = /\/api\/v1\/resellers(\?.*)?$/

/**
 * Reads the language a request asked in, from the first tag of its Accept-Language header.
 * @param header The header value, or an empty string.
 * @returns The language code, `en` when the header names none.
 */
const languageOf = (header: string): string => {
  const first = header.split(',')[0]?.trim().split('-')[0] ?? ''
  return first === '' ? 'en' : first
}

/**
 * Builds the reseller as the panel would send it to a caller of the given language.
 * @param language The request's language.
 * @returns The active reseller with its status name localized.
 */
const localized = (language: string): ResellerDetail => {
  return { ...ACME_HOSTING, statusName: ACTIVE_BY_LANGUAGE[language] ?? 'Active' }
}

/**
 * Signs the administrator in and installs the reseller screens, then overrides the list and the
 * detail so their status name follows the request's Accept-Language. Routes registered later win.
 * @param page The Playwright page under test.
 * @returns Resolves once the routes are installed.
 */
const installPanel = async (page: Page): Promise<void> => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubShellReads(page)
  await stubModules(page, [{ name: 'accounts', displayName: 'Accounts', tier: 'included', state: 'enabled' }])
  await stubAccounts(page, [])
  await stubPlans(page)
  await stubResellers(page)

  await page.route(LIST_PATTERN, async (route) => {
    const language = languageOf(route.request().headers()['accept-language'] ?? '')
    const answer = pagedAnswer([localized(language)], route.request().url(), {
      matches: () => {
        return true
      },
      sorts: {},
      filters: {},
    })
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(answer) })
  })

  await page.route(DETAIL_PATTERN, async (route) => {
    const language = languageOf(route.request().headers()['accept-language'] ?? '')
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(localized(language)) })
  })
}

/**
 * The header's language trigger, found by its test id and role because its accessible name is translated.
 * @param page The page under test.
 * @returns The trigger locator.
 */
const localeTrigger = (page: Page): Locator => {
  return page.getByRole('banner').getByTestId('locale-switcher').locator('[aria-haspopup="menu"]')
}

/**
 * Chooses a language from the header's language menu.
 * @param page The page under test.
 * @param language The language's own name, as the option renders it.
 * @returns Resolves once the option has been chosen.
 */
const chooseLanguage = async (page: Page, language: string): Promise<void> => {
  await localeTrigger(page).click()
  await page.getByRole('menuitemradio', { name: language, exact: true }).click()
}

/**
 * Opens the resellers list from the accounts section of the sidebar, by clicks.
 * @param page The page under test.
 * @returns Resolves once the resellers heading is drawn.
 */
const openResellers = async (page: Page): Promise<void> => {
  await page.setViewportSize({ width: 1440, height: 900 })
  await installPanel(page)
  await setPersistedLocale(page, 'en')
  await page.goto('/accounts')
  await openSection(page, 'Accounts')
  await mainNavigation(page).getByRole('link', { name: 'Resellers', exact: true }).click()
  await expect(page).toHaveURL('/resellers')
}

test('switching the language re-requests the resellers list and its status name changes', async ({ page }) => {
  await openResellers(page)
  const main = page.getByRole('main')
  await expect(main.getByText('Active', { exact: true }).first()).toBeVisible()

  await chooseLanguage(page, 'Հայերեն')
  await expect(main.getByText('Ակտիվ', { exact: true }).first()).toBeVisible()
  await expect(main.getByText('Active', { exact: true })).toHaveCount(0)

  await chooseLanguage(page, 'Русский')
  await expect(main.getByText('Активен', { exact: true }).first()).toBeVisible()
  await expect(main.getByText('Ակտիվ', { exact: true })).toHaveCount(0)
  await expect(page).toHaveURL('/resellers')
})

test('switching the language on a reseller detail page re-requests it with the new language', async ({ page }) => {
  await openResellers(page)
  const main = page.getByRole('main')
  await page.getByRole('link', { name: 'acmehosting', exact: true }).click()
  await expect(page.getByRole('heading', { level: 1, name: 'acmehosting' })).toBeVisible()
  await expect(main.getByText('Active', { exact: true }).first()).toBeVisible()

  // The request that proves the page asked again, with the language the user just chose.
  const refetch = page.waitForRequest((request) => {
    return DETAIL_PATTERN.test(request.url()) && (request.headers()['accept-language'] ?? '').startsWith('hy')
  })
  await chooseLanguage(page, 'Հայերեն')
  await refetch

  await expect(main.getByText('Ակտիվ', { exact: true }).first()).toBeVisible()
  await expect(main.getByText('Active', { exact: true })).toHaveCount(0)
  await expect(page).toHaveURL(/\/resellers\/00000000-0000-0000-0000-0000000000e1$/)
})
