/** Response body of `GET /health`, the Maran.Host liveness endpoint. */
export interface HealthResponse {
  /** Reported backend status, e.g. `"ok"`. */
  status: string
}
