/**
 * Where a hosting account's owner stands in their invitation, mirroring the backend's
 * `Maran.Modules.Identity.Domain.Enums.InvitationState`. The API serializes enums as camelCase
 * strings (panel-wide `JsonStringEnumConverter`), so these are the camelCase forms of the C# names.
 *
 * - `none` — the account has no panel login yet.
 * - `pending` — the owner has been invited and holds a token that still works.
 * - `expired` — the owner was invited but every token they were given has lapsed or been replaced.
 * - `accepted` — the owner has set a password; there is nothing left to hand over.
 */
export type InvitationState = 'none' | 'pending' | 'expired' | 'accepted'

/**
 * What the panel says about an account owner's invitation, mirroring the backend's
 * `InvitationStatusDto`. Carries no token and no link: it is what the panel may say without
 * minting anything.
 */
export interface InvitationStatus {
  /** Whether an invitation is outstanding, lapsed, accepted or was never issued. */
  state: InvitationState
  /** When the newest usable token stops working, as an ISO-8601 string; `null` unless `pending`. */
  expiresAt: string | null
}

/**
 * The one-time link an administrator hands to an account's owner, mirroring the backend's
 * `InvitationLinkDto`.
 *
 * **This is a credential.** It exists in the panel's response and in whatever holds it afterwards,
 * and nowhere on the server. The invitations store keeps it in memory only, and forgets it when the
 * operator leaves the account's page or the invitation is replaced.
 */
export interface InvitationLink {
  /**
   * The link, exactly as the panel sent it: absolute when the installation has a configured public
   * address, otherwise a root-relative path the caller must prefix with the origin it is looking at.
   */
  link: string
  /** When the link stops working, as an ISO-8601 string. */
  expiresAt: string
}

/**
 * Typed access to the invitation endpoints.
 *
 * Called from Pinia stores only — never from a component (rules/vue.md).
 */
export interface InvitationsApi {
  /**
   * Reads where an account owner's invitation stands.
   * @param accountId The hosting account being asked about.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The invitation's state and, while it is pending, when it lapses.
   */
  getStatus: (accountId: string, signal?: AbortSignal) => Promise<InvitationStatus>

  /**
   * Issues a fresh invitation and returns its one-time link, retiring every outstanding one.
   * Nothing is mailed. Administrator-only on the server; the SPA adds no matching check
   * (rules/architecture.md — the endpoint is the boundary).
   * @param accountId The hosting account whose owner is being invited.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The link, shown once, and when it expires.
   */
  issueLink: (accountId: string, signal?: AbortSignal) => Promise<InvitationLink>

  /**
   * Retires every outstanding invitation token for the account's login and mails a new one.
   *
   * The single recovery path for an `AccountCreated` handler that failed after the account row
   * already existed. Administrator-only on the server.
   * @param accountId The hosting account whose owner is being (re)invited.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns True once a new invitation was sent.
   */
  resend: (accountId: string, signal?: AbortSignal) => Promise<boolean>
}
