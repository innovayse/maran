import type { InjectionKey, Ref } from 'vue'

/**
 * What a {@link UiLoadingRegion} provides to every `UiSkeleton` inside it: whether the wait has
 * lasted long enough for placeholders to be shown. Until it is `true` they are mounted, holding the
 * space the content will take, but invisible, so an answer that arrives at once causes neither a
 * flash nor a shift. A skeleton outside any region is always visible.
 */
export const SKELETON_VISIBLE_KEY: InjectionKey<Readonly<Ref<boolean>>> = Symbol('skeleton-visible')
