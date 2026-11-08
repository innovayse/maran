import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import { useMailDomainsStore } from '../stores/mailDomains'

/** What a mail domain picker (`UiCombobox`) is bound to: the current page of matches and its state. */
export interface MailDomainPickerState {
  /** The domains matching what was last typed, as the picker lists them: the chosen value and the name. */
  options: { value: string; label: string }[]
  /** How many domains match in all; more than {@link options} holds when the page is cut short. */
  total: number
  /** Whether the read for what was typed is still in flight. */
  loading: boolean
  /** Why the read failed, already localized; `null` while it has not failed. */
  loadError: string | null
  /** The accessible name of the picker's search field, translated. */
  searchLabel: string
}

/** The search-driven mail domain picker a create form or a filter sheet chooses with. */
export interface MailDomainChoice {
  /** The picker's bindings (`v-bind` on `UiCombobox`), read from the open picker's latest search. */
  picker: ComputedRef<MailDomainPickerState>
  /**
   * Reads the domains matching a search into {@link picker}; bound to `UiCombobox`'s `search`.
   * @param term What was typed; empty for the first domains the panel lists.
   * @returns Resolves once the read has settled.
   */
  search: (term: string) => Promise<void>
}

/**
 * Builds the picker a form chooses a mail domain with: one page of domains per search, never the
 * whole list held in the browser, so an account with many domains reaches every one through the search.
 *
 * Which value a picked option carries depends on who asks: a create form needs the domain's identity
 * (`id`), while a list's filter names the domain in the URL (`name`), where an identity would mean
 * nothing to a reader and would need a second read to print on the filter chip.
 * @param valueOf Whether an option's value is the domain's `id` or its `name`.
 * @returns The picker's bindings and its search.
 */
export const useMailDomainChoice = (valueOf: 'id' | 'name'): MailDomainChoice => {
  const { t } = useI18n()
  const store = useMailDomainsStore()

  const picker = computed((): MailDomainPickerState => {
    const searchLabel = t('common.list.search')
    const read = store.choicePage
    if (read.kind === 'loaded') {
      const options = read.page.items.map((domain): { value: string; label: string } => {
        return { value: valueOf === 'id' ? domain.id : domain.name, label: domain.name }
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
   * Reads the domains matching a search into the store's picker page.
   * @param term What was typed into the picker.
   * @returns Resolves once the read has settled.
   */
  const search = async (term: string): Promise<void> => {
    await store.searchChoices(term)
  }

  return { picker, search }
}
