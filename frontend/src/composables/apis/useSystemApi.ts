import type { HealthResponse } from '../../types/health'
import { useApi } from '../useApi'

/** Public surface of the system API composable returned by {@link useSystemApi}. */
interface SystemApi {
  /**
   * Calls `GET /health` and returns the parsed response.
   * @returns The backend's reported health status.
   */
  getHealth: () => Promise<HealthResponse>
}

/**
 * Typed API composable for host-level system endpoints (currently just
 * `/health`). Built on {@link useApi}; per rules/vue.md this is consumed
 * only by Pinia stores, never directly from a `.vue` file.
 * @returns The {@link SystemApi} with a single `getHealth` method.
 */
export const useSystemApi = (): SystemApi => {
  const api = useApi()

  /**
   * Calls `GET /health` through the shared API client.
   * @returns The backend's reported health status.
   */
  const getHealth = (): Promise<HealthResponse> => api.get<HealthResponse>('/health')

  return { getHealth }
}
