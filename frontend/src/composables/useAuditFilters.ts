import { useI18n } from 'vue-i18n'
import { useListLabels } from './useListLabels'
import { useLocaleStore } from '../stores/locale'
import { formatDate } from '../utils/formatDate'
import { isCalendarDate } from '../utils/isCalendarDate'
import { localDayBoundary } from '../utils/localDayBoundary'
import type { ListFilterSchema } from './useServerTable'
import type { AuditOutcomeChoice } from '../types/audit'
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
 * written, because actions are open-ended and the panel matches one exactly. An outcome is checked
 * against the set the panel listed, and honoured as written until the panel has listed it: the set
 * belongs to the panel, so the bundle keeps no copy of it, and the server stays the authority.
 * @param outcomes Reads the outcomes the panel listed; empty until read or when the read failed.
 * @returns The schema and the chip builder.
 */
export const useAuditFilters = (outcomes: () => readonly AuditOutcomeChoice[]): AuditFilters => {
  const { t } = useI18n()
  const labels = useListLabels()
  const localeStore = useLocaleStore()

  const schema: ListFilterSchema = {
    action: {},
    outcome: {
      /**
       * Honours only an outcome the panel lists; permissive while the list is unknown.
       * @param value The `?outcome=` value.
       * @returns True for a listed outcome, or while the list is unknown.
       */
      accepts: (value: string): boolean => {
        const listed = outcomes()
        return (
          listed.length === 0 ||
          listed.some((choice) => {
            return choice.outcome === value
          })
        )
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
