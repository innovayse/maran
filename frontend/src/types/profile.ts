/**
 * The signed-in person's own profile (rules/vue.md "Types"): the USER, for every role. It is a
 * different domain from a customer's hosting account (`types/account.ts`), which an administrator
 * does not have.
 */
import type { AccountFeatureName } from './accountFeature'
import type { UserRole } from './auth'

/**
 * What the signed-in person is to the account they act for, as `GET /api/v1/profile` reports it:
 * the owner of it, or a member the owner granted a set of features.
 */
export interface AccountAccess {
  /** The person's standing on the account: its owner, or a member the owner invited. */
  role: 'owner' | 'member'
  /** The role's name in the reader's language, as the panel localized it. */
  roleDisplayName: string
  /** The single features the person may use on the account; the whole plan for an owner. */
  features: AccountFeatureName[]
}

/**
 * What the profile read reported about the caller's standing, and the account it was read for. The
 * account id is kept beside it so a standing read for one account is never taken for another's
 * after the caller switched accounts.
 */
export interface HeldAccountAccess {
  /** The account the caller was acting for when the standing was read. */
  accountId: string
  /** What the caller is to that account; `null` when the panel reports none. */
  access: AccountAccess | null
}

/** The signed-in person's profile, as `GET /api/v1/profile` reports it. */
export interface Profile {
  /** The login name. */
  username: string
  /** The contact address password-reset links are sent to. */
  email: string
  /** What the user is allowed to reach. */
  role: UserRole
  /** True when a second factor is enrolled and required at sign-in. */
  isTwoFactorEnabled: boolean
  /** How many devices are signed in right now, this one included. */
  activeSessionCount: number
  /** What the person is to the account they act for; `null` for a login that acts for none. */
  accountAccess: AccountAccess | null
}

/** Public surface of the profile API composable. */
export interface ProfileApi {
  /** Reads the caller's own profile. */
  get: (signal?: AbortSignal) => Promise<Profile>
}
