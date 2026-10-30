import { useI18n } from 'vue-i18n'
import { useListLabels } from './useListLabels'
import { useLocaleStore } from '../stores/locale'
import { formatDate } from '../utils/formatDate'
import { isCalendarDate } from '../utils/isCalendarDate'
import { localDayBoundary } from '../utils/localDayBoundary'
import { BACKUP_KINDS, BACKUP_STATUSES } from '../types/backup'
import type { ListFilterSchema, ListFilterSpec } from './useServerTable'
import type { ActiveFilterChip } from '../types/list'

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
 * chips that name them cannot disagree. A status or reason the panel does not have, and a date that
 * is not a real `YYYY-MM-DD` day, are not honoured from a hand-edited link.
 * @param accountSpec The `account` entry from `useAccountFilter`, which a customer's list ignores.
 * @returns The schema and the chip builder.
 */
export const useBackupHistoryFilters = (accountSpec: ListFilterSpec): BackupHistoryFilters => {
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
        return (BACKUP_STATUSES as readonly string[]).includes(value)
      },
    },
    kind: {
      /**
       * Honours only a reason the panel has.
       * @param value The `?kind=` value.
       * @returns True for a known reason.
       */
      accepts: (value: string): boolean => {
        return (BACKUP_KINDS as readonly string[]).includes(value)
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
      result.push(labels.chip('status', t('backups.list.filters.status'), t(`backups.status.${applied.status}`)))
    }
    if (applied.kind !== '') {
      result.push(labels.chip('kind', t('backups.list.filters.kind'), t(`backups.kind.${applied.kind}`)))
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
