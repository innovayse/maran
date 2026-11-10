import { defineStore } from 'pinia'
import { computed, ref, type ComputedRef, type Ref } from 'vue'
import { useHostAppsApi } from '../composables/apis/useHostAppsApi'
import { ApiError } from '../composables/useApi'
import type { StackStatus } from '../types/hostApp'
import type { Read } from '../types/loading'

/**
 * Owns the stack status: whether a site web server is installed (so sites can exist at all) and
 * which runtimes sites can use. The router's `stackSetupGuard` asks it on navigation; the shell's
 * menu, the site form and the wizard read it.
 *
 * Everything here is presentation advice. The backend refuses to create or change a site while no
 * web server is installed (`apps.stackNotReady`) whatever the browser holds (rules/security.md), so
 * a read that fails or an answer that is stale costs one refused request, never a leak. That is why
 * a failed read leaves {@link isReady} as `null` ("not known") and the guard lets the visit through.
 */
export const useStackStore = defineStore('stack', () => {
  const api = useHostAppsApi()

  /** The panel's last answer, or where the read stands. */
  const status: Ref<Read<StackStatus>> = ref({ state: 'idle' })

  /** Numbers each read so only the newest one's answer is kept. Not reactive. */
  let ticket = 0

  /**
   * Whether a site web server is installed: `null` while the panel has not answered, so a caller
   * can err permissive instead of treating "unknown" as "not ready".
   */
  const isReady: ComputedRef<boolean | null> = computed(() => {
    return status.value.state === 'loaded' ? status.value.value.isReady : null
  })

  /** The installed PHP versions; `null` while the panel has not answered, which is not "none". */
  const phpVersions: ComputedRef<string[] | null> = computed(() => {
    return status.value.state === 'loaded' ? status.value.value.phpVersions : null
  })

  /** The installed Node.js majors; `null` while the panel has not answered, which is not "none". */
  const nodeVersions: ComputedRef<string[] | null> = computed(() => {
    return status.value.state === 'loaded' ? status.value.value.nodeVersions : null
  })

  /**
   * Reads the stack status.
   *
   * A ready stack that is already held is kept unless `force` is set: sites cannot un-ready it
   * except through the apps page, which forces a re-read after every install or removal. A stack
   * that is not ready is asked again each time, because an installer finishing elsewhere is exactly
   * what the visitor is waiting for.
   * @param force Whether to ask again even though a ready answer is held.
   * @returns Resolves once the answer is held, or the read failed.
   */
  const load = async (force: boolean = false): Promise<void> => {
    if (!force && status.value.state === 'loaded' && status.value.value.isReady) {
      return
    }
    ticket += 1
    const mine = ticket
    try {
      const answer = await api.stack()
      if (mine === ticket) {
        status.value = { state: 'loaded', value: answer }
      }
    } catch (error) {
      if (mine === ticket) {
        status.value = { state: 'failed', message: error instanceof ApiError ? error.message : null }
      }
    }
  }

  /**
   * Forgets the held answer, as sign-out does: the next signed-in visit asks again.
   * @returns Nothing.
   */
  const reset = (): void => {
    ticket += 1
    status.value = { state: 'idle' }
  }

  return { status, isReady, phpVersions, nodeVersions, load, reset }
})
