/**
 * Host-level system contract: what the panel asks the backend about the
 * machine it runs on, and the shape of the composable that asks it.
 */

/** What the agent is to the panel, as `GET /health` reports it. */
export type AgentHealth =
  /** The handshake succeeded and the agent speaks a contract revision the panel drives. */
  | 'connected'
  /** The agent could not be reached, did not answer in time, or refused the handshake. */
  | 'unavailable'
  /** The agent answered in a contract revision outside the window the panel drives. */
  | 'incompatible'

/** What the panel database is to the panel, as `GET /health` reports it. */
export type DatabaseHealth =
  /** A connection opens and the panel's schema is in the database. */
  | 'reachable'
  /** The database answered and the panel's schema is not in it. */
  | 'no_schema'
  /** The connection failed or timed out. */
  | 'unreachable'
  /** No connection string is configured. */
  | 'not_configured'

/**
 * Response body of `GET /health`, the Maran.Host health summary. There is no overall status word:
 * the HTTP status is the verdict, and these two members are the facts behind it.
 */
export interface HealthResponse {
  /** What the agent is to the panel. */
  agent: AgentHealth
  /** What the panel database is to the panel. */
  database: DatabaseHealth
}

/** Public surface of the system API composable, `useSystemApi`. */
export interface SystemApi {
  /**
   * Calls `GET /health` and returns the parsed response.
   * @returns The backend's reported health status.
   */
  getHealth: () => Promise<HealthResponse>
}
