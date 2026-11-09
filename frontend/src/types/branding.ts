/**
 * What a reseller gives its customers' shell to show, from the angles the panel needs it: the value
 * the shell and the settings form read, the body the save sends, and the API that carries them
 * (rules/vue.md "Types" — one domain per file).
 */

/**
 * The branding as `GET /api/v1/reseller/branding` and `GET /api/v1/accounts/me/branding` report it.
 * Every member is `null` when it is not set, which means "draw the panel's own": the SPA holds no
 * default name, logo or colour of its own to fall back on for a reseller's customer.
 */
export interface ResellerBranding {
  /** The name shown in place of the panel's, or `null`. */
  displayName: string | null
  /** The accent colour as lower-case `#rrggbb` that carries white text, or `null`. */
  accentColor: string | null
  /** The logo as a base64 data URI of a PNG, JPEG or WebP image, or `null`. */
  logo: string | null
}

/**
 * The body of `PUT /api/v1/reseller/branding`, binding the backend's `SetResellerBrandingCommand`.
 * It replaces all three values together: one sent as `null` goes back to the panel's own.
 */
export type SaveResellerBrandingRequest = ResellerBranding

/** The calls the branding stores make, in one place the composable builds and the stores consume. */
export interface BrandingApi {
  /** Reads the branding of the reseller the caller is, for the settings form. */
  getOwn: (signal?: AbortSignal) => Promise<ResellerBranding>
  /** Replaces the reseller's branding and answers it as stored. */
  save: (request: SaveResellerBrandingRequest, signal?: AbortSignal) => Promise<ResellerBranding>
  /** Reads the branding the caller's own shell shows. */
  getShell: (signal?: AbortSignal) => Promise<ResellerBranding>
}
