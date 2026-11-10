import { useApi } from '../useApi'
import type { ModuleApiRequest, ModuleApiResult } from '../../types/moduleBridge'

/** The header that names the module a relayed request speaks for; the server enforces its scope. */
const FRAME_HEADER = 'X-Maran-Frame'

/** The status a relayed call reports when the panel could not be reached at all. */
const UNREACHABLE_STATUS = 0

/** The calls the shell makes on a packaged module's behalf. */
export interface ModuleBridgeApi {
  /**
   * Performs a framed module's API call with the shell's own session, declaring which module asked.
   * Never rejects: a refusal is a status and a body, and an unreachable panel is status 0.
   * @param moduleId The module's machine name, sent as `X-Maran-Frame`.
   * @param request The method, path and optional body the frame asked for.
   * @returns The status and the parsed body, never headers.
   */
  relay: (moduleId: string, request: ModuleApiRequest) => Promise<ModuleApiResult>
}

/**
 * Builds the module-bridge API on top of the shared low-level client.
 * @returns The {@link ModuleBridgeApi}.
 */
export const useModuleBridgeApi = (): ModuleBridgeApi => {
  const api = useApi()

  /**
   * Sends a framed module's request. The `X-Maran-Frame` header is set here and only here: the
   * frame cannot make requests of its own (opaque origin, no `connect-src`), so it cannot omit or
   * change it, and the middleware refuses any endpoint the named module does not own.
   * @param moduleId The module's machine name.
   * @param request The method, path and optional body.
   * @returns The status and parsed body.
   */
  const relay = async (moduleId: string, request: ModuleApiRequest): Promise<ModuleApiResult> => {
    try {
      return await api.relay(request.path, {
        method: request.method,
        body: request.body,
        headers: { [FRAME_HEADER]: moduleId },
      })
    } catch {
      // The panel did not answer (offline, reset connection). The frame is told so by a status it
      // can recognise; the shell never throws into a frame.
      return { status: UNREACHABLE_STATUS, body: null }
    }
  }

  return { relay }
}
