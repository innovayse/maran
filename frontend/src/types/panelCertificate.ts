/**
 * The panel's own trusted certificate (issue #132), from the angles the panel
 * certificate screen needs it (rules/vue.md "Types" — one domain per file).
 */

/** The certificate as `GET /api/v1/panel-certificate` reports it. */
export interface PanelCertificate {
  /** The host name the certificate was ordered for. */
  hostName: string
  /** When it expires, as an ISO 8601 timestamp. */
  expiresAt: string
  /** When it was installed, as an ISO 8601 timestamp. */
  issuedAt: string
}

/**
 * What the panel's interface is served with, as the screen shows it.
 *
 * Three cases, each its own shape, so the screen never reads a missing
 * certificate as an empty one: a trusted certificate the panel recorded; the
 * installer's self-signed one, with a host name a certificate can be ordered
 * for; or no usable host name at all, with the panel's own sentence saying what
 * to configure.
 */
export type PanelCertificateStatus =
  | { kind: 'trusted'; certificate: PanelCertificate }
  | { kind: 'selfSigned' }
  | { kind: 'noHostName'; message: string }

/** Public surface of the panel-certificate API composable. */
export interface PanelCertificateApi {
  /** Reads the recorded certificate; rejects with the panel's refusal when there is none. */
  get: (signal?: AbortSignal) => Promise<PanelCertificate>
  /** Orders and installs a certificate for the panel's configured host name. */
  issue: (signal?: AbortSignal) => Promise<PanelCertificate>
}
