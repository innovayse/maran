import type { ListLoad, ListRequest, Paged } from './list'

/**
 * Whether a mail domain is receiving and sending: `active`, or `suspended` while its account is.
 * The text a person reads is the row's `statusName`; this only picks the badge tone.
 */
export type MailDomainStatus = 'active' | 'suspended'

/**
 * Outward view of one mail domain, mirroring the backend's `MailDomainDto` field-for-field. It never
 * carries a DKIM private key: the key is generated on the server, kept encrypted there and sent to
 * the mail host, and no response returns it.
 */
export interface MailDomain {
  /** The domain's identity, and the only identifier a request may name. */
  id: string
  /** The account that owns this domain. */
  accountId: string
  /**
   * The owning account's current short name, resolved by the panel. An empty string when the panel
   * does not name that account for this caller.
   */
  ownerName: string
  /** The domain name, lower-case; it equals a hostname of one of the owning account's sites. */
  name: string
  /** Whether the domain is receiving and sending. */
  status: MailDomainStatus
  /** The state as the caller reads it, in their language, produced by the panel. */
  statusName: string
  /** The selector of the DKIM key that signs this domain's mail now; part of the DKIM record's name. */
  dkimSelector: string
  /** How many mailboxes the domain holds. */
  mailboxCount: number
  /** The instant the domain was added, as an ISO-8601 string. */
  createdAt: string
}

/**
 * Request body for `POST /api/v1/mail-domains`, binding the backend's `CreateMailDomainCommand`
 * field-for-field. The domain must be a hostname of one of the account's own sites; the server
 * refuses any other and a name another account already holds.
 */
export interface CreateMailDomainRequest {
  /** The account that will own the domain. */
  accountId: string
  /** The domain name to receive and send mail for. */
  name: string
}

/**
 * One DNS record the customer publishes so the domain's mail is delivered and trusted, mirroring
 * `MailDnsRecordDto`. The panel computes the records and displays them; it publishes none of them.
 */
export interface MailDnsRecord {
  /** The record type as DNS spells it (`MX`, `TXT`). */
  type: string
  /** The record's name, relative to the zone as the server states it (`@`, `selector._domainkey`). */
  name: string
  /** The record's value exactly as it must be entered. */
  value: string
  /** The preference of an `MX` record; `null` for every other type. */
  priority: number | null
  /** What the record is for, in the caller's language (`Mail delivery`, `DKIM signing key`), produced by the panel. */
  purposeName: string
}

/**
 * Typed access to the mail domains endpoints.
 *
 * Called from Pinia stores only — never from a component (rules/vue.md).
 */
export interface MailDomainsApi {
  /**
   * Lists one page of the mail domains the caller may see. Another customer's rows are not in the
   * answer at all.
   * @param request The page, size, search, sort and the account and status filters.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The page the panel answered, with the total across every page.
   */
  list: (request: ListRequest, signal?: AbortSignal) => Promise<Paged<MailDomain>>

  /**
   * Adds a mail domain.
   * @param request The owning account and the domain name.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The domain as created.
   */
  create: (request: CreateMailDomainRequest, signal?: AbortSignal) => Promise<MailDomain>

  /**
   * Removes the domain with its mailboxes' configuration, aliases and DKIM keys.
   * @param id The domain to remove.
   * @param signal Optional abort signal to cancel the in-flight request.
   */
  remove: (id: string, signal?: AbortSignal) => Promise<void>

  /**
   * Starts a new DKIM key for the domain; the old one keeps verifying mail already sent until it retires.
   * @param id The domain whose key rotates.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The domain with its new selector.
   */
  rotateDkim: (id: string, signal?: AbortSignal) => Promise<MailDomain>

  /**
   * Reads the DNS records the customer publishes for the domain.
   * @param id The domain.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The records, in the order they should be entered.
   */
  dnsRecords: (id: string, signal?: AbortSignal) => Promise<MailDnsRecord[]>
}

/** An action the panel offers on a mail domain's row, confirmed by the person before it is sent. */
export type MailDomainAction = 'rotateDkim' | 'remove'

/** A domain and the action the person started on it; the two are only ever known together. */
export interface PendingMailDomainAction {
  /** The domain the action is aimed at. */
  domain: MailDomain
  /** Which action that domain is waiting on. */
  action: MailDomainAction
}

/**
 * What the domain picker last read: its page of matches, the panel's refusal, no answer, or
 * `idle` while the picker has not asked yet. The `idle` kind is the picker's own and is drawn as
 * "loading", because the first search is sent the moment the picker opens.
 */
export type MailDomainChoicesRead = ListLoad<MailDomain> | { kind: 'idle' }
