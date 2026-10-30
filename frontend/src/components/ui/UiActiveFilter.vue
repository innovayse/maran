<script setup lang="ts">
/**
 * The line that says a list is narrowed and offers the way back. A list opened from a link that
 * carries a filter in its URL, or narrowed in its filter sheet, must say so, or the operator reads
 * a short list as the whole of it.
 *
 * Two forms, one component:
 *
 * - **A sentence**: `text` ("Only alpha"), a separator and the clearing action ("Show all"), for a
 *   page narrowed by one URL filter.
 * - **Chips**: one removable chip per applied filter (`chips`), then the clearing action ("Clear
 *   all"). Each chip is a button whose accessible name says what pressing it removes; it emits
 *   `remove` with the chip's key, and the clearing action emits `clear`.
 *
 * A kit component rather than markup repeated on every list page, so the wording's layout, the
 * separator and the clearing action look and announce the same everywhere. Every text is a prop,
 * already translated by the caller.
 */
import UiButton from './UiButton.vue'
import UiIcon from './UiIcon.vue'
import type { ActiveFilterChip } from '../../types/list'

/** Props accepted by {@link UiActiveFilter}. */
withDefaults(
  defineProps<{
    /** What the list is narrowed to, already translated ("Only alpha"); empty when chips say it. */
    text?: string
    /** The applied filters as removable chips; empty for the sentence form. */
    chips?: readonly ActiveFilterChip[]
    /** The clearing action's label, already translated ("Show all", "Clear all"). */
    clearLabel: string
  }>(),
  { text: '', chips: () => {return []} },
)

/** Events emitted by {@link UiActiveFilter}. */
const emit = defineEmits<{
  /** The operator asked for the unfiltered list. */
  (e: 'clear'): void
  /** The operator removed one chip's filter. */
  (e: 'remove', key: string): void
}>()

/**
 * Forwards the clearing action to the page, which owns the URL.
 * @returns Nothing.
 */
const clear = (): void => {
  emit('clear')
}

/**
 * Forwards one chip's removal to the page.
 * @param key The removed filter's name.
 * @returns Nothing.
 */
const remove = (key: string): void => {
  emit('remove', key)
}
</script>

<template>
  <div class="flex flex-wrap items-center gap-x-3 gap-y-2" data-testid="active-filter">
    <template v-if="text !== ''">
      <span class="text-sm text-text-secondary wrap-anywhere">{{ text }}</span>
      <span class="text-sm text-text-muted" aria-hidden="true">·</span>
    </template>
    <ul v-if="chips.length > 0" class="flex min-w-0 flex-wrap items-center gap-2" data-testid="active-filter-chips">
      <li v-for="chip in chips" :key="chip.key" class="min-w-0">
        <UiButton variant="secondary" :aria-label="chip.removeLabel" @click="remove(chip.key)">
          <span class="min-w-0 wrap-anywhere">{{ chip.text }}</span>
          <UiIcon name="x" size="sm" />
        </UiButton>
      </li>
    </ul>
    <UiButton variant="secondary" @click="clear">{{ clearLabel }}</UiButton>
  </div>
</template>
