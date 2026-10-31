import type { InjectionKey, Ref } from 'vue'

/**
 * What the authenticated shell provides to every `UiPageHeading` inside it: the small accent line
 * over the page title, which names the navigation group the screen sits in. It is derived from the
 * navigation (`utils/locateNavigation.ts`) rather than written by each page, so a page cannot name
 * a group the sidebar does not draw it under (issue #209). A heading outside the shell draws none.
 */
export const PAGE_EYEBROW_KEY: InjectionKey<Readonly<Ref<string>>> = Symbol('page-eyebrow')
