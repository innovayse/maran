import { expect, test, type Page } from '@playwright/test'
import { expectTablesAsCards } from '../fixtures/expect-table-cards'
import { mainNavigation, openFullShell } from '../fixtures/open-full-shell'
import { setPersistedLocale } from '../fixtures/set-locale'
import { pendingUpdateRequests, stubPendingUpdates } from '../fixtures/stub-monitoring-routes'
import { stubShellReads } from '../fixtures/stub-shell-reads'
import type { PendingUpdate } from '../../src/types/monitoring'

// Issue #324: Servers > Updates lists the packages the operating system could update, read from
// the package cache. The screen is a server page like every other list, an unreadable cache shows
// the backend's own message and never "nothing to update", and the installed version is a dash
// where the package family does not report it. Navigation is by clicks only.

/** Three updates in a deliberately non-alphabetical catalogue order, so a sort is observable. */
const UPDATES: PendingUpdate[] = [
  { packageName: 'openssl', availableVersion: '3.0.13-0ubuntu3.5', installedVersion: '3.0.13-0ubuntu3.4', source: 'noble-security' },
  { packageName: 'bash', availableVersion: '5.2.21-2ubuntu4', installedVersion: null, source: '' },
  { packageName: 'curl', availableVersion: '8.5.0-2ubuntu10.6', installedVersion: '8.5.0-2ubuntu10.5', source: 'noble-updates' },
]

/** The backend's sentence for an agent that could not read the package cache. */
const CACHE_UNREADABLE = 'The package cache on this server could not be read.'

/** The section toggle's and the page's wording per language, for the click path. */
const LANGUAGES = [
  { locale: 'en', heading: 'Updates', empty: 'Nothing to update', columns: ['Package', 'Installed', 'Available', 'Source'] },
  { locale: 'ru', heading: 'Обновления', empty: 'Обновлять нечего', columns: ['Пакет', 'Установлена', 'Доступна', 'Источник'] },
  { locale: 'hy', heading: 'Թարմացումներ', empty: 'Թարմացնելու բան չկա', columns: ['Փաթեթ', 'Տեղադրված', 'Հասանելի', 'Աղբյուր'] },
] as const

/**
 * Opens the shell as an administrator with the updates reads stubbed, then walks to the page by
 * clicking the Servers section and its Updates item. The section and the item are found by the
 * structure the sidebar gives them, not by their text, so the same walk works in every language.
 * @param page The Playwright page under test.
 * @param updates Every update the stub knows.
 * @param options The interface language and the failure the stub answers with, if any.
 * @returns Resolves once the Updates screen has been opened.
 */
const openUpdates = async (
  page: Page,
  updates: PendingUpdate[],
  options: { locale?: 'en' | 'ru' | 'hy'; failure?: string | null } = {},
): Promise<void> => {
  await setPersistedLocale(page, options.locale ?? 'en')
  await openFullShell(page)
  await stubShellReads(page)
  await stubPendingUpdates(page, updates, options.failure ?? null)

  const section = mainNavigation(page).locator('div.relative', { has: page.locator('[data-nav-section="servers"]') })
  await section.getByRole('button').click()
  await mainNavigation(page).locator('a[href="/servers/updates"]').click()
  await expect(page).toHaveURL(/\/servers\/updates/)
}

/**
 * The updates table's data rows, without the header row.
 * @param page The Playwright page under test.
 * @returns The rows' locator.
 */
const dataRows = (page: Page): ReturnType<Page['locator']> => {
  return page.locator('tbody tr')
}

// The proposition: the stubbed updates reach the table, with the version columns as given and a dash
// where the installed version is not reported. Breakable: a table that printed the available version
// in the installed column, or an empty cell for null, fails the dash assertion.
test('the Updates table lists each package and draws a dash for an unreported installed version', async ({ page }) => {
  await openUpdates(page, UPDATES)

  await expect(dataRows(page)).toHaveCount(3)
  const bash = dataRows(page).filter({ hasText: 'bash' })
  await expect(bash.getByRole('cell', { name: '5.2.21-2ubuntu4', exact: true })).toHaveCount(1)
  await expect(bash.getByRole('cell', { name: '—', exact: true })).toHaveCount(2)
  const openssl = dataRows(page).filter({ hasText: 'openssl' })
  await expect(openssl.getByRole('cell', { name: '3.0.13-0ubuntu3.4', exact: true })).toHaveCount(1)
  await expect(openssl.getByRole('cell', { name: 'noble-security', exact: true })).toHaveCount(1)
})

// The proposition: the search reaches the panel as a query parameter and the screen shows what the
// panel answered; a search matching nothing offers the way back instead of "Nothing to update".
// Breakable: filtering in the browser sends no `search`; reusing the up-to-date state for a search
// that matched nothing tells the operator the server needs no updates.
test('searching narrows the Updates table through the panel and an empty match offers the way back', async ({ page }) => {
  await openUpdates(page, UPDATES)

  await page.getByRole('searchbox', { name: 'Search packages' }).fill('cur')
  await expect(page).toHaveURL(/search=cur/)
  await expect(dataRows(page)).toHaveCount(1)
  await expect(dataRows(page).first()).toContainText('curl')
  expect(pendingUpdateRequests.at(-1)?.searchParams.get('search')).toBe('cur')

  await page.getByRole('searchbox', { name: 'Search packages' }).fill('nothing-like-this')
  await expect(page.getByText('No packages match')).toBeVisible()
  await expect(page.getByText('Nothing to update')).toHaveCount(0)
})

// The proposition: the Package heading sorts through the panel, ascending then descending, and the
// version columns offer no sort. Breakable: a sort control on a version column would send a key the
// panel refuses with a 400.
test('the Package heading sorts the Updates table on the server and the version columns do not sort', async ({ page }) => {
  await openUpdates(page, UPDATES)

  const heading = page.getByRole('columnheader', { name: 'Package' }).getByRole('button')
  await heading.click()
  await expect(page).toHaveURL(/sort=packageName/)
  await expect(dataRows(page).first()).toContainText('bash')
  await heading.click()
  await expect(page).toHaveURL(/sort=-packageName/)
  await expect(dataRows(page).first()).toContainText('openssl')
  expect(pendingUpdateRequests.at(-1)?.searchParams.get('sort')).toBe('-packageName')

  await expect(page.getByRole('columnheader', { name: 'Installed' }).getByRole('button')).toHaveCount(0)
  await expect(page.getByRole('columnheader', { name: 'Available' }).getByRole('button')).toHaveCount(0)
})

// The proposition: the table is a server page. It asks for one page, the next page is a NEW request
// for page 2, and a smaller page size is a new request too. Breakable: a screen that fetched the
// whole list sends no `page` parameter; one that paged in the browser shows pkg26 without a request.
test('the Updates table asks the panel for one page at a time and honours the page size', async ({ page }) => {
  const many: PendingUpdate[] = Array.from({ length: 30 }, (_, index) => {
    return {
      packageName: `pkg${String(index + 1).padStart(2, '0')}`,
      availableVersion: '2.0',
      installedVersion: '1.0',
      source: 'noble-updates',
    }
  })
  await openUpdates(page, many)

  await expect(dataRows(page)).toHaveCount(25)
  expect(pendingUpdateRequests.at(-1)?.searchParams.get('page')).toBe('1')

  const nav = page.getByRole('navigation', { name: 'Pagination' })
  await nav.getByRole('button', { name: 'Next page' }).click()
  await expect(page).toHaveURL(/page=2/)
  await expect(dataRows(page)).toHaveCount(5)
  await expect(dataRows(page).first()).toContainText('pkg26')
  expect(pendingUpdateRequests.at(-1)?.searchParams.get('page')).toBe('2')

  await nav.getByRole('combobox', { name: 'Rows per page' }).click()
  await page.getByRole('option', { name: '10', exact: true }).click()
  await expect(page).toHaveURL(/pageSize=10/)
  await expect(dataRows(page)).toHaveCount(10)
  expect(pendingUpdateRequests.at(-1)?.searchParams.get('pageSize')).toBe('10')
})

// The proposition: a read that SUCCEEDED with no update says "Nothing to update". Breakable: a screen
// that never drew its empty state shows a bare heading.
test('a successful read with no update says there is nothing to update', async ({ page }) => {
  await openUpdates(page, [])

  await expect(page.getByText('Nothing to update')).toBeVisible()
  await expect(page.getByRole('table')).toHaveCount(0)
})

// The proposition: a read that FAILED shows the backend's own sentence and NEVER the empty state, and
// no table either. This is the defect the typed error exists to prevent: an unreadable package cache
// must not look like a server that is up to date. Breakable: a store that turned a failure into an
// empty page shows "Nothing to update" here.
test('a failed read shows the backend message and never says nothing to update', async ({ page }) => {
  await openUpdates(page, UPDATES, { failure: CACHE_UNREADABLE })

  await expect(page.getByRole('alert').filter({ hasText: CACHE_UNREADABLE })).toBeVisible()
  await expect(page.getByText('Nothing to update')).toHaveCount(0)
  await expect(page.getByRole('table')).toHaveCount(0)
})

for (const language of LANGUAGES) {
  // The proposition: the screen is complete in every language, in the table headings and the empty
  // state alike. Breakable: a key missing from one locale renders its dotted name instead.
  test(`the Updates screen reads in ${language.locale}`, async ({ page }) => {
    await openUpdates(page, UPDATES, { locale: language.locale })

    await expect(page.getByRole('heading', { level: 1, name: language.heading })).toBeVisible()
    for (const column of language.columns) {
      await expect(page.getByRole('columnheader', { name: column })).toBeVisible()
    }
    await expect(page.getByText('monitoring.updates')).toHaveCount(0)
  })

  test(`the Updates empty state reads in ${language.locale}`, async ({ page }) => {
    await openUpdates(page, [], { locale: language.locale })

    await expect(page.getByText(language.empty)).toBeVisible()
    await expect(page.getByText('monitoring.updates')).toHaveCount(0)
  })
}

// The proposition (issue #217): at a phone width the table is drawn as cards and nothing scrolls
// sideways, even with the long package versions. The shell is opened by a click path elsewhere in
// this file; this spec only measures the layout, so it opens the route directly like its siblings.
for (const locale of ['en', 'ru', 'hy'] as const) {
  test(`the Updates table is drawn as cards at 390px in ${locale}`, async ({ page }) => {
    await setPersistedLocale(page, locale)
    await openFullShell(page)
    await stubShellReads(page)
    await stubPendingUpdates(page, UPDATES)
    await page.setViewportSize({ width: 390, height: 844 })
    await page.goto('/servers/updates')

    await expect(dataRows(page)).toHaveCount(3)
    await expectTablesAsCards(page, 'none')
  })
}
