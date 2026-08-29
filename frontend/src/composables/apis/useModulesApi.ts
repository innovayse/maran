import { useApi } from '../useApi'
import type { PanelModule } from '../../types/module'

/** The endpoint list of modules is read from. */
const MODULES_PATH = '/api/v1/modules'

/**
 * Typed access to the panel's module catalogue.
 *
 * Called from Pinia stores only — never from a component (rules/vue.md).
 */
export interface ModulesApi {
  /**
   * Fetches every module the panel composed, with its licence state.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The module catalogue.
   */
  list: (signal?: AbortSignal) => Promise<PanelModule[]>
}

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
