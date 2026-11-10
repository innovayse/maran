import { expect, test } from '../fixtures/guarded-test'
import { expectNoHorizontalOverflow } from '../fixtures/measure-horizontal-extent'
import { setPersistedLocale } from '../fixtures/set-locale'
import { stubAccountNames, stubAccounts } from '../fixtures/stub-accounts-route'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import {
  fileEntry,
  stubFileEntries,
  stubFileEntriesProblem,
  stubFilesOptions,
} from '../fixtures/stub-file-browser-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import { stubShellReads } from '../fixtures/stub-shell-reads'
import type { Account } from '../../src/types/account'
import type { FileEntry } from '../../src/types/file'
import type { PanelModule } from '../../src/types/module'
import type { Page } from '@playwright/test'

// The file browser lists one folder a server page at a time: the account, the folder, the dot-name
// toggle, the search, the sort and the page live in the URL, and the SPA filters nothing itself.

const LICENSED_FILES: PanelModule[] = [{ name: 'files', displayName: 'Files', kindDisplayName: 'Core', tier: 'included', state: 'enabled' }]

const PHONE = { width: 390, height: 844 }

/** The address the 390px sweep below opens; `e2e/ui-kit/route-coverage.spec.ts` reads it from here. */
const SWEPT_SCREEN = { path: '/files' }

const ALICE: Account = {
  id: '22222222-2222-2222-2222-222222222222',
  name: 'alice',
  primaryDomain: 'alice.example.com',
  planId: '44444444-4444-4444-4444-444444444444',
  status: 'active',
  statusName: 'Active',
  createdAt: '2026-08-01T10:00:00Z',
  resellerId: null,
  resellerName: null,
}

/** The home: one folder, three files, one dot-name. */
const HOME: FileEntry[] = [
  fileEntry('', 'public_html', { kind: 'directory', sizeBytes: 4096, mode: '755' }),
  fileEntry('', 'notes.txt', { sizeBytes: 1536 }),
  fileEntry('', 'backup.tar.gz', { sizeBytes: 5242880 }),
  fileEntry('', 'readme.md', { sizeBytes: 300 }),
  fileEntry('', '.profile', { sizeBytes: 220 }),
]

/** The folder inside the home. */
const PUBLIC_HTML: FileEntry[] = [fileEntry('public_html', 'index.html', { sizeBytes: 900 })]

/**
 * Signs in as the administrator, licenses the files module and serves the tree.
 * @param page The Playwright page.
 * @param tree The entries of each folder.
 * @returns Every listing request the page made so far.
 */
const openWith = async (page: Page, tree: Record<string, FileEntry[]>): Promise<() => readonly URL[]> => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED_FILES)
  await stubShellReads(page)
  await stubAccounts(page, [ALICE])
  await stubAccountNames(page, [ALICE])
  await stubFilesOptions(page)
  return stubFileEntries(page, tree)
}

test('the browser opens on the first account and lists its home, folders first', async ({ page }) => {
  await openWith(page, { '': HOME })

  await page.goto('/files')

  const rows = page.getByRole('row')
  await expect(rows.filter({ hasText: 'public_html' })).toContainText('Folder')
  await expect(rows.filter({ hasText: 'notes.txt' })).toContainText('1.5 KiB')
  await expect(rows.filter({ hasText: 'backup.tar.gz' })).toContainText('5 MiB')
  await expect(page).toHaveURL(new RegExp(`account=${ALICE.id}`))
  await expect(page.getByRole('row').filter({ hasText: '.profile' })).toHaveCount(0)
  // Folders come first under the default order.
  await expect(page.getByRole('row').nth(1)).toContainText('public_html')
})

test('opening a folder puts it in the URL and the path bar, and Up returns', async ({ page }) => {
  await openWith(page, { '': HOME, public_html: PUBLIC_HTML })

  await page.goto('/files')
  await page.getByRole('link', { name: 'public_html' }).click()

  await expect(page).toHaveURL(/path=public_html/)
  await expect(page.getByRole('row').filter({ hasText: 'index.html' })).toBeVisible()
  const pathBar = page.getByRole('navigation', { name: 'Folder path' })
  await expect(pathBar.getByText('public_html')).toHaveAttribute('aria-current', 'page')

  await page.getByRole('button', { name: 'Up', exact: true }).click()

  await expect(page).not.toHaveURL(/path=/)
  await expect(page.getByRole('row').filter({ hasText: 'notes.txt' })).toBeVisible()
})

test('the path bar links back to the home', async ({ page }) => {
  await openWith(page, { '': HOME, public_html: PUBLIC_HTML })

  await page.goto('/files?path=public_html')
  await page.getByRole('navigation', { name: 'Folder path' }).getByRole('link', { name: 'Home' }).click()

  await expect(page.getByRole('row').filter({ hasText: 'notes.txt' })).toBeVisible()
})

test('the hidden toggle reaches the URL and asks the panel for dot-names', async ({ page }) => {
  const requests = await openWith(page, { '': HOME })

  await page.goto('/files')
  await page.getByRole('switch', { name: 'Show hidden files' }).click()

  await expect(page.getByRole('row').filter({ hasText: '.profile' })).toBeVisible()
  await expect(page).toHaveURL(/hidden=1/)
  expect(
    requests().some((url) => {
      return url.searchParams.get('showHidden') === 'true'
    }),
  ).toBe(true)
})

test('a name search reaches the URL, asks the panel and survives a reload', async ({ page }) => {
  const requests = await openWith(page, { '': HOME })

  await page.goto('/files')
  await page.getByRole('searchbox', { name: 'Search this folder' }).fill('notes')

  await expect(page).toHaveURL(/search=notes/)
  await expect(page.getByRole('row').filter({ hasText: 'notes.txt' })).toBeVisible()
  await expect(page.getByRole('row').filter({ hasText: 'readme.md' })).toHaveCount(0)
  expect(
    requests().some((url) => {
      return url.searchParams.get('search') === 'notes'
    }),
  ).toBe(true)

  await page.reload()
  await expect(page.getByRole('searchbox', { name: 'Search this folder' })).toHaveValue('notes')
})

test('a search matching nothing says so and offers the way back', async ({ page }) => {
  await openWith(page, { '': HOME })

  await page.goto('/files?search=zzz')

  await expect(page.getByText('No files match')).toBeVisible()
  await page.getByRole('button', { name: 'Clear search and filters' }).click()
  await expect(page.getByRole('row').filter({ hasText: 'notes.txt' })).toBeVisible()
})

test('pressing the Size heading sorts on the panel', async ({ page }) => {
  const requests = await openWith(page, { '': HOME })

  await page.goto('/files')
  await page.getByRole('columnheader', { name: 'Size' }).getByRole('button').click()

  await expect(page).toHaveURL(/sort=size/)
  expect(
    requests().some((url) => {
      return url.searchParams.get('sort') === 'size'
    }),
  ).toBe(true)
})

test('a large folder is paged by the panel', async ({ page }) => {
  const many = Array.from({ length: 30 }, (_, index) => {
    return fileEntry('', `file-${String(index + 1).padStart(2, '0')}.txt`)
  })
  await openWith(page, { '': many })

  await page.goto('/files?pageSize=10')

  await expect(page.getByRole('row').filter({ hasText: 'file-10.txt' })).toBeVisible()
  await expect(page.getByRole('row').filter({ hasText: 'file-11.txt' })).toHaveCount(0)
  await page.getByRole('button', { name: 'Next page' }).click()
  await expect(page).toHaveURL(/page=2/)
  await expect(page.getByRole('row').filter({ hasText: 'file-11.txt' })).toBeVisible()
})

test('ticking rows shows the bulk bar and a selection past the batch cap disables its actions', async ({ page }) => {
  await openWith(page, { '': HOME })

  await page.goto('/files')
  await page.getByRole('checkbox', { name: 'Select notes.txt' }).check({ force: true })
  await expect(page.getByTestId('bulk-bar')).toContainText('1 selected')
  await expect(page.getByRole('button', { name: 'Move to trash' })).toBeEnabled()

  await page.getByRole('checkbox', { name: 'Select readme.md' }).check({ force: true })
  await page.getByRole('checkbox', { name: 'Select backup.tar.gz' }).check({ force: true })
  await page.getByRole('checkbox', { name: 'Select public_html' }).check({ force: true })

  await expect(page.getByTestId('bulk-bar')).toContainText('4 selected')
  await expect(page.getByRole('button', { name: 'Move to trash' })).toBeDisabled()
  await expect(page.getByText('at most 3 items')).toBeVisible()
})

test('the keyboard walks the names with the arrows and ticks a row with Space', async ({ page }) => {
  await openWith(page, { '': HOME })

  await page.goto('/files')
  await page.getByRole('link', { name: 'public_html' }).focus()
  await page.keyboard.press('ArrowDown')
  await expect(page.getByRole('button', { name: 'notes.txt', exact: true })).toBeFocused()

  await page.keyboard.press('Space')

  await expect(page.getByRole('checkbox', { name: 'Select notes.txt' })).toBeChecked()
  await page.keyboard.press('ArrowUp')
  await expect(page.getByRole('link', { name: 'public_html' })).toBeFocused()
})

test('a path typed into the address is sent as typed and the panel refusal is shown', async ({ page, guard }) => {
  guard.allow(400, /\/api\/v1\/files\/entries(\?.*)?$/)
  const requests = await openWith(page, { '': HOME })

  await page.goto('/files?path=a/../../etc')

  await expect(page.getByRole('status').filter({ hasText: 'That path is not allowed.' })).toBeVisible()
  await expect(page.getByRole('table')).toHaveCount(0)
  expect(
    requests().some((url) => {
      return url.searchParams.get('path') === 'a/../../etc'
    }),
  ).toBe(true)
})

test("another tenant's account is answered 404 and shown as the panel's text, never as rows", async ({
  page,
  guard,
}) => {
  guard.allow(404, /\/api\/v1\/files\/entries(\?.*)?$/)
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED_FILES)
  await stubShellReads(page)
  await stubAccounts(page, [ALICE])
  await stubAccountNames(page, [ALICE])
  await stubFilesOptions(page)
  await stubFileEntriesProblem(page, 404, 'The account was not found.')

  await page.goto(`/files?account=${ALICE.id}`)

  await expect(page.getByRole('status').filter({ hasText: 'The account was not found.' })).toBeVisible()
  await expect(page.getByRole('table')).toHaveCount(0)
})

for (const locale of ['en', 'ru', 'hy'] as const) {
  test(`the browser fits a 390px screen in ${locale}`, async ({ page }) => {
    await setPersistedLocale(page, locale)
    await page.setViewportSize(PHONE)
    await openWith(page, {
      '': [
        ...HOME,
        fileEntry('', 'a-very-long-file-name-that-has-no-break-points-at-all-and-keeps-going-on-and-on.tar.gz'),
      ],
    })

    await page.goto(SWEPT_SCREEN.path)
    await expect(page.getByRole('table')).toBeVisible()

    await expectNoHorizontalOverflow(page)
  })
}
