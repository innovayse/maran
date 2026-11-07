import { defineStore } from 'pinia'
import { computed, ref, type ComputedRef, type Ref } from 'vue'
import { useAuditApi } from '../composables/apis/useAuditApi'
import { ApiError } from '../composables/useApi'
import type { AuditChainStatus, AuditChainVerification, AuditRetention } from '../types/audit'
import type { LoadingState, Read, Submission } from '../types/loading'
import { loadingStateOf } from '../utils/loadingStateOf'

/**
 * Owns what the audit screen shows about the journal's upkeep: how long entries are kept, and
 * whether the tamper-evidence chain over them still holds. The cards read state from here and call
 * its actions; they never touch the API composable (rules/vue.md).
 *
 * Nothing is invented here: the bounds of the retention setting arrive with the setting, and a
 * verdict exists only after the panel has given one.
 */
export const useAuditIntegrityStore = defineStore('auditIntegrity', () => {
  const api = useAuditApi()

  /** The read of the retention setting: on its way, arrived, or refused. A refused save leaves it untouched. */
  const retentionRead: Ref<Read<AuditRetention>> = ref({ state: 'idle' })

  /** Where the last retention save stands; `failed` carries the panel's refusal (a value outside the bounds). */
  const retentionSave: Ref<Submission> = ref({ state: 'idle' })

  /** The read of the chain's state: on its way, arrived, or refused. */
  const chainRead: Ref<Read<AuditChainStatus>> = ref({ state: 'idle' })

  /** Where the last verification stands; `failed` carries the panel's refusal. */
  const verification: Ref<Submission> = ref({ state: 'idle' })

  /** The verdict of the last verification this session, or `null` before one has run. */
  const verdict: Ref<AuditChainVerification | null> = ref(null)

  /** The retention setting, or `null` until it has been read. */
  const retention: ComputedRef<AuditRetention | null> = computed(() => {
    return retentionRead.value.state === 'loaded' ? retentionRead.value.value : null
  })

  /** Where the retention read stands, in the words the loading region understands. */
  const retentionLoadState: ComputedRef<LoadingState> = computed(() => {
    return loadingStateOf(retentionRead.value)
  })

  /** The panel's reason the retention read failed, rendered verbatim; `null` otherwise. */
  const retentionLoadMessage: ComputedRef<string | null> = computed(() => {
    return retentionRead.value.state === 'failed' ? retentionRead.value.message : null
  })

  /** The panel's reason the last retention save was refused, rendered verbatim; `null` otherwise. */
  const retentionSaveMessage: ComputedRef<string | null> = computed(() => {
    return retentionSave.value.state === 'failed' ? retentionSave.value.message : null
  })

  /** True while the retention setting is being saved. */
  const savingRetention: ComputedRef<boolean> = computed(() => {
    return retentionSave.value.state === 'sending'
  })

  /** The chain's state, or `null` until it has been read. */
  const chain: ComputedRef<AuditChainStatus | null> = computed(() => {
    return chainRead.value.state === 'loaded' ? chainRead.value.value : null
  })

  /** Where the chain read stands, in the words the loading region understands. */
  const chainLoadState: ComputedRef<LoadingState> = computed(() => {
    return loadingStateOf(chainRead.value)
  })

  /** The panel's reason the chain read failed, rendered verbatim; `null` otherwise. */
  const chainLoadMessage: ComputedRef<string | null> = computed(() => {
    return chainRead.value.state === 'failed' ? chainRead.value.message : null
  })

  /** The panel's reason the last verification was refused, rendered verbatim; `null` otherwise. */
  const verificationMessage: ComputedRef<string | null> = computed(() => {
    return verification.value.state === 'failed' ? verification.value.message : null
  })

  /** True while a verification is running. */
  const verifying: ComputedRef<boolean> = computed(() => {
    return verification.value.state === 'sending'
  })

  /**
   * Reads a failure's backend-localized text, or nothing for anything that is not an API error.
   * @param error The caught error.
   * @returns The text, or `null`.
   */
  const messageOf = (error: unknown): string | null => {
    return error instanceof ApiError ? error.message : null
  }

  /**
   * Loads the retention setting with the bounds the panel accepts.
   * @returns Resolves once the request has settled, successfully or not.
   */
  const loadRetention = async (): Promise<void> => {
    retentionRead.value = { state: 'loading' }
    try {
      retentionRead.value = { state: 'loaded', value: await api.retention() }
    } catch (error) {
      retentionRead.value = { state: 'failed', message: messageOf(error) }
    }
  }

  /**
   * Replaces the retention setting, then re-reads it so the screen shows what the panel stored,
   * not what was typed.
   * @param retentionDays Days to keep entries, or `null` to keep them indefinitely.
   * @returns True when the panel stored it.
   */
  const saveRetentionDays = async (retentionDays: number | null): Promise<boolean> => {
    retentionSave.value = { state: 'sending' }
    try {
      await api.saveRetention(retentionDays)
      retentionSave.value = { state: 'done' }
    } catch (error) {
      retentionSave.value = { state: 'failed', message: messageOf(error) }
      return false
    }
    await loadRetention()
    return true
  }

  /**
   * Loads the chain's state: how many entries are sealed, how many wait, and where retention has purged to.
   * @returns Resolves once the request has settled, successfully or not.
   */
  const loadChain = async (): Promise<void> => {
    chainRead.value = { state: 'loading' }
    try {
      chainRead.value = { state: 'loaded', value: await api.chainStatus() }
    } catch (error) {
      chainRead.value = { state: 'failed', message: messageOf(error) }
    }
  }

  /**
   * Asks the panel to verify the whole chain now, keeps its verdict, and re-reads the chain's
   * state so the card shows the counts as they stand now.
   * @returns True when the panel gave a verdict, intact or not; false when it could not.
   */
  const verifyChain = async (): Promise<boolean> => {
    verification.value = { state: 'sending' }
    verdict.value = null
    try {
      verdict.value = await api.verifyChain()
      verification.value = { state: 'done' }
    } catch (error) {
      verification.value = { state: 'failed', message: messageOf(error) }
      return false
    }
    await loadChain()
    return true
  }

  return {
    retention,
    retentionLoadState,
    retentionLoadMessage,
    retentionSave,
    retentionSaveMessage,
    savingRetention,
    chain,
    chainLoadState,
    chainLoadMessage,
    verification,
    verificationMessage,
    verifying,
    verdict,
    loadRetention,
    saveRetentionDays,
    loadChain,
    verifyChain,
  }
})
