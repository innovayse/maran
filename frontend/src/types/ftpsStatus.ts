/**
 * The FTPS daemon's own state, as the administrator's server panel reads it, plus the request that
 * turns the daemon on.
 *
 * Every field here is something the agent MEASURED on the host, except {@link FtpsStatus.enabled},
 * which is what the panel intends. They are side by side on purpose: a screen has to be able to
 * show the two disagreeing rather than pick one and report it as the truth.
 */

/**
 * What `GET /api/v1/ftps-server` answers, mirroring the backend's `FtpsStatusDto` field-for-field.
 *
 * **Administrator-only.** The endpoint carries `AuthorizationPolicies.AdminOnly`, and
 * {@link certificatePath} is why: it is an absolute path on the host, which is operator-facing text
 * a customer must never be shown. A customer's request answers 403, and the store reads that as
 * "not disclosed to me" rather than as a failure.
 */
export interface FtpsStatus {
  /**
   * The hostname the panel persisted when FTPS was last enabled, or `null` when it never has been.
   *
   * Rendered as absence when it is absent. The SPA must not compose a host name of its own: the
   * server's certificate is issued for THIS name, and a customer told to connect to any other one
   * gets a certificate-name mismatch they cannot tell from an attack.
   */
  hostname: string | null
  /** Whether the panel currently wants the daemon running — the intention, not a measurement. */
  enabled: boolean
  /** The service manager reports the FTPS unit active. */
  running: boolean
  /**
   * A TCP connect to the control port, made ON THE HOST, returned a `220` greeting.
   *
   * A local probe. "Answering here" and "unreachable from the internet" are compatible, so no
   * screen may promise reachability from this field alone — which is exactly why the server panel
   * composes it with the firewall's own rule list before it says anything about reachability.
   */
  controlPortAnswered: boolean
  /**
   * The live configuration still forces TLS on both the control and the data channel.
   *
   * `false` whenever the agent could not say otherwise, because an unknown must never read as
   * "encryption is enforced".
   */
  forcedTls: boolean
  /** Certificate material exists for {@link hostname}; `false` also means "was not asked". */
  certificatePresent: boolean
  /** The material is the agent's own self-signed placeholder — every client will warn. */
  certificateIsSelfSigned: boolean
  /** Where the material lives, or would have to be placed. Operator-facing. */
  certificatePath: string
  /** The control port this panel configures. The number an operator is told to open. */
  controlPort: number
  /** The lowest passive data port the LIVE configuration carries; zero when there is none. */
  passivePortMin: number
  /** The highest passive data port the LIVE configuration carries; zero when there is none. */
  passivePortMax: number
  /**
   * The live configuration is the IPv4-only fallback the enable path writes when the kernel refused
   * an IPv6 listening bind. Stated on the screen; nothing decides on it.
   */
  ipv4Only: boolean
}

/**
 * Request body for `POST /api/v1/ftps-server/enable`, binding the backend's `EnableFtpsCommand`.
 *
 * It carries no ports and no TLS options: every number the daemon runs with comes from the server's
 * own `FtpsDefaults`, and the SPA holds no domain constant (rules/vue.md).
 */
export interface EnableFtpsRequest {
  /** The hostname the daemon presents a certificate for. Customers connect to exactly this name. */
  hostname: string
  /**
   * The address to advertise for passive data connections when the host is behind NAT, or an empty
   * string when it is not.
   */
  passiveAddress: string
}

/**
 * Typed access to the FTPS server endpoints. Called from Pinia stores only (rules/vue.md).
 */
export interface FtpsServerApi {
  /**
   * Reads what the FTPS daemon is doing. Administrator-only; a customer's call answers 403.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The measured status.
   */
  status: (signal?: AbortSignal) => Promise<FtpsStatus>

  /**
   * Configures the daemon for a hostname this panel serves and brings it up.
   * @param request The hostname, and the passive address for a host behind NAT.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The status measured after the change.
   */
  enable: (request: EnableFtpsRequest, signal?: AbortSignal) => Promise<FtpsStatus>

  /**
   * Stops the daemon. Customer logins are left exactly as they are, and so is the firewall.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The status measured after the change.
   */
  disable: (signal?: AbortSignal) => Promise<FtpsStatus>
}
