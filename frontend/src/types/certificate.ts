/**
 * The TLS certificate contract, as the panel's Ssl module exposes it.
 *
 * Every field mirrors the backend's `CertificateDto` by name and by shape, and nothing here is
 * derived or renamed: a mapping layer between a DTO and a view model is a second definition of
 * the contract that nothing keeps in step with the first. The SPA renders what the panel exposes
 * (rules/vue.md).
 *
 * There is deliberately no certificate body and no private key anywhere in this file. The panel
 * has no endpoint that returns either — a site's PHP runs as its customer, so a key the API would
 * hand back is a key any script on that site could ask for — and a type that named one would be
 * inviting a screen to display it.
 */

/**
 * Where a certificate came from, and therefore whether the panel renews it. Values are the
 * camelCase member names the panel's `JsonStringEnumConverter` produces for `CertificateSource`.
 */
export type CertificateSource =
  /** Ordered by this panel from an ACME certificate authority, and renewable by it. */
  | "acme"
  /** Supplied by the customer. Never re-ordered, never overwritten by renewal. */
  | "custom";

/** Outward view of one installed certificate, exactly as a row of `GET /api/v1/certificates` reports it. */
export interface Certificate {
  /** The certificate's identity. */
  id: string;
  /** The site it is installed for. */
  siteId: string;
  /** The primary domain it was issued for. */
  domain: string;
  /** Whether the panel issued it or the customer supplied it. */
  source: CertificateSource;
  /** When the panel installed it, as an ISO-8601 string. */
  issuedAt: string;
  /** When it expires, as an ISO-8601 string. Renewal runs thirty days before this. */
  notAfter: string;
  /** When renewal last tried, as an ISO-8601 string, or `null` if it never has. */
  lastRenewalAttemptAt: string | null;
  /**
   * Machine-stable code of the last renewal failure, or `null` when the last attempt succeeded or
   * none has run. A code and never a sentence: the panel translates it, like every other error code
   * it reports.
   */
  lastRenewalErrorCode: string | null;
  /** How many renewal attempts have failed in a row. */
  consecutiveRenewalFailures: number;
  /**
   * Whether the certificate also covers every name directly under the domain (`*.domain`), which
   * the panel validates through the DNS zone it serves for the domain. Renewal repeats the same
   * validation.
   */
  wildcard: boolean;
  /**
   * The last renewal failure as a sentence in the caller's language, written by the panel, or `null`
   * when there is no failure. Rendered as plain text and never used to look anything up.
   */
  lastRenewalError: string | null;
}

/**
 * Which ACME directory the panel orders certificates from, as `GET /api/v1/certificates/acme-directory`
 * reports it. Administrators only: the panel answers every other role with a refusal.
 */
export interface AcmeDirectory {
  /** The directory's full URL. */
  url: string;
  /** The directory's host name, the part worth reading at a glance. */
  host: string;
  /** Machine-stable kind: `production`, `staging` or `custom`. Read for behaviour, never for text. */
  kind: string;
  /** The kind's name in the caller's language, written by the panel. */
  kindName: string;
}

/** Body of `POST /api/v1/certificates`: order a certificate for one of my sites. */
export interface IssueCertificateRequest {
  /** The domain to issue for. It must be a site the caller owns. */
  domain: string;
  /**
   * Whether the certificate should also cover `*.domain`. Only sent as `true` when the panel said
   * it can ({@link IssuanceOptions.wildcardAvailable}); the panel refuses it otherwise.
   */
  wildcard: boolean;
}

/**
 * What the panel can issue for one domain, as `GET /api/v1/certificates/issuance-options` reports it.
 * The panel decides, because only it knows whether it serves the domain's DNS zone.
 */
export interface IssuanceOptions {
  /** The domain the answer is about. */
  domain: string;
  /** Whether a wildcard certificate can be requested for the domain now. */
  wildcardAvailable: boolean;
  /**
   * Machine-stable code of why a wildcard cannot be requested, or `null` when it can. Read for
   * behaviour only, never for text.
   */
  wildcardUnavailableCode: string | null;
  /** That reason as a sentence in the caller's language, written by the panel, or `null` when a wildcard can be requested. */
  wildcardUnavailableMessage: string | null;
}

/** Body of `POST /api/v1/certificates/custom`: install material the customer supplied. */
export interface InstallCustomCertificateRequest {
  /** The domain to install for. It must be a site the caller owns. */
  domain: string;
  /** The PEM-encoded certificate chain. */
  certificatePem: string;
  /**
   * The PEM-encoded private key. It is sent once and never read back: no panel screen displays a
   * private key, and no store holds one after the request settles.
   */
  privateKeyPem: string;
}

/** The material an operator pastes into the upload form: the request without its domain. */
export type CustomCertificateMaterial = Omit<InstallCustomCertificateRequest, "domain">;

/** Why a certificate needs the operator's attention, most urgent first. Worked out by the SPA from `notAfter`. */
export type CertificateAttention = "expired" | "expiring" | "renewalFailed";

/**
 * Typed access to the certificate endpoints.
 *
 * Called from Pinia stores only — never from a component (rules/vue.md).
 */
export interface CertificatesApi {
  /**
   * Lists the certificates installed for one site.
   * @param siteId The site to read.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The site's certificates.
   */
  list: (siteId: string, signal?: AbortSignal) => Promise<Certificate[]>;

  /**
   * Issues a certificate over ACME for the site's domain.
   * @param request The domain to certify.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The issued certificate.
   */
  issue: (request: IssueCertificateRequest, signal?: AbortSignal) => Promise<Certificate>;

  /**
   * Installs certificate material the customer supplied.
   * @param request The domain and its PEM-encoded chain and key.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The installed certificate.
   */
  installCustom: (request: InstallCustomCertificateRequest, signal?: AbortSignal) => Promise<Certificate>;

  /**
   * Removes an installed certificate; the site returns to serving plain HTTP.
   * @param id The certificate to remove.
   * @param signal Optional abort signal to cancel the in-flight request.
   */
  remove: (id: string, signal?: AbortSignal) => Promise<void>;

  /**
   * Reads which kinds of certificate the panel can issue for one domain.
   * @param domain The domain to ask about.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns What can be issued.
   */
  issuanceOptions: (domain: string, signal?: AbortSignal) => Promise<IssuanceOptions>;

  /**
   * Reads which ACME directory the panel orders from. Administrators only.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The configured directory.
   */
  acmeDirectory: (signal?: AbortSignal) => Promise<AcmeDirectory>;
}
