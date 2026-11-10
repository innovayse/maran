import type { ListRequest, Paged } from './list'

/**
 * Every type the authentication domain needs, grouped in one file the way the
 * frontend groups types by domain (rules/vue.md "Types").
 */

/**
 * What a signed-in user is allowed to reach. Mirrors the backend's `UserRole`.
 *
 * **Adding a member here is a decision, and this is where it is taken.** Exactly one place in the
 * SPA branches on this union for presentation: `composables/useCallerRole.ts`, and every choice that
 * depends on the role (the sidebar's administrator entries, the in-page administrator links, the
 * role guard's `adminOnly` and `managerOnly` redirects, whether a form offers an account picker)
 * reads it from there. Each flag it offers is ROLE-EXACT: `isAdmin` is `role === 'admin'` and
 * nothing else, so a role added here is never offered the administrator screens by default. The
 * navigation maps the union to a tree in `composables/useNavigationRole.ts`, whose exhaustive
 * `never` branch makes a new member a type error until it is mapped. Offering is advice and the
 * endpoints are the boundary, so a role added here that the backend refuses costs a hidden link,
 * never an exposed screen.
 */
export type UserRole = 'admin' | 'reseller' | 'customer'

/** The person the panel has signed in. */
export interface AuthenticatedUser {
  /** The user's identity. */
  id: string
  /** The login name, shown in the shell's user block. */
  username: string
  /** The contact address. */
  email: string
  /** What the user is allowed to reach, so the shell can hide what it must not offer. */
  role: UserRole
  /** The hosting account a customer owns; `null` for an administrator or a reseller. */
  accountId: string | null
}

/**
 * The half of a sign-in that exists only once the caller is actually signed in.
 * Every field is present together or the whole object is absent, which is what
 * makes "signed in" a single question instead of four fields that must agree.
 */
export interface AuthenticatedSession {
  /** The signed access token. */
  accessToken: string
  /** When that token expires, so the app can refresh before a call fails. */
  expiresAt: string
  /** Who signed in. */
  user: AuthenticatedUser
  /**
   * True when the panel forces administrators to hold a second factor and this
   * one does not yet. The sign-in SUCCEEDED — there is an access token — but that
   * token reaches only the enrolment endpoints and every other one answers 403
   * until enrolment is finished. That refusal is the server's; this flag only
   * lets the app route straight to enrolment instead of showing a wall of 403s.
   */
  requiresTwoFactorSetup: boolean
  /**
   * True when the login's password was chosen by whoever created the account and has not been
   * replaced yet. Like `requiresTwoFactorSetup` the sign-in SUCCEEDED, but the token reaches
   * only the password change (and sign-out) until the person has chosen their own password, which the
   * server enforces by reading the claim inside the token. This flag only lets the app route straight
   * to the screen that asks for the new password instead of showing a shell of 403s (issue #514).
   */
  requiresPasswordChange: boolean
}

/**
 * Who, if anyone, this browser is signed in as: one value instead of a token, a user and two flags
 * that must agree. Only the arms that hold a token carry one, so nothing can read a token that a
 * sign-in still owed a second factor for.
 */
export type AuthSession =
  | {
      /** Nobody is signed in. */
      state: 'signedOut'
    }
  | {
      /** The password was right and a second factor is still owed; there is no token yet. */
      state: 'awaitingSecondFactor'
      /** The username whose sign-in is waiting. */
      username: string
    }
  | {
      /** Signed in, but the panel requires a second factor this person has not enrolled: the token reaches only enrolment. */
      state: 'enrolmentRequired'
      /** The signed access token. */
      accessToken: string
      /** Who is signed in. */
      user: AuthenticatedUser
    }
  | {
      /** Signed in with a first password somebody else chose: the token reaches only the password change. */
      state: 'passwordChangeRequired'
      /** The signed access token. */
      accessToken: string
      /** Who is signed in. */
      user: AuthenticatedUser
    }
  | {
      /** Fully signed in. */
      state: 'signedIn'
      /** The signed access token. */
      accessToken: string
      /** Who is signed in. */
      user: AuthenticatedUser
    }

/**
 * The body of a sign-in or refresh. There is deliberately no refresh token here:
 * it lives in an httpOnly cookie the page's JavaScript cannot read.
 *
 * `session` is `null` in exactly one case — the password was right and a second
 * factor is still owed — so there is no separate `twoFactorRequired` flag to
 * contradict it, and no token that can arrive without its expiry or its user.
 */
export interface LoginResult {
  /** The signed-in half, or `null` when a second factor is still owed. */
  session: AuthenticatedSession | null
}

/** Credentials for the first step of signing in. */
export interface LoginRequest {
  /** The login name. */
  username: string
  /** The plaintext password. Held only for the duration of the request. */
  password: string
}

/** Credentials plus the second factor, for the step that completes a sign-in. */
export interface VerifyTwoFactorRequest extends LoginRequest {
  /** A code from the authenticator app, or one of the user's recovery codes. */
  code: string
}

/** What the panel needs to create its first administrator. */
export interface CompleteSetupRequest {
  /** The token from the installer's one-time link. */
  token: string
  /** The administrator's login name. */
  username: string
  /** The administrator's contact address. */
  email: string
  /** The chosen password. */
  password: string
}

/**
 * What a password reset needs: the token from the mail, and the password it buys.
 *
 * The token IS the credential, which is why nothing else identifies the account —
 * naming the user here would let a caller aim a token at somebody else's password.
 */
export interface ResetPasswordRequest {
  /** The plaintext token from the reset mail. */
  token: string
  /** The password to set. */
  newPassword: string
}

/**
 * What changing the signed-in caller's own password needs: the password they sign in
 * with today, and its replacement. Nothing names the account — the backend takes the
 * caller from their own access token.
 */
export interface ChangePasswordRequest {
  /** The password the caller signs in with today. */
  currentPassword: string
  /** The password to set. */
  newPassword: string
}

/**
 * What accepting an invitation needs: the token from the mail, and the first
 * password it sets.
 *
 * Shaped exactly like {@link ResetPasswordRequest} for the same reason that
 * request is: the token IS the credential, so nothing here names the account —
 * naming one would let a caller aim a token at somebody else's login.
 */
export interface AcceptInvitationRequest {
  /** The plaintext token from the invitation mail. */
  token: string
  /** The password to set. */
  newPassword: string
}

/** What the panel answers once an invitation's password is set. */
export interface AcceptInvitationResult {
  /** The login name the invitation created, so the person knows what to sign in with. */
  username: string
}

/** Whether the panel still needs its first administrator. */
export interface SetupState {
  /** True once any user exists. */
  isComplete: boolean
}

/** One signed-in device, as shown on the sessions screen. */
export interface Session {
  /** The session's identity, used to revoke it. */
  id: string
  /** When the device signed in. */
  issuedAt: string
  /** When it will be signed out unless it refreshes. */
  expiresAt: string
  /** Where it signed in from. */
  ipAddress: string
  /** What client it signed in with. */
  userAgent: string
  /** True for the device making this request. */
  isCurrent: boolean
}

/** The secret an authenticator app needs, handed out before anything is enabled. */
export interface TotpEnrolment {
  /** The base32 shared secret, for typing in by hand. */
  secret: string
  /** The same secret as an `otpauth://` URI, for the QR code. */
  provisioningUri: string
}

/** The recovery codes, returned the one and only time they are readable. */
export interface RecoveryCodes {
  /** The plaintext codes, in the order they should be shown. */
  codes: string[]
}

/** Public surface of the authentication API composable. */
export interface AuthApi {
  /** Signs in with a username and password. */
  login: (request: LoginRequest, signal?: AbortSignal) => Promise<LoginResult>
  /** Completes a sign-in that stopped for a second factor. */
  verifyTwoFactor: (request: VerifyTwoFactorRequest, signal?: AbortSignal) => Promise<AuthenticatedSession>
  /** Exchanges the refresh cookie for a new access token; `null` when the browser holds no cookie. */
  refresh: (signal?: AbortSignal) => Promise<AuthenticatedSession | null>
  /** Signs out of this device. */
  logout: (signal?: AbortSignal) => Promise<void>
  /** Signs out of every device. */
  logoutEverywhere: (signal?: AbortSignal) => Promise<void>
  /** Reports whether the panel already has an administrator. */
  setupState: (signal?: AbortSignal) => Promise<SetupState>
  /** Creates the first administrator. */
  completeSetup: (request: CompleteSetupRequest, signal?: AbortSignal) => Promise<AuthenticatedUser>
  /** Lists one page of the caller's live sessions. */
  listSessions: (request: ListRequest, signal?: AbortSignal) => Promise<Paged<Session>>
  /** Ends one of the caller's sessions. */
  revokeSession: (id: string, signal?: AbortSignal) => Promise<void>
  /** Starts a two-factor enrolment without enabling anything. */
  beginTwoFactorEnrolment: (signal?: AbortSignal) => Promise<TotpEnrolment>
  /** Completes an enrolment and returns the recovery codes, once. */
  confirmTwoFactorEnrolment: (secret: string, code: string, signal?: AbortSignal) => Promise<RecoveryCodes>
  /** Turns the second factor off. */
  disableTwoFactor: (code: string, signal?: AbortSignal) => Promise<void>
  /** Asks for a password-reset link, and is answered the same way whatever the address is. */
  requestPasswordReset: (email: string, signal?: AbortSignal) => Promise<void>
  /** Changes the signed-in caller's own password, proving the current one. */
  changePassword: (request: ChangePasswordRequest, signal?: AbortSignal) => Promise<void>
  /** Sets a new password from a reset link. */
  resetPassword: (request: ResetPasswordRequest, signal?: AbortSignal) => Promise<void>
  /** Sets the first password for an invited login, from the mailed invitation. */
  acceptInvitation: (request: AcceptInvitationRequest, signal?: AbortSignal) => Promise<AcceptInvitationResult>
}
