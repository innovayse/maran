import type { Page } from '@playwright/test'
import { pagedAnswer, type PagedRules } from './paged-answer'
import type {
  CreatedDatabaseUser,
  DatabasePrivilegeCatalogue,
  DatabasePrivilegeKey,
  DatabaseUser,
  DatabaseUserDetail,
  DatabaseUserPassword,
  GrantableDatabase,
} from '../../src/types/databaseUser'

/** Every request under the database-users endpoint, with or without an id, a sub-path or a query. */
const DATABASE_USERS_PATTERN = /\/api\/v1\/database-users(\/[^?]*)?(\?.*)?$/

/** The account prefix the stubs build a full name with, mirroring what the server would hold. */
const ACCOUNT_PREFIX = 'alice'

/** The password a stubbed create answers with. */
export const stubbedUserPassword = 'Us3r-Cr34ted-Once'

/** The password a stubbed reset answers with, distinct from the created one. */
export const stubbedUserResetPassword = 'Us3r-R3set-Once'

/** The catalogue a stubbed `privilege-presets` answers with: a small closed set, as the panel serves. */
export const STUBBED_CATALOGUE: DatabasePrivilegeCatalogue = {
  privileges: [
    { key: 'select', name: 'SELECT' },
    { key: 'insert', name: 'INSERT' },
    { key: 'update', name: 'UPDATE' },
    { key: 'delete', name: 'DELETE' },
  ],
  presets: [
    { key: 'readOnly', name: 'Read only', privileges: ['select'] },
    { key: 'readWrite', name: 'Read and write', privileges: ['select', 'insert', 'update', 'delete'] },
  ],
}

/** How the endpoint searches, sorts and filters, as the panel does. */
const USER_RULES: PagedRules<DatabaseUser> = {
  matches: (user, term) => {
    return user.fullName.toLowerCase().includes(term)
  },
  sorts: {
    name: (user) => {return user.fullName},
    createdAt: (user) => {return user.createdAt},
  },
  filters: {
    accountId: (user, value) => {return user.accountId === value},
  },
}

/** What a stubbed endpoint recorded, so a spec can read what the page sent the panel. */
export interface DatabaseUsersRecording {
  /** Every list request, oldest first. */
  listRequests: () => readonly URL[]
  /** Every `PUT .../grants/{databaseId}` body with the database it named, oldest first. */
  grantWrites: () => readonly { databaseId: string; privileges: string[] }[]
  /** The ids of the users dropped, oldest first. */
  drops: () => readonly string[]
}

/**
 * Serves the database-users endpoints: the list (honouring `page`, `pageSize`, `search`, `sort` and
 * `accountId`), `GET {id}` with the grantable databases, `privilege-presets`, create, password reset,
 * grant replacement and drop. A dedicated user's drop answers 409 as the panel does.
 *
 * The prefix is applied HERE because it is the server's job: the SPA under test must render the
 * `fullName` it was sent rather than assembling one.
 * @param page The Playwright page whose network the route is installed on.
 * @param users The users the stub knows, in their default order.
 * @param databases The account's databases, so a grant can be added on one the user has none on.
 * @returns A recording of the requests the page made.
 */
export const stubDatabaseUsers = async (
  page: Page,
  users: DatabaseUser[],
  databases: GrantableDatabase[] = [],
): Promise<DatabaseUsersRecording> => {
  const listRequests: URL[] = []
  const grantWrites: { databaseId: string; privileges: string[] }[] = []
  const drops: string[] = []

  await page.route(DATABASE_USERS_PATTERN, async (route) => {
    const request = route.request()
    const url = new URL(request.url())
    const segments = url.pathname.split('/').filter((part) => {return part !== ''})
    // ['api', 'v1', 'database-users', ...rest]
    const rest = segments.slice(3)
    /**
     * Answers the intercepted request with a JSON body.
     * @param status The HTTP status.
     * @param body The value serialized as the body.
     * @returns Resolves once the route is fulfilled.
     */
    const json = async (status: number, body: unknown): Promise<void> => {
      await route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(body) })
    }

    if (rest.length === 0 && request.method() === 'GET') {
      listRequests.push(url)
      await json(200, pagedAnswer(users, request.url(), USER_RULES))
      return
    }
    if (rest.length === 0 && request.method() === 'POST') {
      const submitted = request.postDataJSON() as { accountId: string; nameSuffix: string }
      const created: CreatedDatabaseUser = {
        id: '99999999-9999-9999-9999-999999999999',
        accountId: submitted.accountId,
        nameSuffix: submitted.nameSuffix,
        fullName: `${ACCOUNT_PREFIX}_${submitted.nameSuffix}`,
        password: stubbedUserPassword,
        createdAt: '2026-09-01T10:00:00Z',
      }
      users.push({
        id: created.id,
        accountId: created.accountId,
        ownerName: ACCOUNT_PREFIX,
        nameSuffix: created.nameSuffix,
        fullName: created.fullName,
        isDedicated: false,
        grants: [],
        createdAt: created.createdAt,
      })
      await json(201, created)
      return
    }
    if (rest[0] === 'privilege-presets') {
      await json(200, STUBBED_CATALOGUE)
      return
    }

    const user = users.find((candidate) => {return candidate.id === rest[0]})
    if (user === undefined) {
      await json(404, { code: 'DatabaseUserNotFound', title: 'Not found', detail: 'That user does not exist.' })
      return
    }
    if (rest.length === 1 && request.method() === 'GET') {
      const granted = new Set(user.grants.map((grant) => {return grant.databaseId}))
      const detail: DatabaseUserDetail = {
        ...user,
        grantableDatabases: databases.filter((database) => {return !granted.has(database.id)}),
      }
      await json(200, detail)
      return
    }
    if (rest.length === 1 && request.method() === 'DELETE') {
      if (user.isDedicated) {
        await json(409, {
          code: 'DatabaseUserIsDedicated',
          title: 'Conflict',
          detail: 'A database user is dropped together with its database.',
        })
        return
      }
      drops.push(user.id)
      users.splice(users.indexOf(user), 1)
      await route.fulfill({ status: 204 })
      return
    }
    if (rest[1] === 'password' && request.method() === 'POST') {
      const reset: DatabaseUserPassword = { id: user.id, fullName: user.fullName, password: stubbedUserResetPassword }
      await json(200, reset)
      return
    }
    if (rest[1] === 'grants' && request.method() === 'PUT') {
      const databaseId = rest[2] ?? ''
      const { privileges } = request.postDataJSON() as { privileges: DatabasePrivilegeKey[] }
      grantWrites.push({ databaseId, privileges })
      user.grants = user.grants.filter((grant) => {return grant.databaseId !== databaseId})
      const target = databases.find((database) => {return database.id === databaseId})
      if (privileges.length > 0 && target !== undefined) {
        user.grants.push({ databaseId, databaseFullName: target.fullName, privileges })
      }
      await route.fulfill({ status: 204 })
      return
    }
    await json(404, { code: 'NotFound', title: 'Not found', detail: 'No such stubbed route.' })
  })

  return {
    listRequests: (): readonly URL[] => {return listRequests},
    grantWrites: (): readonly { databaseId: string; privileges: string[] }[] => {return grantWrites},
    drops: (): readonly string[] => {return drops},
  }
}
