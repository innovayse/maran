import { useI18n } from 'vue-i18n'
import { useListLabels } from './useListLabels'
import { useBackupOptionsStore } from '../stores/backupOptions'
import { useLocaleStore } from '../stores/locale'
import { formatDate } from '../utils/formatDate'
import { isCalendarDate } from '../utils/isCalendarDate'
import { localDayBoundary } from '../utils/localDayBoundary'
import type { ListFilterSchema, ListFilterSpec } from './useServerTable'
import type { ActiveFilterChip } from '../types/list'
import type { BackupChoice } from '../types/backupOptions'

/** The backup history's filter vocabulary: how each is read from the URL, and how it is shown as a chip. */
export interface BackupHistoryFilters {
  /** The `useServerTable` schema: `account`, `status`, `kind`, `from` and `to`. */
  schema: ListFilterSchema
  /**
   * One removable chip per applied filter.
   * @param applied Every filter with its applied value, empty when unset.
   * @param accountName The name of the account the history is narrowed to.
   * @returns The chips, in the order the sheet lists the fields.
   */
  chips: (applied: Record<string, string>, accountName: string) => ActiveFilterChip[]
}

/**
 * The backup history's filters in one place, so the page that reads them from the URL and the
 * chips that name them cannot disagree. A status or reason the panel does not list, and a date that
 * is not a real `YYYY-MM-DD` day, are not honoured from a hand-edited link; until the panel has
 * listed its statuses and reasons a value is honoured as written, because the server is the
 * authority and the bundle keeps no copy of either set.
 * @param accountSpec The `account` entry from `useAccountFilter`, which a customer's list ignores.
 * @returns The schema and the chip builder.
 */
export const useBackupHistoryFilters = (accountSpec: ListFilterSpec): BackupHistoryFilters => {
  const { t } = useI18n()
  const labels = useListLabels()
  const localeStore = useLocaleStore()
  const optionsStore = useBackupOptionsStore()

  /**
   * Whether a value is one the panel listed. An empty list is an unknown one (not yet read, or the
   * read failed), and the answer then is yes: the lists are advice, the endpoint is the boundary.
   * @param list The panel's list of choices.
   * @param value The value found in the URL.
   * @returns True when the value is listed or the list is unknown.
   */
  const isListed = (list: readonly BackupChoice[], value: string): boolean => {
    return (
      list.length === 0 ||
      list.some((choice) => {
        return choice.value === value
      })
    )
  }

  const schema: ListFilterSchema = {
    account: accountSpec,
    status: {
      /**
       * Honours only a status the panel lists; permissive while the list is unknown.
       * @param value The `?status=` value.
       * @returns True for a listed status, or while the list is unknown.
       */
      accepts: (value: string): boolean => {
        return isListed(optionsStore.options.statuses, value)
      },
    },
    kind: {
      /**
       * Honours only a reason the panel lists; permissive while the list is unknown.
       * @param value The `?kind=` value.
       * @returns True for a listed reason, or while the list is unknown.
       */
      accepts: (value: string): boolean => {
        return isListed(optionsStore.options.kinds, value)
      },
    },
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
   * The panel's own name for a value of one of its choice lists.
   * @param list The list the value belongs to.
   * @param value The value as applied.
   * @returns The name, or the value itself until the list has arrived.
   */
  const nameOf = (list: BackupChoice[], value: string): string => {
    return (
      list.find((choice) => {
        return choice.value === value
      })?.label ?? value
    )
  }

  /**
   * One removable chip per applied filter.
   * @param applied Every filter with its applied value, empty when unset.
   * @param accountName The name of the account the history is narrowed to.
   * @returns The chips.
   */
  const chips = (applied: Record<string, string>, accountName: string): ActiveFilterChip[] => {
    const result: ActiveFilterChip[] = []
    if (applied.account !== '') {
      result.push(labels.chip('account', t('backups.list.filters.account'), accountName))
    }
    if (applied.status !== '') {
      const status = nameOf(optionsStore.options.statuses, applied.status)
      result.push(labels.chip('status', t('backups.list.filters.status'), status))
    }
    if (applied.kind !== '') {
      const kind = nameOf(optionsStore.options.kinds, applied.kind)
      result.push(labels.chip('kind', t('backups.list.filters.kind'), kind))
    }
    if (applied.from !== '') {
      result.push(labels.chip('from', t('backups.list.filters.from'), calendarDay(applied.from)))
    }
    if (applied.to !== '') {
      result.push(labels.chip('to', t('backups.list.filters.to'), calendarDay(applied.to)))
    }
    return result
  }

  return { schema, chips }
}
