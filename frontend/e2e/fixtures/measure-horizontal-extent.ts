import { expect, type Locator, type Page } from '@playwright/test'

/** One element whose box leaves the viewport, described well enough to find it from a report. */
export interface Offender {
  /** Tag name and the first words of its text. */
  element: string
  /** Left edge in CSS pixels. */
  left: number
  /** Right edge in CSS pixels. */
  right: number
}

/**
 * Collects every element inside the main region whose box pokes out of the viewport and is not
 * contained by an ancestor (below the main region) that clips or scrolls its overflow. That
 * exemption is what lets a table scroll inside its own container without being reported, while
 * an unclipped long word, an over-wide card or a rigid grid is.
 * @param main The main region.
 * @returns The offending elements; an empty list means the region fits.
 */
export const findOverflowing = async (main: Locator): Promise<Offender[]> => {
  return main.evaluate((root: Element): Offender[] => {
    const limit = window.innerWidth
    const found: Offender[] = []

    for (const element of Array.from(root.querySelectorAll('*'))) {
      const rect = element.getBoundingClientRect()
      const style = window.getComputedStyle(element)
      if (
        rect.width === 0 ||
        rect.height === 0 ||
        style.visibility === 'hidden' ||
        style.display === 'none'
      ) {
        continue
      }
      if (rect.right <= limit + 0.5 && rect.left >= -0.5) {
        continue
      }

      let contained = false
      for (
        let ancestor = element.parentElement;
        ancestor !== null && ancestor !== root;
        ancestor = ancestor.parentElement
      ) {
        const overflowX = window.getComputedStyle(ancestor).overflowX
        if (overflowX !== 'visible') {
          contained = true
          break
        }
      }
      if (contained) {
        continue
      }

      const words = (element.textContent ?? '').trim().replace(/\s+/g, ' ').slice(0, 40)
      found.push({
        element: `${element.tagName.toLowerCase()} "${words}"`,
        left: Math.round(rect.left),
        right: Math.round(rect.right),
      })
    }

    return found
  })
}

/**
 * Asserts the screen as drawn now does not extend sideways, measured three ways because the
 * shell clips overflow with `overflow-x: hidden` on its main region, which makes any single probe
 * blind to a page that is silently cut off instead of scrolling:
 *
 * 1. the document does not scroll sideways (`scrollWidth <= innerWidth`);
 * 2. the main region does not hold content wider than itself (`scrollWidth <= clientWidth`, which
 *    still reports hidden overflow, unlike a scrollbar);
 * 3. no element inside the main region has a box poking out of the viewport, unless an ancestor
 *    below the main region deliberately clips or scrolls it.
 *
 * Waits for the web fonts first: a font swap changes line breaks, and measuring before it
 * settles would measure a transient.
 * @param page The page, with the screen's content already on screen.
 * @returns Resolves once all three measurements held.
 */
export const expectNoHorizontalOverflow = async (page: Page): Promise<void> => {
  await page.evaluate(() => {
    return document.fonts.ready
  })

  const main = page.getByRole('main')
  await expect(main).toBeVisible()

  const documentWidth = await page.evaluate(() => {
    return {
      scrollWidth: window.document.documentElement.scrollWidth,
      innerWidth: window.innerWidth,
    }
  })
  expect(documentWidth.scrollWidth, 'the document is no wider than the viewport').toBeLessThanOrEqual(
    documentWidth.innerWidth,
  )

  const region = await main.evaluate((element) => {
    return {
      scrollWidth: element.scrollWidth,
      clientWidth: element.clientWidth,
    }
  })
  expect(region.scrollWidth, 'the main region holds nothing wider than itself').toBeLessThanOrEqual(
    region.clientWidth,
  )

  expect(await findOverflowing(main), 'no element leaves the viewport').toEqual([])
}
