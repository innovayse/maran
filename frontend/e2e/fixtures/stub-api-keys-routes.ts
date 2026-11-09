import type { Page, Route } from '@playwright/test'
import { pagedAnswer, type PagedRules } from './paged-answer'
import type { ApiKey, ApiKeyChoice, CreateApiKeyRequest } from '../../src/types/apiKey'

/**
 * Where a stubbed panel serves one owner's keys: the collection (list, create, and the revoke beneath
 * it) and the base the states are read from. The states live in one place for every owner, so an
 * overseen reseller's keys are listed under its own path while the states come from the administrator's.
 */
export interface ApiKeyPaths {
  /** The keys collection path, such as `/api/v1/api-keys`. */
  collection: string
  /** The path whose `/statuses` answers the states filter. */
  statusesBase: string
}

/** The administrator's own keys. */
export const ADMINISTRATOR_KEY_PATHS: ApiKeyPaths = { collection: '/api/v1/api-keys', statusesBase: '/api/v1/api-keys' }

/** A signed-in reseller's own keys. */
export const RESELLER_KEY_PATHS: ApiKeyPaths = {
  collection: '/api/v1/reseller/api-keys',
  statusesBase: '/api/v1/reseller/api-keys',
}

/**
 * The paths an administrator reads one reseller's keys under.
 * @param resellerId The reseller whose keys they are.
 * @returns The reseller's collection, with the states taken from the administrator's.
 */
export const overseenKeyPaths = (resellerId: string): ApiKeyPaths => {
  return { collection: `/api/v1/resellers/${resellerId}/api-keys`, statusesBase: '/api/v1/api-keys' }
}

/**
 * Escapes a path for use inside a regular expression.
 * @param path The literal path.
 * @returns The path with every pattern character escaped.
 */
const literal = (path: string): string => {
  return path.replace(/[.*+?^${}()|[\]\\/]/g, '\\$&')
}

/** The secret a stubbed create answers with, `mk_<prefix>_<token>`. Distinctive so a search for it cannot hit anything else. */
export const stubbedPlaintext = 'mk_9f8e7d6c_Zq3VbN8xTt2LrPw6KdYh5GsA1JcUe4MoXi7'

/** The instant a stubbed create records against the key it makes. */
export const stubbedCreatedAt = '2026-09-01T12:00:00+00:00'

/**
 * The scopes a stubbed panel offers unless a spec says otherwise. The names are deliberately NOT the
 * wording the real panel ships, so a control found by these words proves the dialog renders what
 * the panel sent and holds no scope list of its own.
 */
export const stubbedScopes: ApiKeyChoice[] = [
  { value: 'sites.read', name: 'Look at sites' },
  { value: 'sites.write', name: 'Change sites' },
]

/** The states a stubbed panel names, deliberately unlike the raw wire values for the same reason. */
export const stubbedStatuses: ApiKeyChoice[] = [
  { value: 'active', name: 'In force' },
  { value: 'expired', name: 'Lapsed' },
  { value: 'revoked', name: 'Withdrawn' },
]

/**
 * The name a stubbed row carries for each state: the panel's wording, which the table prints as it
 * arrives. Taken from {@link stubbedStatuses} so the row and the filter sheet agree.
 * @param status The state wire value.
 * @returns The stubbed panel's name for it.
 */
const stubbedStatusName = (status: string): string => {
  return stubbedStatuses.find((choice) => {return choice.value === status})?.name ?? status
}

/** How the list endpoint searches, sorts and filters, as the panel does: name or prefix, three sort keys, one filter. */
const KEY_RULES: PagedRules<ApiKey> = {
  matches: (key, term) => {
    return key.name.toLowerCase().includes(term) || key.prefix.toLowerCase().includes(term)
  },
  sorts: {
    name: (key) => {
      return key.name
    },
    createdAt: (key) => {
      return key.createdAt
    },
    lastUsedAt: (key) => {
      return key.lastUsedAt ?? ''
    },
  },
  filters: {
    status: (key, value) => {
      return key.status === value
    },
  },
}

/**
 * Builds one stored key; a spec overrides only what it is about.
 * @param overrides The fields that differ from an active, never-used key.
 * @returns The key.
 */
export const apiKeyRow = (overrides: Partial<ApiKey> = {}): ApiKey => {
  const status = overrides.status ?? 'active'
  return {
    id: '10000000-0000-0000-0000-000000000001',
    name: 'Billing integration',
    prefix: 'a1b2c3d4',
    scopes: ['sites.read'],
    allowedCidrs: [],
    createdAt: '2026-08-01T10:00:00+00:00',
    expiresAt: null,
    lastUsedAt: null,
    lastUsedIp: null,
    revokedAt: null,
    status,
    statusName: stubbedStatusName(status),
    ...overrides,
  }
}

/**
 * Builds a catalogue of numbered active keys, `Key 01`, `Key 02` and so on, in the panel's default order.
 * @param count How many keys.
 * @returns The keys.
 */
export const numberedKeys = (count: number): ApiKey[] => {
  return Array.from({ length: count }, (_, index): ApiKey => {
    const number = String(index + 1).padStart(2, '0')
    return apiKeyRow({
      id: `10000000-0000-0000-0000-0000000001${number}`,
      name: `Key ${number}`,
      prefix: `0000${number}ab`,
    })
  })
}

/** What a spec tells the stub: the keys it holds and the refusals it should answer with. */
export interface StubbedApiKeys {
  /** The keys the stubbed panel reports, mutated in place by the stubbed writes. */
  keys: ApiKey[]
  /** The scopes the create dialog is offered; {@link stubbedScopes} when absent. */
  scopes?: ApiKeyChoice[]
  /** The states the filter is offered; {@link stubbedStatuses} when absent. */
  statuses?: ApiKeyChoice[]
  /** When set, the listing answers 403 with this sentence. */
  listRefusal?: string
  /** When set, the create answers 422 with this sentence. */
  createRefusal?: string
  /** When set, the revoke answers 409 with this sentence. */
  revokeRefusal?: string
}

/** What the stub observed, so a spec can assert on the wire and not only on the screen. */
export interface ApiKeyCalls {
  /** Every listing request, in order. */
  lists: URL[]
  /** Every create body that was sent, in order. */
  created: CreateApiKeyRequest[]
  /** The id each revoke named, in order. */
  revoked: string[]
  /** How many times the scopes were read. */
  scopeReads: number
}

/**
 * Fulfils with an RFC 7807 problem the way the panel refuses.
 * @param route The intercepted request.
 * @param status The HTTP status.
 * @param detail The backend-localized sentence.
 * @returns Resolves once the response is sent.
 */
const refuse = async (route: Route, status: number, detail: string): Promise<void> => {
  await route.fulfill({
    status,
    contentType: 'application/problem+json',
    body: JSON.stringify({ code: 'Refused', title: detail, detail, status }),
  })
}

/**
 * Fulfils the five API key endpoints and applies every write to the given list: a create adds an
 * active key and answers 201 with the key and its plaintext, a revoke marks the key revoked and
 * answers an empty 204, so a re-read after a change reports what the change did.
 * @param page The Playwright page whose network the routes are installed on.
 * @param state The keys and refusals the stub plays.
 * @param paths Where the keys are served; the administrator's own unless a spec says otherwise.
 * @returns A live view of the calls made.
 */
export const stubApiKeys = async (
  page: Page,
  state: StubbedApiKeys,
  paths: ApiKeyPaths = ADMINISTRATOR_KEY_PATHS,
): Promise<ApiKeyCalls> => {
  const calls: ApiKeyCalls = { lists: [], created: [], revoked: [], scopeReads: 0 }
  const COLLECTION_PATTERN = new RegExp(`${literal(paths.collection)}(\\?.*)?$`)
  const ENTRY_PATTERN = new RegExp(`${literal(paths.collection)}/[^/?]+$`)
  const SCOPES_PATTERN = new RegExp(`${literal(paths.collection)}/scopes(\\?.*)?$`)
  const STATUSES_PATTERN = new RegExp(`${literal(paths.statusesBase)}/statuses(\\?.*)?$`)

  await page.route(COLLECTION_PATTERN, async (route) => {
    const request = route.request()

    if (request.method() === 'POST') {
      if (state.createRefusal !== undefined) {
        await refuse(route, 422, state.createRefusal)
        return
      }
      const submitted = request.postDataJSON() as CreateApiKeyRequest
      calls.created.push(submitted)
      const created = apiKeyRow({
        id: `20000000-0000-0000-0000-00000000000${calls.created.length}`,
        name: submitted.name,
        prefix: '9f8e7d6c',
        scopes: submitted.scopes,
        allowedCidrs: submitted.allowedCidrs ?? [],
        expiresAt: submitted.expiresAt,
        createdAt: stubbedCreatedAt,
      })
      state.keys = [created, ...state.keys]
      await route.fulfill({
        status: 201,
        contentType: 'application/json',
        body: JSON.stringify({ key: created, secret: stubbedPlaintext }),
      })
      return
    }

    calls.lists.push(new URL(request.url()))
    if (state.listRefusal !== undefined) {
      await refuse(route, 403, state.listRefusal)
      return
    }
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(pagedAnswer(state.keys, request.url(), KEY_RULES)),
    })
  })

  await page.route(ENTRY_PATTERN, async (route) => {
    const id = new URL(route.request().url()).pathname.split('/').pop() ?? ''
    if (state.revokeRefusal !== undefined) {
      await refuse(route, 409, state.revokeRefusal)
      return
    }
    calls.revoked.push(id)
    state.keys = state.keys.map((key) => {
      return key.id === id ? { ...key, status: 'revoked', statusName: stubbedStatusName('revoked'), revokedAt: stubbedCreatedAt } : key
    })
    await route.fulfill({ status: 204 })
  })

  await page.route(SCOPES_PATTERN, async (route) => {
    calls.scopeReads += 1
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(state.scopes ?? stubbedScopes),
    })
  })

  await page.route(STATUSES_PATTERN, async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(state.statuses ?? stubbedStatuses),
    })
  })

  return calls
}
