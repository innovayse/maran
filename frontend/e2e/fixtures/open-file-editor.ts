import type { Page } from '@playwright/test'
import { stubAccountNames, stubAccounts } from './stub-accounts-route'
import { stubSignedIn } from './stub-auth-routes'
import { stubFileEditorRoutes, type FileEditorStub, type StubbedTextFile } from './stub-file-editor-routes'
import { stubHealthy } from './stub-health-route'
import { stubModules } from './stub-modules-route'
import { stubShellReads } from './stub-shell-reads'
import type { Account } from '../../src/types/account'
import type { PanelModule } from '../../src/types/module'

/** The licence list that has the file manager on. */
const LICENSED: PanelModule[] = [{ name: 'files', displayName: 'File manager', kindDisplayName: 'Core', tier: 'included', state: 'enabled' }]

/** The account whose file the editor specs open. */
export const EDITOR_ACCOUNT: Account = {
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

/** The file the pretend disk starts with unless a spec gives other text. */
export const INITIAL_FILE: StubbedTextFile = { content: 'debug = false\n', modifiedAt: '2026-11-07T09:00:00Z', mode: '644' }

/** What a spec may steer when it opens the editor. */
export interface OpenFileEditorOptions {
  /** The text the pretend disk holds at the start. */
  content?: string
  /** The file's path inside the account's home; its extension picks the highlighting. */
  path?: string
  /** While pending, the stubbed read of the file waits for it. */
  holdReads?: Promise<void>
}

/**
 * The address of the editor on a file of {@link EDITOR_ACCOUNT}, the way a link from the file manager builds it.
 * @param path The file's path inside the account.
 * @returns The path and query to open.
 */
export const fileEditorAddress = (path: string): string => {
  return `/files/edit?account=${EDITOR_ACCOUNT.id}&path=${encodeURIComponent(path)}`
}

/**
 * Opens the editor on the stubbed file.
 * @param page The page to drive.
 * @param options What to hold, and which text and path to open.
 * @returns The stub, to read recorded saves from and to steer the disk with.
 */
export const openFileEditor = async (page: Page, options: OpenFileEditorOptions = {}): Promise<FileEditorStub> => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubShellReads(page)
  await stubAccounts(page, [EDITOR_ACCOUNT])
  // The folder the "back" link lands on names the account it was opened for.
  await stubAccountNames(page, [EDITOR_ACCOUNT])
  // The folder the "back" link lands on; an empty listing is enough for a spec about leaving.
  await page.route(/\/api\/v1\/files\/entries(\?.*)?$/, async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({ items: [], total: 0, page: 1, pageSize: 25 }),
    })
  })
  const stub = await stubFileEditorRoutes(page, { ...INITIAL_FILE, content: options.content ?? INITIAL_FILE.content })
  stub.readsHeld = options.holdReads ?? null
  await page.goto(fileEditorAddress(options.path ?? 'www/config.txt'))
  return stub
}
