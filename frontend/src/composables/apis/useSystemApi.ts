import type { HealthResponse, SystemApi } from '../../types/system'
import { useApi } from '../useApi'

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
  const getHealth = (): Promise<HealthResponse> => {
    return api.get<HealthResponse>('/health')
  }

  return { getHealth }
}
