import { expectTablesAsCards, type CardActions } from '../fixtures/expect-table-cards'
import { expect, test } from '../fixtures/guarded-test'
import { expectNoHorizontalOverflow } from '../fixtures/measure-horizontal-extent'
import { READY_SERVER, discoveredAccount, stubRemoteServers } from '../fixtures/migration'
import { setPersistedLocale } from '../fixtures/set-locale'
import {
  ALICE_ACCOUNT,
  EXPORT_ID,
  IMPORT_ID,
  STAGED_IMPORT,
  exportOf,
  grantOf,
  openAccountMoveScreen,
} from '../fixtures/stub-account-move-routes'
import { DRAFT_JOB, JOB_ID, openMigrationJobs } from '../fixtures/stub-migration-jobs'
import type { Page } from '@playwright/test'

// The phone-width contract (issue #82) for the server-migration screens (issues #127 and #197): no
// screen scrolls sideways at 390px in any of the three languages. Host names, key fingerprints,
// package checksums and account names are long and unbreakable by nature, and the Russian and
// Armenian labels are longer than the English ones. The route census
// (`ui-kit/route-coverage.spec.ts`) reads the `path:` literals of this file, so a migration route
// added without a row fails there.

/** The phone viewport the criterion names. */
const PHONE = { width: 390, height: 844 }

/** The interface languages the panel ships. */
const LOCALES = ['en', 'ru', 'hy'] as const

/** One screen to measure, and how to know its content is drawn. */
interface Screen {
  /** Name used in the generated test title. */
  name: string
  /** The path to open. */
  path: string
  /** For a screen with tables: whether they are cards with an actions column. */
  cards?: CardActions
  /** Text the stubbed data prints once the screen has loaded; absent for a form that shows none. */
  loaded?: string
}

const SCREENS: Screen[] = [
  { name: 'the remote servers list', path: '/migration/remote-servers', cards: 'none', loaded: READY_SERVER.name },
  { name: 'the new remote server form', path: '/migration/remote-servers/new' },
  { name: 'the remote server page', path: `/migration/remote-servers/${READY_SERVER.id}`, loaded: READY_SERVER.name },
  { name: 'the remote server edit form', path: `/migration/remote-servers/${READY_SERVER.id}/edit` },
  { name: 'the server migrations list', path: '/migration/jobs', cards: 'none', loaded: 'Move the old server' },
  { name: 'the new server migration form', path: '/migration/jobs/new' },
  { name: 'the server migration page', path: `/migration/jobs/${JOB_ID}`, loaded: 'Move the old server' },
  { name: 'the account exports list', path: '/migration/account-exports', cards: 'none', loaded: 'alice' },
  { name: 'the account imports list', path: '/migration/account-imports', cards: 'none', loaded: 'alice' },
  { name: 'the pull grants list', path: '/migration/export-grants', cards: 'none', loaded: 'alice' },
  { name: 'the account export form', path: '/migration/account-export' },
  { name: 'the account export page', path: `/migration/account-export/${EXPORT_ID}`, loaded: 'alice' },
  { name: 'the account import form', path: '/migration/account-import' },
  { name: 'the account import page', path: `/migration/account-import/${IMPORT_ID}`, loaded: 'alice' },
]

/**
 * Signs an administrator in with every migration read answered, then opens one address at a phone's width.
 * @param page The page to drive.
 * @param locale The interface language to persist before the first load.
 * @param path The address to open.
 * @returns Resolves once the address has been requested.
 */
const openAtPhoneWidth = async (page: Page, locale: (typeof LOCALES)[number], path: string): Promise<void> => {
  await setPersistedLocale(page, locale)
  // The account-move stubs sign in and answer the pickers; the jobs stub adds the job endpoints; the
  // remote server stub is registered last. Each answers only its own endpoints.
  await openAccountMoveScreen(page, {
    exports: [exportOf(ALICE_ACCOUNT)],
    imports: [STAGED_IMPORT],
    grants: [grantOf({})],
  })
  // The job reads the registered server it moves from, which is the one stubbed below.
  await openMigrationJobs(page, { job: { ...DRAFT_JOB, remoteServerId: READY_SERVER.id } })
  await stubRemoteServers(page, {
    servers: [READY_SERVER],
    accounts: { [READY_SERVER.id]: [discoveredAccount('alice')] },
  })
  await page.setViewportSize(PHONE)
  await page.goto(path)
}

for (const screen of SCREENS) {
  for (const locale of LOCALES) {
    test(`${screen.name} does not scroll sideways at 390px in ${locale}`, async ({ page }) => {
      await openAtPhoneWidth(page, locale, screen.path)

      await expect(page.getByRole('heading', { level: 1 })).toBeVisible()
      if (screen.loaded !== undefined) {
        // Positive control on readiness: the stubbed data is on screen, so the measurement below is of
        // the loaded screen and not of its skeleton.
        await expect(page.getByRole('main').getByText(screen.loaded).first()).toBeVisible()
      }
      await expectNoHorizontalOverflow(page)

      if (screen.cards !== undefined) {
        await expectTablesAsCards(page, screen.cards)
      }
    })
  }
}
