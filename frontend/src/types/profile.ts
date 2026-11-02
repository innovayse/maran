/**
 * The signed-in person's own profile (rules/vue.md "Types"): the USER, for every role. It is a
 * different domain from a customer's hosting account (`types/account.ts`), which an administrator
 * does not have.
 */
import type { AccountFeatureName } from './accountFeature'
import type { UserRole } from './auth'

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
  /**
   * How the person stands towards the account the sign-in is acting for, or `null` when it acts for
   * none (an administrator). Absent from an older panel. Advice for the screen only: a screen that
   * cannot read it must offer everything, because the endpoints decide.
   */
  accountAccess?: AccountAccess | null
}

/** How a person stands towards an account: whether they own it or were added, and what they may use. */
export interface AccountAccess {
  /** `owner` is not restricted by grants; `member` may use only {@link AccountAccess.features}. */
  role: 'owner' | 'member'
  /** The single feature ids the person may use there; for an owner, every feature. */
  features: AccountFeatureName[]
}

/** Public surface of the profile API composable. */
export interface ProfileApi {
  /** Reads the caller's own profile. */
  get: (signal?: AbortSignal) => Promise<Profile>
}
