import { useI18n } from 'vue-i18n'
import { useListLabels } from './useListLabels'
import { useLocaleStore } from '../stores/locale'
import { formatDate } from '../utils/formatDate'
import { isCalendarDate } from '../utils/isCalendarDate'
import { localDayBoundary } from '../utils/localDayBoundary'
import { TASK_STATUSES } from '../types/panelTask'
import type { ListFilterSchema, ListFilterSpec } from './useServerTable'
import type { ActiveFilterChip } from '../types/list'

/** The tasks list's filter vocabulary: how each is read from the URL, and how it is shown as a chip. */
export interface TaskFilters {
  /** The `useServerTable` schema: `account`, `status`, `kind`, `from` and `to`. */
  schema: ListFilterSchema
  /**
   * One removable chip per applied filter.
   * @param applied Every filter with its applied value, empty when unset.
   * @param accountName The name of the account the list is narrowed to.
   * @param kindName The operator-facing name of the applied kind.
   * @returns The chips, in the order the sheet lists the fields.
   */
  chips: (applied: Record<string, string>, accountName: string, kindName: string) => ActiveFilterChip[]
}

/**
 * The tasks list's filters in one place, so the page that reads them from the URL and the chips
 * that name them cannot disagree. A status the panel does not have, and a date that is not a real
 * `YYYY-MM-DD` day, are not honoured from a hand-edited link. A kind is honoured as written: kinds
 * are open-ended (a module compiled later records its own), and the panel matches it exactly.
 * @param accountSpec The `account` entry from `useAccountFilter`, which a customer's list ignores.
 * @returns The schema and the chip builder.
 */
export const useTaskFilters = (accountSpec: ListFilterSpec): TaskFilters => {
  const { t } = useI18n()
  const labels = useListLabels()
  const localeStore = useLocaleStore()

  const schema: ListFilterSchema = {
    account: accountSpec,
    status: {
      /**
       * Honours only a status the panel has.
       * @param value The `?status=` value.
       * @returns True for a known status.
       */
      accepts: (value: string): boolean => {
        return (TASK_STATUSES as readonly string[]).includes(value)
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
   * @returns The chips.
   */
  const chips = (applied: Record<string, string>, accountName: string, kindName: string): ActiveFilterChip[] => {
    const result: ActiveFilterChip[] = []
    if (applied.account !== '') {
      result.push(labels.chip('account', t('tasks.list.filters.account'), accountName))
    }
    if (applied.status !== '') {
      result.push(labels.chip('status', t('tasks.list.filters.status'), t(`tasks.statuses.${applied.status}`)))
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
