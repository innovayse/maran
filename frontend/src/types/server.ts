/**
 * The servers the panel manages, from the angles the shell's server selector needs them: what the
 * panel reports and the API that reads it (issue #319; rules/vue.md "Types" — one domain per file).
 *
 * The panel manages one server today, the machine it runs on. Fleet mode (issue #180) adds servers
 * reached over the network; the list shape is already a list so that arrival changes data, not
 * contract. How a server is reached (the wire's `kind`) joins the shape when a second way exists,
 * with its members; a one-member union before then would say nothing.
 */

/** One server the panel manages, as `GET /api/v1/servers` reports it (`ServerDto`). */
export interface Server {
  /** How the panel reaches the server, named in the reader's language (the wire's `kind` is not read here). */
  kindDisplayName: string;
  /** The host name the panel is configured to be reached by; `null` when none is configured. */
  hostName: string | null;
}

/** Public surface of the servers API composable. */
export interface ServersApi {
  /** Lists the servers the panel manages. */
  list: (signal?: AbortSignal) => Promise<Server[]>;
}
