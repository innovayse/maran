import { useApi } from '../useApi'
import type { ModulesApi, PanelModule } from '../../types/module'

/** The endpoint list of modules is read from. */
const MODULES_PATH = '/api/v1/modules'

/**
 * Builds the modules API on top of the shared low-level client.
 * @returns The {@link ModulesApi} bound to the panel's module endpoints.
 */
export const useModulesApi = (): ModulesApi => {
  const api = useApi()

  return {
    list: (signal?: AbortSignal): Promise<PanelModule[]> => api.get<PanelModule[]>(MODULES_PATH, signal),
  }
}
