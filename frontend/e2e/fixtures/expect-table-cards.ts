import { expect, type Page } from '@playwright/test'

/** Whether the tables on a screen carry a row-actions column. */
export type CardActions = 'actions' | 'none'

/**
 * Asserts that every table in the main region is drawn as cards at a phone width (issue #217): the
 * layout is the card one, nothing in it scrolls sideways, every labelled line shows its column's
 * heading, and every row's actions are fully on screen without any horizontal scroll.
 *
 * The vacuity guards are on the axes that can go blind: at least one table, at least one labelled
 * line, and — for a screen declared to have actions — at least one actions cell, so a table that
 * stopped rendering cards (or lost its actions column) fails here instead of passing over nothing.
 * @param page The page, with its tables loaded.
 * @param actions Whether the screen's tables have a row-actions column.
 * @returns Resolves once every table has been measured.
 */
export const expectTablesAsCards = async (page: Page, actions: CardActions): Promise<void> => {
  const report = await page.getByRole('main').evaluate((main) => {
    const scrollers = [...main.querySelectorAll<HTMLElement>('[data-testid="table-scroller"]')]
    const viewport = window.innerWidth
    const headings = new Set(
      [...main.querySelectorAll('th')].map((heading) => {
        return (heading.textContent ?? '').trim()
      }),
    )
    const labels = [...main.querySelectorAll<HTMLElement>('td[data-placement="line"] > span[aria-hidden="true"]')]
    const actionCells = [...main.querySelectorAll<HTMLElement>('td[data-placement="actions"]')]
    return {
      tables: scrollers.length,
      layouts: scrollers.map((scroller) => {
        return scroller.dataset.layout ?? ''
      }),
      scrolling: scrollers
        .filter((scroller) => {
          return scroller.scrollWidth > scroller.clientWidth + 1
        })
        .flatMap((scroller) => {
          // Name what pokes past the card's edge, so a failure says which value refused to wrap.
          const edge = scroller.getBoundingClientRect().right
          return [...scroller.querySelectorAll<HTMLElement>('tbody *')]
            .filter((element) => {
              return element.getBoundingClientRect().right > edge + 1
            })
            .slice(0, 3)
            .map((element) => {
              return `<${element.tagName.toLowerCase()} class="${element.className}"> "${(element.textContent ?? '').trim().slice(0, 30)}"`
            })
        }),
      labels: labels.length,
      hiddenLabels: labels
        .filter((label) => {
          return label.getBoundingClientRect().width === 0 || !headings.has((label.textContent ?? '').trim())
        })
        .map((label) => {
          return label.textContent ?? ''
        }),
      actionCells: actionCells.length,
      offscreenActions: actionCells
        .filter((cell) => {
          const box = cell.getBoundingClientRect()
          return box.width === 0 || box.left < -0.5 || box.right > viewport + 0.5
        })
        .map((cell) => {
          return Math.round(cell.getBoundingClientRect().right)
        }),
    }
  })

  expect(report.tables, 'the screen draws at least one table').toBeGreaterThan(0)
  expect(report.layouts, 'every table is drawn as cards').toEqual(Array<string>(report.tables).fill('cards'))
  expect(report.scrolling, 'no card table scrolls sideways').toEqual([])
  expect(report.labels, 'the cards label their lines').toBeGreaterThan(0)
  expect(report.hiddenLabels, 'every line label is drawn and is its column heading').toEqual([])
  if (actions === 'actions') {
    expect(report.actionCells, 'the rows carry their actions').toBeGreaterThan(0)
  }
  expect(report.offscreenActions, 'every row action is fully on screen').toEqual([])
}
