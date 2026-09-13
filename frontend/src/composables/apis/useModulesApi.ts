import { useApi } from '../useApi'
import type { ModulesApi, PanelModule } from '../../types/module'

/** The endpoint list of modules is read from. */
const MODULES_PATH = '/api/v1/modules'

/**
 * Builds the modules API on top of the shared low-level client.
 *
 * Each call is a named `const` arrow function with its own JSDoc rather than an
 * anonymous entry in the returned object, and every type it names comes from
 * `src/types/` (rules/vue.md).
 * @returns The {@link ModulesApi} bound to the panel's module endpoints.
 */
export const useModulesApi = (): ModulesApi => {
  const api = useApi()

  /**
   * Lists every module the panel has loaded, with its licence tier and state.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The catalogue, in the backend's registration order.
   */
  const list = (signal?: AbortSignal): Promise<PanelModule[]> => {
    return api.get<PanelModule[]>(MODULES_PATH, signal)
  }

  return { list }
}
