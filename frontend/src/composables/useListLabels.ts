import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import type { ActiveFilterChip } from '../types/list'
import type { PaginationLabels } from '../components/ui/UiPagination.vue'

/** The translated texts every server-paged list page hands its kit controls. */
export interface ListLabels {
  /** Every text `UiPagination` shows, in the interface language. */
  pagination: ComputedRef<PaginationLabels>
  /**
   * Builds one removable chip for an applied filter: "Plan: Starter", named "Remove filter: Plan:
   * Starter" for a screen reader.
   * @param key The filter's name, emitted when the chip is removed.
   * @param filter The filter's own label, already translated ("Plan").
   * @param value What it narrows to, already translated or as the server named it ("Starter").
   * @returns The chip.
   */
  chip: (key: string, filter: string, value: string) => ActiveFilterChip
}

/**
 * The `common.list.*` texts a server-paged list page passes to `UiPagination` and
 * `UiActiveFilter`, built once here so every list words its controls the same way and no page
 * assembles the pagination labels itself.
 * @returns The pagination labels and the chip builder.
 */
export const useListLabels = (): ListLabels => {
  const { t } = useI18n()

  // The functions it holds are declared below; the computed reads them only when first evaluated.
  const pagination: ComputedRef<PaginationLabels> = computed(() => {
    return {
      navigation: t('common.list.label'),
      first: t('common.list.first'),
      previous: t('common.list.previous'),
      next: t('common.list.next'),
      last: t('common.list.last'),
      pageSize: t('common.list.pageSize'),
      page: pageName,
      position,
      range,
    }
  })

  /**
   * Names one numbered page.
   * @param page The page.
   * @returns "Page 4".
   */
  const pageName = (page: number): string => {
    return t('common.list.page', { page })
  }

  /**
   * States the position in the phone layout.
   * @param page The current page.
   * @param pages How many pages there are.
   * @returns "Page 2 of 13".
   */
  const position = (page: number, pages: number): string => {
    return t('common.list.position', { page, pages })
  }

  /**
   * States which rows are showing.
   * @param from The first row's position.
   * @param to The last row's position.
   * @param total Rows across every page.
   * @returns "Showing 26–50 of 312".
   */
  const range = (from: number, to: number, total: number): string => {
    return t('common.list.range', { from, to, total })
  }

  /**
   * Builds one removable chip for an applied filter.
   * @param key The filter's name.
   * @param filter The filter's own label, already translated.
   * @param value What it narrows to.
   * @returns The chip.
   */
  const chip = (key: string, filter: string, value: string): ActiveFilterChip => {
    const text = t('common.list.chip', { filter, value })
    return { key, text, removeLabel: t('common.list.removeFilter', { filter: text }) }
  }

  return { pagination, chip }
}
