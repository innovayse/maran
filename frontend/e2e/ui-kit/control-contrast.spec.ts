import { contrastRatio, type Rgba } from '../fixtures/contrast-ratio'
import { seriousViolations } from '../fixtures/expect-no-serious-violations'
import { expect, test } from '../fixtures/guarded-test'
import type { Locator, Page } from '@playwright/test'

// Issue #543: a control must be findable (its edge, WCAG 1.4.11) and its keyboard focus must be
// visible (2.4.7, 1.4.11), in both themes. `token-contrast.spec.ts` measures the tokens from
// `main.css`; this spec measures what the browser actually paints on the real kit components
// (`e2e/fixtures/control-contrast-harness.html`), so a component that reaches for the decorative
// `--b1`/`--b2`, or drops its ring with `outline: none` and no replacement, fails here even though
// every token is fine.

/** The ratio WCAG 1.4.11 asks of a control's boundary and of a focus indicator. */
const LINE = 3

/** The themes the panel ships. */
const THEMES = ['dark', 'light'] as const

/**
 * One kit control: how to find the element that DRAWS its edge and whether that edge is what
 * identifies it (a filled or borderless button is identified by its fill or its text instead).
 */
interface Control {
  /** Name used in failure messages. */
  name: string
  /** Finds the focusable element. */
  find: (page: Page) => Locator
  /** Finds the element that paints the edge and the focus ring when it is not the focusable one. */
  paint?: (focusable: Locator) => Locator
  /** True when the border alone says "this is a control", so it must reach 3:1. */
  edgeIdentifies: boolean
}

/** Every kit control the harness mounts. */
const CONTROLS: Control[] = [
  { name: 'UiInput', find: (page): Locator => {return page.getByRole('textbox', { name: 'Name' })}, edgeIdentifies: true },
  { name: 'UiSelect', find: (page): Locator => {return page.getByRole('combobox', { name: 'Kind' })}, edgeIdentifies: true },
  {
    name: 'UiCheckbox',
    find: (page): Locator => {return page.getByRole('checkbox', { name: 'Agree' })},
    paint: (input): Locator => {return input.locator('xpath=following-sibling::span[1]')},
    edgeIdentifies: true,
  },
  {
    name: 'UiRadio',
    find: (page): Locator => {return page.getByRole('radio', { name: 'Choice' })},
    paint: (input): Locator => {return input.locator('xpath=following-sibling::span[1]')},
    edgeIdentifies: true,
  },
  { name: 'UiSwitch', find: (page): Locator => {return page.getByRole('switch', { name: 'Enabled' })}, edgeIdentifies: true },
  { name: 'UiButton secondary', find: (page): Locator => {return page.getByRole('button', { name: 'Secondary' })}, edgeIdentifies: true },
  { name: 'UiButton field', find: (page): Locator => {return page.getByRole('button', { name: 'Field' })}, edgeIdentifies: true },
  { name: 'UiButton destructive', find: (page): Locator => {return page.getByRole('button', { name: 'Destructive' })}, edgeIdentifies: true },
  { name: 'UiButton primary', find: (page): Locator => {return page.getByRole('button', { name: 'Primary' })}, edgeIdentifies: false },
  { name: 'UiButton ghost', find: (page): Locator => {return page.getByRole('button', { name: 'Ghost' })}, edgeIdentifies: false },
]

/** What the browser reports for a painted control, as raw computed strings. */
interface Painted {
  /** Computed `border-top-color` of the painting element. */
  border: string
  /** Computed `box-shadow` of the painting element while it holds keyboard focus. */
  focusShadow: string
  /** Computed `outline-style` while focused. */
  focusOutline: string
  /** Computed `border-top-color` while focused. */
  focusBorder: string
  /** Computed background of the panel the control sits on. */
  surface: string
}

/**
 * Parses a computed colour.
 * @param value A computed `rgb(r, g, b)` or `rgba(r, g, b, a)` string.
 * @returns The channels.
 */
const rgba = (value: string): Rgba => {
  const n = value.match(/[\d.]+/g)?.map(Number) ?? []
  expect(n.length, `unreadable colour "${value}"`).toBeGreaterThanOrEqual(3)
  return { r: n[0], g: n[1], b: n[2], a: n[3] ?? 1 }
}

/**
 * Splits a computed `box-shadow` into its colours, in paint order (the first listed is on top).
 * @param shadow The computed value.
 * @returns One colour per layer, empty for `none`.
 */
const shadowColours = (shadow: string): Rgba[] => {
  return [...shadow.matchAll(/rgba?\([^)]*\)/g)].map((m) => {return rgba(m[0])})
}

/**
 * Tabs to a control and reads how it is painted at rest and in focus.
 * @param page The harness page.
 * @param control The control.
 * @returns The computed styles.
 */
const paintOf = async (page: Page, control: Control): Promise<Painted> => {
  const focusable = control.find(page)
  const painter = control.paint === undefined ? focusable : control.paint(focusable)
  await page.mouse.move(0, 0)
  const read = (): Promise<{ border: string; shadow: string; outline: string }> => {
    return painter.evaluate((el) => {
      const style = getComputedStyle(el)
      return { border: style.borderTopColor, shadow: style.boxShadow, outline: style.outlineStyle }
    })
  }
  const rest = await read()
  // Keyboard focus, not a click: `:focus-visible` is what the kit styles, and a click on a text
  // field would match it for the wrong reason.
  await page.evaluate(() => {(document.activeElement as HTMLElement | null)?.blur()})
  for (let step = 0; step < CONTROLS.length + 2; step += 1) {
    await page.keyboard.press('Tab')
    if (await focusable.evaluate((el) => {return el === document.activeElement})) {
      break
    }
  }
  await expect(focusable).toBeFocused()
  const focused = await read()
  const surface = await page.getByRole('region', { name: 'Kit controls' }).evaluate((el) => {return getComputedStyle(el).backgroundColor})
  return { border: rest.border, focusShadow: focused.shadow, focusOutline: focused.outline, focusBorder: focused.border, surface }
}

/**
 * Opens the harness in a theme.
 * @param page The Playwright page.
 * @param theme The theme.
 * @returns Resolves once the controls are drawn.
 */
const open = async (page: Page, theme: string): Promise<void> => {
  await page.goto(`/e2e/fixtures/control-contrast-harness.html?theme=${theme}`)
  await expect(page.getByRole('region', { name: 'Kit controls' })).toBeVisible()
}

for (const theme of THEMES) {
  test.describe(`kit control contrast, ${theme} theme`, () => {
    for (const control of CONTROLS) {
      test(`${control.name}: edge and focus indicator reach 3:1`, async ({ page }) => {
        await open(page, theme)
        const painted = await paintOf(page, control)
        const surface = rgba(painted.surface)
        // The surface must be opaque or every figure below measures against a guess.
        expect(surface.a).toBe(1)

        if (control.edgeIdentifies) {
          const edge = contrastRatio(rgba(painted.border), surface)
          expect(edge, `${control.name} border ${painted.border} on ${painted.surface}`).toBeGreaterThanOrEqual(LINE)
        }

        // The focus indicator: a shadow ring (outermost layer) or, for a text field, a solid accent
        // border. `outline: none` with neither is the defect.
        const layers = shadowColours(painted.focusShadow)
        // Computed shadows list the innermost layer first: the gap, then the ring outside it.
        const ring = layers.length > 0 ? layers[layers.length - 1] : rgba(painted.focusBorder)
        expect(
          layers.length > 0 || painted.focusOutline !== 'none',
          `${control.name} has no focus ring: box-shadow ${painted.focusShadow}, outline ${painted.focusOutline}`,
        ).toBe(true)
        // Against the page the ring sits on, and against the gap between it and the control.
        expect(contrastRatio(ring, surface), `${control.name} focus ring on the panel`).toBeGreaterThanOrEqual(LINE)
        if (layers.length > 1) {
          expect(contrastRatio(layers[0], ring), `${control.name} focus ring against its gap`).toBeGreaterThanOrEqual(LINE)
        }
      })
    }

    test('axe finds no serious violation on the controls', async ({ page }) => {
      await open(page, theme)
      expect(await seriousViolations(page)).toEqual([])
    })
  })
}

test('control: the decorative border is below the line, so the measurement can fail', async ({ page }) => {
  // Inverse control. Paint a box with the decorative `--b2` and measure it the way the cases above
  // do: it must come out under 3:1 in both themes, or the assertion above is a pass over nothing.
  for (const theme of THEMES) {
    await open(page, theme)
    const ratio = await page.evaluate(() => {
      const probe = document.createElement('div')
      probe.style.border = '1px solid var(--b2)'
      document.body.append(probe)
      return { border: getComputedStyle(probe).borderTopColor, surface: getComputedStyle(document.querySelector('section') as Element).backgroundColor }
    })
    expect(contrastRatio(rgba(ratio.border), rgba(ratio.surface))).toBeLessThan(LINE)
  }
})
