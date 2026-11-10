import type { ButtonSize, ButtonVariant } from '../components/ui/UiButton.vue'
import { buttonClasses } from './buttonClasses'

/**
 * Text, spacing and focus classes of the dropdown's own trigger, beneath the box the look adds.
 * Not applied when a `triggerVariant` is set: the button styling owns every one of these properties
 * then, and a second owner is exactly the conflict that used to need important modifiers.
 */
const OWN_TRIGGER_CLASSES =
  'inline-flex items-center gap-1.5 text-sm text-text-primary transition-colors focus-visible:shadow-focus focus-visible:outline-none disabled:cursor-not-allowed disabled:text-text-muted'

/**
 * Classes of a dropdown's trigger button. A `triggerVariant` hands the whole look to the kit's button
 * styling, so the trigger is the same button a caller would have rendered with `UiButton` and no
 * ancestor has to reach into the dropdown's markup to restyle it; otherwise the box is the
 * dropdown's own (boxed or bare) on top of the shared text and focus classes.
 * @param triggerSize Geometry of the trigger when `triggerVariant` is set.
 * @param bare Whether the own trigger draws no box of its own (the sidebar's identity row).
 * @param triggerVariant The `UiButton` variant to draw the trigger as; left out for the dropdown's own.
 * @returns The class string for the trigger button.
 */
export const dropdownTriggerClasses = (
  triggerSize: ButtonSize,
  bare: boolean,
  triggerVariant?: ButtonVariant,
): string => {
  if (triggerVariant !== undefined) {
    return buttonClasses(triggerVariant, triggerSize)
  }
  const box = bare
    ? 'w-full rounded-control px-1.5 py-1 enabled:hover:bg-surface-3'
    : 'min-h-control rounded-control border border-border-subtle bg-surface-2 px-3 py-1.5 font-medium enabled:hover:border-border-strong enabled:hover:bg-surface-3'
  return `${OWN_TRIGGER_CLASSES} ${box}`
}
