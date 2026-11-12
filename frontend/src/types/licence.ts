/**
 * The panel's licence as the administrator-only "Licence" screen needs it (rules/vue.md "Types" —
 * one domain per file). Every sentence and every display name arrives already localized from the
 * backend and is rendered as plain text; the SPA holds no list of states, reasons or tiers.
 */

/** Whether the panel holds a licence it accepts. */
export type LicenceState = 'valid' | 'absent' | 'refused'

/** Why an installed licence was refused. */
export type LicenceRefusalReason =
  | 'malformed'
  | 'signatureInvalid'
  | 'expired'
  | 'productMismatch'
  | 'fingerprintMismatch'
  | 'noVerificationKey'

/** Where the periodic lease that keeps a licence alive stands. */
export type LicenceLeaseState =
  | 'missing'
  | 'refused'
  | 'fresh'
  | 'renewalOverdue'
  | 'expired'
  | 'futureDated'
  | 'wrongLicence'
  | 'wrongMachine'
  | 'notRequired'

/** What asking the licensing service for a lease amounted to, as the panel reports it after an install. */
export type LicenceLeaseRequest =
  | 'renewed'
  | 'notDue'
  | 'skippedOffline'
  | 'skippedNoLicence'
  | 'notConfigured'
  | 'unreachable'
  | 'refused'
  | 'answerRejected'
  | 'notNewer'
  | 'writeFailed'
  | 'licenceChanged'
  | 'renewalInProgress'

/** What `GET /api/v1/licence-status` answers, and what a successful install answers with. */
export interface LicenceStatus {
  /** Whether a licence is installed and accepted. */
  state: LicenceState
  /** The licence's identifier, or `null` when none is installed. */
  licenceId: string | null
  /** The licence tier, or `null` when none is installed. */
  tier: string | null
  /** The paid modules the licence covers, or `null` when none is installed. */
  modules: string[] | null
  /** When the licence expires, as an ISO 8601 timestamp, or `null` when it does not or none is installed. */
  expiry: string | null
  /** Why the licence was refused, or `null` when it was not. */
  refusalReason: LicenceRefusalReason | null
  /** One sentence describing the state, already localized by the backend. */
  sentence: string
  /** What the administrator can do about it, already localized, or `null` when nothing is needed. */
  advice: string | null
  /** The state in the words an operator reads, already localized by the backend. */
  stateDisplayName: string
  /** The refusal reason in the words an operator reads, already localized, or `null` when not refused. */
  refusalReasonDisplayName: string | null
  /** True when the licence is verified on this server without contacting a licensing service. */
  isOffline: boolean
  /** Where the lease stands. */
  lease: LicenceLeaseState
  /** The lease state in the words an operator reads, already localized by the backend. */
  leaseDisplayName: string
  /** Until when the lease is valid, as an ISO 8601 timestamp, or `null` when there is no lease. */
  leaseValidUntil: string | null
  /** This server's ID exactly as a licence names it to be bound here, or `null` when the panel could not read it. */
  serverId: string | null
  /** The marketplace page where a licence is claimed for this server, or `null` when none can be made. */
  claimUrl: string | null
  /** What the lease request made by the install this answers amounted to; `null` on a plain read. */
  leaseRequest: LicenceLeaseRequest | null
  /** `leaseRequest` as a sentence, already localized by the backend; `null` exactly when it is. */
  leaseRequestDisplayName: string | null
  /**
   * Whole days until the licence expires (rounded up), present exactly when it is still in date and within one of
   * the backend's warning thresholds (30, 14 and 3 days by default); `null` otherwise. The backend owns the
   * thresholds: this SPA never compares dates to decide whether to warn.
   */
  expiryWarningDaysLeft: number | null
  /** The expiry warning as a sentence naming the expiry day, already localized; `null` exactly when there is no warning. */
  expiryWarning: string | null
  /** The renewal steps (claim the licence again, or install a new licence file), already localized; `null` exactly when there is no warning. */
  expiryRenewalAdvice: string | null
}

/** What `POST /api/v1/licence-status/install` takes. */
export interface InstallLicenceRequest {
  /** The licence text exactly as pasted. Long JSON; never rendered as markup and never logged. */
  rawLicenceText: string
}

/** Public surface of the licence API composable. */
export interface LicenceApi {
  /** Reads the licence status. */
  status: (signal?: AbortSignal) => Promise<LicenceStatus>
  /** Installs a pasted licence and answers with the status that results. */
  install: (rawLicenceText: string, signal?: AbortSignal) => Promise<LicenceStatus>
}
