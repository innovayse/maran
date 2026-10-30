import type { Page } from '@playwright/test'
import { pagedAnswer, type PagedRules } from './paged-answer'
import type { CreatedDatabase, Database, DatabasePassword } from '../../src/types/database'

/** The collection endpoint the databases list and the create form both talk to, with or without a query. */
const DATABASES_PATTERN = /\/api\/v1\/databases(\?.*)?$/

/** The single-database endpoint, which answers the drop. */
const DATABASE_PATTERN = '**/api/v1/databases/*'

/** The password-reset endpoint. A `*` never spans a `/`, so this is narrower than the one above. */
const DATABASE_PASSWORD_PATTERN = '**/api/v1/databases/*/password'

/** The account prefix the stubs build a full name with, mirroring what the server would hold. */
const ACCOUNT_PREFIX = 'alice'

/** The password a stubbed create answers with — a value no other fixture in the suite uses. */
export const stubbedCreatedPassword = 'Cr34ted-P4ssw0rd-Once'

/** The password a stubbed reset answers with, distinct from the created one so a spec can tell them apart. */
export const stubbedResetPassword = 'R3set-P4ssw0rd-Once'

/** How the databases endpoint searches, sorts and filters, as the panel does. */
const DATABASE_RULES: PagedRules<Database> = {
  matches: (database, term) => {
    return database.fullName.toLowerCase().includes(term)
  },
  sorts: {
    name: (database) => {
      return database.fullName
    },
    createdAt: (database) => {
      return database.createdAt
    },
  },
  filters: {
    accountId: (database, value) => {
      return database.accountId === value
    },
  },
}

/**
 * Fulfils `GET /api/v1/databases` as a page honouring `page`, `pageSize`, `search`, `sort` and
 * `accountId`, and answers a `POST` by echoing the submitted body back as a created database,
 * prefixed the way the server prefixes it and carrying a password; the created row joins the
 * catalogue, so the list read the page makes after a create lists it.
 *
 * The prefix is applied HERE, in the stub, because it is the server's job: the SPA under test must
 * render the `fullName` it was sent rather than assembling one, and a stub that echoed the bare
 * suffix would let a page that assembles its own names pass.
 * @param page The Playwright page whose network the route is installed on.
 * @param databases The databases the stub knows, in their default order.
 * @returns A function reporting every list request so far, oldest first, so a spec can read what
 * the page asked the panel for.
 */
export const stubDatabases = async (page: Page, databases: Database[]): Promise<() => readonly URL[]> => {
  const listRequests: URL[] = []

  await page.route(DATABASES_PATTERN, async (route) => {
    if (route.request().method() === 'POST') {
      const submitted = route.request().postDataJSON() as {
        accountId: string
        name: string
        dbUserName: string
      }
      const created: CreatedDatabase = {
        id: '99999999-9999-9999-9999-999999999999',
        accountId: submitted.accountId,
        name: submitted.name,
        fullName: `${ACCOUNT_PREFIX}_${submitted.name}`,
        dbUserName: `${ACCOUNT_PREFIX}_${submitted.dbUserName}`,
        password: stubbedCreatedPassword,
        createdAt: '2026-09-01T10:00:00Z',
      }
      databases.push({
        id: created.id,
        accountId: created.accountId,
        ownerName: ACCOUNT_PREFIX,
        name: created.name,
        fullName: created.fullName,
        dbUserName: created.dbUserName,
        createdAt: created.createdAt,
      })
      await route.fulfill({
        status: 201,
        contentType: 'application/json',
        body: JSON.stringify(created),
      })
      return
    }

    listRequests.push(new URL(route.request().url()))
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(pagedAnswer(databases, route.request().url(), DATABASE_RULES)),
    })
  })

  return () => {
    return listRequests
  }
}

/**
 * Fulfils `GET /api/v1/databases` with an RFC 7807 problem body, so a spec can assert the page
 * renders the backend's own already-localized message verbatim.
 * @param page The Playwright page whose network the route is installed on.
 * @param detail The backend-localized message the stub reports in `detail`.
 * @returns Resolves once the route is installed.
 */
export const stubDatabasesProblem = async (page: Page, detail: string): Promise<void> => {
  await page.route(DATABASES_PATTERN, async (route) => {
    await route.fulfill({
      status: 500,
      contentType: 'application/problem+json',
      body: JSON.stringify({ code: 'HostUnexpectedError', title: 'Unexpected error', detail }),
    })
  })
}

/**
 * Serves an empty `GET /api/v1/databases` page but rejects `POST` with an RFC 7807 problem body,
 * so a spec can assert the create form surfaces the server's own message rather than invented copy.
 * @param page The Playwright page whose network the route is installed on.
 * @param detail The backend-localized message the stub reports in `detail`.
 * @returns Resolves once the route is installed.
 */
export const stubCreateDatabaseProblem = async (page: Page, detail: string): Promise<void> => {
  await page.route(DATABASES_PATTERN, async (route) => {
    if (route.request().method() === 'POST') {
      await route.fulfill({
        status: 409,
        contentType: 'application/problem+json',
        body: JSON.stringify({ code: 'DatabaseNameTaken', title: 'Conflict', detail }),
      })
      return
    }
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(pagedAnswer<Database>([], route.request().url(), DATABASE_RULES)),
    })
  })
}

/**
 * Fulfils `POST /api/v1/databases/{id}/password` with a new password, and `DELETE
 * /api/v1/databases/{id}` with a success.
 *
 * Installed AFTER {@link stubDatabases} in a spec: Playwright gives priority to the most recently
 * registered route, and these two patterns are the narrower ones.
 * @param page The Playwright page whose network the routes are installed on.
 * @param dbUserName The fully-qualified MySQL user the reset reports the new password for.
 * @returns Resolves once both routes are installed.
 */
export const stubDatabaseActions = async (page: Page, dbUserName: string): Promise<void> => {
  await page.route(DATABASE_PATTERN, async (route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: 'true' })
  })

  await page.route(DATABASE_PASSWORD_PATTERN, async (route) => {
    const id = new URL(route.request().url()).pathname.split('/').slice(-2)[0] ?? ''
    const reset: DatabasePassword = { id, dbUserName, password: stubbedResetPassword }
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(reset),
    })
  })
}
