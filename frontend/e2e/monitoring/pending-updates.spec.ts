import { expect, test, type Page } from '@playwright/test'
import { expectTablesAsCards } from '../fixtures/expect-table-cards'
import { openFullShell } from '../fixtures/open-full-shell'
import { setPersistedLocale } from '../fixtures/set-locale'
import { pendingUpdateRequests, stubPendingUpdates, type StubbedPendingUpdate, type StubbedUpdateNotices } from '../fixtures/stub-monitoring-routes'
import { stubShellReads } from '../fixtures/stub-shell-reads'

// Issue #324: Servers > Updates lists the packages the operating system could update, read from
// the package cache. The screen is a server page like every other list, an unreadable cache shows
// the backend's own message and never "nothing to update", and the installed version is a dash
// where the package family does not report it. Navigation is by clicks only.

/** Three updates in a deliberately non-alphabetical catalogue order, so a sort is observable. */
const UPDATES: StubbedPendingUpdate[] = [
  { packageName: 'openssl', availableVersion: '3.0.13-0ubuntu3.5', installedVersion: '3.0.13-0ubuntu3.4', source: 'noble-security', classification: 'security' },
  { packageName: 'bash', availableVersion: '5.2.21-2ubuntu4', installedVersion: null, source: '', classification: 'unmeasured' },
  { packageName: 'curl', availableVersion: '8.5.0-2ubuntu10.6', installedVersion: '8.5.0-2ubuntu10.5', source: 'noble-updates', classification: 'ordinary' },
]

/** The backend's sentence for an agent that could not read the package cache. */
const CACHE_UNREADABLE = 'The package cache on this server could not be read.'

/** The section toggle's and the page's wording per language, for the click path. */
const LANGUAGES = [
  { locale: 'en', heading: 'Updates', empty: 'Nothing to update', columns: ['Package', 'Installed', 'Available', 'Source', 'Type'], security: 'Security', notice: '2 security updates waiting', reboot: 'A reboot is required to finish applying updates.' },
  { locale: 'ru', heading: 'Обновления', empty: 'Обновлять нечего', columns: ['Пакет', 'Установлена', 'Доступна', 'Источник', 'Тип'], security: 'Безопасность', notice: 'Ожидают 2 обновления безопасности', reboot: 'Для завершения установки обновлений требуется перезагрузка.' },
  { locale: 'hy', heading: 'Թարմացումներ', empty: 'Թարմացնելու բան չկա', columns: ['Փաթեթ', 'Տեղադրված', 'Հասանելի', 'Աղբյուր', 'Տեսակ'], security: 'Անվտանգություն', notice: 'Սպասվում է 2 անվտանգության թարմացում', reboot: 'Թարմացումների կիրառումն ավարտելու համար անհրաժեշտ է վերագործարկում։' },
] as const

/**
 * Opens the shell as an administrator with the updates reads stubbed, then walks to the page by
 * clicking the Servers section and its Updates item. The section and the item are found by the
 * structure the sidebar gives them, not by their text, so the same walk works in every language.
 * @param page The Playwright page under test.
 * @param updates Every update the stub knows.
 * @param options The interface language, the failure the stub answers with, if any, and the notices' answers.
 * @returns Resolves once the Updates screen has been opened.
 */
const openUpdates = async (
  page: Page,
  updates: StubbedPendingUpdate[],
  options: { locale?: 'en' | 'ru' | 'hy'; failure?: string | null; notices?: StubbedUpdateNotices } = {},
): Promise<void> => {
  await setPersistedLocale(page, options.locale ?? 'en')
  await openFullShell(page)
  await stubShellReads(page)
  await stubPendingUpdates(page, updates, options.failure ?? null, options.notices ?? {})

  // The sidebar is found as the complementary landmark's navigation: its accessible name is
  // translated, so a name-based lookup would only work in English. The section's own link also
  // points at its first page, so the item is told apart by the `data-nav-parent` it carries.
  const sidebar = page.getByRole('complementary').getByRole('navigation')
  const section = sidebar.locator('div.relative', { has: page.locator('[data-nav-section="servers"]') })
  await section.getByRole('button').click()
  await sidebar.locator('a[data-nav-parent="servers"][href="/servers/updates"]').click()
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
  const many: StubbedPendingUpdate[] = Array.from({ length: 30 }, (_, index) => {
    return {
      packageName: `pkg${String(index + 1).padStart(2, '0')}`,
      availableVersion: '2.0',
      installedVersion: '1.0',
      source: 'noble-updates',
      classification: 'ordinary',
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

  await expect(page.getByRole('status').filter({ hasText: CACHE_UNREADABLE })).toBeVisible()
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

  // The proposition: the classification badge and both notices read in every language, with the
  // plural the language needs. Breakable: a key missing from one locale renders its dotted name.
  test(`the classification badge and the notices read in ${language.locale}`, async ({ page }) => {
    await openUpdates(page, UPDATES, { locale: language.locale, notices: { securityCount: 2, reboot: true } })

    await expect(dataRows(page).filter({ hasText: 'openssl' }).getByRole('cell', { name: language.security, exact: true })).toHaveCount(1)
    await expect(page.getByText(language.notice)).toBeVisible()
    await expect(page.getByText(language.reboot)).toBeVisible()
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

// The proposition: each row carries its classification as text, and an update the agent could not
// classify says so rather than passing for ordinary. Breakable: a badge that mapped `unmeasured` to
// "Ordinary" fails the bash row.
test('each Updates row names its classification and an unmeasured one says so', async ({ page }) => {
  await openUpdates(page, UPDATES)

  const cell = (row: string, name: string): ReturnType<Page['locator']> => {
    return dataRows(page).filter({ hasText: row }).getByRole('cell', { name, exact: true })
  }
  await expect(cell('openssl', 'Security')).toHaveCount(1)
  await expect(cell('curl', 'Ordinary')).toHaveCount(1)
  await expect(cell('bash', 'Not measured')).toHaveCount(1)
  await expect(cell('bash', 'Ordinary')).toHaveCount(0)
})

// The proposition: the Type heading sorts through the panel as `security`, security updates first.
// Breakable: a heading with no sort key, or one sorting in the browser, sends no `security` sort.
test('the Type heading sorts security updates first through the panel', async ({ page }) => {
  await openUpdates(page, UPDATES)

  await page.getByRole('columnheader', { name: 'Type' }).getByRole('button').click()
  await expect(page).toHaveURL(/sort=security/)
  await expect(dataRows(page).first()).toContainText('openssl')
  expect(pendingUpdateRequests.at(-1)?.searchParams.get('sort')).toBe('security')
})

/**
 * Narrows the list to security updates through the filter trigger and the sheet it opens.
 * @param page The Playwright page, already on the Updates screen.
 * @returns The filter dialog, which the caller may inspect while it is open.
 */
const openFilterDialog = async (page: Page): Promise<ReturnType<Page['locator']>> => {
  await page.getByRole('button', { name: 'Filters' }).click()
  const dialog = page.getByRole('dialog', { name: 'Filters' })
  await expect(dialog).toBeVisible()
  return dialog
}

/**
 * Picks Security in the open dialog's Type field and applies it.
 * @param page The Playwright page.
 * @param dialog The open filter dialog.
 * @returns Resolves once Apply was pressed.
 */
const applySecurity = async (page: Page, dialog: ReturnType<Page['locator']>): Promise<void> => {
  await dialog.getByRole('combobox', { name: 'Type' }).click()
  await page.getByRole('option', { name: 'Security', exact: true }).click()
  await dialog.getByRole('button', { name: 'Apply' }).click()
}

// The proposition: on a desktop the filter trigger opens a centred dialog, and applying Security
// narrows the list through the PANEL: the request carries the classification and the browser
// filters nothing itself. Breakable: a client-side filter leaves the last request without the
// parameter; a filter that never reaches the URL fails the URL assertion.
test('the filter dialog narrows the Updates list to security updates through the panel', async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 })
  await openUpdates(page, UPDATES)
  await expect(dataRows(page)).toHaveCount(3)
  expect(pendingUpdateRequests.at(-1)?.searchParams.get('classification')).toBeNull()

  const dialog = await openFilterDialog(page)
  await applySecurity(page, dialog)

  await expect(page).toHaveURL(/classification=security/)
  await expect(dataRows(page)).toHaveCount(1)
  await expect(dataRows(page).first()).toContainText('openssl')
  expect(pendingUpdateRequests.at(-1)?.searchParams.get('classification')).toBe('security')
})

// The proposition: a filter that matches nothing offers the way back and does not claim the server
// is up to date. Breakable: reusing "Nothing to update" for a narrowed empty answer.
test('a classification filter that matches nothing says no packages match, not that nothing is waiting', async ({ page }) => {
  await openUpdates(page, UPDATES.filter((row) => {
    return row.classification !== 'security'
  }))

  const dialog = await openFilterDialog(page)
  await applySecurity(page, dialog)

  await expect(page.getByText('No packages match')).toBeVisible()
  await expect(page.getByText('Nothing to update')).toHaveCount(0)
})

// The proposition: at 390px the same trigger opens a sheet anchored to the bottom edge, and applying
// there narrows the list through the panel exactly as on a desktop. Breakable: a sheet that is the
// centred dialog ends well above the bottom edge.
test('at 390px the filters open as a bottom sheet and narrow the Updates list through the panel', async ({ page }) => {
  await setPersistedLocale(page, 'en')
  await openFullShell(page)
  await stubShellReads(page)
  await stubPendingUpdates(page, UPDATES)
  await page.setViewportSize({ width: 390, height: 844 })
  // The drawer replaces the sidebar at this width, so the page is opened directly, like the card layout spec below.
  await page.goto('/servers/updates')
  await expect(dataRows(page)).toHaveCount(3)

  const dialog = await openFilterDialog(page)
  const box = await dialog.boundingBox()
  expect(box).not.toBeNull()
  expect((box?.y ?? 0) + (box?.height ?? 0)).toBeGreaterThan(844 - 2)

  await applySecurity(page, dialog)

  await expect(page).toHaveURL(/classification=security/)
  await expect(dataRows(page)).toHaveCount(1)
  expect(pendingUpdateRequests.at(-1)?.searchParams.get('classification')).toBe('security')
})

// The proposition: the security summary is said only when the panel measured it. A count is said
// with its plural, zero is a real answer, and null draws nothing. Breakable: a summary that read null
// as zero says "None of the waiting updates is a security update" about a server nobody measured.
test('the security summary appears for a measured count', async ({ page }) => {
  await openUpdates(page, UPDATES, { notices: { securityCount: 1 } })
  await expect(page.getByText('1 security update waiting')).toBeVisible()
})

test('a zero security count says none of the waiting updates is a security update', async ({ page }) => {
  await openUpdates(page, UPDATES, { notices: { securityCount: 0 } })
  await expect(page.getByText('None of the waiting updates is a security update.')).toBeVisible()
})

test('an unmeasured security count draws no security summary at all', async ({ page }) => {
  await openUpdates(page, UPDATES, { notices: { securityCount: null } })

  // Positive control: the list rendered, so the missing notice is the null's doing.
  await expect(dataRows(page)).toHaveCount(3)
  await expect(page.getByText(/security updates? waiting/)).toHaveCount(0)
  await expect(page.getByText('None of the waiting updates is a security update.')).toHaveCount(0)
})

// The proposition: the reboot notice is shown only for a measured `true`. False, not measured and a
// failed read all draw nothing, and never "no reboot needed". Breakable: a notice shown for
// `required !== false` appears on the null and failure rows.
const REBOOT_NOTICE = 'A reboot is required to finish applying updates.'

test('a required reboot is announced above the Updates list', async ({ page }) => {
  await openUpdates(page, UPDATES, { notices: { reboot: true } })
  await expect(page.getByText(REBOOT_NOTICE)).toBeVisible()
})

for (const reboot of [false, null, 'failure'] as const) {
  test(`a reboot answer of ${String(reboot)} draws no reboot notice`, async ({ page }) => {
    // The security notice is the positive control: the summary region rendered, so the absent
    // reboot notice is the answer's doing and not a summary that never drew.
    await openUpdates(page, UPDATES, { notices: { securityCount: 1, reboot } })

    await expect(page.getByText('1 security update waiting')).toBeVisible()
    await expect(dataRows(page)).toHaveCount(3)
    await expect(page.getByText(REBOOT_NOTICE)).toHaveCount(0)
    await expect(page.getByText(/reboot/i)).toHaveCount(0)
  })
}

// The proposition: when the count read fails the security summary is absent: a count the panel could not
// give is not zero. Breakable: a store that kept a stale or zero count shows the "none" sentence.
test('a failed list read draws no security summary beside the backend message', async ({ page }) => {
  await openUpdates(page, UPDATES, { failure: CACHE_UNREADABLE, notices: { securityCount: 3, reboot: true } })

  await expect(page.getByRole('status').filter({ hasText: CACHE_UNREADABLE })).toBeVisible()
  // Positive control: the reboot read still succeeds beside the failed count, so the notices region
  // rendered and the absent security summary is the failed count's doing, not a region that never drew.
  await expect(page.getByText(REBOOT_NOTICE)).toBeVisible()
  await expect(page.getByText(/security updates? waiting/)).toHaveCount(0)
  await expect(page.getByText('None of the waiting updates is a security update.')).toHaveCount(0)
})

// The proposition: when nothing is pending and no filter is set the page shows only its empty state,
// so the security notice does not render even though the panel measured a count. Breakable: a
// summary that ignored the empty list prints "None of the waiting updates is a security update"
// beside "Nothing to update".
test('an empty list with no filter shows the empty state and no security notice', async ({ page }) => {
  await openUpdates(page, [], { notices: { securityCount: 0 } })

  await expect(page.getByText('Nothing to update')).toBeVisible()
  await expect(page.getByText('None of the waiting updates is a security update.')).toHaveCount(0)
  await expect(page.getByText(/security updates? waiting/)).toHaveCount(0)
})

// The proposition: a kind the SPA has no tone for is still drawn, with the panel's own name, and no
// locale key. Breakable: a badge that looked its text up in a locale table prints a dotted key, and
// one that required a known kind prints nothing for this row.
test('a kind the panel names but the SPA knows no tone for is drawn with the panel name', async ({ page }) => {
  await openUpdates(page, [
    { packageName: 'kernel', availableVersion: '6.8.1', installedVersion: '6.8.0', source: 'noble-updates', classification: 'security' },
    { packageName: 'tzdata', availableVersion: '2025b', installedVersion: '2025a', source: 'noble-updates', classification: 'deferred' },
  ])

  await expect(dataRows(page).filter({ hasText: 'tzdata' }).getByRole('cell', { name: 'deferred', exact: true })).toHaveCount(1)
  await expect(page.getByText('monitoring.updates')).toHaveCount(0)
})

// The proposition: a `?classification=` value the panel never listed is dropped, not rendered as a
// raw value or a locale key in a chip, and the list is not narrowed by it. Breakable: a chip built
// from the URL value shows "Type: bogus" or a dotted key.
test('an unknown classification in the URL draws no chip and no raw key', async ({ page }) => {
  await setPersistedLocale(page, 'en')
  await openFullShell(page)
  await stubShellReads(page)
  await stubPendingUpdates(page, UPDATES)
  await page.setViewportSize({ width: 1440, height: 900 })
  await page.goto('/servers/updates?classification=bogus')

  // Positive control: the list rendered, so the absent chip is the unknown value's doing.
  await expect(dataRows(page)).toHaveCount(3)
  await expect(page.getByText('bogus')).toHaveCount(0)
  await expect(page.getByText('monitoring.updates')).toHaveCount(0)
})

// The proposition: a listed kind in the URL shows its chip with the panel's name.
test('a listed classification in the URL shows a chip named by the panel', async ({ page }) => {
  await setPersistedLocale(page, 'en')
  await openFullShell(page)
  await stubShellReads(page)
  await stubPendingUpdates(page, UPDATES)
  await page.setViewportSize({ width: 1440, height: 900 })
  await page.goto('/servers/updates?classification=security')

  await expect(dataRows(page)).toHaveCount(1)
  await expect(page.getByRole('button', { name: /Remove filter.*Security/ })).toBeVisible()
})
