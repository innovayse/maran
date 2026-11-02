import type { NavigationLabel } from '../types/navigation'

/**
 * The text a navigation entry is drawn and searched with.
 * @param label The entry's label.
 * @param translate The i18n `t` of the calling component.
 * @returns The translated key, or the panel's own text.
 */
export const navigationLabelText = (label: NavigationLabel, translate: (key: string) => string): string => {
  return label.kind === 'key' ? translate(label.key) : label.text
}
