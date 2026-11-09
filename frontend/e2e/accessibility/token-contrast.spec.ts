import { readFileSync } from 'node:fs'
import { dirname, join, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { expect, test } from '@playwright/test'
import {
  WHITE,
  compositeOver,
  contrastRatio,
  readThemeTokens,
  tokenColour,
  type Rgba,
  type ThemeTokens,
  type TokenMap,
} from '../fixtures/contrast-ratio'

// Issue #82: the contrast table in docs/design/redesign-2026-10/README.md section 3 was typed in
// by hand, so a token edit could invalidate every number in it without a single gate noticing. This
// spec recomputes the ratio of every pairing that table lists from `main.css` itself, in both
// themes, and fails under the WCAG line for the pairing's kind: 4.5:1 for text, 3:1 for a graphic
// and for a control boundary (1.4.11).
//
// It needs no browser: the tokens are text in a stylesheet. It still runs under Playwright because
// that is the suite's only runner (rules/testing.md).
//
// What it can see: a token value, and a surface or an accent wash laid over one. UNOBSERVED HERE:
// text over an image or a backdrop blur (the sign-in glass card), badge text on a status-coloured
// wash (the badge measures its own pairs), and anything a component paints that is not a token.
// axe and review still own those.

/** Repository root, resolved from this spec's own location rather than from the runner's cwd. */
const REPO_ROOT = resolve(dirname(fileURLToPath(import.meta.url)), '..', '..', '..')

/** The stylesheet that defines the tokens; the single source of truth the README is written from. */
const MAIN_CSS = join(REPO_ROOT, 'frontend', 'src', 'assets', 'css', 'main.css')

/** The design contract whose section 3 table this spec recomputes. */
const DESIGN_README = join(REPO_ROOT, 'docs', 'design', 'redesign-2026-10', 'README.md')

/** What a pairing is for, which decides the line it has to clear. */
type PairingKind = 'text' | 'graphic' | 'boundary' | 'decoration'

/** The surfaces a panel can be painted with, from the page floor to the pressed step. */
type Surface = 'bg' | 's1' | 's2' | 's3'

/**
 * One row of the README table, as a rule that is measured against each listed surface.
 */
interface Pairing {
  /** A fragment of the row's first cell in the README, so the row and the rule can be matched. */
  readme: string
  /** The foreground token, or `white` for text on an accent fill. */
  foreground: string
  /** The token the foreground sits on directly (a fill), instead of a surface; unset for a surface. */
  fill?: string
  /** An accent wash laid over each surface before the foreground is drawn on it. */
  wash?: string
  /** The surfaces the pairing is measured on. Ignored when `fill` is set. */
  surfaces: Surface[]
  /** What the pairing is for. */
  kind: PairingKind
}

/** A known-failing pairing: it must stay failing by no more than this, and must still fail. */
interface Exemption {
  /** The lowest ratio the pairing may fall to; a lower figure means the token got worse. */
  floor: number
  /** Why the pairing is allowed to fail, and the rule that keeps components off it. */
  why: string
}

/** The result of measuring one pairing on one surface in one theme. */
interface Measurement {
  /** Human-readable name of the pair, for the failure message. */
  label: string
  /** The WCAG ratio. */
  ratio: number
  /** The kind of pairing, which decides the line. */
  kind: PairingKind
}

/** The ratio text needs (WCAG 1.4.3 AA). */
const TEXT_LINE = 4.5

/** The ratio a graphic or a control boundary needs (WCAG 1.4.11). */
const NON_TEXT_LINE = 3.0

/**
 * Every row of README section 3's "Dark theme" table, once. The light theme is measured against the
 * same rules, because the README states the light values in section 2 and the same pairings must
 * hold there.
 */
const PAIRINGS: Pairing[] = [
  { readme: 'white on `--ac`', foreground: 'white', fill: 'ac', surfaces: [], kind: 'text' },
  { readme: 'white on `--acf`', foreground: 'white', fill: 'acf', surfaces: [], kind: 'text' },
  { readme: 'white on `--ach`', foreground: 'white', fill: 'ach', surfaces: [], kind: 'text' },
  { readme: '`--ac` against the `--s2` track', foreground: 'ac', surfaces: ['s2'], kind: 'graphic' },
  { readme: '`--ac` against `--b1`', foreground: 'ac', fill: 'b1', surfaces: [], kind: 'graphic' },
  { readme: '`--act` `#5290FF` on `--s1`', foreground: 'act', surfaces: ['s1'], kind: 'text' },
  { readme: 'on the `--acs` wash over `--s1` / `--s2`', foreground: 'act', wash: 'acs', surfaces: ['s1', 's2'], kind: 'text' },
  { readme: 'on the `--acs` wash over `--s3`', foreground: 'act', wash: 'acs', surfaces: ['s3'], kind: 'text' },
  { readme: '`--acl` `#8FBCFF` on `--s1`', foreground: 'acl', surfaces: ['s1'], kind: 'graphic' },
  { readme: '`--t1` on', foreground: 't1', surfaces: ['bg', 's1', 's2'], kind: 'text' },
  { readme: '`--t2` on', foreground: 't2', surfaces: ['bg', 's1', 's2'], kind: 'text' },
  { readme: '`--t3` on', foreground: 't3', surfaces: ['bg', 's1', 's2', 's3'], kind: 'text' },
  { readme: '`--gr` / `--am` on `--s1`', foreground: 'gr', surfaces: ['s1'], kind: 'text' },
  { readme: '`--gr` / `--am` on `--s1`', foreground: 'am', surfaces: ['s1'], kind: 'text' },
  { readme: '`--rd` on `--bg` / `--s1`', foreground: 'rd', surfaces: ['bg', 's1'], kind: 'text' },
  { readme: '`--rd` on `--s2`', foreground: 'rd', surfaces: ['s2'], kind: 'text' },
  { readme: '`--rd` on `--s3`', foreground: 'rd', surfaces: ['s3'], kind: 'text' },
  { readme: '`--b1` against `--s1`', foreground: 'b1', surfaces: ['s1'], kind: 'decoration' },
  { readme: '`--b2` against `--s1`', foreground: 'b2', surfaces: ['s1'], kind: 'decoration' },
  { readme: '`--bc` against', foreground: 'bc', surfaces: ['s1', 's2', 's3'], kind: 'boundary' },
  { readme: '`--bch` against', foreground: 'bch', surfaces: ['s1', 's2', 's3'], kind: 'boundary' },
]

/**
 * The pairings that fail on purpose, keyed `theme:label`. The README names each one as a failure
 * and gives the rule that keeps components off it; they are asserted rather than skipped so a token
 * edit cannot make them worse unseen, and so a fix (the pairing starting to pass) forces the entry
 * and the README to be removed together.
 */
const KNOWN_FAILING = new Map<string, Exemption>([
  [
    'dark:white on --ac',
    { floor: 3.88, why: '--ac is for graphics; white text sits on --acf (README: the one most likely mistake)' },
  ],
  [
    'light:white on --ac',
    { floor: 3.88, why: '--ac is inherited unchanged into the light theme and is graphics only' },
  ],
  [
    'dark:--act on --acs over --s3',
    { floor: 4.38, why: 'README residual risks: no accent text on a wash over the pressed surface' },
  ],
  [
    'dark:--rd on --s3',
    { floor: 4.12, why: 'README residual risks: no error text on --s3' },
  ],
])

/**
 * Names a pairing's foreground the way the README does.
 * @param pairing The pairing.
 * @returns For example `--act` or `white`.
 */
const foregroundName = (pairing: Pairing): string => {
  return pairing.foreground === 'white' ? 'white' : `--${pairing.foreground}`
}

/**
 * Measures one pairing on every surface (or fill) it lists, in one theme.
 * @param pairing The rule.
 * @param tokens The theme's tokens.
 * @returns One measurement per surface or fill.
 */
const measure = (pairing: Pairing, tokens: TokenMap): Measurement[] => {
  const foreground: Rgba = pairing.foreground === 'white' ? WHITE : tokenColour(tokens, pairing.foreground)
  if (pairing.fill !== undefined) {
    const label = `${foregroundName(pairing)} on --${pairing.fill}`
    return [{ label, ratio: contrastRatio(foreground, tokenColour(tokens, pairing.fill)), kind: pairing.kind }]
  }
  return pairing.surfaces.map((surface) => {
    const base = tokenColour(tokens, surface)
    // A wash is translucent, so it is composited over its surface first and the text is then
    // measured against the colour the browser actually paints.
    const background = pairing.wash === undefined ? base : compositeOver(tokenColour(tokens, pairing.wash), base)
    const where = pairing.wash === undefined ? `--${surface}` : `--${pairing.wash} over --${surface}`
    return { label: `${foregroundName(pairing)} on ${where}`, ratio: contrastRatio(foreground, background), kind: pairing.kind }
  })
}

/**
 * Decides which measurements break the rules, given the exemptions that apply to a theme.
 * @param theme The theme's name, the first half of an exemption key.
 * @param measurements Everything measured in that theme.
 * @returns One sentence per problem; empty when the theme holds.
 */
const problemsOf = (theme: string, measurements: Measurement[]): string[] => {
  const problems: string[] = []
  for (const m of measurements) {
    const line = m.kind === 'text' ? TEXT_LINE : NON_TEXT_LINE
    const exemption = KNOWN_FAILING.get(`${theme}:${m.label}`)
    const shown = m.ratio.toFixed(3)
    if (m.kind === 'decoration') {
      // Decoration is allowed to be faint, but must stay faint: if a border ever reaches 3:1 the
      // README's "not a control boundary" statement is out of date and a component may start
      // relying on it.
      if (m.ratio >= NON_TEXT_LINE) {
        problems.push(`${theme}: ${m.label} is ${shown}:1, now a boundary; the README calls it decoration`)
      }
    } else if (exemption !== undefined) {
      if (m.ratio >= line) {
        problems.push(`${theme}: ${m.label} is ${shown}:1 and passes now; drop its exemption and the README's failure note`)
      } else if (m.ratio < exemption.floor) {
        problems.push(`${theme}: ${m.label} is ${shown}:1, below its recorded floor ${exemption.floor} (${exemption.why})`)
      }
    } else if (m.ratio < line) {
      problems.push(`${theme}: ${m.label} is ${shown}:1, under ${line}:1 for ${m.kind}`)
    }
  }
  return problems
}

/**
 * Measures every pairing in one theme.
 * @param tokens The theme's tokens.
 * @returns All measurements.
 */
const measureAll = (tokens: TokenMap): Measurement[] => {
  return PAIRINGS.flatMap((pairing) => {return measure(pairing, tokens)})
}

/**
 * Reads the rows of the README's "Dark theme" contrast table.
 * @returns The first cell of every data row, header and separator excluded.
 */
const readmeRows = (): string[] => {
  const lines = readFileSync(DESIGN_README, 'utf8').split('\n')
  const start = lines.findIndex((line) => {return line.trim() === '### Dark theme'})
  if (start < 0) {
    throw new Error('README section 3 has no "### Dark theme" heading; the table this spec checks moved')
  }
  const rows: string[] = []
  let inTable = false
  for (const line of lines.slice(start + 1)) {
    if (line.startsWith('|')) {
      inTable = true
      rows.push(line.split('|')[1].trim())
    } else if (inTable) {
      break
    }
  }
  // The first two lines of a markdown table are the header and its separator.
  return rows.slice(2)
}

/** The tokens of both themes, read once from the stylesheet the panel ships. */
const THEMES: ThemeTokens = readThemeTokens(readFileSync(MAIN_CSS, 'utf8'))

test.describe('design token contrast, recomputed from main.css', () => {
  test('every pairing in the dark theme clears its WCAG line, known failures held at their floor', () => {
    const measurements = measureAll(THEMES.dark)
    // Vacuity guard on the axis that can go blind: the measurement list itself. If the pairings
    // were all dropped, the empty problem list below would be a pass over nothing.
    expect(measurements.length).toBeGreaterThanOrEqual(PAIRINGS.length)
    expect(problemsOf('dark', measurements)).toEqual([])
  })

  test('every pairing in the light theme clears its WCAG line, known failures held at their floor', () => {
    const measurements = measureAll(THEMES.light)
    expect(measurements.length).toBeGreaterThanOrEqual(PAIRINGS.length)
    expect(problemsOf('light', measurements)).toEqual([])
  })

  test('the light theme measures different colours from the dark one', () => {
    // The light map is the dark map overlaid by the light block. If the overlay silently matched
    // nothing, the light test above would re-measure the dark theme and certify it twice.
    expect(THEMES.light.get('s1')).not.toBe(THEMES.dark.get('s1'))
    expect(THEMES.light.get('t1')).not.toBe(THEMES.dark.get('t1'))
    expect(THEMES.light.get('acf')).toBe(THEMES.dark.get('acf'))
  })

  test('the rules cover exactly the rows of the README table, one rule per row or per half-row', () => {
    const rows = readmeRows()
    const covered = new Set<string>()
    for (const pairing of PAIRINGS) {
      const row = rows.find((cell) => {return cell.includes(pairing.readme)})
      expect(row, `no README row contains "${pairing.readme}"`).toBeDefined()
      covered.add(row as string)
    }
    // Every README row has a rule, and no rule points at a row that is gone. A row added to the
    // README without a rule here (or the reverse) changes one of the two numbers.
    expect(covered.size).toBe(rows.length)
    expect([...covered].sort()).toEqual([...rows].sort())
  })

  test('a planted #2E7BFF under white text fails, and the shipped fill passes', () => {
    // Inverse control. A gate that refused everything would also "fail" the planted value, so the
    // shipped --acf must be accepted by the same code path in the same breath.
    const whiteOnFill = PAIRINGS.find((pairing) => {return pairing.readme === 'white on `--acf`'}) as Pairing
    const planted: TokenMap = new Map(THEMES.dark)
    planted.set('acf', '#2E7BFF')
    expect(problemsOf('dark', measure(whiteOnFill, planted))).toHaveLength(1)
    expect(problemsOf('dark', measure(whiteOnFill, THEMES.dark))).toEqual([])
  })

  test('a planted weakening of a held-at-the-line token is reported', () => {
    // --rd on --s2 is 4.502:1, a hair over the line. Nudging one channel must trip the check, which
    // shows the margin the README warns about is observed rather than assumed.
    const errorOnRaised = PAIRINGS.find((pairing) => {return pairing.readme === '`--rd` on `--s2`'}) as Pairing
    const planted: TokenMap = new Map(THEMES.dark)
    planted.set('rd', '#E4484D')
    expect(problemsOf('dark', measure(errorOnRaised, planted))).toHaveLength(1)
    expect(problemsOf('dark', measure(errorOnRaised, THEMES.dark))).toEqual([])
  })

  test('the reader takes values from rules, not from comments, and overlays the light block', () => {
    // Positive control for the parser: a decoy hex in a comment must not become a token, and the
    // light rule must override only what it names.
    const css = [
      '/* --s1: #ff0000; decoy */',
      ":root,\nhtml[data-theme='dark'] {\n  --s1: #101216; /* a panel */\n  --t1: #e9ebee;\n}",
      "html[data-theme='light'] {\n  --s1: #ffffff;\n}",
    ].join('\n')
    const read = readThemeTokens(css)
    expect(read.dark.get('s1')).toBe('#101216')
    expect(read.light.get('s1')).toBe('#ffffff')
    expect(read.light.get('t1')).toBe('#e9ebee')
  })
})
