import type { ApiDownload } from './api'
import type { ListRequest, Paged } from './list'

/**
 * An account exported for another Maran server, mirroring the backend's `AccountExportDto`
 * (`/api/v1/account-exports`, issue #127). The package itself is never part of this shape: it is
 * fetched by {@link AccountMoveApi.downloadPackage}.
 */
export interface AccountExport {
  /** The export's identity. */
  id: string
  /** The account's system user name. */
  accountName: string
  /** The artifact's size in bytes. */
  sizeBytes: number
  /** The artifact's lowercase hexadecimal SHA-256. */
  sha256: string
  /** How many database dumps the artifact holds. */
  databaseCount: number
  /** The ids of the modules that contributed a section to the package. */
  sectionModules: string[]
  /** This server's signing-key fingerprint, which the receiving administrator compares and retypes. */
  keyFingerprint: string
  /** When the export was made (ISO 8601). */
  createdAt: string
  /** When its package stops being served (ISO 8601). */
  expiresAt: string
}

/** What issuing a pull grant sends, mirroring the backend's `IssueExportGrantCommand` body. */
export interface IssueExportGrantRequest {
  /** An address range the pull must come from, or `null` for any address. */
  allowedFrom: string | null
  /** How long the grant works in minutes, or `null` for the panel's default; the panel caps it. */
  lifetimeMinutes: number | null
}

/**
 * A pull grant just issued, mirroring the backend's `ExportGrantDto`. The token is returned this
 * once; the panel stores only its digest and no endpoint returns it again.
 */
export interface ExportGrant {
  /** The grant's identity, used to revoke it. */
  id: string
  /** The export the grant opens. */
  exportId: string
  /** The bearer token the receiving panel presents. Shown once and never kept on screen afterwards. */
  token: string
  /** The address range the pull must come from, or `null` for any. */
  allowedFrom: string | null
  /** When the grant stops working (ISO 8601). */
  expiresAt: string
}

/** Where a pull grant stands, mirroring the closed set of names the backend's `ExportGrantSummaryDto.State` carries. */
export type ExportGrantState = 'Live' | 'Used' | 'Revoked' | 'Expired'

/**
 * One pull grant in the grants list, mirroring the backend's `ExportGrantSummaryDto`. The token is
 * never part of it: it was shown once, when the grant was issued, and exists nowhere afterwards.
 */
export interface ExportGrantSummary {
  /** The grant's identity, with which an administrator revokes it. */
  id: string
  /** The export the grant opens. */
  exportId: string
  /** The exported account's system user name. */
  accountName: string
  /** Where the grant stands. */
  state: ExportGrantState
  /** The state as an operator reads it, in the caller's language, worded by the panel. */
  stateName: string
  /** The address range the pull must come from, or `null` for any. */
  allowedFrom: string | null
  /** How many times the head of the package was read. */
  manifestReads: number
  /** When the grant was issued (ISO 8601). */
  createdAt: string
  /** When the grant stops working (ISO 8601). */
  expiresAt: string
  /** When the artifact was read (ISO 8601), or `null`. */
  artifactConsumedAt: string | null
  /** When an administrator revoked it (ISO 8601), or `null`. */
  revokedAt: string | null
}

/** Where an import stands, mirroring the backend's `AccountImportState` (stored and sent by name). */
export type AccountImportState = 'Staged' | 'Running' | 'Completed' | 'RolledBack' | 'RolledBackIncomplete' | 'Discarded'

/** An import of one account, mirroring the backend's `AccountImportDto`. */
export interface AccountImport {
  /** The import's identity. */
  id: string
  /** Where the import stands. */
  state: AccountImportState
  /** The state as an operator reads it, in the caller's language, worded by the panel. */
  stateName: string
  /** The account's system user name, which this server keeps. */
  accountName: string
  /** The account's primary domain on the sending server. */
  primaryDomain: string
  /** The name the sending server gave itself. */
  sourceHostName: string
  /** The fingerprint of the key that signed the package, as confirmed. */
  keyFingerprint: string
  /** The artifact's size in bytes. */
  sizeBytes: number
  /** The size of the home directory inside the artifact, in bytes. */
  homeBytes: number
  /** How many database dumps the artifact holds. */
  databaseCount: number
  /** The ids of the modules whose sections the package carries. */
  sectionModules: string[]
  /** The account the import created, or `null` until it has. */
  targetAccountId: string | null
  /** The step the job is at (a module id or `restore`), or `null`. */
  stage: string | null
  /** The code of what stopped the import, or `null`; behaviour only, never a text lookup. */
  failureCode: string | null
  /** The sentence for {@link failureCode} in the caller's language, or `null` when the import did not fail. */
  failureName: string | null
  /** Whether generated credentials wait to be claimed once. */
  hasIssuedSecrets: boolean
  /** When the package was accepted (ISO 8601). */
  createdAt: string
  /** When the import ended (ISO 8601), or `null`. */
  finishedAt: string | null
}

/** How much a dry-run finding matters, mirroring the backend's closed finding severity names. */
export type MigrationFindingSeverity = 'Info' | 'Warning' | 'Conflict'

/** One finding of the dry run, mirroring the backend's `MigrationFindingDto`; its sentence arrives localized. */
export interface MigrationFinding {
  /** The module that found it. */
  moduleId: string
  /** How much it matters. */
  severity: MigrationFindingSeverity
  /** The machine-stable code; used for behaviour only, never as a text lookup. */
  code: string
  /** The sentence in the caller's language. */
  message: string
  /** What the finding is about. */
  subject: string
}

/** The dry run of an import, mirroring the backend's `AccountImportReportDto`. */
export interface AccountImportReport {
  /** The import judged. */
  importId: string
  /** Whether the report holds no conflict. Advice: the panel refuses a start with a conflict regardless. */
  canStart: boolean
  /** The findings, conflicts first. */
  findings: MigrationFinding[]
}

/** What starting an import sends, mirroring the backend's `StartAccountImportCommand` body. */
export interface StartAccountImportRequest {
  /** The plan the account is created against on this server. */
  planId: string
  /** The reseller that is to own the account, or `null` for the administrator's own. */
  resellerId: string | null
  /** The account's name, retyped; compared by the panel ordinally and case-sensitively. */
  confirmAccountName: string
  /** The sending server's key fingerprint, retyped; compared by the panel ordinally and case-sensitively. */
  confirmKeyFingerprint: string
}

/** A credential the import generated, mirroring the backend's `IssuedSecretDto`. Returned once. */
export interface IssuedSecret {
  /** What the credential opens. */
  subject: string
  /** The generated credential. */
  secret: string
}

/** The action of the open export in flight, for the controls that disable themselves meanwhile. */
export type AccountExportAction = 'create' | 'download' | 'grant' | 'revoke'

/** The action of the open import in flight. */
export type AccountImportAction = 'stage' | 'report' | 'start' | 'discard' | 'claim'

/**
 * The account-move endpoints (`/api/v1/account-exports`, `/api/v1/account-imports`), as the stores
 * consume them. Called from Pinia stores only, never from a component (rules/vue.md).
 */
export interface AccountMoveApi {
  /**
   * Exports one account: takes the backup and gathers what every module hands over.
   * @param accountId The account to export.
   * @param signal Optional abort signal.
   * @returns The export as made.
   */
  createExport: (accountId: string, signal?: AbortSignal) => Promise<AccountExport>

  /**
   * Lists one page of the exports, searched by the exported account's name.
   * @param request The page, size, search and sort.
   * @param signal Optional abort signal.
   * @returns The page.
   */
  listExports: (request: ListRequest, signal?: AbortSignal) => Promise<Paged<AccountExport>>

  /**
   * Lists one page of the pull grants, searched by the exported account's name and narrowed to a state.
   * @param request The page, size, search, sort and the `state` filter.
   * @param signal Optional abort signal.
   * @returns The page; no row carries a token.
   */
  listGrants: (request: ListRequest, signal?: AbortSignal) => Promise<Paged<ExportGrantSummary>>

  /**
   * Reads one export.
   * @param id The export's identity.
   * @param signal Optional abort signal.
   * @returns The export.
   */
  getExport: (id: string, signal?: AbortSignal) => Promise<AccountExport>

  /**
   * Fetches the package of an export as a file.
   * @param id The export's identity.
   * @param signal Optional abort signal.
   * @returns The file and the name the panel proposed.
   */
  downloadPackage: (id: string, signal?: AbortSignal) => Promise<ApiDownload>

  /**
   * Issues a single-use pull grant. The token comes back once.
   * @param id The export's identity.
   * @param request The grant's bounds.
   * @param signal Optional abort signal.
   * @returns The grant with its token.
   */
  issueGrant: (id: string, request: IssueExportGrantRequest, signal?: AbortSignal) => Promise<ExportGrant>

  /**
   * Revokes a grant before it is used or expires.
   * @param grantId The grant's identity.
   * @param signal Optional abort signal.
   * @returns Resolves once the panel revoked it.
   */
  revokeGrant: (grantId: string, signal?: AbortSignal) => Promise<void>

  /**
   * Uploads a package; the panel verifies it against the fingerprint before accepting it.
   * @param file The package file.
   * @param fingerprint The sending server's key fingerprint, as read on that server.
   * @param signal Optional abort signal.
   * @returns The staged import.
   */
  stageImport: (file: Blob, fingerprint: string, signal?: AbortSignal) => Promise<AccountImport>

  /**
   * Lists one page of the imports, searched by the account's or the sending server's name and narrowed to a state.
   * @param request The page, size, search, sort and the `state` filter.
   * @param signal Optional abort signal.
   * @returns The page.
   */
  listImports: (request: ListRequest, signal?: AbortSignal) => Promise<Paged<AccountImport>>

  /**
   * Reads one import.
   * @param id The import's identity.
   * @param signal Optional abort signal.
   * @returns The import.
   */
  getImport: (id: string, signal?: AbortSignal) => Promise<AccountImport>

  /**
   * Judges a staged import against this server and changes nothing.
   * @param id The import's identity.
   * @param planId The plan the account would be created against.
   * @param signal Optional abort signal.
   * @returns The dry-run report.
   */
  getReport: (id: string, planId: string, signal?: AbortSignal) => Promise<AccountImportReport>

  /**
   * Starts the job that creates the account.
   * @param id The import's identity.
   * @param request The plan, the optional reseller and the two retyped confirmations.
   * @param signal Optional abort signal.
   * @returns The import as it now stands.
   */
  startImport: (id: string, request: StartAccountImportRequest, signal?: AbortSignal) => Promise<AccountImport>

  /**
   * Discards a staged import without starting it.
   * @param id The import's identity.
   * @param signal Optional abort signal.
   * @returns The import as it now stands.
   */
  discardImport: (id: string, signal?: AbortSignal) => Promise<AccountImport>

  /**
   * Claims, once, the credentials the import generated.
   * @param id The import's identity.
   * @param signal Optional abort signal.
   * @returns The credentials.
   */
  claimSecrets: (id: string, signal?: AbortSignal) => Promise<IssuedSecret[]>
}
