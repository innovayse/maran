import { useI18n } from 'vue-i18n'
import { useListLabels } from './useListLabels'
import { useLocaleStore } from '../stores/locale'
import { formatDate } from '../utils/formatDate'
import { isCalendarDate } from '../utils/isCalendarDate'
import { localDayBoundary } from '../utils/localDayBoundary'
import type { ListFilterSchema } from './useServerTable'
import type { ActiveFilterChip } from '../types/list'

/** The audit journal's filter vocabulary: how each is read from the URL, and how it is shown as a chip. */
export interface AuditFilters {
  /** The `useServerTable` schema: `action`, `outcome`, `from` and `to`. */
  schema: ListFilterSchema
  /**
   * One removable chip per applied filter.
   * @param applied Every filter with its applied value, empty when unset.
   * @param actionName The operator-facing name of the applied action.
   * @param outcomeName The operator-facing name of the applied outcome.
   * @returns The chips, in the order the sheet lists the fields.
   */
  chips: (applied: Record<string, string>, actionName: string, outcomeName: string) => ActiveFilterChip[]
}

/**
 * The audit journal's filters in one place, so the page that reads them from the URL and the chips
 * that name them cannot disagree. The journal records no account, so there is no account filter;
 * the actor is reached through the search. An outcome the panel does not have, and a date that is
 * not a real `YYYY-MM-DD` day, are not honoured from a hand-edited link; an action is honoured as
 * written, because actions are open-ended and the panel matches one exactly. So is an outcome: the
 * set belongs to the panel, which lists it and refuses a value it does not have, so the bundle
 * keeps no copy of it to check a link against.
 * @returns The schema and the chip builder.
 */
export const useAuditFilters = (): AuditFilters => {
  const { t } = useI18n()
  const labels = useListLabels()
  const localeStore = useLocaleStore()

  const schema: ListFilterSchema = {
    action: {},
    outcome: {},
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
   * @param actionName The operator-facing name of the applied action.
   * @param outcomeName The operator-facing name of the applied outcome.
   * @returns The chips.
   */
  const chips = (applied: Record<string, string>, actionName: string, outcomeName: string): ActiveFilterChip[] => {
    const result: ActiveFilterChip[] = []
    if (applied.action !== '') {
      result.push(labels.chip('action', t('app.audit.filters.action'), actionName))
    }
    if (applied.outcome !== '') {
      result.push(labels.chip('outcome', t('app.audit.filters.outcome'), outcomeName))
    }
    if (applied.from !== '') {
      result.push(labels.chip('from', t('app.audit.filters.from'), calendarDay(applied.from)))
    }
    if (applied.to !== '') {
      result.push(labels.chip('to', t('app.audit.filters.to'), calendarDay(applied.to)))
    }
    return result
  }

  return { schema, chips }
}
