import type { SiteChoice } from '../types/site'

/**
 * The panel's label for a value of a closed set.
 *
 * The label is the panel's own text in the caller's language. While the list has not arrived, or
 * for a value it does not list, the raw value is shown rather than a made-up name.
 *
 * @param listed The panel's choices, or `undefined` while they are unknown.
 * @param value The chosen value.
 * @returns The label to show.
 */
export const choiceLabelOf = (listed: readonly SiteChoice[] | undefined, value: string): string => {
  return (
    listed?.find((choice) => {
      return choice.value === value
    })?.label ?? value
  )
}
