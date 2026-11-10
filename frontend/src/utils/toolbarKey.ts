import type { InjectionKey } from 'vue'

/**
 * What a `UiToolbar` provides to the controls inside it: the fact that they sit on a toolbar's one
 * line. A field there hides its stacked label visually (the label stays its accessible name),
 * because a visible label above the input is exactly what pushes one control off the shared
 * centre line. The value is always `true`; a control outside a toolbar finds no provider.
 */
export const TOOLBAR_KEY: InjectionKey<true> = Symbol('toolbar')
