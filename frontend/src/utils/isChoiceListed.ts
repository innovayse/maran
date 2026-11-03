import type { SiteChoice } from '../types/site'

/**
 * Whether a value is one of the choices the panel listed for a closed set.
 *
 * While the list has not arrived (`undefined`) the answer is yes: the panel's lists are advice for
 * the pickers, the endpoint is the boundary, and a link the SPA cannot judge yet must not be
 * refused what the server would accept (rules/vue.md "Forms").
 *
 * @param listed The panel's choices, or `undefined` while they are unknown.
 * @param value The value a link or a form carries.
 * @returns True when the value is listed or the list is unknown.
 */
export const isChoiceListed = (listed: readonly SiteChoice[] | undefined, value: string): boolean => {
  return (
    listed === undefined ||
    listed.some((choice) => {
      return choice.value === value
    })
  )
}
