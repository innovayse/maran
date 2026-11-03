import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import { useResellersStore } from '../stores/resellers'
import type { AccountPickerState } from './useAccountChoice'

/** The search-driven reseller picker a screen binds to `UiCombobox`. */
export interface ResellerPicker {
  /** The picker's bindings (`v-bind` on `UiCombobox`), read from the open picker's latest search. */
  picker: ComputedRef<AccountPickerState>
  /**
   * Reads the resellers matching a search into {@link picker}; bound to `UiCombobox`'s `search`.
   * @param term What was typed; empty for the first resellers, alphabetical.
   * @returns Resolves once the read has settled.
   */
  search: (term: string) => Promise<void>
}

/**
 * The reseller picker: one page of resellers per search on `GET /api/v1/resellers/choices`, never a
 * whole list held in the browser, so every reseller the panel has is reachable through the search
 * however many there are. The same shape as `usePlanChoice`'s picker, which `UiCombobox` binds.
 *
 * Presentation only: the endpoint is administrator-only and the server refuses anyone else.
 * @returns The picker's bindings and its search.
 */
export const useResellerChoice = (): ResellerPicker => {
  const { t } = useI18n()
  const resellersStore = useResellersStore()

  const picker = computed((): AccountPickerState => {
    const searchLabel = t('common.list.search')
    const read = resellersStore.choicePage
    if (read.kind === 'loaded') {
      const options = read.page.items.map((reseller): { value: string; label: string } => {
        return { value: reseller.id, label: reseller.name }
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
   * Reads the resellers matching a search.
   * @param term What was typed into the picker.
   * @returns Resolves once the read has settled.
   */
  const search = async (term: string): Promise<void> => {
    await resellersStore.searchChoices(term)
  }

  return { picker, search }
}
