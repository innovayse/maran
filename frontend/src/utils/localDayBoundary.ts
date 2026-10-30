import { endOfDay, parse, startOfDay } from 'date-fns'

/** Which end of a calendar day an instant is asked for. */
export type DayEdge = 'start' | 'end'

/** The form a calendar date travels in: what `UiDateInput` gives and a list's URL holds. */
const CALENDAR_DATE = 'yyyy-MM-dd'

/**
 * The instant a calendar day starts or ends at in the reader's own time zone, as ISO-8601.
 *
 * A date filter is picked as a day (`2026-10-01`) and the panel filters instants, so the day has to
 * become an instant somewhere; here, in the browser, because the reader's time zone is known only
 * here. The end is the day's last millisecond, so a window whose two ends are the same day holds
 * that whole day.
 * @param date A calendar date as `YYYY-MM-DD`.
 * @param edge `start` for the day's first instant, `end` for its last.
 * @returns The instant, in UTC ISO-8601 form.
 */
export const localDayBoundary = (date: string, edge: DayEdge): string => {
  const day = parse(date, CALENDAR_DATE, new Date())
  return (edge === 'start' ? startOfDay(day) : endOfDay(day)).toISOString()
}
