/**
 * Host-level system contract: what the panel asks the backend about the
 * machine it runs on, and the shape of the composable that asks it.
 */

/** Response body of `GET /health`, the Maran.Host liveness endpoint. */
export interface HealthResponse {
  /** Reported backend status, e.g. `"ok"`. */
  status: string
}

/** Public surface of the system API composable, `useSystemApi`. */
export interface SystemApi {
  /**
   * Calls `GET /health` and returns the parsed response.
   * @returns The backend's reported health status.
   */
  getHealth: () => Promise<HealthResponse>
}
