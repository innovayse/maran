import type { ApiDownload } from './api'
import type { ListRequest, Paged } from './list'

/** Where a whole-server migration job is, mirroring the backend's `MigrationJobState`. */
export type MigrationJobState = 'Draft' | 'Running' | 'Paused' | 'Completed' | 'Cancelled'

/** Where one account of a job is, mirroring the backend's `MigrationAccountState`. */
export type MigrationAccountState =
  | 'Pending'
  | 'Running'
  | 'Synced'
  | 'CuttingOver'
  | 'Completed'
  | 'Paused'
  | 'Failed'
  | 'Cancelled'
  | 'RolledBack'
  | 'RolledBackIncomplete'

/** What an administrator asks of a whole job, mirroring `MigrationJobAction`. */
export type MigrationJobAction = 'start' | 'pause' | 'resume' | 'cancel'

/** What an administrator asks of one account of a job, mirroring `MigrationAccountAction`. */
export type MigrationAccountAction = 'retry' | 'cancel' | 'cutOver'

/** A migration job as the screens show it, mirroring the backend's `MigrationJobDto`. */
export interface MigrationJob {
  /** The job's identity. */
  id: string
  /** The registered server the accounts come from. */
  remoteServerId: string
  /** The administrator's label. */
  name: string
  /** The job's state. */
  state: MigrationJobState
  /** The state as an operator reads it, in the caller's language, worded by the panel. */
  stateName: string
  /** How many accounts are transferred at once, 1 to 8. */
  concurrency: number
  /** The most KiB per second a transfer reads, or `null` for no limit. */
  bandwidthLimitKib: number | null
  /** For a Maran source, the origin the packages are pulled from. */
  sourceUrl: string | null
  /** How many accounts the job holds. */
  accountCount: number
  /** How many accounts are in each state, by state name. */
  accountStates: Record<string, number>
  /** When the job was created. */
  createdAt: string
  /** When the engine was started. */
  startedAt: string | null
  /** When the job ended. */
  finishedAt: string | null
}

/** One account of a job, mirroring `MigrationJobAccountDto`. Carries no secret. */
export interface MigrationJobAccount {
  /** The row's identity. */
  id: string
  /** The account's system user name on the source; it keeps it here. */
  sourceAccountName: string
  /** The account's home on the source. */
  sourceHome: string
  /** The size the discovery measured, or `null`. */
  sourceDiskBytes: number | null
  /** How many sites the discovery found. */
  siteCount: number
  /** How many databases the discovery found. */
  databaseCount: number
  /** How many crontab entries were counted (not transferred). */
  cronEntryCount: number
  /** How many mailboxes were counted (not transferred). */
  mailboxCount: number
  /** The plan the account is created against, once mapped. */
  planId: string | null
  /** The reseller that owns the account. */
  resellerId: string | null
  /** The owner's e-mail. */
  ownerEmail: string | null
  /** The owner's language. */
  ownerLanguage: string
  /** The primary domain here. */
  primaryDomain: string | null
  /** Whether a pull grant is held; the grant itself is never returned. */
  hasPullGrant: boolean
  /** The account's state. */
  state: MigrationAccountState
  /** The state as an operator reads it, in the caller's language, worded by the panel. */
  stateName: string
  /** The step the engine is at. */
  stage: string | null
  /** The machine code of what stopped the account. */
  failureCode: string | null
  /** That code as a sentence in the caller's language. */
  failureName: string | null
  /** The account created here. */
  targetAccountId: string | null
  /** How many rsync runs the last pull took. */
  slicesCompleted: number
  /** How many databases the last pass loaded. */
  databasesLoaded: number
  /** How many bytes of dump the last pass loaded. */
  dumpBytes: number
  /** Whether generated credentials wait to be claimed once. */
  hasIssuedSecrets: boolean
  /** When the initial sync finished. */
  syncedAt: string | null
  /** When the cut-over finished. */
  cutOverAt: string | null
}

/** The request to create a job, mirroring `CreateMigrationJobCommand`. */
export interface MigrationJobRequest {
  /** The registered server. */
  serverId: string
  /** The administrator's label. */
  name: string
  /** The discovered accounts to migrate. */
  discoveredAccountIds: string[]
  /** How many accounts at once, or omitted for the default. */
  concurrency?: number
  /** The bandwidth limit, or omitted for none. */
  bandwidthLimitKib?: number
  /** For a Maran source, the pull origin. */
  sourceUrl?: string
  /** For a Maran source, the confirmed key fingerprint. */
  expectedKeyFingerprint?: string
}

/** The mapping request, mirroring `SaveMigrationMappingCommand`. A field left out keeps what the row holds. */
export interface MigrationMappingRequest {
  /** The account rows to change; empty means every account of the job. */
  accountIds: string[]
  /** The plan. */
  planId?: string
  /** The owner's e-mail (one account only). */
  ownerEmail?: string
  /** The owner's language. */
  ownerLanguage?: string
  /** The primary domain (one account only). */
  primaryDomain?: string
  /** For a Maran source, the single-use grant (one account only); never returned. */
  pullGrant?: string
}

/** One finding of the dry run, mirroring `MigrationAccountFindingDto`. */
export interface MigrationFinding {
  /** The source account. */
  sourceAccountName: string
  /** The module that found it. */
  moduleId: string
  /** Info, Warning or Conflict. */
  severity: 'Info' | 'Warning' | 'Conflict'
  /** The machine-stable code. */
  code: string
  /** The code as a sentence in the caller's language. */
  message: string
  /** What it is about. */
  subject: string
}

/** The dry run of a job, mirroring `MigrationJobReportDto`. */
export interface MigrationJobReport {
  /** The job. */
  jobId: string
  /** Whether no account has a conflict; advice, because starting judges again. */
  canStart: boolean
  /** Every finding, conflicts first. */
  findings: MigrationFinding[]
}

/** The comparison report of one account's cut-over, mirroring `MigrationAccountReportDto`. */
export interface MigrationAccountReport {
  /** The source account. */
  sourceAccountName: string
  /** The size of the source home measured by the final pass. */
  sourceBytes: number | null
  /** How many rsync runs the final pass took. */
  slices: number
  /** Whether the final pass ended successfully. */
  filesConverged: boolean
  /** The databases loaded. */
  databases: { name: string; dumpBytes: number }[]
  /** What this panel's web server answered for each site. */
  sites: { domain: string; statusCode: number | null; title: string | null; serves: boolean }[]
  /** When the cut-over finished. */
  cutOverAt: string | null
}

/** Where the domains of a job resolve now, mirroring `MigrationDnsAdviceDto`. */
export interface MigrationDnsAdvice {
  /** One row per domain. */
  rows: { domain: string; addresses: string[]; pointsHere: boolean; resolved: boolean }[]
  /** The addresses this server holds. */
  thisServerAddresses: string[]
}

/** One credential the migration generated, shown once. */
export interface MigrationIssuedSecret {
  /** What the credential opens. */
  subject: string
  /** The credential. */
  secret: string
}

/** Which action on a job or an account is in flight. */
export type MigrationJobBusy = 'start' | 'pause' | 'resume' | 'cancel' | 'limits' | 'dryRun' | 'mapping' | 'dns' | 'account'

/** The migration-jobs API the store drives. */
export interface MigrationJobsApi {
  /** Lists one page of jobs. */
  list: (request: ListRequest, signal?: AbortSignal) => Promise<Paged<MigrationJob>>
  /** Reads one job. */
  get: (id: string, signal?: AbortSignal) => Promise<MigrationJob>
  /** Creates a draft job. */
  create: (request: MigrationJobRequest, signal?: AbortSignal) => Promise<MigrationJob>
  /** Lists one page of a job's accounts. */
  listAccounts: (id: string, request: ListRequest, signal?: AbortSignal) => Promise<Paged<MigrationJobAccount>>
  /** Maps accounts of a draft job; answers how many changed. */
  map: (id: string, request: MigrationMappingRequest, signal?: AbortSignal) => Promise<number>
  /** The dry run. */
  dryRun: (id: string, signal?: AbortSignal) => Promise<MigrationJobReport>
  /** Starts, pauses, resumes or cancels the job. */
  control: (id: string, action: MigrationJobAction, signal?: AbortSignal) => Promise<MigrationJob>
  /** Changes the limits. */
  setLimits: (id: string, concurrency: number, bandwidthLimitKib: number | null, signal?: AbortSignal) => Promise<MigrationJob>
  /** Retries, cancels or cuts over one account. */
  controlAccount: (id: string, accountId: string, action: MigrationAccountAction, signal?: AbortSignal) => Promise<MigrationJobAccount>
  /** The comparison report of one account. */
  report: (id: string, accountId: string, signal?: AbortSignal) => Promise<MigrationAccountReport>
  /** Claims the generated credentials once. */
  claimSecrets: (id: string, accountId: string, signal?: AbortSignal) => Promise<MigrationIssuedSecret[]>
  /** Where the job's domains resolve now. */
  dns: (id: string, signal?: AbortSignal) => Promise<MigrationDnsAdvice>
  /** Downloads the summary file of a job, as JSON or CSV. */
  summary: (id: string, asJson: boolean, signal?: AbortSignal) => Promise<ApiDownload>
}
