import { useApi } from '../useApi'
import type {
  InstalledModule,
  InstallModuleRequest,
  MarketplaceApi,
  MarketplaceCatalogue,
  MarketplaceModuleDetail,
  MarketplaceStatus,
  ModuleOperationAccepted,
  ModuleUpdates,
  RemoveModuleRequest,
  RollbackModuleRequest,
  SetMarketplaceEnabledRequest,
  UpdateModuleRequest,
} from '../../types/marketplace'

/**
 * The endpoint family every marketplace call goes through. Kept a plain string constant, and every
 * path is written out as a template at its call, because `maran api` resolves request paths
 * statically and cannot follow a path-building helper.
 */
const MARKETPLACE_PATH = '/api/v1/marketplace'

/**
 * Builds the marketplace API on top of the shared low-level client.
 *
 * Typed calls and nothing else: whether a call may be made at all (the marketplace switch) is the
 * store's decision, and whether an operation is allowed is the panel's.
 * @returns The {@link MarketplaceApi} bound to the panel's marketplace endpoints.
 */
export const useMarketplaceApi = (): MarketplaceApi => {
  const api = useApi()

  /**
   * Reads the marketplace switch and the facts the switch card prints.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The status.
   */
  const status = (signal?: AbortSignal): Promise<MarketplaceStatus> => {
    return api.get<MarketplaceStatus>(`${MARKETPLACE_PATH}/status`, signal)
  }

  /**
   * Moves the marketplace switch.
   * @param request The new position and the administrator's re-entered password.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The status after the change.
   */
  const setEnabled = (request: SetMarketplaceEnabledRequest, signal?: AbortSignal): Promise<MarketplaceStatus> => {
    return api.put<MarketplaceStatus>(`${MARKETPLACE_PATH}/settings`, request, signal)
  }

  /**
   * Reads one page of the catalogue. A parameter that is empty is not sent: the panel binds an
   * empty one as a missing value and refuses it rather than reading it as "none".
   * @param channel The release channel, or `null` for the panel's own.
   * @param search The search text, or an empty string for none.
   * @param cursor The previous page's `nextCursor`, or `null` for the first page.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The page.
   */
  const listCatalogue = (
    channel: string | null,
    search: string,
    cursor: string | null,
    signal?: AbortSignal,
  ): Promise<MarketplaceCatalogue> => {
    const params = new URLSearchParams()
    if (channel !== null && channel !== '') {
      params.set('channel', channel)
    }
    if (search !== '') {
      params.set('search', search)
    }
    if (cursor !== null && cursor !== '') {
      params.set('cursor', cursor)
    }
    const query = params.toString()
    return api.get<MarketplaceCatalogue>(`${MARKETPLACE_PATH}/catalogue${query === '' ? '' : `?${query}`}`, signal)
  }

  /**
   * Reads one module with its versions and manifest summary.
   * @param id The module's machine id.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The module's detail.
   */
  const getModule = (id: string, signal?: AbortSignal): Promise<MarketplaceModuleDetail> => {
    const moduleId = encodeURIComponent(id)
    return api.get<MarketplaceModuleDetail>(`${MARKETPLACE_PATH}/modules/${moduleId}`, signal)
  }

  /**
   * Lists the modules installed on this panel.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The installed modules.
   */
  const listInstalled = (signal?: AbortSignal): Promise<InstalledModule[]> => {
    return api.get<InstalledModule[]>(`${MARKETPLACE_PATH}/installed`, signal)
  }

  /**
   * Checks for newer versions of the installed modules. This one call is the update check.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The modules with a newer version.
   */
  const checkUpdates = (signal?: AbortSignal): Promise<ModuleUpdates> => {
    return api.get<ModuleUpdates>(`${MARKETPLACE_PATH}/updates`, signal)
  }

  /**
   * Starts an install.
   * @param id The module's machine id.
   * @param request The version, the acknowledgement and the re-entered password.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The task that carries the install.
   */
  const install = (
    id: string,
    request: InstallModuleRequest,
    signal?: AbortSignal,
  ): Promise<ModuleOperationAccepted> => {
    const moduleId = encodeURIComponent(id)
    return api.post<ModuleOperationAccepted>(`${MARKETPLACE_PATH}/modules/${moduleId}/install`, request, signal)
  }

  /**
   * Starts an update.
   * @param id The module's machine id.
   * @param request The version, the acknowledgement and the re-entered password.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The task that carries the update.
   */
  const update = (
    id: string,
    request: UpdateModuleRequest,
    signal?: AbortSignal,
  ): Promise<ModuleOperationAccepted> => {
    const moduleId = encodeURIComponent(id)
    return api.post<ModuleOperationAccepted>(`${MARKETPLACE_PATH}/modules/${moduleId}/update`, request, signal)
  }

  /**
   * Starts a removal. The module's data is kept.
   * @param id The module's machine id.
   * @param request The re-entered password.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The task that carries the removal.
   */
  const remove = (
    id: string,
    request: RemoveModuleRequest,
    signal?: AbortSignal,
  ): Promise<ModuleOperationAccepted> => {
    const moduleId = encodeURIComponent(id)
    return api.post<ModuleOperationAccepted>(`${MARKETPLACE_PATH}/modules/${moduleId}/remove`, request, signal)
  }

  /**
   * Starts a rollback to the previous version.
   * @param id The module's machine id.
   * @param request The re-entered password.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The task that carries the rollback.
   */
  const rollback = (
    id: string,
    request: RollbackModuleRequest,
    signal?: AbortSignal,
  ): Promise<ModuleOperationAccepted> => {
    const moduleId = encodeURIComponent(id)
    return api.post<ModuleOperationAccepted>(`${MARKETPLACE_PATH}/modules/${moduleId}/rollback`, request, signal)
  }

  /**
   * Restarts the panel so a staged module starts. The server answers 202 with no body.
   * @param id The module whose staged install the restart is for.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns Resolves once the server has accepted the request.
   */
  const restart = (id: string, signal?: AbortSignal): Promise<void> => {
    const moduleId = encodeURIComponent(id)
    return api.post<void>(`${MARKETPLACE_PATH}/modules/${moduleId}/restart`, {}, signal)
  }

  return { status, setEnabled, listCatalogue, getModule, listInstalled, checkUpdates, install, update, remove, rollback, restart }
}
