import { computed, ref, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useCronStore } from '../stores/cron'
import type { CronEntry } from '../types/cronEntry'

/** What {@link useCronRemoval} hands the scheduled tasks screen. */
export interface CronRemoval {
  /** The entry awaiting a removal confirmation, or `null` when none is. */
  pendingRemovalId: Ref<string | null>
  /** The confirmation's accessible name, naming the command that would stop running. */
  confirmationTitle: ComputedRef<string>
  /**
   * Asks for confirmation before removing an entry.
   * @param entryId The entry the operator acted on.
   * @returns Nothing.
   */
  askRemove: (entryId: string) => void
  /**
   * Abandons a pending removal.
   * @returns Nothing.
   */
  cancelRemove: () => void
  /**
   * Carries out the confirmed removal.
   * @returns Resolves once the request has settled.
   */
  confirmRemove: () => Promise<void>
}

/**
 * The confirm-then-remove flow for one crontab entry: only one removal can be awaiting an answer,
 * and the menu that opened it is already gone by the time the dialog appears.
 * @returns The pending entry and the three actions that move it.
 */
export const useCronRemoval = (): CronRemoval => {
  const { t } = useI18n()
  const store = useCronStore()

  const pendingRemovalId: Ref<string | null> = ref(null)

  /** The entry the removal confirmation is open for, or `null` when none is. */
  const pendingEntry: ComputedRef<CronEntry | null> = computed(() => {
    return (
      store.entries.find((entry) => {
        return entry.entryId === pendingRemovalId.value
      }) ?? null
    )
  })

  /**
   * The confirmation's accessible name, naming the command that would stop running.
   */
  const confirmationTitle: ComputedRef<string> = computed(() => {
    const pending = pendingEntry.value
    return pending === null ? '' : t('cron.list.confirmRemoveTitle', { command: pending.command })
  })

  /**
   * Asks for confirmation before removing an entry.
   * @param entryId The entry the operator acted on.
   * @returns Nothing.
   */
  const askRemove = (entryId: string): void => {
    pendingRemovalId.value = entryId
  }

  /**
   * Abandons a pending removal.
   * @returns Nothing.
   */
  const cancelRemove = (): void => {
    pendingRemovalId.value = null
  }

  /**
   * Carries out the confirmed removal.
   * @returns Resolves once the request has settled.
   */
  const confirmRemove = async (): Promise<void> => {
    if (pendingRemovalId.value === null) {
      return
    }
    await store.remove(pendingRemovalId.value)

    // Closed after the request settles rather than before it is sent: the dialog holds the
    // spinner that tells the operator the panel is working.
    pendingRemovalId.value = null
  }

  return { pendingRemovalId, confirmationTitle, askRemove, cancelRemove, confirmRemove }
}
