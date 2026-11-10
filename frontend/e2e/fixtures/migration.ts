import { expect, type Page, type Route } from '@playwright/test'
import { pagedAnswer, type PagedRules } from './paged-answer'
import { FULL_CATALOGUE, mainNavigation, openSection } from './open-full-shell'
import { stubSignedIn } from './stub-auth-routes'
import { stubHealthy } from './stub-health-route'
import { stubLandingReads } from './stub-landing-reads'
import { stubModules } from './stub-modules-route'
import { stubShellReads } from './stub-shell-reads'
import type { PanelModule } from '../../src/types/module'
import type {
  DiscoveredAccount,
  DiscoveredAccountDetail,
  DiscoveryWarning,
  HostKeyCandidate,
  DiscoveredSiteBackend,
  RemoteFailureKind,
  RemoteServerState,
  RemoteSourceKind,
  RemoteServer,
  RemoteServerDetail,
  RemoteServerRequest,
} from '../../src/types/migrationSource'

/** The whole remote-servers API, under which one handler dispatches by path. */
const REMOTE_SERVERS_API = /\/api\/v1\/remote-servers/

/** The names the panel gives each state in English, as its display-name resources spell them. */
const STATE_NAMES: Record<RemoteServerState, string> = {
  hostKeyUnconfirmed: 'Host key not confirmed',
  pinned: 'Host key confirmed',
  ready: 'ready',
  hostKeyChanged: 'Host key changed',
  authRefused: 'Key refused',
  unreachable: 'unreachable',
}

/** The names the panel gives each failure kind in English. */
const FAILURE_NAMES: Record<RemoteFailureKind, string> = {
  hostKeyChanged: "The server's host key changed",
  authRefused: 'The server refused the key',
  unreachable: 'The server could not be reached',
  addressRefused: 'The address is not allowed',
}

/** The names the panel gives each kind of source in English. */
const KIND_NAMES: Record<RemoteSourceKind, string> = {
  genericLinux: 'Linux server',
  maran: 'Server running Maran',
}

/** The names the panel gives each site backend in English. */
const BACKEND_NAMES: Record<DiscoveredSiteBackend, string> = {
  unknown: 'unknown',
  static: 'Static files',
  php: 'PHP',
  proxy: 'Proxied to another process',
}

/** The path segments that name a part of the API rather than a row, so the rest are identities. */
const LITERAL_SEGMENTS: ReadonlySet<string> = new Set([
  'remote-servers',
  'host-keys',
  'scan',
  'host-key',
  'test',
  'discover',
  'accounts',
])

/** The catalogue every migration spec runs under: the whole panel, with the migration module licensed. */
export const MIGRATION_MODULES: PanelModule[] = [
  ...FULL_CATALOGUE,
  { name: 'servermigration', displayName: 'Server migration', kindDisplayName: 'Core', tier: 'included', state: 'enabled' },
]

/**
 * A synthetic private-key marker. The panel never sends a private key; a spec asserts that nothing
 * on screen contains it, with a positive control that proves the search can find it.
 */
export const PRIVATE_KEY_MARKER = '-----BEGIN OPENSSH PRIVATE KEY-----'

/** The public key the stub generates for every server it adds. */
const PUBLIC_KEY = 'ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIMaranStubPublicKeyForSpecs0000000000 maran-migration'

/** The scanned host keys of the stub: an ed25519 key and an rsa key, each with its fingerprint. */
export const SCANNED_KEYS: HostKeyCandidate[] = [
  { algorithm: 'ssh-ed25519', fingerprint: 'SHA256:5Y9fT0b3o6Xz1mQp7rVnKc2LdWuEhJ8aGtB4sNiOlRw' },
  { algorithm: 'ssh-rsa', fingerprint: 'SHA256:Zq1HkM8dPxV3nLtYw5cRb7JuGeA2oSfT9iXj6DvNmCs' },
]

/** The fingerprint a changed server presents, different from every scanned one above. */
export const OTHER_FINGERPRINT = 'SHA256:Q7wErTyUiOpAsDfGhJkLzXcVbNm1234567890aBcDeF'

/** A server just added: its key is not on the remote side yet and its host key is unconfirmed. */
export const FRESH_SERVER: RemoteServerDetail = {
  id: '11111111-1111-1111-1111-111111111111',
  name: 'old-shared',
  host: 'old.example.com',
  port: 22,
  sshUser: 'root',
  state: 'hostKeyUnconfirmed',
  stateName: STATE_NAMES.hostKeyUnconfirmed,
  hostKeyFingerprint: null,
  lastTestedAt: null,
  kind: null,
  kindName: null,
  accountsFound: null,
  createdAt: '2026-11-01T10:00:00Z',
  allowedFrom: '203.0.113.10',
  publicKey: PUBLIC_KEY,
  authorizedKeysLine: `from="203.0.113.10",no-port-forwarding,no-agent-forwarding,no-X11-forwarding,no-pty ${PUBLIC_KEY}`,
  hostKeyAlgorithm: null,
  hostKeyConfirmedAt: null,
  hostKeyCandidates: SCANNED_KEYS,
  lastFailure: null,
  lastFailureName: null,
  warnings: [],
}

/** A server whose connection was tested: ready, with the facts the test learned. */
export const READY_SERVER: RemoteServerDetail = {
  ...FRESH_SERVER,
  id: '22222222-2222-2222-2222-222222222222',
  name: 'legacy-web',
  host: 'legacy.example.net',
  state: 'ready',
  stateName: STATE_NAMES.ready,
  hostKeyFingerprint: SCANNED_KEYS[0]?.fingerprint ?? null,
  hostKeyAlgorithm: 'ssh-ed25519',
  hostKeyConfirmedAt: '2026-11-02T10:00:00Z',
  hostKeyCandidates: [],
  allowedFrom: null,
  authorizedKeysLine: `no-port-forwarding,no-agent-forwarding,no-X11-forwarding,no-pty ${PUBLIC_KEY}`,
  lastTestedAt: '2026-11-02T10:05:00Z',
  kind: 'genericLinux',
  kindName: KIND_NAMES.genericLinux,
  systemRelease: 'Debian GNU/Linux 12',
  isRoot: true,
  homeFreeBytes: 50 * 1024 * 1024 * 1024,
  homeTotalBytes: 100 * 1024 * 1024 * 1024,
  databaseServer: 'MariaDb',
  webServer: 'Nginx',
  mailFound: true,
}

/** A server whose pinned host key no longer matches what the server presents. */
export const CHANGED_SERVER: RemoteServerDetail = {
  ...READY_SERVER,
  id: '33333333-3333-3333-3333-333333333333',
  name: 'rebuilt-box',
  host: 'rebuilt.example.org',
  state: 'hostKeyChanged',
  stateName: STATE_NAMES.hostKeyChanged,
  lastFailure: 'hostKeyChanged',
  lastFailureName: FAILURE_NAMES.hostKeyChanged,
  hostKeyCandidates: [{ algorithm: 'ssh-ed25519', fingerprint: OTHER_FINGERPRINT }],
}

/** A server whose host key is pinned but whose connection was never tested. */
export const PINNED_SERVER: RemoteServerDetail = {
  ...READY_SERVER,
  id: '44444444-4444-4444-4444-444444444444',
  name: 'pinned-only',
  host: 'pinned.example.com',
  state: 'pinned',
  stateName: STATE_NAMES.pinned,
  hostKeyCandidates: [],
  lastTestedAt: null,
  kind: null,
  kindName: null,
  isRoot: null,
  homeFreeBytes: null,
  homeTotalBytes: null,
  databaseServer: null,
  webServer: null,
  mailFound: null,
  systemRelease: null,
}

/**
 * Derives a stable identity in the UUID shape the contract demands from a login name, so every
 * account of a spec has its own id and the same name always has the same one.
 * @param name The login name.
 * @returns A version-4-shaped UUID.
 */
const accountIdFor = (name: string): string => {
  // Two 32-bit FNV-1a runs with different seeds give twelve hex digits, enough to tell a spec's few
  // accounts apart.
  const run = (seed: number): string => {
    let hash = seed
    for (const character of name) {
      hash = Math.imul(hash ^ (character.codePointAt(0) ?? 0), 0x01000193) >>> 0
    }
    return hash.toString(16).padStart(8, '0')
  }
  return `acc00000-0000-4000-8000-${run(0x811c9dc5)}${run(0x2545f491).slice(0, 4)}`
}

/**
 * Builds a discovered account the way the panel answers one.
 * @param name The login name.
 * @param fields What differs from a plain account with one site and one database.
 * @returns The account with its sites and databases.
 */
export const discoveredAccount = (name: string, fields: Partial<DiscoveredAccountDetail> = {}): DiscoveredAccountDetail => {
  return {
    id: accountIdFor(name),
    name,
    home: `/home/${name}`,
    diskBytes: 5 * 1024 * 1024,
    diskKnown: true,
    siteCount: 1,
    databaseCount: 1,
    cronEntryCount: 0,
    mailboxCount: 0,
    domains: [`${name}.example.com`],
    sites: [
      {
        domain: `${name}.example.com`,
        aliases: [`www.${name}.example.com`],
        backend: 'php',
        backendName: BACKEND_NAMES.php,
        phpVersion: '8.2',
        documentRoot: `/home/${name}/public_html`,
      },
    ],
    databases: [{ name: `${name}_main`, sizeBytes: 2 * 1024 * 1024, sizeKnown: true }],
    ...fields,
  }
}

/** One synthetic warning discovery can raise, with the sentence the panel would have localized. */
export const MAIL_WARNING: DiscoveryWarning = {
  kind: 'mailNotMigrated',
  subject: 'alice',
  message: 'Mail is not migrated; the mailboxes of this account are left behind.',
}

/** One refused request the stub was told to answer with a problem body. */
export interface MigrationRefusal {
  /** The RFC 7807 `detail`, already localized the way the panel's own text is. */
  detail: string
  /** The status to answer with; 409 when left out. */
  status?: number
}

/** What a spec seeds the stub with, and reads back after the page acted. */
export interface MigrationStubState {
  /** The servers the stub holds. */
  servers: RemoteServerDetail[]
  /** The discovered accounts of each server, by server id. */
  accounts: Record<string, DiscoveredAccountDetail[]>
  /** The host keys a scan answers with, for a new server and for a rescan. */
  scannedKeys: HostKeyCandidate[]
  /** How the next connection test ends: `null` is success. */
  testFailure: Exclude<RemoteFailureKind, 'addressRefused'> | null
  /** What a discovery run reports as warnings. */
  warnings: DiscoveryWarning[]
  /** Requests to refuse, by `"<METHOD> <shape>"`, with `:id` for each identity. */
  refusals: Record<string, MigrationRefusal>
  /** Every request the stub saw, oldest first. */
  requests: { method: string; url: URL; body: unknown }[]
}

/** The paging rules of the servers endpoint. */
const SERVER_RULES: PagedRules<RemoteServer> = {
  matches: (server, term) => {
    return server.name.toLowerCase().includes(term) || server.host.toLowerCase().includes(term)
  },
  sorts: {
    name: (server) => {
      return server.name
    },
    lastTestedAt: (server) => {
      return server.lastTestedAt ?? ''
    },
  },
  filters: {
    state: (server, value) => {
      return server.state === value
    },
  },
}

/** The paging rules of the discovered accounts endpoint. */
const ACCOUNT_RULES: PagedRules<DiscoveredAccountDetail> = {
  matches: (account, term) => {
    return (
      account.name.toLowerCase().includes(term) ||
      account.domains.some((domain) => {
        return domain.toLowerCase().includes(term)
      })
    )
  },
  sorts: {
    name: (account) => {
      return account.name
    },
    disk: (account) => {
      return String(account.diskBytes).padStart(15, '0')
    },
    sites: (account) => {
      return String(account.siteCount).padStart(6, '0')
    },
    databases: (account) => {
      return String(account.databaseCount).padStart(6, '0')
    },
  },
  filters: {
    has: (account, value) => {
      switch (value) {
        case 'sites':
          return account.siteCount > 0
        case 'databases':
          return account.databaseCount > 0
        case 'cron':
          return account.cronEntryCount > 0
        default:
          return account.mailboxCount > 0
      }
    },
  },
}

/**
 * Answers a request with a JSON body.
 * @param route The intercepted request.
 * @param body The body.
 * @param status The status; 200 when left out.
 * @returns Resolves once the response is sent.
 */
const json = async (route: Route, body: unknown, status = 200): Promise<void> => {
  await route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(body) })
}

/**
 * Answers a request with an RFC 7807 problem body, the shape the SPA renders verbatim.
 * @param route The intercepted request.
 * @param detail The panel's localized sentence.
 * @param status The status; 409 when left out.
 * @returns Resolves once the response is sent.
 */
const problem = async (route: Route, detail: string, status = 409): Promise<void> => {
  await route.fulfill({
    status,
    contentType: 'application/problem+json',
    body: JSON.stringify({ code: 'MigrationStubRefusal', title: 'Refused', detail }),
  })
}

/**
 * Reduces a server's detail to what the list endpoint sends, which is never more than the list DTO.
 * @param server The detail.
 * @returns The list row.
 */
const toListRow = (server: RemoteServerDetail): RemoteServer => {
  return {
    id: server.id,
    name: server.name,
    host: server.host,
    port: server.port,
    sshUser: server.sshUser,
    state: server.state,
    stateName: server.stateName,
    hostKeyFingerprint: server.hostKeyFingerprint,
    lastTestedAt: server.lastTestedAt,
    kind: server.kind,
    kindName: server.kindName,
    accountsFound: server.accountsFound,
    createdAt: server.createdAt,
  }
}

/**
 * Reduces a discovered account's detail to what the list endpoint sends.
 * @param account The detail.
 * @returns The list row.
 */
const toAccountRow = (account: DiscoveredAccountDetail): DiscoveredAccount => {
  return {
    id: account.id,
    name: account.name,
    home: account.home,
    diskBytes: account.diskBytes,
    diskKnown: account.diskKnown,
    siteCount: account.siteCount,
    databaseCount: account.databaseCount,
    cronEntryCount: account.cronEntryCount,
    mailboxCount: account.mailboxCount,
    domains: account.domains,
  }
}

/**
 * Installs one handler for the whole remote-servers API, backed by mutable state, so a spec seeds
 * what the panel holds, drives the page and reads back what changed. Each resource answers like
 * the real endpoint: a list is a page honouring `page`, `pageSize`, `search`, `sort` and its
 * filters, a missing server is a 404 problem, the host key is pinned only to a scanned candidate,
 * and a request named in {@link MigrationStubState.refusals} is refused with the given sentence.
 * No answer ever carries a private key, as the real endpoint never does.
 * @param page The Playwright page whose network the route is installed on.
 * @param seed What the panel holds; anything left out starts empty or at its default.
 * @returns The live state.
 */
export const stubRemoteServers = async (page: Page, seed: Partial<MigrationStubState> = {}): Promise<MigrationStubState> => {
  const state: MigrationStubState = {
    servers: [],
    accounts: {},
    scannedKeys: SCANNED_KEYS,
    testFailure: null,
    warnings: [],
    refusals: {},
    requests: [],
    // A copy: a test changes the live state, and the seed's objects are shared fixtures every
    // other test in the worker reads.
    ...structuredClone(seed),
  }

  await page.route(REMOTE_SERVERS_API, async (route) => {
    const request = route.request()
    const url = new URL(request.url())
    const method = request.method()
    const body: unknown = request.postData() === null ? undefined : request.postDataJSON()
    state.requests.push({ method, url, body })

    const parts = url.pathname.replace('/api/v1/', '').split('/')
    const shape = parts
      .map((part) => {
        return LITERAL_SEGMENTS.has(part) ? part : ':id'
      })
      .join('/')
    const refusal = state.refusals[`${method} ${shape}`]
    if (refusal !== undefined) {
      await problem(route, refusal.detail, refusal.status)
      return
    }

    const notFound = (): Promise<void> => {
      return problem(route, 'There is no such resource.', 404)
    }
    const rowId = parts[1] ?? ''
    const server = state.servers.find((candidate) => {
      return candidate.id === rowId
    })

    switch (`${method} ${shape}`) {
      case 'GET remote-servers':
        return json(route, pagedAnswer(state.servers.map(toListRow), request.url(), SERVER_RULES))
      case 'POST remote-servers': {
        const added = body as RemoteServerRequest
        const made: RemoteServerDetail = {
          ...FRESH_SERVER,
          id: '99999999-9999-9999-9999-999999999999',
          name: added.name,
          host: added.host,
          port: added.port,
          sshUser: added.sshUser,
          allowedFrom: added.allowedFrom ?? null,
          hostKeyCandidates: state.scannedKeys,
        }
        state.servers.push(made)
        return json(route, made, 201)
      }
      case 'GET remote-servers/:id':
        return server === undefined ? notFound() : json(route, server)
      case 'PUT remote-servers/:id': {
        if (server === undefined) {
          return notFound()
        }
        const changed = body as RemoteServerRequest
        const retargeted = changed.host !== server.host || changed.port !== server.port || changed.sshUser !== server.sshUser
        Object.assign(server, changed)
        if (retargeted) {
          // A new target forgets the pin, as the panel does.
          Object.assign(server, {
            state: 'hostKeyUnconfirmed',
            stateName: STATE_NAMES.hostKeyUnconfirmed,
            hostKeyFingerprint: null,
            hostKeyAlgorithm: null,
            hostKeyConfirmedAt: null,
            hostKeyCandidates: state.scannedKeys,
          })
        }
        return json(route, server)
      }
      case 'POST remote-servers/:id/host-keys/scan':
        if (server === undefined) {
          return notFound()
        }
        server.hostKeyCandidates = state.scannedKeys
        return json(route, server)
      case 'POST remote-servers/:id/host-key': {
        if (server === undefined) {
          return notFound()
        }
        const fingerprint = (body as { fingerprint: string }).fingerprint
        const chosen = server.hostKeyCandidates.find((candidate) => {
          return candidate.fingerprint === fingerprint
        })
        if (chosen === undefined) {
          return problem(route, 'The fingerprint is none of the scanned host keys.')
        }
        Object.assign(server, {
          state: 'pinned',
          stateName: STATE_NAMES.pinned,
          hostKeyFingerprint: chosen.fingerprint,
          hostKeyAlgorithm: chosen.algorithm,
          hostKeyConfirmedAt: '2026-11-03T10:00:00Z',
          hostKeyCandidates: [],
          lastFailure: null,
          lastFailureName: null,
        })
        return json(route, server)
      }
      case 'POST remote-servers/:id/test': {
        if (server === undefined) {
          return notFound()
        }
        if (server.hostKeyFingerprint === null || server.hostKeyFingerprint === undefined) {
          return problem(route, 'Confirm the host key before testing the connection.')
        }
        if (state.testFailure !== null) {
          Object.assign(server, {
            state: state.testFailure,
            stateName: STATE_NAMES[state.testFailure],
            lastFailure: state.testFailure,
            lastFailureName: FAILURE_NAMES[state.testFailure],
            lastTestedAt: '2026-11-03T11:00:00Z',
          })
        } else {
          Object.assign(server, { ...READY_SERVER, id: server.id, name: server.name, host: server.host, lastTestedAt: '2026-11-03T11:00:00Z' })
        }
        return json(route, server)
      }
      case 'POST remote-servers/:id/discover': {
        if (server === undefined) {
          return notFound()
        }
        if (server.state !== 'ready' && server.state !== 'pinned') {
          return problem(route, 'The server must have a confirmed host key before it can be discovered.')
        }
        const found = state.accounts[server.id] ?? []
        Object.assign(server, { accountsFound: found.length, discoveredAt: '2026-11-03T12:00:00Z', warnings: state.warnings })
        return json(route, { discoveredAt: '2026-11-03T12:00:00Z', accountCount: found.length, warnings: state.warnings })
      }
      case 'GET remote-servers/:id/accounts': {
        if (server === undefined) {
          return notFound()
        }
        const page = pagedAnswer(state.accounts[server.id] ?? [], request.url(), ACCOUNT_RULES)
        return json(route, { ...page, items: page.items.map(toAccountRow) })
      }
      case 'GET remote-servers/:id/accounts/:id': {
        const account = (state.accounts[rowId] ?? []).find((candidate) => {
          return candidate.id === parts[3]
        })
        return account === undefined ? notFound() : json(route, account)
      }
      case 'DELETE remote-servers/:id':
        if (server === undefined) {
          return notFound()
        }
        state.servers = state.servers.filter((candidate) => {
          return candidate.id !== rowId
        })
        return json(route, { publicKey: server.publicKey, hostKeyFingerprint: server.hostKeyFingerprint ?? null })
      default:
        return notFound()
    }
  })

  return state
}

/**
 * Signs an administrator in on a desktop, installs the stubs every migration screen reads, and
 * opens the panel's landing page, from which a spec reaches the screens by clicking.
 * @param page The Playwright page under test.
 * @param seed What the panel holds; see {@link stubRemoteServers}.
 * @returns The stub's live state.
 */
export const openMigrationScreen = async (page: Page, seed: Partial<MigrationStubState> = {}): Promise<MigrationStubState> => {
  await page.setViewportSize({ width: 1440, height: 900 })
  await stubSignedIn(page)
  await stubShellReads(page)
  await stubLandingReads(page)
  await stubHealthy(page)
  await stubModules(page, MIGRATION_MODULES)
  const state = await stubRemoteServers(page, seed)
  await page.goto('/')
  await expect(page.locator('nav [data-nav-section]').first()).toBeVisible()
  return state
}

/**
 * Reaches the remote servers list the way a person does: by the sidebar's "Remote servers" link,
 * inside the "Moving accounts" section the migration pages share.
 * @param page The Playwright page under test.
 * @returns Resolves once the list's heading is drawn.
 */
export const openRemoteServersList = async (page: Page): Promise<void> => {
  await openSection(page, 'Moving accounts')
  await mainNavigation(page).getByRole('link', { name: 'Remote servers', exact: true }).click()
  await expect(page.getByRole('heading', { level: 1, name: 'Remote servers' })).toBeVisible()
}
