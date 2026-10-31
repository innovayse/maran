/**
 * Colour themes the panel ships: the two palettes `main.css` defines.
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

/**
 * What the reader can choose in the theme menu, in the order the menu offers them: one of the
 * two palettes, or `system`, which follows the operating system's light or dark preference and
 * keeps following it when it changes.
 *
 * A separate list from {@link SUPPORTED_THEMES} because the two answer different questions:
 * a preference is what the reader asked for and what is persisted, a theme is the palette
 * actually painted. `system` is never painted; it resolves to one of the two.
 */
export const THEME_PREFERENCES = ['light', 'dark', 'system'] as const

/** A theme preference the reader can choose; persisted as-is. */
export type ThemePreference = (typeof THEME_PREFERENCES)[number]
