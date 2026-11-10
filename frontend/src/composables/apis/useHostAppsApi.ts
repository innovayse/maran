import { useApi } from '../useApi'
import { listQuery } from '../../utils/listQuery'
import type { ListRequest, Paged } from '../../types/list'
import type {
  HostApp,
  HostAppOperationAccepted,
  HostAppServiceAction,
  HostAppServiceState,
  HostAppsApi,
  InstallableHostApp,
  StackStatus,
} from '../../types/hostApp'

/** The endpoint the host's apps are listed and managed through. */
const APPS_PATH = '/api/v1/apps'

/** The list endpoint's parameter for each filter the apps page reads from its URL. */
const LIST_FILTER_PARAMS: Readonly<Record<string, string>> = { kind: 'kind', state: 'state' }

/**
 * Builds the apps API on top of the shared low-level client. An app id travels in the path, so it
 * is encoded; the panel validates it against its closed catalogue regardless.
 * @returns The {@link HostAppsApi} bound to the panel's apps endpoints.
 */
export const useHostAppsApi = (): HostAppsApi => {
  const api = useApi()

  /**
   * Lists one page of the installed apps.
   * @param request The page, size, search, sort and the kind and state filters.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The page the panel answered.
   */
  const list = (request: ListRequest, signal?: AbortSignal): Promise<Paged<HostApp>> => {
    return api.get<Paged<HostApp>>(`${APPS_PATH}${listQuery(request, LIST_FILTER_PARAMS)}`, signal)
  }

  /**
   * Lists the apps the host's package sources can install.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns Every installable app.
   */
  const listInstallable = (signal?: AbortSignal): Promise<InstallableHostApp[]> => {
    return api.get<InstallableHostApp[]>(`${APPS_PATH}/installable`, signal)
  }

  /**
   * Reads the stack status.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns Whether sites can exist yet, and which runtimes they can use.
   */
  const stack = (signal?: AbortSignal): Promise<StackStatus> => {
    return api.get<StackStatus>(`${APPS_PATH}/stack`, signal)
  }

  /**
   * Starts an install.
   * @param id The catalogue id to install.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The accepted operation, naming its task.
   */
  const install = (id: string, signal?: AbortSignal): Promise<HostAppOperationAccepted> => {
    return api.post<HostAppOperationAccepted>(`${APPS_PATH}/${encodeURIComponent(id)}/install`, undefined, signal)
  }

  /**
   * Starts an uninstall.
   * @param id The catalogue id to remove.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The accepted operation, naming its task.
   */
  const uninstall = (id: string, signal?: AbortSignal): Promise<HostAppOperationAccepted> => {
    return api.post<HostAppOperationAccepted>(`${APPS_PATH}/${encodeURIComponent(id)}/uninstall`, undefined, signal)
  }

  /**
   * Asks an app's service to start, stop, restart or reload.
   * @param id The catalogue id.
   * @param action What to ask the service to do.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The service's state after the action.
   */
  const control = (id: string, action: HostAppServiceAction, signal?: AbortSignal): Promise<HostAppServiceState> => {
    return api.post<HostAppServiceState>(`${APPS_PATH}/${encodeURIComponent(id)}/service/${action}`, undefined, signal)
  }

  return { list, listInstallable, stack, install, uninstall, control }
}
