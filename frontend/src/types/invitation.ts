/**
 * What the panel says about an account owner's invitation, mirroring the backend's
 * `InvitationStatusDto`. Carries no token and no link: it is what the panel may say without
 * minting anything. Exactly one of four shapes told apart by `state`, so the expiry exists only on
 * the shape that has one and a screen never reads "no expiry" off a `null`.
 *
 * - `none` — the account has no panel login yet.
 * - `pending` — the owner has been invited and holds a token that still works, until `expiresAt`.
 * - `expired` — the owner was invited but every token they were given has lapsed or been replaced.
 * - `accepted` — the owner has set a password; there is nothing left to hand over.
 */
export type InvitationStatus =
  /** The owner holds an invitation that still works. */
  | {
      /** Discriminator: an invitation is outstanding. */
      state: 'pending'
      /** When the newest usable token stops working, as an ISO-8601 string. */
      expiresAt: string
    }
  /** Every other state, none of which has an expiry. */
  | {
      /** Discriminator: no login yet, a lapsed invitation, or an accepted one. */
      state: 'none' | 'expired' | 'accepted'
    }

/** Where a hosting account's owner stands in their invitation: the `state` of {@link InvitationStatus}. */
export type InvitationState = InvitationStatus['state']

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
 * Whose invitation is being read or replaced: a hosting account's owner, or a reseller's login.
 * The two live at different endpoints and mean different things to the person reading the screen,
 * but share everything else (the states, the link, the resend), so one type names the target and
 * the rest of the invitation code is written once.
 */
export interface InvitationSubject {
  /** Which kind of login is invited: the owner of a hosting account, or a reseller. */
  kind: 'account' | 'reseller'
  /** The hosting account's or the reseller's identity. */
  id: string
}

/**
 * Typed access to the invitation endpoints.
 *
 * Called from Pinia stores only — never from a component (rules/vue.md).
 */
export interface InvitationsApi {
  /**
   * Reads where an invitation stands.
   * @param subject Whose invitation is asked about.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The invitation's state and, while it is pending, when it lapses.
   */
  getStatus: (subject: InvitationSubject, signal?: AbortSignal) => Promise<InvitationStatus>

  /**
   * Issues a fresh invitation and returns its one-time link, retiring every outstanding one.
   * Nothing is mailed. Administrator-only on the server; the SPA adds no matching check
   * (rules/architecture.md — the endpoint is the boundary).
   * @param subject Whose login is being invited.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The link, shown once, and when it expires.
   */
  issueLink: (subject: InvitationSubject, signal?: AbortSignal) => Promise<InvitationLink>

  /**
   * Retires every outstanding invitation token for the login and mails a new one.
   *
   * The single recovery path for an `AccountCreated` handler that failed after the account row
   * already existed. Administrator-only on the server.
   * @param subject Whose login is being (re)invited.
   * @param signal Optional abort signal to cancel the in-flight request.
   */
  resend: (subject: InvitationSubject, signal?: AbortSignal) => Promise<void>
}
