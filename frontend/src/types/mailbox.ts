import type { ListRequest, Paged } from './list'

/**
 * Whether a mailbox accepts mail and logins: `active`; `suspended` by the panel (the account or the
 * domain is); or `disabled` by its owner. The text a person reads is the row's `statusName`.
 */
export type MailboxStatus = 'active' | 'suspended' | 'disabled'

/**
 * Outward view of one mailbox, mirroring the backend's `MailboxDto` field-for-field. There is no
 * password here and nothing for one to come from: the server keeps only a hash of it.
 */
export interface Mailbox {
  /** The mailbox's identity, and the only identifier a request may name. */
  id: string
  /** The account that owns this mailbox. */
  accountId: string
  /**
   * The owning account's current short name, resolved by the panel. An empty string when the panel
   * does not name that account for this caller.
   */
  ownerName: string
  /** The identity of the mail domain the mailbox lives in. */
  mailDomainId: string
  /** The address, `localpart@domain`, as a mail client types it as the login. */
  address: string
  /** The size limit of the mailbox, in MiB. */
  quotaMib: number
  /** How much of the quota the mailbox uses, in MiB; `null` while the mail host has not reported it. */
  usedMib: number | null
  /** The most messages the mailbox may send in an hour. */
  sendLimitPerHour: number
  /** Whether the mailbox accepts mail and logins. */
  status: MailboxStatus
  /** The state as the caller reads it, in their language, produced by the panel. */
  statusName: string
  /** The owner's switch as stored; `status` hides it while the account is suspended. */
  enabled: boolean
  /** The instant the mailbox was created, as an ISO-8601 string. */
  createdAt: string
}

/**
 * Request body for `POST /api/v1/mailboxes`, binding the backend's `CreateMailboxCommand`
 * field-for-field. The owning account is the domain's own, never named here.
 */
export interface CreateMailboxRequest {
  /** The mail domain the mailbox lives in. */
  mailDomainId: string
  /** The part of the address before the `@`. */
  localPart: string
  /** The password the owner chose; the server stores only a hash of it. */
  password: string
  /** The size limit, in MiB. */
  quotaMib: number
}

/**
 * Request body for `PUT /api/v1/mailboxes/{id}`, binding `UpdateMailboxCommand`: the mailbox's
 * limits and whether it is switched on. The address and the password change through other calls.
 */
export interface UpdateMailboxRequest {
  /** The new size limit, in MiB. */
  quotaMib: number
  /** The new hourly send limit. */
  sendLimitPerHour: number
  /** Whether the owner has the mailbox switched on; a mailbox the panel suspended stays suspended. */
  enabled: boolean
}

/** Request body for `PUT /api/v1/mailboxes/{id}/password`, binding `ChangeMailboxPasswordCommand`. */
export interface ChangeMailboxPasswordRequest {
  /** The new password; the server stores only a hash of it and ends nothing that is already open. */
  password: string
}

/**
 * Typed access to the mailboxes endpoints.
 *
 * Called from Pinia stores only — never from a component (rules/vue.md).
 */
export interface MailboxesApi {
  /**
   * Lists one page of the mailboxes the caller may see.
   * @param request The page, size, search, sort and the account, domain and status filters.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The page the panel answered, with the total across every page.
   */
  list: (request: ListRequest, signal?: AbortSignal) => Promise<Paged<Mailbox>>

  /**
   * Creates a mailbox.
   * @param request The domain, local part, password and quota.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The mailbox as created.
   */
  create: (request: CreateMailboxRequest, signal?: AbortSignal) => Promise<Mailbox>

  /**
   * Changes a mailbox's quota, send limit and on/off state. Another customer's mailbox answers 404.
   * @param id The mailbox to change.
   * @param request The new limits and state.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The mailbox as stored.
   */
  update: (id: string, request: UpdateMailboxRequest, signal?: AbortSignal) => Promise<Mailbox>

  /**
   * Sets a new password. Another customer's mailbox answers 404.
   * @param id The mailbox to re-credential.
   * @param request The new password.
   * @param signal Optional abort signal to cancel the in-flight request.
   */
  changePassword: (id: string, request: ChangeMailboxPasswordRequest, signal?: AbortSignal) => Promise<void>

  /**
   * Removes the mailbox and the mail it holds.
   * @param id The mailbox to remove.
   * @param signal Optional abort signal to cancel the in-flight request.
   */
  remove: (id: string, signal?: AbortSignal) => Promise<void>
}

/** The dialog a mailbox row can open over the list. */
export type MailboxAction = 'edit' | 'password' | 'remove'

/** A mailbox and the dialog the person opened for it; the two are only ever known together. */
export interface PendingMailboxAction {
  /** The mailbox the dialog is aimed at. */
  mailbox: Mailbox
  /** Which dialog is open for it. */
  action: MailboxAction
}
