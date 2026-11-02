import type { Page } from '@playwright/test'
import type { Favourites } from '../../src/types/favourite'

/** The pinned-pages endpoint, with or without a key after it. */
const PINNED_PAGES_PATTERN = /\/api\/v1\/me\/pinned-pages(\/[^/?]+)?(\?.*)?$/

/**
 * What the stubbed panel holds for one person. Shared between pages on purpose: a second browser
 * opened on the same state is the same person on another device, which is what "pins follow the
 * person" asserts.
 */
export interface PinnedPagesState {
  /** The pinned keys, in pin order. */
  keys: string[]
  /** The most pages the stubbed panel lets the person pin. */
  limit: number
}

/**
 * Builds the answer every pinned-pages call gives: the list after the call.
 * @param state What the panel holds.
 * @returns The body, in the shape of `Favourites`.
 */
const answer = (state: PinnedPagesState): Favourites => {
  return {
    items: state.keys.map((itemKey, position) => {
      return { itemKey, position }
    }),
    limit: state.limit,
  }
}

/**
 * Fulfils `GET`, `POST` and `DELETE /api/v1/me/pinned-pages` from an in-memory state the way the
 * panel does: a duplicate or a pin beyond the limit is a 409, unpinning what is not pinned a 404,
 * and every success answers with the list after the change.
 * @param page The Playwright page whose network the route is installed on.
 * @param state What the panel holds; mutated by pins and unpins.
 * @returns Resolves once the route is installed.
 */
export const stubPinnedPages = async (page: Page, state: PinnedPagesState): Promise<void> => {
  await page.route(PINNED_PAGES_PATTERN, async (route) => {
    const request = route.request()
    const refuse = async (status: number, title: string): Promise<void> => {
      await route.fulfill({
        status,
        contentType: 'application/problem+json',
        body: JSON.stringify({ title, status }),
      })
    }

    if (request.method() === 'POST') {
      const itemKey = (request.postDataJSON() as { itemKey: string }).itemKey
      if (state.keys.includes(itemKey)) {
        await refuse(409, 'That page is already pinned.')
        return
      }
      if (state.keys.length >= state.limit) {
        await refuse(409, 'You have pinned as many pages as you can.')
        return
      }
      state.keys.push(itemKey)
    } else if (request.method() === 'DELETE') {
      const itemKey = decodeURIComponent(new URL(request.url()).pathname.split('/').pop() ?? '')
      if (!state.keys.includes(itemKey)) {
        await refuse(404, 'That page is not pinned.')
        return
      }
      state.keys = state.keys.filter((key) => {
        return key !== itemKey
      })
    }

    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(answer(state)) })
  })
}
