import type { Page } from '@playwright/test'
import { pagedAnswer, type PagedRules } from './paged-answer'
import type { FileEntry, FilesOptions } from '../../src/types/file'

/** The listing endpoint, with or without a query. */
const ENTRIES_PATTERN = /\/api\/v1\/files\/entries(\?.*)?$/

/** The limits endpoint. */
const OPTIONS_PATTERN = /\/api\/v1\/files\/options(\?.*)?$/

/** The limits a stubbed panel reports; the batch cap is small so a spec can pass it. */
export const STUBBED_FILES_OPTIONS: FilesOptions = {
  maxUploadBytes: 104857600,
  maxEditBytes: 1048576,
  maxDownloadBytes: 268435456,
  maxBatchItems: 3,
  trashRetentionDays: 30,
  extractMaxEntries: 100000,
  extractMaxBytes: 2147483648,
  extractMaxRatio: 100,
  archiveFormats: ['zip', 'tar.gz'],
}

/**
 * A stubbed folder entry, the fields a spec states; the rest is filled with plain values.
 * @param directory The folder it lives in, relative to the home.
 * @param name Its name.
 * @param overrides Anything else the spec cares about.
 * @returns The entry as the panel would send it.
 */
export const fileEntry = (directory: string, name: string, overrides: Partial<FileEntry> = {}): FileEntry => {
  return {
    name,
    path: directory === '' ? name : `${directory}/${name}`,
    kind: 'file',
    sizeBytes: 2048,
    modifiedAt: '2026-10-01T10:00:00Z',
    mode: '644',
    isSymlink: false,
    ...overrides,
  }
}

/** How the stub searches and sorts a folder the way the panel's sort map names the columns. */
const RULES: PagedRules<FileEntry> = {
  matches: (row, term) => {
    return row.name.toLowerCase().includes(term)
  },
  sorts: {
    name: (row) => {
      return row.name
    },
    modified: (row) => {
      return row.modifiedAt
    },
    size: (row) => {
      return String(row.sizeBytes).padStart(12, '0')
    },
  },
  filters: {},
}

/**
 * Serves the folder listing from a tree of folders, answering as the panel does: the requested
 * folder's entries, dot-names only when `showHidden=true`, folders first under the default order,
 * then the usual search, sort and page. A path containing `..` is refused with a 400 problem, the
 * way the server's path rule would, so a spec can watch the SPA send a path as typed.
 * @param page The Playwright page whose network the route is installed on.
 * @param tree The entries of each folder, by the folder's path ('' is the home).
 * @returns A function reporting every listing request so far, oldest first.
 */
export const stubFileEntries = async (
  page: Page,
  tree: Readonly<Record<string, readonly FileEntry[]>>,
): Promise<() => readonly URL[]> => {
  const requests: URL[] = []
  await page.route(ENTRIES_PATTERN, async (route) => {
    const url = new URL(route.request().url())
    requests.push(url)
    const folder = url.searchParams.get('path') ?? ''
    if (folder.split('/').includes('..')) {
      await route.fulfill({
        status: 400,
        contentType: 'application/problem+json',
        body: JSON.stringify({ code: 'FilesPathInvalid', title: 'Invalid path', detail: 'That path is not allowed.' }),
      })
      return
    }
    const showHidden = url.searchParams.get('showHidden') === 'true'
    const rows = [...(tree[folder] ?? [])]
      .filter((entry) => {
        return showHidden || !entry.name.startsWith('.')
      })
      .sort((left, right) => {
        return Number(right.kind === 'directory') - Number(left.kind === 'directory')
      })
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(pagedAnswer(rows, url.toString(), RULES)),
    })
  })
  return () => {
    return requests
  }
}

/**
 * Fulfils the listing with a problem body, so a spec can assert the screen shows the panel's own
 * already-localized text, as it does for another tenant's account (404).
 * @param page The Playwright page whose network the route is installed on.
 * @param status The HTTP status.
 * @param detail The localized message in `detail`.
 * @returns Resolves once the route is installed.
 */
export const stubFileEntriesProblem = async (page: Page, status: number, detail: string): Promise<void> => {
  await page.route(ENTRIES_PATTERN, async (route) => {
    await route.fulfill({
      status,
      contentType: 'application/problem+json',
      body: JSON.stringify({ code: 'AccountNotFound', title: 'Not found', detail }),
    })
  })
}

/**
 * Fulfils the limits endpoint.
 * @param page The Playwright page whose network the route is installed on.
 * @returns Resolves once the route is installed.
 */
export const stubFilesOptions = async (page: Page): Promise<void> => {
  await page.route(OPTIONS_PATTERN, async (route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(STUBBED_FILES_OPTIONS) })
  })
}
