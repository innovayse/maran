/** The accent fill under white text, which the shell's primary controls read. */
export const ACCENT_FILL_PROPERTY = '--acf'

/** The hover of the accent fill. */
export const ACCENT_HOVER_PROPERTY = '--ach'

/**
 * The CSS custom properties a reseller's accent colour overrides: the fill and its hover. The hover
 * is derived in CSS (`color-mix`) so no colour arithmetic lives in the SPA. The one place that knows
 * which tokens a reseller colour replaces and how the hover follows from it, shared by the shell and
 * the branding preview.
 * @param color The accent colour, as `#rrggbb`.
 * @returns The property names mapped to the values to set.
 */
export const brandingAccentStyle = (color: string): Record<string, string> => {
  return {
    [ACCENT_FILL_PROPERTY]: color,
    [ACCENT_HOVER_PROPERTY]: `color-mix(in srgb, ${color} 80%, black)`,
  }
}
