import { expect, type Locator } from '@playwright/test'

/** The box of one toolbar control, in CSS pixels. */
export interface ControlBox {
  /** The control's accessible-ish description, for the failure message. */
  name: string
  /** Distance from the top of the document. */
  top: number
  /** The box's height. */
  height: number
  /** The vertical centre. */
  centre: number
}

/**
 * Reads the box of every direct child of a toolbar.
 * @param toolbar The toolbar's locator.
 * @returns One box per control, in document order.
 */
export const measureToolbar = async (toolbar: Locator): Promise<ControlBox[]> => {
  return toolbar.locator(':scope > *').evaluateAll((elements) => {
    return elements.map((element) => {
      const rect = element.getBoundingClientRect()
      const scrollTop = window.scrollY
      return {
        name: (element.textContent ?? '').trim().slice(0, 24) || element.tagName.toLowerCase(),
        top: rect.top + scrollTop,
        height: rect.height,
        centre: rect.top + scrollTop + rect.height / 2,
      }
    })
  })
}

/**
 * Asserts the kit's two toolbar promises: every control is the same height, and the controls that
 * share a line share its centre. Controls wrap onto further lines when the width runs out, so the
 * centre is compared inside each line (controls whose tops are within half a control height of
 * each other), not across lines.
 * @param boxes The measured controls.
 * @returns Nothing; throws through `expect` when a promise is broken.
 */
export const expectAligned = (boxes: readonly ControlBox[]): void => {
  expect(boxes.length).toBeGreaterThan(1)
  const heights = boxes.map((box) => {
    return Math.round(box.height)
  })
  expect(new Set(heights).size, `control heights ${JSON.stringify(boxes.map((b) => {return [b.name, b.height]}))}`).toBe(1)

  const lines: ControlBox[][] = []
  for (const box of boxes) {
    const line = lines.find((candidate) => {
      return Math.abs(candidate[0].top - box.top) < box.height / 2
    })
    if (line === undefined) {
      lines.push([box])
    } else {
      line.push(box)
    }
  }
  for (const line of lines) {
    const centres = line.map((box) => {
      return box.centre
    })
    const spread = Math.max(...centres) - Math.min(...centres)
    expect(spread, `centre lines ${JSON.stringify(line.map((b) => {return [b.name, b.centre]}))}`).toBeLessThanOrEqual(1)
  }
}
