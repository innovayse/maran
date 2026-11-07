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
  /** The `useServerTable` schema: `action`, `outcome`, `from`, `to`, `actor` and `account`. */
  schema: ListFilterSchema
  /**
   * One removable chip per applied filter.
   * @param applied Every filter with its applied value, empty when unset.
   * @param actionName The operator-facing name of the applied action.
   * @param outcomeName The operator-facing name of the applied outcome.
   * @param accountName The name of the applied account, as the account choice lists it.
   * @returns The chips, in the order the sheet lists the fields.
   */
  chips: (
    applied: Record<string, string>,
    actionName: string,
    outcomeName: string,
    accountName: string,
  ) => ActiveFilterChip[]
}

/** The longest login name the actor filter honours; the panel refuses a longer one. */
const ACTOR_MAX_LENGTH = 64

/** The shape of an account id, the only value the account filter takes. */
const ACCOUNT_ID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i

/**
 * The audit journal's filters in one place, so the page that reads them from the URL and the chips
 * that name them cannot disagree. The actor is a typed login name, matched exactly by the panel, and
 * the account is one the account choice lists, sent as its id; an entry carries an account only when
 * the panel could attribute it to one, so the account filter narrows to those entries.
 * An outcome the panel does not have, and a date that is
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
    actor: {
      /**
       * Honours an actor no longer than the panel accepts.
       * @param value The `?actor=` value.
       * @returns True when the value is within the limit.
       */
      accepts: (value: string): boolean => {
        return value.length <= ACTOR_MAX_LENGTH
      },
    },
    account: {
      /**
       * Honours only a well-formed account id; the panel stays the authority on whether it exists.
       * @param value The `?account=` value.
       * @returns True for an account id's shape.
       */
      accepts: (value: string): boolean => {
        return ACCOUNT_ID.test(value)
      },
    },
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
   * @param accountName The name of the applied account, as the account choice lists it.
   * @returns The chips.
   */
  const chips = (
    applied: Record<string, string>,
    actionName: string,
    outcomeName: string,
    accountName: string,
  ): ActiveFilterChip[] => {
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
    if (applied.actor !== '') {
      result.push(labels.chip('actor', t('app.audit.filters.actor'), applied.actor))
    }
    if (applied.account !== '') {
      result.push(labels.chip('account', t('app.audit.filters.account'), accountName))
    }
    return result
  }

  return { schema, chips }
}
