import type { ListRequest, Paged } from './list'

/**
 * What an alias does with the mail sent to it: `alias` delivers to one or more mailboxes of the same
 * account, `forwarder` sends it on to an outside address, `catchAll` takes everything sent to the
 * domain that no other address claims. The text a person reads is the row's `kindName`.
 */
export type MailAliasKind = 'alias' | 'forwarder' | 'catchAll'

/**
 * Outward view of one alias, forwarder or catch-all, mirroring the backend's `MailAliasDto`
 * field-for-field.
 */
export interface MailAlias {
  /** The alias's identity, and the only identifier a request may name. */
  id: string
  /** The account that owns this alias. */
  accountId: string
  /**
   * The owning account's current short name, resolved by the panel. An empty string when the panel
   * does not name that account for this caller.
   */
  ownerName: string
  /** The identity of the mail domain the alias lives in. */
  mailDomainId: string
  /** The address the mail is sent to, as the server spells it (`*@domain` for a catch-all). */
  address: string
  /** What the alias does. */
  kind: MailAliasKind
  /** The kind as the caller reads it, in their language, produced by the panel. */
  kindName: string
  /** Where the mail goes, one address per entry. */
  targets: string[]
  /** The instant the alias was created, as an ISO-8601 string. */
  createdAt: string
}

/**
 * Request body for `POST /api/v1/mail-aliases`, binding the backend's `CreateMailAliasCommand`
 * field-for-field. The owning account is the domain's own, never named here.
 */
export interface CreateMailAliasRequest {
  /** The mail domain the alias lives in. */
  mailDomainId: string
  /** What the alias does. */
  kind: MailAliasKind
  /** The part of the address before the `@`; empty for a catch-all, which has none. */
  localPart: string
  /** Where the mail goes, one address per entry. */
  targets: string[]
}

/** Request body for `PUT /api/v1/mail-aliases/{id}`, binding `UpdateMailAliasCommand`: the address and kind stay. */
export interface UpdateMailAliasRequest {
  /** The new destinations, one address per entry; they replace the old ones. */
  targets: string[]
}

/**
 * Typed access to the mail aliases endpoints.
 *
 * Called from Pinia stores only — never from a component (rules/vue.md).
 */
export interface MailAliasesApi {
  /**
   * Lists one page of the aliases the caller may see.
   * @param request The page, size, search, sort and the account, domain and kind filters.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The page the panel answered, with the total across every page.
   */
  list: (request: ListRequest, signal?: AbortSignal) => Promise<Paged<MailAlias>>

  /**
   * Creates an alias, forwarder or catch-all.
   * @param request The domain, kind, local part and targets.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The alias as created.
   */
  create: (request: CreateMailAliasRequest, signal?: AbortSignal) => Promise<MailAlias>

  /**
   * Replaces an alias's destinations. Another customer's alias answers 404.
   * @param id The alias to change.
   * @param request The new destinations.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The alias as stored.
   */
  update: (id: string, request: UpdateMailAliasRequest, signal?: AbortSignal) => Promise<MailAlias>

  /**
   * Removes the alias; mail sent to it is refused afterwards.
   * @param id The alias to remove.
   * @param signal Optional abort signal to cancel the in-flight request.
   */
  remove: (id: string, signal?: AbortSignal) => Promise<void>
}

/** The dialog an alias row can open over the list. */
export type MailAliasAction = 'edit' | 'remove'

/** An alias and the dialog the person opened for it; the two are only ever known together. */
export interface PendingMailAliasAction {
  /** The alias the dialog is aimed at. */
  alias: MailAlias
  /** Which dialog is open for it. */
  action: MailAliasAction
}
