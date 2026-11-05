import type { Page } from '@playwright/test'
import { pagedAnswer, type PagedRules } from './paged-answer'
import type {
  AddSftpUserKeyRequest,
  CreatedSftpUser,
  SftpUser,
  SftpUserKey,
  SftpUserPassword,
} from '../../src/types/sftpUser'

/** The collection endpoint the SFTP list and the create form both talk to, with or without a query. */
const SFTP_USERS_PATTERN = /\/api\/v1\/sftp-users(\?.*)?$/

/** The single-login endpoint, which answers the removal. */
const SFTP_USER_PATTERN = '**/api/v1/sftp-users/*'

/** The password-reset endpoint. A `*` never spans a `/`, so this is narrower than the one above. */
const SFTP_USER_PASSWORD_PATTERN = '**/api/v1/sftp-users/*/password'

/** The account prefix the stubs build a full login with, mirroring what the host would hold. */
const ACCOUNT_PREFIX = 'alice'

/** The password a stubbed create answers with — a value no other fixture in the suite uses. */
export const stubbedCreatedSftpPassword = 'Cr34ted-SFTP-P4ss-Once'

/** The password a stubbed reset answers with, distinct from the created one so a spec can tell them apart. */
export const stubbedResetSftpPassword = 'R3set-SFTP-P4ss-Once'

/** How the SFTP logins endpoint searches, sorts and filters, as the panel does. */
const SFTP_USER_RULES: PagedRules<SftpUser> = {
  matches: (user, term) => {
    return user.fullName.toLowerCase().includes(term)
  },
  sorts: {
    name: (user) => {
      return user.fullName
    },
    createdAt: (user) => {
      return user.createdAt
    },
  },
  filters: {
    accountId: (user, value) => {
      return user.accountId === value
    },
  },
}

/**
 * Fulfils `GET /api/v1/sftp-users` as a page honouring `page`, `pageSize`, `search`, `sort` and
 * `accountId`, and answers a `POST` by echoing the submitted body back as a created login, prefixed
 * the way the host prefixes it and carrying a password. The created row joins the catalogue, so the
 * page's re-read after a create shows it.
 *
 * The prefix is applied HERE, in the stub, because it is the server's job: the SPA under test must
 * render the `fullName` it was sent rather than assembling one, and a stub that echoed the bare
 * suffix would let a page that assembles its own names pass.
 * @param page The Playwright page whose network the route is installed on.
 * @param users The logins the stub knows, in their default order.
 * @returns A function reporting every list request so far, oldest first, so a spec can read what
 * the page asked the panel for.
 */
export const stubSftpUsers = async (page: Page, users: SftpUser[]): Promise<() => readonly URL[]> => {
  const listRequests: URL[] = []

  await page.route(SFTP_USERS_PATTERN, async (route) => {
    if (route.request().method() === 'POST') {
      const submitted = route.request().postDataJSON() as { accountId: string; name: string }
      const created: CreatedSftpUser = {
        id: '99999999-9999-9999-9999-999999999999',
        accountId: submitted.accountId,
        name: submitted.name,
        fullName: `${ACCOUNT_PREFIX}_${submitted.name}`,
        password: stubbedCreatedSftpPassword,
        createdAt: '2026-09-01T10:00:00Z',
      }
      users.push({
        id: created.id,
        accountId: created.accountId,
        ownerName: ACCOUNT_PREFIX,
        name: created.name,
        fullName: created.fullName,
        protocol: 'Sftp',
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
      body: JSON.stringify(pagedAnswer(users, route.request().url(), SFTP_USER_RULES)),
    })
  })

  return () => {
    return listRequests
  }
}

/**
 * Fulfils `GET /api/v1/sftp-users` with an RFC 7807 problem body, so a spec can assert the page
 * renders the backend's own already-localized message verbatim.
 * @param page The Playwright page whose network the route is installed on.
 * @param detail The backend-localized message the stub reports in `detail`.
 * @returns Resolves once the route is installed.
 */
export const stubSftpUsersProblem = async (page: Page, detail: string): Promise<void> => {
  await page.route(SFTP_USERS_PATTERN, async (route) => {
    await route.fulfill({
      status: 500,
      contentType: 'application/problem+json',
      body: JSON.stringify({ code: 'HostUnexpectedError', title: 'Unexpected error', detail }),
    })
  })
}

/**
 * Serves an empty `GET /api/v1/sftp-users` page but rejects `POST` with an RFC 7807 problem body,
 * so a spec can assert the create form surfaces the server's own message rather than invented copy.
 * @param page The Playwright page whose network the route is installed on.
 * @param detail The backend-localized message the stub reports in `detail`.
 * @returns Resolves once the route is installed.
 */
export const stubCreateSftpUserProblem = async (page: Page, detail: string): Promise<void> => {
  await page.route(SFTP_USERS_PATTERN, async (route) => {
    if (route.request().method() === 'POST') {
      await route.fulfill({
        status: 409,
        contentType: 'application/problem+json',
        body: JSON.stringify({ code: 'SftpUserNameTaken', title: 'Conflict', detail }),
      })
      return
    }
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({ items: [], total: 0, page: 1, pageSize: 25 }),
    })
  })
}

/**
 * Fulfils `POST /api/v1/sftp-users/{id}/password` with a new password, and `DELETE
 * /api/v1/sftp-users/{id}` with an empty 204.
 *
 * Installed AFTER {@link stubSftpUsers} in a spec: Playwright gives priority to the most recently
 * registered route, and these two patterns are the narrower ones.
 * @param page The Playwright page whose network the routes are installed on.
 * @param fullName The system login the reset reports the new password for.
 * @returns Resolves once both routes are installed.
 */
export const stubSftpUserActions = async (page: Page, fullName: string): Promise<void> => {
  await page.route(SFTP_USER_PATTERN, async (route) => {
    await route.fulfill({ status: 204 })
  })

  await page.route(SFTP_USER_PASSWORD_PATTERN, async (route) => {
    const id = new URL(route.request().url()).pathname.split('/').slice(-2)[0] ?? ''
    const reset: SftpUserPassword = { id, fullName, password: stubbedResetSftpPassword }
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(reset),
    })
  })
}

/** A login's key collection: `GET` lists a page, `POST` adds. The login id is in the path, then `/keys`. */
const SFTP_USER_KEYS_PATTERN = /\/api\/v1\/sftp-users\/[^/]+\/keys(\?.*)?$/

/** One key of a login: `DELETE` removes it. */
const SFTP_USER_KEY_PATTERN = /\/api\/v1\/sftp-users\/[^/]+\/keys\/[^/?]+$/

/** The fingerprint a stubbed add answers with, distinct from every catalogue fixture. */
export const stubbedAddedKeyFingerprint = 'SHA256:AddedKeyFingerprintFromTheStub0123456789abc'

/** How the keys endpoint searches (by label) and sorts, as the panel does. */
const SFTP_KEY_RULES: PagedRules<SftpUserKey> = {
  matches: (key, term) => {
    return key.label.toLowerCase().includes(term)
  },
  sorts: {
    label: (key) => {
      return key.label
    },
    createdAt: (key) => {
      return key.createdAt
    },
  },
  filters: {},
}

/** Refusals a spec can have the keys stub answer with; each is an RFC 7807 `detail` the panel localized. */
export interface SftpKeyRefusals {
  /** The `detail` of a 400 answering every `POST`, or absent to accept. */
  add?: string
  /** The `detail` of a 409 answering every `DELETE`, or absent to accept. */
  remove?: string
}

/** What a spec can read back from {@link stubSftpUserKeys}. */
export interface SftpKeyStubReads {
  /** Every list request so far, oldest first. */
  listRequests: () => readonly URL[]
  /** Every body `POST`ed so far, oldest first. */
  added: () => readonly AddSftpUserKeyRequest[]
  /** The path of every `DELETE` so far, oldest first. */
  removed: () => readonly string[]
}

/**
 * Fulfils the keys endpoints of any login: a page honouring `page`, `pageSize`, `search` and `sort`
 * on `GET`, a created key on `POST` (which joins the catalogue), and an empty 204 on `DELETE` (which
 * leaves it). A refusal from {@link SftpKeyRefusals} answers instead, with an RFC 7807 body, so a
 * spec can assert the dialog renders the backend's own sentence verbatim.
 *
 * The two patterns are disjoint (the collection ends at `/keys`, a key's path continues past it), so the
 * order of registration does not decide which route answers.
 * @param page The Playwright page whose network the routes are installed on.
 * @param keys The keys the stub knows, in their default order; mutated by an accepted add or remove.
 * @param refusals Refusals to answer instead of accepting.
 * @returns Readers of what the page asked.
 */
export const stubSftpUserKeys = async (
  page: Page,
  keys: SftpUserKey[],
  refusals: SftpKeyRefusals = {},
): Promise<SftpKeyStubReads> => {
  const listRequests: URL[] = []
  const added: AddSftpUserKeyRequest[] = []
  const removed: string[] = []

  await page.route(SFTP_USER_KEYS_PATTERN, async (route) => {
    if (route.request().method() === 'POST') {
      const submitted = route.request().postDataJSON() as AddSftpUserKeyRequest
      added.push(submitted)
      if (refusals.add !== undefined) {
        await route.fulfill({
          status: 400,
          contentType: 'application/problem+json',
          body: JSON.stringify({ code: 'SftpKeyInvalid', title: 'Validation', detail: refusals.add }),
        })
        return
      }
      const created: SftpUserKey = {
        id: `aaaaaaaa-0000-0000-0000-${String(keys.length + 1).padStart(12, '0')}`,
        label: submitted.label,
        algorithm: submitted.publicKey.split(' ')[0] ?? '',
        fingerprint: stubbedAddedKeyFingerprint,
        createdAt: '2026-09-01T10:00:00Z',
      }
      keys.push(created)
      await route.fulfill({ status: 201, contentType: 'application/json', body: JSON.stringify(created) })
      return
    }

    listRequests.push(new URL(route.request().url()))
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(pagedAnswer(keys, route.request().url(), SFTP_KEY_RULES)),
    })
  })

  await page.route(SFTP_USER_KEY_PATTERN, async (route) => {
    const path = new URL(route.request().url()).pathname
    removed.push(path)
    if (refusals.remove !== undefined) {
      await route.fulfill({
        status: 409,
        contentType: 'application/problem+json',
        body: JSON.stringify({ code: 'SftpSessionsNotEnded', title: 'Conflict', detail: refusals.remove }),
      })
      return
    }
    const keyId = path.split('/').at(-1)
    const index = keys.findIndex((key) => {
      return key.id === keyId
    })
    if (index >= 0) {
      keys.splice(index, 1)
    }
    await route.fulfill({ status: 204 })
  })

  return {
    listRequests: (): readonly URL[] => {
      return listRequests
    },
    added: (): readonly AddSftpUserKeyRequest[] => {
      return added
    },
    removed: (): readonly string[] => {
      return removed
    },
  }
}
