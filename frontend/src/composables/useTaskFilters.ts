import { useI18n } from 'vue-i18n'
import { useListLabels } from './useListLabels'
import { useLocaleStore } from '../stores/locale'
import { formatDate } from '../utils/formatDate'
import { isCalendarDate } from '../utils/isCalendarDate'
import { localDayBoundary } from '../utils/localDayBoundary'
import type { ListFilterSchema, ListFilterSpec } from './useServerTable'
import type { ActiveFilterChip } from '../types/list'
import type { TaskStatusChoice } from '../types/panelTask'

/** The tasks list's filter vocabulary: how each is read from the URL, and how it is shown as a chip. */
export interface TaskFilters {
  /** The `useServerTable` schema: `account`, `status`, `kind`, `from` and `to`. */
  schema: ListFilterSchema
  /**
   * One removable chip per applied filter.
   * @param applied Every filter with its applied value, empty when unset.
   * @param accountName The name of the account the list is narrowed to.
   * @param kindName The operator-facing name of the applied kind.
   * @param statusName The operator-facing name of the applied status.
   * @returns The chips, in the order the sheet lists the fields.
   */
  chips: (
    applied: Record<string, string>,
    accountName: string,
    kindName: string,
    statusName: string,
  ) => ActiveFilterChip[]
}

/**
 * The tasks list's filters in one place, so the page that reads them from the URL and the chips
 * that name them cannot disagree. A date that is not a real `YYYY-MM-DD` day is not honoured from a
 * hand-edited link. A kind is honoured as written: kinds are open-ended (a module compiled later
 * records its own), and the panel matches it exactly. A status is checked against the set the panel
 * listed, and honoured as written until the panel has listed it: the set belongs to the panel, so
 * the bundle keeps no copy of it, and the server stays the authority.
 * @param accountSpec The `account` entry from `useAccountFilter`, which a customer's list ignores.
 * @param statuses Reads the statuses the panel listed; empty until read or when the read failed.
 * @returns The schema and the chip builder.
 */
export const useTaskFilters = (
  accountSpec: ListFilterSpec,
  statuses: () => readonly TaskStatusChoice[],
): TaskFilters => {
  const { t } = useI18n()
  const labels = useListLabels()
  const localeStore = useLocaleStore()

  const schema: ListFilterSchema = {
    account: accountSpec,
    status: {
      /**
       * Honours only a status the panel lists; permissive while the list is unknown.
       * @param value The `?status=` value.
       * @returns True for a listed status, or while the list is unknown.
       */
      accepts: (value: string): boolean => {
        const listed = statuses()
        return (
          listed.length === 0 ||
          listed.some((choice) => {
            return choice.status === value
          })
        )
      },
    },
    kind: {},
    from: { accepts: isCalendarDate },
    to: { accepts: isCalendarDate },
  }

  /**
   * A calendar day from the URL, as the reader's locale writes a date.
   * @param date The day as `YYYY-MM-DD`.
   * @returns The formatted day.
   */
  const calendarDay = (date: string): string => {
    return formatDate(localDayBoundary(date, 'start'), localeStore.current)
  }

  /**
   * One removable chip per applied filter.
   * @param applied Every filter with its applied value, empty when unset.
   * @param accountName The name of the account the list is narrowed to.
   * @param kindName The operator-facing name of the applied kind.
   * @param statusName The operator-facing name of the applied status.
   * @returns The chips.
   */
  const chips = (
    applied: Record<string, string>,
    accountName: string,
    kindName: string,
    statusName: string,
  ): ActiveFilterChip[] => {
    const result: ActiveFilterChip[] = []
    if (applied.account !== '') {
      result.push(labels.chip('account', t('tasks.list.filters.account'), accountName))
    }
    if (applied.status !== '') {
      result.push(labels.chip('status', t('tasks.list.filters.status'), statusName))
    }
    if (applied.kind !== '') {
      result.push(labels.chip('kind', t('tasks.list.filters.kind'), kindName))
    }
    if (applied.from !== '') {
      result.push(labels.chip('from', t('tasks.list.filters.from'), calendarDay(applied.from)))
    }
    if (applied.to !== '') {
      result.push(labels.chip('to', t('tasks.list.filters.to'), calendarDay(applied.to)))
    }
    return result
  }

  return { schema, chips }
}
