import type { ListRequest, Paged } from './list'

/**
 * Where a remote server (a migration source) stands, mirroring the backend's `RemoteServerState`.
 * Enums travel as camelCase strings, as the panel-wide JSON options write every enum.
 */
export type RemoteServerState =
  /** Added; the scanned host keys wait for the administrator to confirm one. */
  | 'hostKeyUnconfirmed'
  /** A host key is pinned; the connection has not been tested since. */
  | 'pinned'
  /** The last connection test succeeded. */
  | 'ready'
  /** The server presented a different host key than the pinned one. */
  | 'hostKeyChanged'
  /** The server refused our key. */
  | 'authRefused'
  /** The server could not be reached. */
  | 'unreachable'

/** Why the last connection test failed, mirroring the backend's `RemoteFailureKind`. */
export type RemoteFailureKind = 'hostKeyChanged' | 'authRefused' | 'unreachable' | 'addressRefused'

/** What kind of server the source is, mirroring the backend's `RemoteSourceKind`. */
export type RemoteSourceKind = 'genericLinux' | 'maran'

/** The backend's `DiscoveryWarningKind`: the closed set of things discovery can warn about. */
export type DiscoveryWarningKind =
  | 'nameRefused'
  | 'siteUnreadable'
  | 'cronUnreadable'
  | 'databasesUnreadable'
  | 'diskUnreadable'
  | 'truncated'
  | 'mailNotMigrated'

/** How a discovered site is served, mirroring the backend's `DiscoveredSiteBackend`. */
export type DiscoveredSiteBackend = 'unknown' | 'static' | 'php' | 'proxy'

/** The database server found on the source. */
export type RemoteDatabaseServer = 'MariaDb' | 'MySql'

/** The web server found on the source. */
export type RemoteWebServer = 'Nginx' | 'Apache'

/**
 * A remote server as the list sees it, mirroring the backend's `RemoteServerDto`. Never carries
 * key material of any kind.
 */
export interface RemoteServer {
  /** The server's identity. */
  id: string
  /** The name the administrator gave it. */
  name: string
  /** The host name or address the panel connects to. */
  host: string
  /** The SSH port. */
  port: number
  /** The login on the remote server. */
  sshUser: string
  /** Where the server stands. */
  state: RemoteServerState
  /** The state in the caller's language, as the panel names it. */
  stateName: string
  /** The pinned host key's fingerprint, or absent while none is pinned. */
  hostKeyFingerprint?: string | null
  /** When the connection was last tested, or absent when it never was. */
  lastTestedAt?: string | null
  /** What the last successful test found the server to be, or absent when not determined. */
  kind?: RemoteSourceKind | null
  /** The kind in the caller's language, or absent when there is none. */
  kindName?: string | null
  /** How many accounts the last discovery found, or absent when none ran. */
  accountsFound?: number | null
  /** When the server was added. */
  createdAt: string
}

/** One host key the panel scanned from the server and offers for confirmation. */
export interface HostKeyCandidate {
  /** The key algorithm (`ssh-ed25519`, ...). */
  algorithm: string
  /** The `SHA256:` fingerprint the administrator compares with the source. */
  fingerprint: string
}

/** One thing discovery could not read or chose not to carry, mirroring `DiscoveryWarningDto`. */
export interface DiscoveryWarning {
  /** The closed kind of warning. */
  kind: DiscoveryWarningKind
  /** What the warning is about (an account, a site, a database); source data, shown as received. */
  subject: string
  /** The sentence, already localized by the panel; shown as received. */
  message: string
}

/** A remote server with everything the detail screen shows, mirroring `RemoteServerDetailDto`. */
export interface RemoteServerDetail extends RemoteServer {
  /** The address list the source's `authorized_keys` entry is limited to, or absent. */
  allowedFrom?: string | null
  /** Our public key, the line the operator adds to the source. */
  publicKey: string
  /** The complete `authorized_keys` line, built by the panel. */
  authorizedKeysLine: string
  /** The pinned host key's algorithm. */
  hostKeyAlgorithm?: string | null
  /** When the host key was confirmed. */
  hostKeyConfirmedAt?: string | null
  /** The scanned host keys awaiting confirmation. */
  hostKeyCandidates: HostKeyCandidate[]
  /** Why the last test failed, or absent. */
  lastFailure?: RemoteFailureKind | null
  /** The failure in the caller's language, or absent when there is none. */
  lastFailureName?: string | null
  /** The source's operating system release. */
  systemRelease?: string | null
  /** The Maran version, when the source runs one. */
  maranVersion?: string | null
  /** Whether the login is root. */
  isRoot?: boolean | null
  /** Free bytes on the home filesystem. */
  homeFreeBytes?: number | null
  /** Total bytes on the home filesystem. */
  homeTotalBytes?: number | null
  /** The database server found. */
  databaseServer?: RemoteDatabaseServer | null
  /** The web server found. */
  webServer?: RemoteWebServer | null
  /** Whether mail storage was found (mail is not migrated). */
  mailFound?: boolean | null
  /** When discovery last ran. */
  discoveredAt?: string | null
  /** The warnings of the last discovery. */
  warnings: DiscoveryWarning[]
}

/** What the panel sends to add a server or to change its target, mirroring the add/edit commands. */
export interface RemoteServerRequest {
  /** The name, unique among remote servers. */
  name: string
  /** The host name or address. */
  host: string
  /** The SSH port. */
  port: number
  /** The remote login. */
  sshUser: string
  /** The addresses the key is limited to in `authorized_keys`; absent or null for no limit. */
  allowedFrom?: string | null
}

/** What the panel answers when a server is removed: the key the operator should take off the source. */
export interface RemovedRemoteServer {
  /** Our public key, to remove from the source's `authorized_keys`. */
  publicKey: string
  /** The fingerprint that had been pinned, if any. */
  hostKeyFingerprint?: string | null
}

/** The outcome of a discovery run, mirroring `DiscoveryResultDto`. */
export interface DiscoveryResult {
  /** When discovery finished. */
  discoveredAt: string
  /** How many accounts it found. */
  accountCount: number
  /** What it could not read or does not carry. */
  warnings: DiscoveryWarning[]
}

/** An account found on the source, as the list sees it, mirroring `DiscoveredAccountDto`. */
export interface DiscoveredAccount {
  /** The discovered account's identity. */
  id: string
  /** The login name on the source. */
  name: string
  /** The home directory on the source. */
  home: string
  /** Disk used under the home, in bytes; meaningful only when {@link diskKnown}. */
  diskBytes: number
  /** Whether the disk figure could be read. */
  diskKnown: boolean
  /** How many sites it serves. */
  siteCount: number
  /** How many databases it owns. */
  databaseCount: number
  /** How many cron entries it has. */
  cronEntryCount: number
  /** How many mailboxes were found (mail is not migrated). */
  mailboxCount: number
  /** The domains of its sites. */
  domains: string[]
}

/** A site of a discovered account. */
export interface DiscoveredSite {
  /** The primary domain. */
  domain: string
  /** The alias domains. */
  aliases: string[]
  /** How the site is served. */
  backend: DiscoveredSiteBackend
  /** The backend in the caller's language, as the panel names it. */
  backendName: string
  /** The PHP version, when the site runs PHP and it is known. */
  phpVersion?: string | null
  /** The document root on the source. */
  documentRoot: string
}

/** A database of a discovered account. */
export interface DiscoveredDatabase {
  /** The database name. */
  name: string
  /** Its size in bytes; meaningful only when {@link sizeKnown}. */
  sizeBytes: number
  /** Whether the size could be read. */
  sizeKnown: boolean
}

/** A discovered account with its sites and databases, mirroring `DiscoveredAccountDetailDto`. */
export interface DiscoveredAccountDetail extends DiscoveredAccount {
  /** The sites it serves. */
  sites: DiscoveredSite[]
  /** The databases it owns. */
  databases: DiscoveredDatabase[]
}

/** The `has` filter of the discovered-accounts list: accounts that have this kind of content. */
export type DiscoveredAccountHas = 'sites' | 'databases' | 'cron' | 'mail'

/**
 * Typed access to the remote-server endpoints (administrator-only on the server).
 *
 * Called from Pinia stores only, never from a component (rules/vue.md).
 */
export interface RemoteServersApi {
  /**
   * Lists one page of remote servers; the `state` filter is sent as the endpoint's `state`.
   * @param request The page, size, search, sort and filters.
   * @param signal Optional abort signal.
   * @returns The page the panel answered.
   */
  list: (request: ListRequest, signal?: AbortSignal) => Promise<Paged<RemoteServer>>

  /**
   * Reads one remote server.
   * @param id The server's identity.
   * @param signal Optional abort signal.
   * @returns The server with its detail.
   */
  get: (id: string, signal?: AbortSignal) => Promise<RemoteServerDetail>

  /**
   * Adds a server: the panel generates its key and scans its host keys.
   * @param request The server's name, address, port, login and optional address limit.
   * @param signal Optional abort signal.
   * @returns The server as stored, in `hostKeyUnconfirmed`.
   */
  add: (request: RemoteServerRequest, signal?: AbortSignal) => Promise<RemoteServerDetail>

  /**
   * Changes a server's name, target or address limit.
   * @param id The server's identity.
   * @param request The new values.
   * @param signal Optional abort signal.
   * @returns The server as it now stands.
   */
  edit: (id: string, request: RemoteServerRequest, signal?: AbortSignal) => Promise<RemoteServerDetail>

  /**
   * Scans the server's host keys again, replacing the candidates (never pins).
   * @param id The server's identity.
   * @param signal Optional abort signal.
   * @returns The server with its new candidates.
   */
  rescanHostKeys: (id: string, signal?: AbortSignal) => Promise<RemoteServerDetail>

  /**
   * Pins the host key whose fingerprint is given.
   * @param id The server's identity.
   * @param fingerprint The exact `SHA256:` fingerprint of one scanned candidate.
   * @param signal Optional abort signal.
   * @returns The server with the key pinned.
   */
  confirmHostKey: (id: string, fingerprint: string, signal?: AbortSignal) => Promise<RemoteServerDetail>

  /**
   * Tests the connection with the pinned key.
   * @param id The server's identity.
   * @param signal Optional abort signal.
   * @returns The server with the test's outcome recorded.
   */
  test: (id: string, signal?: AbortSignal) => Promise<RemoteServerDetail>

  /**
   * Removes a server.
   * @param id The server's identity.
   * @param signal Optional abort signal.
   * @returns The public key to take off the source.
   */
  remove: (id: string, signal?: AbortSignal) => Promise<RemovedRemoteServer>

  /**
   * Reads the source's inventory and replaces the discovered rows.
   * @param id The server's identity.
   * @param signal Optional abort signal.
   * @returns How many accounts were found and what could not be read.
   */
  discover: (id: string, signal?: AbortSignal) => Promise<DiscoveryResult>

  /**
   * Lists one page of the accounts discovered on a server; the `has` filter is sent as `has`.
   * @param id The server's identity.
   * @param request The page, size, search, sort and filters.
   * @param signal Optional abort signal.
   * @returns The page the panel answered.
   */
  listAccounts: (id: string, request: ListRequest, signal?: AbortSignal) => Promise<Paged<DiscoveredAccount>>

  /**
   * Reads one discovered account with its sites and databases.
   * @param id The server's identity.
   * @param accountId The discovered account's identity.
   * @param signal Optional abort signal.
   * @returns The account's detail.
   */
  getAccount: (id: string, accountId: string, signal?: AbortSignal) => Promise<DiscoveredAccountDetail>
}

/** Which action on the open remote server is in flight; at most one runs at a time. */
export type RemoteServerAction = 'rescan' | 'confirm' | 'test' | 'discover' | 'remove'
