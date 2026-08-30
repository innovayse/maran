/**
 * Elements a Tab press can reach. Disabled controls and `tabindex="-1"` are
 * excluded because they are exactly what a focus trap must skip over.
 *
 * Not exported: this file exports one unit (rules/vue.md), and a caller that
 * needed the raw selector would be running the query itself instead of asking
 * for its result.
 */
const FOCUSABLE_SELECTOR =
  'a[href], button:not([disabled]), input:not([disabled]), textarea:not([disabled]), select:not([disabled]), [tabindex]:not([tabindex="-1"])'

/**
 * Collects the focusable descendants of `root`, in DOM order.
 *
 * Read from the DOM rather than from a registry the component maintains: the
 * contents arrive through a slot, so the component cannot know which of them a
 * caller rendered with `v-if` at any given moment. DOM order is also exactly
 * the order the user meets them in, which is what a focus trap has to honour.
 * @param root The container to search, or null before it is mounted.
 * @returns The focusable elements inside `root`, empty when it is not mounted.
 */
export const focusableElements = (root: HTMLElement | null): HTMLElement[] =>
  Array.from(root?.querySelectorAll<HTMLElement>(FOCUSABLE_SELECTOR) ?? [])
