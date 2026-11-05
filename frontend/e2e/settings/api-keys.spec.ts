import { expect, test, type Locator, type Page } from '@playwright/test'
import { expectNoSeriousViolations } from '../fixtures/expect-no-serious-violations'
import { FULL_CATALOGUE, mainNavigation, openSection } from '../fixtures/open-full-shell'
import { setPersistedLocale } from '../fixtures/set-locale'
import {
  apiKeyRow,
  numberedKeys,
  stubApiKeys,
  stubbedPlaintext,
  type ApiKeyCalls,
  type StubbedApiKeys,
} from '../fixtures/stub-api-keys-routes'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import { RESELLER_USER, stubResellerSession } from '../fixtures/stub-reseller-session'
import type { ApiKey } from '../../src/types/apiKey'
import type { AuthenticatedUser } from '../../src/types/auth'

// The API keys screen (administrators only): a server-paged list searched and filtered by state on
// the panel, a create dialog whose scopes come from the panel, a secret shown once, and a revoke
// behind a confirmation. Every spec reaches the page through the sidebar's Security group.

const PHONE = { width: 390, height: 844 }

const CUSTOMER: AuthenticatedUser = {
  id: '00000000-0000-0000-0000-000000000099',
  username: 'olive',
  email: 'olive@example.com',
  role: 'customer',
  accountId: '11111111-1111-1111-1111-111111111111',
}

const BILLING = apiKeyRow()
const LEGACY = apiKeyRow({
  id: '10000000-0000-0000-0000-000000000002',
  name: 'Legacy exporter',
  prefix: 'e5f6a7b8',
  scopes: ['sites.read', 'sites.write'],
  expiresAt: '2026-08-15T00:00:00+00:00',
  status: 'expired',
})
const OLD_HOOK = apiKeyRow({
  id: '10000000-0000-0000-0000-000000000003',
  name: 'Old webhook',
  prefix: 'c9d0e1f2',
  revokedAt: '2026-08-20T00:00:00+00:00',
  status: 'revoked',
})

/**
 * Signs in as an administrator with every module, stubs the API keys endpoints and lands on the
 * dashboard, with every other `/api/v1` read answered by an empty 404 so the shell around the
 * screen is drawn either way.
 * @param page The Playwright page.
 * @param state The keys and refusals the panel plays.
 * @returns The calls the stub will record.
 */
const openPanel = async (page: Page, state: StubbedApiKeys): Promise<ApiKeyCalls> => {
  await page.setViewportSize({ width: 1440, height: 900 })
  await page.route(/\/api\/v1\//, async (route) => {
    await route.fulfill({
      status: 404,
      contentType: 'application/problem+json',
      body: JSON.stringify({ title: 'Not found', status: 404 }),
    })
  })
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, FULL_CATALOGUE)
  const calls = await stubApiKeys(page, state)
  await page.goto('/')
  await expect(page.locator('nav [data-nav-section]').first()).toBeVisible()
  return calls
}

/**
 * Opens the API keys screen by clicking: the Security group, then its API keys item.
 * @param page The Playwright page.
 * @returns Resolves once the screen's heading is drawn.
 */
const goToApiKeys = async (page: Page): Promise<void> => {
  await openSection(page, 'Security')
  await mainNavigation(page).getByRole('link', { name: 'API keys', exact: true }).click()
  await expect(page.getByRole('heading', { level: 1, name: 'API keys' })).toBeVisible()
}

/**
 * Opens the panel with the given keys and walks to the screen.
 * @param page The Playwright page.
 * @param keys The keys the panel holds.
 * @param extra Refusals to play.
 * @returns The calls the stub will record.
 */
const openApiKeys = async (page: Page, keys: ApiKey[], extra: Partial<StubbedApiKeys> = {}): Promise<ApiKeyCalls> => {
  const calls = await openPanel(page, { keys, ...extra })
  await goToApiKeys(page)
  return calls
}

/**
 * The key rows on screen, found by the names the numbered catalogue gives them.
 * @param page The Playwright page.
 * @returns A locator over the data rows.
 */
const numberedRows = (page: Page): Locator => {
  return page.getByRole('row', { name: /Key \d\d/ })
}

/**
 * Whether a value sits in either web storage, by key or by content.
 * @param page The Playwright page.
 * @param needle The value to look for.
 * @returns True when localStorage or sessionStorage holds it.
 */
const storedAnywhere = (page: Page, needle: string): Promise<boolean> => {
  return page.evaluate((value) => {
    return [window.localStorage, window.sessionStorage].some((storage) => {
      return Object.keys(storage).some((key) => {
        return key.includes(value) || (storage.getItem(key) ?? '').includes(value)
      })
    })
  }, needle)
}

/**
 * Creates the key named in the dialog, with nothing else filled in, and waits for the secret.
 * @param page The Playwright page.
 * @param name The label to type.
 * @returns Resolves once the one-time dialog is open.
 */
const createKeyNamed = async (page: Page, name: string): Promise<void> => {
  await page.getByRole('button', { name: 'Create key' }).click()
  const dialog = page.getByRole('dialog', { name: 'Create an API key' })
  await dialog.getByRole('textbox', { name: 'Name' }).fill(name)
  await dialog.getByRole('button', { name: 'Create key' }).click()
  await expect(page.getByRole('dialog', { name: 'Your new API key' })).toBeVisible()
}

test('the Security group opens the API keys list, with states named by the panel', async ({ page }) => {
  await openApiKeys(page, [BILLING, LEGACY, OLD_HOOK])

  const active = page.getByRole('row').filter({ hasText: 'Billing integration' })
  await expect(active.getByText('In force')).toBeVisible()
  await expect(active).toContainText('a1b2c3d4')
  await expect(active).toContainText('Look at sites')
  await expect(active).toContainText('Never used')
  await expect(page.getByRole('row').filter({ hasText: 'Legacy exporter' }).getByText('Lapsed')).toBeVisible()
  await expect(page.getByRole('row').filter({ hasText: 'Legacy exporter' })).toContainText('Look at sites, Change sites')
  await expect(page.getByRole('row').filter({ hasText: 'Old webhook' }).getByText('Withdrawn')).toBeVisible()
  // The wire's own spelling is never what the reader is given.
  await expect(page.getByRole('table')).not.toContainText('revoked')
})

test('a panel with no keys shows the empty state and no table', async ({ page }) => {
  await openApiKeys(page, [])

  await expect(page.getByText('No API keys yet')).toBeVisible()
  await expect(page.getByRole('table')).toHaveCount(0)
})

test('the state filter opens as a dialog on a desktop and narrows the list on the panel', async ({ page }) => {
  const calls = await openApiKeys(page, [BILLING, LEGACY, OLD_HOOK])

  await page.getByRole('button', { name: 'Filters' }).click()
  await expect(page.getByTestId('filter-sheet')).toHaveCount(0)
  const dialog = page.getByRole('dialog', { name: 'Filters' })
  await dialog.getByRole('combobox', { name: 'State' }).click()
  await page.getByRole('option', { name: 'Lapsed' }).click()
  await dialog.getByRole('button', { name: 'Apply' }).click()

  await expect(page).toHaveURL(/\?status=expired$/)
  await expect(page.getByRole('row').filter({ hasText: 'Legacy exporter' })).toBeVisible()
  await expect(page.getByRole('row').filter({ hasText: 'Billing integration' })).toHaveCount(0)
  expect(
    calls.lists.some((url) => {
      return url.searchParams.get('status') === 'expired'
    }),
  ).toBe(true)
})

test('at 390px the state filter is a bottom sheet and narrows the list on the panel', async ({ page }) => {
  const calls = await openApiKeys(page, [BILLING, LEGACY, OLD_HOOK])
  await page.setViewportSize(PHONE)

  await page.getByRole('button', { name: 'Filters' }).click()
  const sheet = page.getByTestId('filter-sheet')
  await expect(sheet).toBeVisible()
  await expect
    .poll(async () => {
      const box = await sheet.boundingBox()
      return box === null ? null : Math.round(box.y + box.height)
    })
    .toBe(PHONE.height)
  await sheet.getByRole('combobox', { name: 'State' }).click()
  await page.getByRole('option', { name: 'Withdrawn' }).click()
  await sheet.getByRole('button', { name: 'Apply' }).click()

  await expect(page).toHaveURL(/\?status=revoked$/)
  await expect(page.getByRole('row').filter({ hasText: 'Old webhook' })).toBeVisible()
  await expect(page.getByRole('row').filter({ hasText: 'Billing integration' })).toHaveCount(0)
  expect(
    calls.lists.some((url) => {
      return url.searchParams.get('status') === 'revoked'
    }),
  ).toBe(true)
})

test('a search is sent to the panel and one that matches nothing is the no-results state', async ({ page }) => {
  const calls = await openApiKeys(page, numberedKeys(30))

  await page.getByRole('searchbox', { name: 'Search API keys' }).fill('Key 2')
  await expect(numberedRows(page)).toHaveCount(10)
  expect(
    calls.lists.some((url) => {
      return url.searchParams.get('search') === 'Key 2'
    }),
  ).toBe(true)

  await page.getByRole('searchbox', { name: 'Search API keys' }).fill('nowhere')
  await expect(page.getByText('No keys match')).toBeVisible()
  await expect(page.getByText('No API keys yet')).toHaveCount(0)
})

test('the next page of keys is read from the panel', async ({ page }) => {
  const calls = await openApiKeys(page, numberedKeys(30))
  await expect(numberedRows(page)).toHaveCount(25)

  await page.getByRole('navigation', { name: 'Pagination' }).getByRole('button', { name: 'Next page' }).click()

  await expect(page).toHaveURL(/\?page=2$/)
  await expect(numberedRows(page)).toHaveCount(5)
  await expect(numberedRows(page).first()).toContainText('Key 26')
  expect(
    calls.lists.some((url) => {
      return url.searchParams.get('page') === '2'
    }),
  ).toBe(true)
})

test('the create dialog advises on a missing name and a malformed range before anything is sent', async ({ page }) => {
  const calls = await openApiKeys(page, [BILLING])

  await page.getByRole('button', { name: 'Create key' }).click()
  const dialog = page.getByRole('dialog', { name: 'Create an API key' })
  await expect(dialog.getByText('Enter a name for the key.')).toHaveCount(0)
  await dialog.getByRole('button', { name: 'Create key' }).click()
  await expect(dialog.getByText('Enter a name for the key.')).toBeVisible()

  await dialog.getByRole('textbox', { name: 'Name' }).fill('Nightly export')
  await dialog.getByRole('textbox', { name: 'Allowed addresses' }).fill('not-a-range')
  await dialog.getByLabel('Expires on').fill('2020-01-01')
  await dialog.getByRole('button', { name: 'Create key' }).click()
  await expect(dialog.getByText('Each line must be an address range such as 203.0.113.0/24.')).toBeVisible()
  await expect(dialog.getByText('Choose a day in the future.')).toBeVisible()

  expect(calls.created).toEqual([])
})

test('the create dialog offers the scopes the panel lists and sends null for a blank range and expiry', async ({
  page,
}) => {
  const calls = await openApiKeys(page, [BILLING])

  await page.getByRole('button', { name: 'Create key' }).click()
  const dialog = page.getByRole('dialog', { name: 'Create an API key' })
  await expect(dialog.getByRole('checkbox')).toHaveCount(2)
  await expect(dialog.getByRole('checkbox', { name: 'Look at sites' })).toBeVisible()
  await dialog.getByRole('checkbox', { name: 'Change sites' }).check()
  await dialog.getByRole('textbox', { name: 'Name' }).fill('  Nightly export  ')
  await dialog.getByRole('button', { name: 'Create key' }).click()

  await expect(page.getByRole('dialog', { name: 'Your new API key' })).toBeVisible()
  expect(calls.created).toEqual([
    { name: 'Nightly export', scopes: ['sites.write'], allowedCidrs: null, expiresAt: null },
  ])
})

test('ranges typed one per line and an expiry day are sent as a list and an instant', async ({ page }) => {
  const calls = await openApiKeys(page, [BILLING])

  await page.getByRole('button', { name: 'Create key' }).click()
  const dialog = page.getByRole('dialog', { name: 'Create an API key' })
  await dialog.getByRole('textbox', { name: 'Name' }).fill('Office only')
  await dialog.getByRole('textbox', { name: 'Allowed addresses' }).fill('203.0.113.0/24\n\n198.51.100.0/25')
  await dialog.getByLabel('Expires on').fill('2099-12-31')
  await dialog.getByRole('button', { name: 'Create key' }).click()

  await expect(page.getByRole('dialog', { name: 'Your new API key' })).toBeVisible()
  expect(calls.created).toHaveLength(1)
  expect(calls.created[0]?.allowedCidrs).toEqual(['203.0.113.0/24', '198.51.100.0/25'])
  expect(calls.created[0]?.expiresAt).toMatch(/^2099-12-3[01]T/)
})

test('a refused create shows the panel sentence in the dialog and creates nothing', async ({ page }) => {
  const sentence = 'A key with this name already exists on the panel.'
  await openApiKeys(page, [BILLING], { createRefusal: sentence })

  await page.getByRole('button', { name: 'Create key' }).click()
  const dialog = page.getByRole('dialog', { name: 'Create an API key' })
  await dialog.getByRole('textbox', { name: 'Name' }).fill('Billing integration')
  await dialog.getByRole('button', { name: 'Create key' }).click()

  await expect(dialog.getByText(sentence)).toBeVisible()
  await expect(page.getByRole('dialog', { name: 'Your new API key' })).toHaveCount(0)
})

test('the one-time dialog shows the key with a warning, and copying puts it on the clipboard', async ({
  page,
  context,
}) => {
  await context.grantPermissions(['clipboard-read', 'clipboard-write'])
  await openApiKeys(page, [BILLING])

  await createKeyNamed(page, 'Nightly export')

  const dialog = page.getByRole('dialog', { name: 'Your new API key' })
  await expect(dialog.getByTestId('api-key-secret')).toHaveText(stubbedPlaintext)
  await expect(dialog.getByText('Copy this key now. It is shown only once and cannot be shown again.')).toBeVisible()
  await expect(dialog.getByText('Nightly export')).toBeVisible()

  await dialog.getByRole('button', { name: 'Copy key' }).click()
  await expect(dialog.getByRole('button', { name: 'Copied' })).toBeVisible()
  expect(await page.evaluate(() => navigator.clipboard.readText())).toBe(stubbedPlaintext)

  // Neither the backdrop nor Escape may destroy the only copy.
  await page.keyboard.press('Escape')
  await expect(dialog).toBeVisible()
})

test('closing the one-time dialog removes the key from the page and it was never stored anywhere', async ({ page }) => {
  await openApiKeys(page, [BILLING])

  // Positive control for the storage probe: a planted copy is found, then removed.
  await page.evaluate((value) => {
    window.sessionStorage.setItem('probe', value)
  }, stubbedPlaintext)
  expect(await storedAnywhere(page, stubbedPlaintext)).toBe(true)
  await page.evaluate(() => {
    window.sessionStorage.removeItem('probe')
  })
  expect(await storedAnywhere(page, stubbedPlaintext)).toBe(false)

  await createKeyNamed(page, 'Nightly export')

  // While it is on show: visible, and still in no storage and no address.
  await expect(page.getByTestId('api-key-secret')).toHaveText(stubbedPlaintext)
  expect(await page.content()).toContain(stubbedPlaintext)
  expect(await storedAnywhere(page, stubbedPlaintext)).toBe(false)
  expect(page.url()).not.toContain(stubbedPlaintext)

  await page.getByRole('dialog', { name: 'Your new API key' }).getByRole('button', { name: 'Done' }).click()

  await expect(page.getByRole('dialog', { name: 'Your new API key' })).toHaveCount(0)
  await expect(page.getByTestId('api-key-secret')).toHaveCount(0)
  expect(await page.content()).not.toContain(stubbedPlaintext)
  expect(await storedAnywhere(page, stubbedPlaintext)).toBe(false)
  expect(page.url()).not.toContain(stubbedPlaintext)
  // The list read again: the new key is a row, identified by its public prefix only.
  const created = page.getByRole('row').filter({ hasText: 'Nightly export' })
  await expect(created).toContainText('9f8e7d6c')
})

test('leaving the screen while the key is on show forgets it', async ({ page }) => {
  await openApiKeys(page, [BILLING])
  await createKeyNamed(page, 'Nightly export')
  await expect(page.getByTestId('api-key-secret')).toHaveText(stubbedPlaintext)

  // The modal is not dismissible, so the browser's Back button is the way off the screen.
  await page.goBack()

  await expect(page.getByTestId('api-key-secret')).toHaveCount(0)
  expect(await page.content()).not.toContain(stubbedPlaintext)
})

test('revoking a key asks first, sends the DELETE and reads the list again', async ({ page }) => {
  const calls = await openApiKeys(page, [BILLING, LEGACY])

  const row = page.getByRole('row').filter({ hasText: 'Billing integration' })
  await row.getByRole('button', { name: 'Actions for the key Billing integration' }).click()
  await page.getByRole('menuitem', { name: 'Revoke' }).click()
  const dialog = page.getByRole('dialog', { name: 'Revoke the key Billing integration' })
  await expect(dialog.getByText('The key stops working at once and cannot be restored.', { exact: false })).toBeVisible()
  expect(calls.revoked).toEqual([])
  const listsBefore = calls.lists.length
  await dialog.getByRole('button', { name: 'Revoke key' }).click()

  await expect(page.getByText('The key Billing integration was revoked.')).toBeVisible()
  expect(calls.revoked).toEqual([BILLING.id])
  expect(calls.lists.length).toBeGreaterThan(listsBefore)
  await expect(row.getByText('Withdrawn')).toBeVisible()
  // A revoked key has nothing left to do.
  await expect(row.getByRole('button', { name: 'Actions for the key Billing integration' })).toHaveCount(0)
})

test('cancelling the revoke confirmation sends nothing', async ({ page }) => {
  const calls = await openApiKeys(page, [BILLING])

  await page
    .getByRole('row')
    .filter({ hasText: 'Billing integration' })
    .getByRole('button', { name: 'Actions for the key Billing integration' })
    .click()
  await page.getByRole('menuitem', { name: 'Revoke' }).click()
  await page
    .getByRole('dialog', { name: 'Revoke the key Billing integration' })
    .getByRole('button', { name: 'Cancel' })
    .click()

  await expect(page.getByRole('dialog')).toHaveCount(0)
  expect(calls.revoked).toEqual([])
})

test('a refused revoke shows the panel sentence as received', async ({ page }) => {
  const sentence = 'This key belongs to the integration that is running right now.'
  await openApiKeys(page, [BILLING], { revokeRefusal: sentence })

  await page
    .getByRole('row')
    .filter({ hasText: 'Billing integration' })
    .getByRole('button', { name: 'Actions for the key Billing integration' })
    .click()
  await page.getByRole('menuitem', { name: 'Revoke' }).click()
  await page
    .getByRole('dialog', { name: 'Revoke the key Billing integration' })
    .getByRole('button', { name: 'Revoke key' })
    .click()

  await expect(page.getByText(sentence)).toBeVisible()
  await expect(page.getByText('The key Billing integration was revoked.')).toHaveCount(0)
})

test('a refused list shows the panel sentence as received and no table', async ({ page }) => {
  const sentence = 'Keys are not available to this caller.'
  await openApiKeys(page, [BILLING], { listRefusal: sentence })

  await expect(page.getByText(sentence)).toBeVisible()
  await expect(page.getByRole('table')).toHaveCount(0)
})

for (const chrome of [
  {
    locale: 'ru' as const,
    section: 'Показать разделы «Безопасность»',
    item: 'API-ключи',
    create: 'Создать ключ',
    search: 'Поиск API-ключей',
    filters: 'Фильтры',
    form: 'Создание API-ключа',
  },
  {
    locale: 'hy' as const,
    section: 'Ցույց տալ «Անվտանգություն» բաժինները',
    item: 'API բանալիներ',
    create: 'Ստեղծել բանալի',
    search: 'Որոնել API բանալիներ',
    filters: 'Զտիչներ',
    form: 'API բանալու ստեղծում',
  },
]) {
  test(`the API keys screen draws its own chrome in ${chrome.locale}`, async ({ page }) => {
    await setPersistedLocale(page, chrome.locale)
    await openPanel(page, { keys: [BILLING] })

    await mainNavigation(page).getByRole('button', { name: chrome.section }).click()
    await mainNavigation(page).getByRole('link', { name: chrome.item, exact: true }).click()

    await expect(page.getByRole('heading', { level: 1, name: chrome.item })).toBeVisible()
    await expect(page.getByRole('searchbox', { name: chrome.search })).toBeVisible()
    await expect(page.getByRole('button', { name: chrome.filters })).toBeVisible()
    // The state is the panel's own word, whatever the interface language.
    await expect(page.getByRole('row').filter({ hasText: 'Billing integration' }).getByText('In force')).toBeVisible()
    await page.getByRole('button', { name: chrome.create }).click()
    await expect(page.getByRole('dialog', { name: chrome.form })).toBeVisible()
  })
}

test('a customer is not offered the API keys item or the Security group', async ({ page }) => {
  await stubSignedIn(page, CUSTOMER)
  await stubHealthy(page)
  await stubModules(page, FULL_CATALOGUE)
  await stubApiKeys(page, { keys: [BILLING] })

  await page.goto('/')
  const nav = mainNavigation(page)
  await expect(nav.getByRole('link', { name: 'System status' })).toBeVisible()

  await expect(nav.getByRole('link', { name: 'API keys', exact: true })).toHaveCount(0)
  await expect(nav.getByRole('button', { name: 'Show the Security pages' })).toHaveCount(0)
})

test('a reseller is not offered the API keys item or the Security group', async ({ page }) => {
  await stubResellerSession(page, undefined, FULL_CATALOGUE)
  await stubApiKeys(page, { keys: [BILLING] })

  await page.goto('/')
  const nav = mainNavigation(page)
  await expect(page.getByText(RESELLER_USER.username, { exact: false }).first()).toBeVisible()

  await expect(nav.getByRole('link', { name: 'API keys', exact: true })).toHaveCount(0)
  await expect(nav.getByRole('button', { name: 'Show the Security pages' })).toHaveCount(0)
})

test('axe finds no serious violation on the list or on any of its dialogs', async ({ page }) => {
  await openApiKeys(page, [BILLING, LEGACY, OLD_HOOK])
  await expect(page.getByRole('row').filter({ hasText: 'Old webhook' })).toBeVisible()
  await expectNoSeriousViolations(page)

  await page.getByRole('button', { name: 'Filters' }).click()
  await expect(page.getByRole('dialog', { name: 'Filters' })).toBeVisible()
  await expectNoSeriousViolations(page)
  await page.keyboard.press('Escape')
  await expect(page.getByRole('dialog')).toHaveCount(0)

  await page.getByRole('button', { name: 'Create key' }).click()
  await expect(page.getByRole('dialog', { name: 'Create an API key' })).toBeVisible()
  await expectNoSeriousViolations(page)
  await page.getByRole('dialog', { name: 'Create an API key' }).getByRole('button', { name: 'Cancel' }).click()

  await page
    .getByRole('row')
    .filter({ hasText: 'Billing integration' })
    .getByRole('button', { name: 'Actions for the key Billing integration' })
    .click()
  await page.getByRole('menuitem', { name: 'Revoke' }).click()
  await expect(page.getByRole('dialog', { name: 'Revoke the key Billing integration' })).toBeVisible()
  await expectNoSeriousViolations(page)
  await page.getByRole('dialog', { name: 'Revoke the key Billing integration' }).getByRole('button', { name: 'Cancel' }).click()

  await createKeyNamed(page, 'Nightly export')
  await expectNoSeriousViolations(page)
})
