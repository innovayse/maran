import type { AuthenticatedUser } from './auth'

/** What a view-as session views as: an account's owner login, or a reseller's login (issue #179). */
export type ViewAsKind = 'account' | 'reseller'

/**
 * The view-as session the current access token belongs to, exactly as the panel reports it
 * (`ViewingAsDto`, issues #317 and #314). The "You are viewing as" banner is drawn from this and
 * from nothing the SPA decided on its own.
 */
export interface ViewingAs {
  /** Whether the subject is an account or a reseller; what `subjectId` identifies and where Back returns to. */
  kind: ViewAsKind
  /** The id of the account or the reseller being viewed, as `kind` says. */
  subjectId: string
  /** The subject's name, as the banner shows it. */
  subjectName: string
  /** The login being viewed as. */
  username: string
  /** The operator viewing, who Back returns to. */
  operatorUsername: string
  /** When the view-as session ends on its own (ISO 8601). */
  expiresAt: string
}

/** The answer to `GET /api/v1/view-as`: the session behind the current token, or none. */
export interface ViewAsState {
  /** The view-as session, or `null` when the caller is signed in as themselves. */
  viewing: ViewingAs | null
}

/**
 * The answer to `POST /api/v1/view-as` and `POST /api/v1/view-as/reseller`. It carries no refresh token and sets no cookie: the
 * operator's own refresh cookie is what brings them back, and a view-as session cannot be renewed.
 */
export interface ViewAsSession {
  /** The view-as access token, held in memory only, like every access token. */
  accessToken: string
  /** When it stops being accepted; the same instant the view-as session ends. */
  expiresAt: string
  /** The login the token signs in as. */
  user: AuthenticatedUser
  /** What the banner shows. */
  viewing: ViewingAs
}

/** The body of `POST /api/v1/view-as`. */
export interface StartViewAsRequest {
  /** The account to view as. */
  accountId: string
}

/** The body of `POST /api/v1/view-as/reseller`. */
export interface StartViewAsResellerRequest {
  /** The reseller to view as. */
  resellerId: string
}

/** Public surface of the view-as API composable. */
export interface ViewAsApi {
  /** Reads the view-as session the current token belongs to. */
  current: (signal?: AbortSignal) => Promise<ViewAsState>
  /** Starts viewing as an account's login. */
  start: (request: StartViewAsRequest, signal?: AbortSignal) => Promise<ViewAsSession>
  /** Starts viewing as a reseller's login. */
  startReseller: (request: StartViewAsResellerRequest, signal?: AbortSignal) => Promise<ViewAsSession>
  /** Ends the view-as session the current token belongs to. */
  stop: (signal?: AbortSignal) => Promise<void>
}
