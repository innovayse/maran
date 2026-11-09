/**
 * WCAG 2.2 contrast arithmetic and the reader of the design tokens in `main.css`, for the
 * node-only token-contrast spec. Nothing here touches a browser: the tokens are plain text in a
 * stylesheet, so the check reads that text and does the sums itself.
 */

/** One colour as the channels the contrast formula needs: 0 to 255 per channel, alpha 0 to 1. */
export interface Rgba {
  /** Red channel, 0 to 255. */
  r: number
  /** Green channel, 0 to 255. */
  g: number
  /** Blue channel, 0 to 255. */
  b: number
  /** Opacity, 0 (clear) to 1 (opaque). */
  a: number
}

/** A theme's tokens: the custom property name without its dashes (`s1`, `act`) to its CSS value. */
export type TokenMap = Map<string, string>

/** Both themes as `main.css` defines them. The light map already carries the dark values it inherits. */
export interface ThemeTokens {
  /** The dark theme, which is the default. */
  dark: TokenMap
  /** The light theme: dark values overlaid with the light block's overrides. */
  light: TokenMap
}

/** The one colour that is not a token: the text on an accent fill. */
export const WHITE: Rgba = { r: 255, g: 255, b: 255, a: 1 }

/** A `#rrggbb` colour. */
const HEX_COLOUR = /^#([0-9a-f]{2})([0-9a-f]{2})([0-9a-f]{2})$/i

/** An `rgb(r g b)` or `rgb(r g b / a)` colour in the space-separated syntax the stylesheet uses. */
const RGB_COLOUR = /^rgb\(\s*(\d+)\s+(\d+)\s+(\d+)\s*(?:\/\s*([\d.]+)\s*)?\)$/i

/** A block comment, which can quote hex values that are not tokens. */
const CSS_COMMENT = /\/\*[\s\S]*?\*\//g

/** A custom property declaration, `--name: value;`, inside a block body. */
const DECLARATION = /--([a-z0-9-]+)\s*:\s*([^;]+);/gi

/**
 * Parses a `#rrggbb` or `rgb(r g b / a)` colour.
 * @param value The CSS value, trimmed or not.
 * @returns The colour as channels.
 * @throws Error when the value is neither syntax, so an unreadable token fails loudly instead of
 *   being measured as black.
 */
export const parseColour = (value: string): Rgba => {
  const text = value.trim()
  const hex = HEX_COLOUR.exec(text)
  if (hex !== null) {
    return { r: parseInt(hex[1], 16), g: parseInt(hex[2], 16), b: parseInt(hex[3], 16), a: 1 }
  }
  const rgb = RGB_COLOUR.exec(text)
  if (rgb !== null) {
    return { r: Number(rgb[1]), g: Number(rgb[2]), b: Number(rgb[3]), a: rgb[4] === undefined ? 1 : Number(rgb[4]) }
  }
  throw new Error(`Cannot read "${value}" as #rrggbb or rgb(r g b / a)`)
}

/**
 * Lays a possibly translucent colour over an opaque one, the way the browser paints it.
 * @param top The colour on top.
 * @param below The opaque colour underneath.
 * @returns The opaque result.
 */
export const compositeOver = (top: Rgba, below: Rgba): Rgba => {
  const mix = (over: number, under: number): number => {return over * top.a + under * (1 - top.a)}
  return { r: mix(top.r, below.r), g: mix(top.g, below.g), b: mix(top.b, below.b), a: 1 }
}

/**
 * Computes WCAG relative luminance.
 * @param colour An opaque colour.
 * @returns Luminance from 0 (black) to 1 (white).
 */
export const relativeLuminance = (colour: Rgba): number => {
  const linear = (channel: number): number => {
    const unit = channel / 255
    return unit <= 0.04045 ? unit / 12.92 : Math.pow((unit + 0.055) / 1.055, 2.4)
  }
  return 0.2126 * linear(colour.r) + 0.7152 * linear(colour.g) + 0.0722 * linear(colour.b)
}

/**
 * Computes the WCAG contrast ratio between two opaque colours.
 * @param first One colour.
 * @param second The other colour; order does not matter.
 * @returns A ratio from 1 to 21.
 */
export const contrastRatio = (first: Rgba, second: Rgba): number => {
  const a = relativeLuminance(first)
  const b = relativeLuminance(second)
  return (Math.max(a, b) + 0.05) / (Math.min(a, b) + 0.05)
}

/**
 * Reads the custom properties declared in the body of one rule.
 * @param body The text between a rule's braces.
 * @returns Name (no dashes) to raw value.
 */
const declarationsOf = (body: string): TokenMap => {
  const found: TokenMap = new Map()
  for (const match of body.matchAll(DECLARATION)) {
    found.set(match[1], match[2].trim())
  }
  return found
}

/**
 * Finds the body of the rule whose selector list contains the given text.
 * @param css Comment-free stylesheet text.
 * @param selector A fragment of the selector, for example `html[data-theme='light']`.
 * @returns The text between that rule's braces.
 * @throws Error when no such rule exists, because a renamed selector would otherwise measure an
 *   empty theme.
 */
const bodyOfRule = (css: string, selector: string): string => {
  const at = css.indexOf(selector)
  if (at < 0) {
    throw new Error(`No rule with selector ${selector} in the stylesheet`)
  }
  const open = css.indexOf('{', at)
  const close = css.indexOf('}', open)
  return css.slice(open + 1, close)
}

/**
 * Reads both themes' tokens out of `main.css`.
 * @param css The stylesheet text.
 * @returns The dark tokens and the light tokens (dark overlaid by the light block).
 */
export const readThemeTokens = (css: string): ThemeTokens => {
  const bare = css.replace(CSS_COMMENT, '')
  const dark = declarationsOf(bodyOfRule(bare, "html[data-theme='dark']"))
  const light: TokenMap = new Map(dark)
  for (const [name, value] of declarationsOf(bodyOfRule(bare, "html[data-theme='light']"))) {
    light.set(name, value)
  }
  return { dark, light }
}

/**
 * Resolves one token to a colour.
 * @param tokens A theme's tokens.
 * @param name The token name without dashes.
 * @returns The colour.
 * @throws Error when the theme does not define the token.
 */
export const tokenColour = (tokens: TokenMap, name: string): Rgba => {
  const value = tokens.get(name)
  if (value === undefined) {
    throw new Error(`Token --${name} is not defined in the theme`)
  }
  return parseColour(value)
}
