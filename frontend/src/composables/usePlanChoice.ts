import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import { useAccountsStore } from '../stores/accounts'
import type { Plan } from '../types/account'
import { formatMegabytes } from '../utils/formatBytes'
import type { AccountPickerState } from './useAccountChoice'

/** The search-driven plan picker a screen binds to `UiCombobox`. */
export interface PlanChoice {
  /** The picker's bindings (`v-bind` on `UiCombobox`), read from the open picker's latest search. */
  picker: ComputedRef<AccountPickerState>
  /**
   * Reads the plans matching a search into {@link picker}; bound to `UiCombobox`'s `search`.
   * @param term What was typed; empty for the first plans, alphabetical.
   * @returns Resolves once the read has settled.
   */
  search: (term: string) => Promise<void>
}

/**
 * The plan picker: one page of plans per search on `GET /api/v1/plans/choices`, never a whole list
 * held in the browser, so every plan the panel sells is reachable through the search however many
 * there are. The same shape as {@link useAccountChoice}'s picker, which `UiCombobox` binds.
 *
 * Each option is labelled with the plan's name, its disk in a human unit and its sites as a plural
 * form (#222), because the limits are what make the choice meaningful rather than a list of words.
 * @returns The picker's bindings and its search.
 */
export const usePlanChoice = (): PlanChoice => {
  const { t } = useI18n()
  const accountsStore = useAccountsStore()

  /**
   * Labels one plan the way every plan picker shows it.
   * @param plan The plan.
   * @returns Its name, disk and site count, translated.
   */
  const labelOf = (plan: Plan): string => {
    return t(
      'accounts.form.planOption',
      { name: plan.displayName, disk: formatMegabytes(plan.diskQuotaMb), count: plan.maxSites },
      plan.maxSites,
    )
  }

  const picker = computed((): AccountPickerState => {
    const searchLabel = t('common.list.search')
    const read = accountsStore.planChoicePage
    if (read.kind === 'loaded') {
      const options = read.page.items.map((plan): { value: string; label: string } => {
        return { value: plan.id, label: labelOf(plan) }
      })
      return { options, total: read.page.total, loading: false, loadError: null, searchLabel }
    }
    if (read.kind === 'refused') {
      return { options: [], total: 0, loading: false, loadError: read.message, searchLabel }
    }
    if (read.kind === 'unreachable') {
      return { options: [], total: 0, loading: false, loadError: t('common.list.unreachable'), searchLabel }
    }
    return { options: [], total: 0, loading: true, loadError: null, searchLabel }
  })

  /**
   * Reads the plans matching a search.
   * @param term What was typed into the picker.
   * @returns Resolves once the read has settled.
   */
  const search = async (term: string): Promise<void> => {
    await accountsStore.searchPlanChoices(term)
  }

  return { picker, search }
}
