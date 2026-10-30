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
 * The trailing-arrow nudge: a forward arrow inside a button (`UiIcon name="arrowRight"`, which
 * lucide renders with the `lucide-arrow-right` class) moves 2px towards where the action leads
 * while the pointer is on the button. It is the button's own child, so the selector stays inside
 * the kit; a caller never reaches in. The nudge is `motion-safe` only: a reader who asked for
 * reduced motion gets a still arrow. A disabled button does not nudge.
 */
const ARROW_NUDGE_CLASSES =
  '[&_.lucide-arrow-right]:transition-transform [&_.lucide-arrow-right]:duration-200 motion-safe:not-disabled:hover:[&_.lucide-arrow-right]:translate-x-0.5'

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
      return 'h-control min-w-control justify-center gap-1.5 px-2.5 text-sm'
    case 'icon':
      return 'size-control shrink-0 justify-center p-0 text-sm'
    case 'icon-sm':
      return 'size-inset shrink-0 justify-center p-0 text-sm'
    case 'tab':
      return 'min-w-0 flex-col justify-center gap-1 p-0 text-xs leading-tight'
    case 'md':
    default:
      if (variant === 'field') {
        return 'min-h-control justify-start gap-2 px-3 py-1.5 text-sm'
      }
      return `min-h-control justify-center gap-1.5 py-1.5 text-sm ${variant === 'primary' ? 'px-4' : 'px-3'}`
  }
}

/**
 * Builds the complete class string of a button: the shared base, the arrow nudge, the variant, the
 * size and the font weight. `UiButton` and `UiDropdown`'s trigger both call it, so a trigger that is drawn as a
 * button is the same button and not a second copy that drifts.
 * @param variant The visual weight asked for.
 * @param size The size asked for.
 * @returns Space-separated utility classes with no property set twice.
 */
export const buttonClasses = (variant: ButtonVariant, size: ButtonSize): string => {
  const weight = size === 'tab' ? 'font-semibold' : variantWeight(variant)
  return `${BASE_CLASSES} ${ARROW_NUDGE_CLASSES} ${variantClasses(variant)} ${sizeClasses(variant, size)} ${weight}`
}
