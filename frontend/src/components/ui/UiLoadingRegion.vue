<script setup lang="ts">
/**
 * The region of a screen whose content comes from the server: it says, once and politely, that the
 * content is loading and then that it has loaded, and decides when placeholders may be seen.
 *
 * **One announcement, not one per placeholder.** The region carries a single visually hidden
 * `role="status"` (`aria-live="polite"`) whose text is "Loading…" while `state` is `loading` and
 * "Loaded" once it is `loaded`, and the region is `aria-busy` meanwhile. Every {@link UiSkeleton}
 * inside is `aria-hidden`, so a screen reader hears the two words and nothing of the shapes.
 *
 * **The content draws its own skeleton.** The default slot is rendered while loading and once
 * loaded alike: a {@link UiTable} with `loading` keeps its real header over placeholder rows, a
 * {@link UiForm} or {@link UiDescriptionList} with `loading` keeps its real labels. A screen with no
 * such component (a figure tile, a card) renders its `UiSkeleton` shapes in the slot itself.
 *
 * **No flash, no shift.** The placeholders are mounted for the whole wait, so the space the content
 * will take is held from the first frame, but they stay invisible until the wait has lasted 150 ms
 * ({@link useDelayedFlag}). An answer faster than that shows no placeholder at all, and the content
 * lands in space that was already there.
 *
 * **An error is never a skeleton.** With `state` `error` the default slot is not rendered; the
 * `error` slot is (the page's `UiAlert`, which announces itself), so a failed load can never look
 * like one still in flight.
 */
import { computed, onMounted, provide, ref, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useDelayedFlag } from '../../composables/useDelayedFlag'
import { SKELETON_VISIBLE_KEY } from '../../utils/skeletonVisibleKey'
import type { LoadingState } from '../../types/loading'

/** Props accepted by {@link UiLoadingRegion}. */
const props = defineProps<{
  /** Where the content stands; see {@link LoadingState}. */
  state: LoadingState
}>()

/** Slots of {@link UiLoadingRegion}. */
defineSlots<{
  /** The content, rendered while loading (drawing its placeholders) and once loaded. */
  default?: () => unknown
  /** What a failed load shows instead of the content: the page's alert. */
  error?: () => unknown
}>()

const { t } = useI18n()

/**
 * Whether the region is in the document. The wait a reader sees starts when the region appears,
 * not when it is set up: a large screen can take a while between the two, and a timer started at
 * set-up would show the placeholders after a visible wait much shorter than the delay.
 */
const isMounted: Ref<boolean> = ref(false)

/** Whether the content is still on its way. */
const isLoading: ComputedRef<boolean> = computed(() => {
  return props.state === 'loading'
})

/** Whether the reader is waiting on the region: it is on screen and its content is loading. */
const isWaiting: ComputedRef<boolean> = computed(() => {
  return isMounted.value && isLoading.value
})

/** Whether the wait has lasted long enough for placeholders to be shown. */
const skeletonVisible = useDelayedFlag(isWaiting)

/** The status line's text; empty on an error, which the alert announces itself. */
const statusText: ComputedRef<string> = computed(() => {
  if (props.state === 'loading') {
    return t('common.loading.busy')
  }
  return props.state === 'loaded' ? t('common.loading.done') : ''
})

provide(SKELETON_VISIBLE_KEY, skeletonVisible)

onMounted(() => {
  isMounted.value = true
})
</script>

<template>
  <div :aria-busy="isLoading ? 'true' : undefined" data-testid="loading-region" :data-state="state">
    <p role="status" aria-live="polite" class="sr-only" data-testid="loading-status">{{ statusText }}</p>
    <slot v-if="state === 'error'" name="error" />
    <slot v-else />
  </div>
</template>
