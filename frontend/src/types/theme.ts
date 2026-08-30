/**
 * Colour themes the panel ships, in the order the theme control offers them.
 *
 * The list and the type live together because the type is derived from the
 * list: they are one fact, and splitting them would let a theme be added to
 * one without the other.
 *
 * Dark is first because it is the design's baseline: `index.html` ships
 * `data-theme="dark"` so the very first paint is already correct, and light
 * is the deviation the user opts into.
 */
export const SUPPORTED_THEMES = ['dark', 'light'] as const

/** A colour theme the SPA supports; written to `<html data-theme>`. */
export type AppTheme = (typeof SUPPORTED_THEMES)[number]
