import { computed, onMounted, watch, type ComputedRef, type WatchSource } from 'vue'
import { useI18n } from 'vue-i18n'
import { useCallerRole } from './useCallerRole'
import { useAccountsStore } from '../stores/accounts'
import type { AccountResourceKind, AccountResourceUsage } from '../types/accountResourceSummary'

/** What {@link usePlanLimit} hands a list page. */
export interface PlanLimit {
  /** Whether the customer's plan allowance for this resource is used up. */
  reached: ComputedRef<boolean>
  /** The sentence explaining why the create action is not offered, or `null` while it is. */
  reason: ComputedRef<string | null>
}

/**
 * Tells a customer's list page whether the plan still allows one more of a resource (issue #312,
 * building on the resource summary of #133). When the allowance is used up the page withholds its
 * create action and prints {@link PlanLimit.reason} in its place.
 *
 * Advice only. The endpoint refuses a create past the plan on its own, so this errs permissive:
 * an administrator, a summary still loading or refused, a count the server could not take, an
 * unlimited resource and a kind this server does not count all leave the action offered
 * (rules/security.md). Only a counted reading at or above a stated limit withholds it.
 *
 * The summary is read on mount and again whenever `changed` reports a new value, so a create or a
 * delete that moves the count moves the state with it.
 * @param kind The resource the page creates.
 * @param changed A source whose value changes after the page's list is re-read (its rows ref).
 * @returns The limit state for that resource.
 */
export const usePlanLimit = (kind: AccountResourceKind, changed: WatchSource): PlanLimit => {
  const { t } = useI18n()
  const { isCustomer } = useCallerRole()
  const accountsStore = useAccountsStore()

  /** The usage row for {@link kind}, when the summary is loaded and counted it. */
  const usage: ComputedRef<AccountResourceUsage | null> = computed((): AccountResourceUsage | null => {
    const read = accountsStore.mineResourceSummary
    if (!isCustomer.value || read.state !== 'loaded') {
      return null
    }
    return (
      read.summary.resources.find((candidate): boolean => {
        return candidate.kind === kind
      }) ?? null
    )
  })

  const reached: ComputedRef<boolean> = computed((): boolean => {
    const row = usage.value
    return (
      row !== null &&
      row.count.state === 'counted' &&
      row.limit.kind === 'limited' &&
      row.count.used >= row.limit.maximum
    )
  })

  const reason: ComputedRef<string | null> = computed((): string | null => {
    const row = usage.value
    return reached.value && row !== null && row.limit.kind === 'limited'
      ? t('planLimit.reached', { kind: t(`accounts.resources.kinds.${kind}`), limit: row.limit.maximum })
      : null
  })

  /**
   * Reads the caller's own summary; an administrator has no account of their own to read.
   * @returns Resolves once the read settled; a failure leaves the action offered.
   */
  const refresh = async (): Promise<void> => {
    if (isCustomer.value) {
      await accountsStore.loadMineResourceSummary()
    }
  }

  onMounted(() => {
    void refresh()
  })

  watch(changed, () => {
    void refresh()
  })

  return { reached, reason }
}
