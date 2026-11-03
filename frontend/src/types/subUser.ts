import type { AccountFeatureName } from './accountFeature'
import type { ListRequest, Paged } from './list'

/**
 * Whether a sub-user can sign in yet, as the panel names it. `invited` until they accept the mailed
 * invitation and choose their own password. Mirrors the backend's `UserLifecycle`, camel-cased. A
 * suspension is never a state of the person: it belongs to the account's membership, so it is not here.
 */
export type SubUserState = 'active' | 'invited'

/**
 * One person the account's owner added to the account, mirroring the backend's `SubUserDto`
 * field-for-field. There is no password and nothing from which to derive one: the person chooses
 * their own through the mailed invitation.
 */
export interface SubUser {
  /** The sub-user's login id, and the only identifier a request may name. */
  id: string
  /** The name they sign in with. */
  username: string
  /** The address the invitation went to. */
  email: string
  /** Whether they have accepted the invitation. */
  state: SubUserState
  /**
   * The gated features they may use, one id per feature (`databases`, `fileTransfer`). The ids are
   * the panel's; the SPA keeps no list of them and labels them from {@link GrantableFeature}.
   */
  features: AccountFeatureName[]
  /** When the owner added them, as an ISO-8601 string. */
  createdAt: string
  /** Their most recent sign-in as an ISO-8601 string, or `null` before the first. */
  lastLoginAt: string | null
}

/** One feature an owner may give a sub-user, with its name in the interface language. */
export interface GrantableFeature {
  /** The feature's name on the wire, sent back in a request to grant it. */
  feature: AccountFeatureName
  /** What the feature is called on screen, already localized by the panel. */
  featureDisplayName: string
}

/** Request body for `POST /api/v1/me/sub-users`, binding the backend's `InviteSubUserCommand`. */
export interface InviteSubUserRequest {
  /** The name the new person will sign in with. */
  username: string
  /** The address the invitation is mailed to. */
  email: string
  /** The feature ids the person may use; may be empty. */
  features: AccountFeatureName[]
}

/**
 * The panel's answer to an invitation request (`202 Accepted`), binding the backend's
 * `SubUserInvitationDto`. It echoes what the owner typed and nothing else: whether the login name or
 * address was already in use anywhere on the server is deliberately not readable from it, so the
 * answer for a taken request is identical to the answer for a free one. The person appears in the
 * list only when the invitation was created.
 */
export interface SubUserInvitation {
  /** The login name the owner asked for, as typed. */
  username: string
  /** The address the owner asked the invitation to be sent to, as typed. */
  email: string
}

/** Request body for `PUT /api/v1/me/sub-users/{id}/features`, binding `ChangeSubUserGrantsCommand`. */
export interface ChangeSubUserFeaturesRequest {
  /** The complete new set of feature ids. */
  features: AccountFeatureName[]
}

/**
 * Typed access to the sub-users endpoints.
 *
 * Called from Pinia stores only — never from a component (rules/vue.md).
 */
export interface SubUsersApi {
  /**
   * Lists one page of the people the caller added to their account.
   * @param request The page, size, search and sort.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The page the panel answered, with the total across every page.
   */
  list: (request: ListRequest, signal?: AbortSignal) => Promise<Paged<SubUser>>

  /**
   * Lists the features an owner may give, named in the interface language.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns Every grantable feature.
   */
  features: (signal?: AbortSignal) => Promise<GrantableFeature[]>

  /**
   * Asks the panel to add a person and mail them an invitation. The answer is the same whether or not
   * the name or address was free, so the caller re-reads the list to see the outcome.
   * @param request Their login name, address and features.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The request as accepted, echoing the owner's own input.
   */
  invite: (request: InviteSubUserRequest, signal?: AbortSignal) => Promise<SubUserInvitation>

  /**
   * Replaces the features a sub-user may use. Another account's sub-user answers 404.
   * @param id The sub-user to change.
   * @param request The complete new set of feature ids.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The sub-user as they now stand.
   */
  changeFeatures: (id: string, request: ChangeSubUserFeaturesRequest, signal?: AbortSignal) => Promise<SubUser>

  /**
   * Ends a sub-user's access to the account. Another account's sub-user answers 404.
   * @param id The sub-user to remove.
   * @param signal Optional abort signal to cancel the in-flight request.
   */
  remove: (id: string, signal?: AbortSignal) => Promise<void>
}

/** An action the page offers on a sub-user's row. */
export type SubUserAction = 'features' | 'remove'
