import { expect, test } from '../fixtures/guarded-test'
import {
  ALICE_ACCOUNT,
  EXPORT_ID,
  GRANT_ID,
  IMPORT_ID,
  ROLLED_BACK_CODE,
  ROLLED_BACK_NAME,
  exportOf,
  grantOf,
  importOf,
  openAccountExports,
  openAccountImports,
  openAccountMoveScreen,
  openExportGrants,
} from '../fixtures/stub-account-move-routes'
import type { AccountExport } from '../../src/types/accountMove'
import type { Page } from '@playwright/test'

// The three server-paged lists of the account move (issue #127): the exports made, the imports
// staged or run, and the pull grants issued. Every screen is reached by clicking, from the sidebar
// down. The stub answers with the contract's shapes and pages, searches, filters and sorts the way
// the panel does, so a spec reads back what the page asked of it: the browser never narrows a list
// itself. Each narrowing control has its inverse (the control cleared, the row back).

/**
 * An export of a differently named account, shaped like the panel's `AccountExportDto`.
 * @param name The account's system user name.
 * @param index Which export this is; it makes the identity unique and the order stable.
 * @returns The export.
 */
const exportNamed = (name: string, index: number): AccountExport => {
  const id = `e0e0e0e0-0000-4000-8000-${index.toString().padStart(12, '0')}`
  return { ...exportOf(ALICE_ACCOUNT), id, accountName: name }
}

/**
 * Applies one state in a list's filter sheet.
 * @param page The Playwright page under test.
 * @param state The state's name as the sheet shows it.
 * @returns Resolves once the sheet is applied.
 */
const filterByState = async (page: Page, state: string): Promise<void> => {
  await page.getByTestId('filter-trigger').click()
  const sheet = page.getByRole('dialog', { name: 'Filters' })
  await sheet.getByRole('combobox', { name: 'State' }).click()
  await page.getByRole('option', { name: state, exact: true }).click()
  await sheet.getByRole('button', { name: 'Apply' }).click()
}

test.describe('the exports list', () => {
  test('pages, searches and sorts on the panel, never in the browser', async ({ page }) => {
    const exports = Array.from({ length: 30 }, (_, index) => {
      return exportNamed(`acct${(index + 1).toString().padStart(2, '0')}`, index + 1)
    })
    const state = await openAccountMoveScreen(page, { exports })
    await openAccountExports(page)

    // Paging: 25 of 30 on the first page, and the panel was asked for the second.
    await expect(page.getByRole('link', { name: 'acct01', exact: true })).toBeVisible()
    await expect(page.getByRole('link', { name: 'acct26', exact: true })).toHaveCount(0)
    await page.getByRole('navigation', { name: 'Pagination' }).getByRole('button', { name: 'Next page' }).click()
    await expect(page.getByRole('link', { name: 'acct26', exact: true })).toBeVisible()
    expect(
      state.requests.some((request) => {
        return request.url.pathname === '/api/v1/account-exports' && request.url.searchParams.get('page') === '2'
      }),
    ).toBe(true)

    // Search: by account name, and the panel was asked.
    await page.getByRole('searchbox', { name: 'Search exports' }).fill('acct07')
    await expect(page.getByRole('link', { name: 'acct07', exact: true })).toBeVisible()
    await expect(page.getByRole('link', { name: 'acct08', exact: true })).toHaveCount(0)
    expect(
      state.requests.some((request) => {
        return request.url.searchParams.get('search') === 'acct07'
      }),
    ).toBe(true)

    // The inverse: with the search cleared the rows are back.
    await page.getByRole('searchbox', { name: 'Search exports' }).fill('')
    await expect(page.getByRole('link', { name: 'acct08', exact: true })).toBeVisible()

    // Sort: the account column, descending after two presses, asked of the panel.
    const account = page.getByRole('columnheader', { name: 'Account' }).getByRole('button')
    await account.click()
    await account.click()
    await expect(page).toHaveURL(/sort=-accountName/)
    expect(
      state.requests.some((request) => {
        return request.url.searchParams.get('sort') === '-accountName'
      }),
    ).toBe(true)
    await expect(page.getByRole('link', { name: 'acct30', exact: true })).toBeVisible()
  })

  test('says there are no exports and offers the first one', async ({ page }) => {
    await openAccountMoveScreen(page)
    await openAccountExports(page)

    await expect(page.getByText('No exports yet')).toBeVisible()
    // Two ways to the form in the screen's content: the heading's action and the empty state's own.
    await expect(page.getByRole('main').getByRole('link', { name: 'Export an account' })).toHaveCount(2)
  })

  test('shows no-results, not the empty state, when a search matches nothing, and the way back brings the rows again', async ({ page }) => {
    await openAccountMoveScreen(page, { exports: [exportNamed('alice', 1)] })
    await openAccountExports(page)
    await expect(page.getByRole('link', { name: 'alice', exact: true })).toBeVisible()

    await page.getByRole('searchbox', { name: 'Search exports' }).fill('nobody')

    await expect(page.getByText('No exports match')).toBeVisible()
    await expect(page.getByText('No exports yet')).toHaveCount(0)
    await page.getByRole('button', { name: 'Clear search and filters' }).click()
    await expect(page.getByRole('link', { name: 'alice', exact: true })).toBeVisible()
  })

  test('opens an export from its row', async ({ page }) => {
    await openAccountMoveScreen(page, { exports: [exportNamed('alice', 1)] })
    await openAccountExports(page)

    await page.getByRole('link', { name: 'alice', exact: true }).click()

    await expect(page.getByRole('heading', { level: 1, name: 'Export of alice' })).toBeVisible()
  })
})

test.describe('the imports list', () => {
  test('lists the imports with their state and narrows by state through the filter sheet', async ({ page }) => {
    const state = await openAccountMoveScreen(page, {
      imports: [
        importOf({ id: IMPORT_ID, accountName: 'alice', state: 'Completed' }),
        importOf({ id: 'b2b2b2b2-0000-4000-8000-000000000002', accountName: 'bob', state: 'RolledBack' }),
      ],
    })
    await openAccountImports(page)

    await expect(page.getByRole('link', { name: 'alice', exact: true })).toBeVisible()
    await expect(page.getByRole('link', { name: 'bob', exact: true })).toBeVisible()

    await filterByState(page, 'Rolled back')

    await expect(page).toHaveURL(/state=RolledBack/)
    await expect(page.getByTestId('active-filter-chips')).toContainText('State: Rolled back')
    await expect(page.getByRole('link', { name: 'bob', exact: true })).toBeVisible()
    await expect(page.getByRole('link', { name: 'alice', exact: true })).toHaveCount(0)
    expect(
      state.requests.some((request) => {
        return request.url.pathname === '/api/v1/account-imports' && request.url.searchParams.get('state') === 'RolledBack'
      }),
    ).toBe(true)

    // The inverse: removing the chip brings the other import back.
    await page.getByRole('button', { name: 'Remove filter: State: Rolled back' }).click()
    await expect(page).not.toHaveURL(/state=/)
    await expect(page.getByRole('link', { name: 'alice', exact: true })).toBeVisible()
  })

  test('searches by the sending server too, on the panel', async ({ page }) => {
    const state = await openAccountMoveScreen(page, {
      imports: [
        importOf({ id: IMPORT_ID, accountName: 'alice', sourceHostName: 'old-panel.example.net' }),
        importOf({ id: 'b2b2b2b2-0000-4000-8000-000000000002', accountName: 'bob', sourceHostName: 'other.example.org' }),
      ],
    })
    await openAccountImports(page)

    await page.getByRole('searchbox', { name: 'Search imports' }).fill('other.example')

    await expect(page.getByRole('link', { name: 'bob', exact: true })).toBeVisible()
    await expect(page.getByRole('link', { name: 'alice', exact: true })).toHaveCount(0)
    expect(
      state.requests.some((request) => {
        return request.url.searchParams.get('search') === 'other.example'
      }),
    ).toBe(true)
  })

  test('links only a running import to its task, narrowed to the import kind and the account', async ({ page }) => {
    await openAccountMoveScreen(page, {
      imports: [
        importOf({ id: IMPORT_ID, accountName: 'alice', state: 'Running', stage: 'sites' }),
        importOf({ id: 'b2b2b2b2-0000-4000-8000-000000000002', accountName: 'bob', state: 'Completed' }),
      ],
    })
    await openAccountImports(page)

    const running = page.getByRole('row').filter({ has: page.getByRole('link', { name: 'alice', exact: true }) })
    await expect(running.getByRole('link', { name: 'Open the background task of the import of alice' })).toHaveAttribute(
      'href',
      /\/tasks\?(?=.*kind=AccountImport)(?=.*search=alice)/,
    )
    // The inverse: a finished import has no task to follow.
    const finished = page.getByRole('row').filter({ has: page.getByRole('link', { name: 'bob', exact: true }) })
    await expect(finished.getByRole('link', { name: /background task/ })).toHaveCount(0)
  })

  test('links the running import on its own screen to its task', async ({ page }) => {
    await openAccountMoveScreen(page, {
      imports: [importOf({ id: IMPORT_ID, accountName: 'alice', state: 'Running', stage: 'sites' })],
      runningReads: 1000,
    })
    await openAccountImports(page)

    await page.getByRole('link', { name: 'alice', exact: true }).click()

    await expect(page.getByTestId('import-progress')).toBeVisible()
    await expect(page.getByRole('link', { name: 'Follow it in the background tasks' })).toHaveAttribute(
      'href',
      /\/tasks\?(?=.*kind=AccountImport)(?=.*search=alice)/,
    )
  })

  test("shows the panel's sentence for a failure, with the code beneath it, and none for an import that did not fail", async ({ page }) => {
    await openAccountMoveScreen(page, {
      imports: [
        importOf({
          id: IMPORT_ID,
          accountName: 'alice',
          state: 'RolledBack',
          failureCode: ROLLED_BACK_CODE,
          failureName: ROLLED_BACK_NAME,
          finishedAt: '2026-11-09T10:09:00Z',
        }),
        importOf({ id: 'b2b2b2b2-0000-4000-8000-000000000002', accountName: 'bob', state: 'Completed', finishedAt: '2026-11-09T10:09:00Z' }),
      ],
    })
    await openAccountImports(page)

    await page.getByRole('link', { name: 'alice', exact: true }).click()
    await expect(page.getByTestId('import-failure-name')).toHaveText(ROLLED_BACK_NAME)
    await expect(page.getByText(`Code: ${ROLLED_BACK_CODE}`)).toBeVisible()

    // The inverse: an import that did not fail shows neither.
    await openAccountImports(page)
    await page.getByRole('link', { name: 'bob', exact: true }).click()
    await expect(page.getByTestId('import-outcome')).toBeVisible()
    await expect(page.getByTestId('import-failure-name')).toHaveCount(0)
    await expect(page.getByText(/^Code: /)).toHaveCount(0)
  })

  test('says there are no imports and offers the first one', async ({ page }) => {
    await openAccountMoveScreen(page)
    await openAccountImports(page)

    await expect(page.getByText('No imports yet')).toBeVisible()
    // Two ways to the form in the screen's content: the heading's action and the empty state's own.
    await expect(page.getByRole('main').getByRole('link', { name: 'Import an account' })).toHaveCount(2)
  })
})

test.describe('the pull grants list', () => {
  test('lists the grants without a token and narrows by state through the filter sheet', async ({ page }) => {
    const state = await openAccountMoveScreen(page, {
      grants: [
        grantOf({ id: GRANT_ID, accountName: 'alice', state: 'Live' }),
        grantOf({ id: 'a1a1a1a1-0000-4000-8000-000000000002', accountName: 'bob', state: 'Used', manifestReads: 2, artifactConsumedAt: '2026-11-09T10:12:00Z' }),
      ],
    })
    await openExportGrants(page)

    await expect(page.getByRole('link', { name: 'alice', exact: true })).toBeVisible()
    await expect(page.getByRole('link', { name: 'bob', exact: true })).toBeVisible()

    await filterByState(page, 'Used')

    await expect(page).toHaveURL(/state=Used/)
    await expect(page.getByTestId('active-filter-chips')).toContainText('State: Used')
    await expect(page.getByRole('link', { name: 'bob', exact: true })).toBeVisible()
    await expect(page.getByRole('link', { name: 'alice', exact: true })).toHaveCount(0)
    expect(
      state.requests.some((request) => {
        return request.url.pathname === '/api/v1/account-exports/grants' && request.url.searchParams.get('state') === 'Used'
      }),
    ).toBe(true)

    await page.getByRole('button', { name: 'Remove filter: State: Used' }).click()
    await expect(page.getByRole('link', { name: 'alice', exact: true })).toBeVisible()
  })

  test('offers revoking on a live grant only, and only after the confirmation', async ({ page }) => {
    const state = await openAccountMoveScreen(page, {
      grants: [
        grantOf({ id: GRANT_ID, accountName: 'alice', state: 'Live' }),
        grantOf({ id: 'a1a1a1a1-0000-4000-8000-000000000002', accountName: 'bob', state: 'Used' }),
      ],
    })
    await openExportGrants(page)

    // The inverse first: a used grant has no actions to offer.
    await expect(page.getByRole('button', { name: 'Actions for the grant of bob' })).toHaveCount(0)

    await page.getByRole('button', { name: 'Actions for the grant of alice' }).click()
    await page.getByRole('menuitem', { name: 'Revoke' }).click()
    await page.getByRole('dialog', { name: 'Revoke this grant?' }).getByRole('button', { name: 'Cancel' }).click()
    expect(state.revokedGrants).toHaveLength(0)

    await page.getByRole('button', { name: 'Actions for the grant of alice' }).click()
    await page.getByRole('menuitem', { name: 'Revoke' }).click()
    await page.getByRole('dialog', { name: 'Revoke this grant?' }).getByRole('button', { name: 'Revoke grant' }).click()

    // The page read its rows again, so the state shown is the panel's.
    const row = page.getByRole('row').filter({ has: page.getByRole('link', { name: 'alice', exact: true }) })
    await expect(row.getByText('Revoked', { exact: true })).toBeVisible()
    await expect(page.getByRole('button', { name: 'Actions for the grant of alice' })).toHaveCount(0)
    expect(state.revokedGrants).toEqual([GRANT_ID])
  })

  test('is reached from an export with the account as the search, and finds a grant issued there', async ({ page }) => {
    const state = await openAccountMoveScreen(page, {
      exports: [exportNamed('alice', 1)],
      grants: [grantOf({ id: 'a1a1a1a1-0000-4000-8000-000000000002', accountName: 'bob', state: 'Used' })],
    })
    await openAccountExports(page)
    await page.getByRole('link', { name: 'alice', exact: true }).click()
    await page.getByRole('button', { name: 'Issue grant' }).click()
    await page.getByRole('dialog', { name: 'Pull grant issued' }).getByRole('button', { name: 'I have copied it' }).click()

    await page.getByRole('link', { name: 'See the grants of alice' }).click()

    await expect(page.getByRole('heading', { level: 1, name: 'Pull grants' })).toBeVisible()
    await expect(page).toHaveURL(/search=alice/)
    expect(
      state.requests.some((request) => {
        return request.url.pathname === '/api/v1/account-exports/grants' && request.url.searchParams.get('search') === 'alice'
      }),
    ).toBe(true)
    await expect(page.getByRole('link', { name: 'alice', exact: true })).toBeVisible()
    // The inverse: another account's grant is not in an alice-only list.
    await expect(page.getByRole('link', { name: 'bob', exact: true })).toHaveCount(0)
    expect(state.grants.some((grant) => {
      return grant.exportId === EXPORT_ID
    })).toBe(true)
  })

  test('says there are no grants and sends the administrator to the exports', async ({ page }) => {
    await openAccountMoveScreen(page)
    await openExportGrants(page)

    await expect(page.getByText('No pull grants yet')).toBeVisible()
    await expect(page.getByRole('link', { name: 'Go to the exports' })).toBeVisible()
  })
})
