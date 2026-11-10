import { defineStore } from 'pinia'
import { ref, type Ref } from 'vue'
import { useModuleBridgeApi } from '../composables/apis/useModuleBridgeApi'
import type { ModuleApiRequest, ModuleApiResult, ModuleFailureRecord } from '../types/moduleBridge'

/**
 * Owns what the shell does for packaged modules' frames beyond drawing them: the one place that
 * performs a framed module's API calls, and the record of the frames that were dropped.
 *
 * A frame has no network of its own, so each `api` message it sends ends up here, goes out with the
 * shell's session and the module's name in `X-Maran-Frame`, and comes back as a status and a body.
 */
export const useModuleBridgeStore = defineStore('moduleBridge', () => {
  const api = useModuleBridgeApi()

  /**
   * The last failure of each module's frame, by module name. A module that recovers (a retry
   * reached `ready`) has its entry removed. Reported to the operator's diagnosis; nothing else
   * reads it for behaviour.
   */
  const failures: Ref<Record<string, ModuleFailureRecord>> = ref({})

  /**
   * Performs a framed module's API call.
   * @param moduleId The module's machine name.
   * @param request The validated request.
   * @returns The status and parsed body; never rejects.
   */
  const relay = (moduleId: string, request: ModuleApiRequest): Promise<ModuleApiResult> => {
    return api.relay(moduleId, request)
  }

  /**
   * Records that a module's frame was dropped.
   * @param moduleId The module's machine name.
   * @param failure Why it was dropped and which violations led there.
   * @returns Nothing.
   */
  const reportFailure = (moduleId: string, failure: ModuleFailureRecord): void => {
    failures.value = { ...failures.value, [moduleId]: failure }
  }

  /**
   * Forgets a module's recorded failure, because its frame reached `ready`.
   * @param moduleId The module's machine name.
   * @returns Nothing.
   */
  const clearFailure = (moduleId: string): void => {
    failures.value = Object.fromEntries(
      Object.entries(failures.value).filter(([id]): boolean => {
        return id !== moduleId
      }),
    )
  }

  /**
   * Forgets every recorded failure; called with the rest of a signed-in identity's state.
   * @returns Nothing.
   */
  const clear = (): void => {
    failures.value = {}
  }

  return { failures, relay, reportFailure, clearFailure, clear }
})
