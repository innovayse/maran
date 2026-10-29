import type { ButtonSize, ButtonVariant } from '../components/ui/UiButton.vue'

/**
 * Classes every button shares whatever its variant or size: the flex row, the border, the
 * transition and the disabled look. Nothing here is a property a variant or a size sets, which is
 * the whole point of the split: two utilities for one property are resolved by the order Tailwind
 * emits them, not by the order they are written in a template, so a property is owned by exactly
 * one of the three parts and never restated by a caller with an important modifier.
 */
const BASE_CLASSES =
  'inline-flex items-center border text-center transition-colors focus-visible:outline-none disabled:cursor-not-allowed disabled:border-border-subtle disabled:bg-surface-2 disabled:text-text-muted disabled:shadow-none'

/**
 * Shape, focus ring, fill, text colour and hover of one variant. Hover is written
 * `not-disabled:hover:` so a disabled button never answers the pointer, and so a link rendering of
 * the same classes (an `<a>` is never `:enabled`) still gets its hover.
 * @param variant The visual weight asked for.
 * @returns The variant's utility classes, without padding, font weight or size.
 */
const variantClasses = (variant: ButtonVariant): string => {
  switch (variant) {
    case 'secondary':
      return 'rounded-control focus-visible:shadow-focus border-border-subtle bg-surface-2 text-text-primary not-disabled:hover:border-border-strong not-disabled:hover:bg-surface-3'
    case 'ghost':
      return 'rounded-control focus-visible:shadow-focus border-transparent text-text-secondary not-disabled:hover:bg-surface-3 not-disabled:hover:text-text-primary'
    case 'quiet':
      return 'rounded-control focus-visible:shadow-focus border-transparent text-text-muted not-disabled:hover:bg-surface-3 not-disabled:hover:text-text-primary'
    case 'field':
      return 'rounded-control focus-visible:shadow-focus border-border-control bg-surface-2 text-text-muted not-disabled:hover:border-border-strong'
    case 'destructive':
      return 'rounded-control focus-visible:shadow-focus-danger border-danger bg-surface-1 text-danger not-disabled:hover:bg-page'
    case 'ai':
      return 'rounded-control focus-visible:shadow-focus border-violet bg-surface-1 text-violet not-disabled:hover:bg-page'
    case 'primary':
    default:
      return 'rounded-full focus-visible:shadow-focus border-transparent bg-accent-fill text-white shadow-glow not-disabled:hover:bg-accent-hover'
  }
}

/**
 * Font weight of one variant. Kept apart from {@link variantClasses} because the `tab` size
 * overrides it: a tab-bar cell has to match the semibold labels of the links beside it, and a
 * weight that both the variant and the size set would be the conflict this file exists to avoid.
 * @param variant The visual weight asked for.
 * @returns The variant's font-weight utility.
 */
const variantWeight = (variant: ButtonVariant): string => {
  switch (variant) {
    case 'field':
      return 'font-normal'
    case 'destructive':
    case 'ai':
    case 'primary':
      return 'font-semibold'
    default:
      return 'font-medium'
  }
}

/**
 * Box of one size: height, padding, gap, justification and text size. The default `md` size
 * depends on the variant, because the primary pill is wider than a rectangle and a field button
 * sits its content at the start; the others are a fixed geometry whatever the variant.
 * @param variant The visual weight asked for, which only the `md` size reads.
 * @param size The size asked for.
 * @returns The size's utility classes.
 */
const sizeClasses = (variant: ButtonVariant, size: ButtonSize): string => {
  switch (size) {
    case 'bar':
      return 'h-10 min-w-10 justify-center gap-2 px-2.5 text-sm'
    case 'icon':
      return 'size-10 shrink-0 justify-center p-0 text-sm'
    case 'icon-sm':
      return 'size-8 shrink-0 justify-center p-0 text-sm'
    case 'tab':
      return 'min-w-0 flex-col justify-center gap-1 p-0 text-xs leading-tight'
    case 'md':
    default:
      if (variant === 'field') {
        return 'min-h-10 justify-start gap-2.5 px-3.5 py-2 text-sm'
      }
      return `min-h-10 justify-center gap-2 py-2 text-sm ${variant === 'primary' ? 'px-5' : 'px-4'}`
  }
}

/**
 * Builds the complete class string of a button: the shared base, the variant, the size and the
 * font weight. `UiButton` and `UiDropdown`'s trigger both call it, so a trigger that is drawn as a
 * button is the same button and not a second copy that drifts.
 * @param variant The visual weight asked for.
 * @param size The size asked for.
 * @returns Space-separated utility classes with no property set twice.
 */
export const buttonClasses = (variant: ButtonVariant, size: ButtonSize): string => {
  const weight = size === 'tab' ? 'font-semibold' : variantWeight(variant)
  return `${BASE_CLASSES} ${variantClasses(variant)} ${sizeClasses(variant, size)} ${weight}`
}
