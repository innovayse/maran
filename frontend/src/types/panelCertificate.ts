/**
 * The panel's own trusted certificate (issue #132), from the angles the panel
 * certificate screen needs it (rules/vue.md "Types" — one domain per file).
 */

/** The certificate as `POST /api/v1/panel-certificate` reports it once installed. */
export interface PanelCertificate {
  /** The host name the certificate was ordered for. */
  hostName: string
  /** When it expires, as an ISO 8601 timestamp. */
  expiresAt: string
  /** When it was installed, as an ISO 8601 timestamp. */
  issuedAt: string
}

/**
 * What `GET /api/v1/panel-certificate` answers (always 200 for these two cases). A panel installed by
 * address has no host name a certificate can be ordered for; that is an ordinary state, so the flag
 * tells the two cases apart and the fields of the other case are `null`. A panel with a host name and
 * no recorded certificate is still the panel's `PanelCertificateNotIssued` refusal.
 */
export type PanelCertificateReading =
  | {
      /** A host name is configured and a trusted certificate is recorded for it. */
      hostNameConfigured: true
      /** Always `null` in this case. */
      hostNameNotice: null
      /** The host name the certificate was ordered for. */
      hostName: string
      /** When it expires, as an ISO 8601 timestamp. */
      expiresAt: string
      /** When it was installed, as an ISO 8601 timestamp. */
      issuedAt: string
    }
  | {
      /** No usable host name is configured. */
      hostNameConfigured: false
      /** The panel's own sentence, in the caller's language, saying what to configure. */
      hostNameNotice: string
      /** Always `null` in this case. */
      hostName: null
      /** Always `null` in this case. */
      expiresAt: null
      /** Always `null` in this case. */
      issuedAt: null
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
  /** Reads the recorded certificate, or that no host name is configured; rejects when none is recorded. */
  get: (signal?: AbortSignal) => Promise<PanelCertificateReading>
  /** Orders and installs a certificate for the panel's configured host name. */
  issue: (signal?: AbortSignal) => Promise<PanelCertificate>
}
