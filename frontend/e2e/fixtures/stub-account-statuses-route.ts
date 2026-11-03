import type { Page } from '@playwright/test'
import type { AccountStatusChoice } from '../../src/types/account'

/** The account states list the badges, the list's filter and its chips read. */
const ACCOUNT_STATUSES_PATTERN = /\/api\/v1\/accounts\/statuses(\?.*)?$/

/** What each shipped language calls the two states, as the backend's resource files do. */
const NAMES: Readonly<Record<string, readonly [string, string]>> = {
  en: ['Active', 'Suspended'],
  ru: ['Активен', 'Приостановлен'],
  hy: ['Ակտիվ', 'Կասեցված'],
}

/**
 * Names the two account states in the language a request asks for, falling back to English the way
 * the panel does.
 * @param acceptLanguage The request's `Accept-Language` header, or an empty string.
 * @returns The two states, each with its name.
 */
export const accountStatusesFor = (acceptLanguage: string): AccountStatusChoice[] => {
  const language = Object.keys(NAMES).find((candidate) => {
    return acceptLanguage.toLowerCase().startsWith(candidate)
  })
  const [active, suspended] = NAMES[language ?? 'en']
  return [
    { status: 'active', statusName: active },
    { status: 'suspended', statusName: suspended },
  ]
}

/**
 * Fulfils `GET /api/v1/accounts/statuses` with both states named in the request's language, as the
 * panel does for an administrator and for a customer alike.
 * @param page The Playwright page whose network the route is installed on.
 * @returns Resolves once the route is installed.
 */
export const stubAccountStatuses = async (page: Page): Promise<void> => {
  await page.route(ACCOUNT_STATUSES_PATTERN, async (route) => {
    const language = route.request().headers()['accept-language'] ?? ''
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(accountStatusesFor(language)),
    })
  })
}
