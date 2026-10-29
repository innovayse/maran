<script setup lang="ts">
/**
 * Jump-to palette: type a few letters, land on a screen. Opened with the keyboard shortcut or
 * the app bar's search field.
 *
 * It searches the navigation the panel already holds — the modules the backend reported — and
 * nothing else. That is a deliberate limit rather than a first step: a palette that also
 * searched accounts, files or logs would need a search endpoint that does not exist, and a box
 * that silently finds nothing is worse than one whose scope is obvious.
 *
 * **The selected row is a solid `--acf` fill with white text**, the same language as the active
 * sidebar entry (5.14:1). A wash with accent text would depend on what is behind it: the modal's
 * surface is not a fixed step, and accent text on the wash over `--s3` is 4.40:1. A fill with
 * white text does not.
 *
 * The dialog itself, its focus trap and its Escape handling belong to `UiModal`; the command
 * palette spec pins that Escape closes it, which is the kit default for every dialog.
 */
import { computed, nextTick, ref, useId, watch, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRouter } from 'vue-router'
import UiModal from '../ui/UiModal.vue'
import UiSearchInput from '../ui/UiSearchInput.vue'
import UiIcon from '../ui/UiIcon.vue'
import type { NavigationEntry } from '../../types/navigation'

/** Props accepted by {@link ShellCommandPalette}. */
const props = defineProps<{
  /** Whether the palette is shown; owned by the shell. */
  open: boolean
  /** Navigation entries to search, exactly as the sidebar renders them. */
  entries: readonly NavigationEntry[]
}>()

/** Events emitted by {@link ShellCommandPalette}. */
const emit = defineEmits<{
  /** The palette asked to close — by Escape, the backdrop, or a chosen entry. */
  (event: 'close'): void
}>()

const { t } = useI18n()
const router = useRouter()

/** What the user has typed. */
const query: Ref<string> = ref('')

/** Index of the entry the keyboard is on, into {@link matches}. */
const activeIndex: Ref<number> = ref(0)

/** Id of the listbox, so the search field can name what it controls (`aria-controls`). */
const listId: string = useId()

/** The listbox element, searched for the active row so it can be scrolled into view. */
const listElement: Ref<HTMLElement | null> = ref(null)

/** The search field, focused when the palette opens. */
const searchField: Ref<InstanceType<typeof UiSearchInput> | null> = ref(null)

/**
 * Entries whose label contains the query, case-insensitively.
 *
 * Matching is a plain substring over the label the backend supplied, not a fuzzy
 * score: a fuzzy match on a list this short surprises more often than it helps,
 * and the label is the only text the panel can honestly claim to know.
 */
const matches: ComputedRef<readonly NavigationEntry[]> = computed(() => {
  const needle = query.value.trim().toLocaleLowerCase()
  if (needle === '') {
    return props.entries
  }

  return props.entries.filter((entry) => {
    return entryLabel(entry).toLocaleLowerCase().includes(needle)
  })
})

/** Id of the row the keyboard is on, or undefined when nothing matches. */
const activeOptionId: ComputedRef<string | undefined> = computed(() => {
  const entry = matches.value[activeIndex.value]
  return entry === undefined ? undefined : optionId(entry)
})

/**
 * The text shown for an entry: the panel's own screens carry an i18n key, a
 * module carries the display name the backend localized.
 * @param entry The entry to name.
 * @returns The label to render and to search against.
 */
const entryLabel = (entry: NavigationEntry): string => {
  return entry.labelKey === null ? (entry.label ?? '') : t(entry.labelKey)
}

/**
 * The DOM id of an entry's option row; the search field points at it with
 * `aria-activedescendant`, which is how a screen reader hears the selection
 * while focus never leaves the input.
 * @param entry The entry whose row to name.
 * @returns The id carried by that entry's `li`.
 */
const optionId = (entry: NavigationEntry): string => {
  return `palette-option-${entry.key}`
}

/**
 * Navigates to an entry and closes the palette.
 * @param entry The entry the user chose.
 * @returns Resolves once navigation has been dispatched.
 */
const choose = async (entry: NavigationEntry): Promise<void> => {
  emit('close')
  await router.push(entry.target)
}

/**
 * Moves the keyboard selection, clamped to the ends of the list.
 * @param step 1 to move down, -1 to move up.
 * @returns Nothing; the active index updates synchronously.
 */
const moveSelection = (step: number): void => {
  const last = matches.value.length - 1
  if (last < 0) {
    return
  }

  activeIndex.value = Math.min(Math.max(activeIndex.value + step, 0), last)
}

/**
 * Opens the currently selected entry.
 * @returns Resolves once navigation has been dispatched, or immediately when
 * nothing matches.
 */
const chooseActive = async (): Promise<void> => {
  const entry = matches.value[activeIndex.value]
  if (entry !== undefined) {
    await choose(entry)
  }
}

// A fresh open starts from an empty query with the first entry selected: a
// palette that reopens holding the last search makes the user delete it first.
watch(
  (): boolean => {
    return props.open
  },
  async (open: boolean): Promise<void> => {
    if (!open) {
      return
    }

    query.value = ''
    activeIndex.value = 0
    await nextTick()
    searchField.value?.focus()
  },
)

// The list scrolls inside `max-h-72`, and the keyboard moves a selection that
// focus does not follow, so without this the active row walks out of view.
// `flush: 'post'` waits for the re-render, and `block: 'nearest'` moves the list
// only as far as needed instead of recentring on every key press.
watch(
  activeIndex,
  (index: number): void => {
    listElement.value?.children[index]?.scrollIntoView({ block: 'nearest' })
  },
  { flush: 'post' },
)

// Typing narrows the list, so the previous selection can point past its end.
watch(matches, (): void => {
  activeIndex.value = 0
})
</script>

<template>
  <UiModal
    :open="open"
    :title="t('app.shell.paletteTitle')"
    :close-label="t('app.shell.paletteClose')"
    @close="emit('close')"
  >
    <div
      class="flex flex-col gap-3"
      @keydown.down.prevent="moveSelection(1)"
      @keydown.up.prevent="moveSelection(-1)"
      @keydown.enter.prevent="chooseActive"
    >
      <UiSearchInput
        ref="searchField"
        v-model="query"
        :label="t('app.shell.paletteFieldLabel')"
        :placeholder="t('app.shell.palettePlaceholder')"
        :clear-label="t('app.shell.paletteClear')"
        input-role="combobox"
        :aria-controls="matches.length > 0 ? listId : undefined"
        :aria-expanded="matches.length > 0"
        :aria-activedescendant="activeOptionId"
      />

      <!-- role=listbox with an active descendant, not a list of buttons: the
           focus stays in the search field while the arrow keys move the
           selection, which is the pattern a palette needs. The field is the
           combobox that owns it (`aria-controls` / `aria-activedescendant`). -->
      <ul
        v-if="matches.length > 0"
        :id="listId"
        ref="listElement"
        class="flex max-h-72 flex-col gap-1 overflow-y-auto"
        role="listbox"
        :aria-label="t('app.shell.paletteResultsLabel')"
      >
        <li
          v-for="(entry, index) in matches"
          :id="optionId(entry)"
          :key="entry.key"
          role="option"
          :aria-selected="index === activeIndex"
          class="flex cursor-pointer items-center gap-3 rounded-control px-3 py-2.5 text-sm font-medium"
          :class="
            index === activeIndex
              ? 'bg-accent-fill text-white'
              : 'text-text-primary hover:bg-surface-3'
          "
          @click="choose(entry)"
        >
          <UiIcon :name="entry.icon" size="lg" />
          <span class="min-w-0 flex-1 truncate">{{ entryLabel(entry) }}</span>
          <span
            v-if="entry.locked"
            class="font-display text-xs font-extrabold tracking-caps uppercase"
            :class="index === activeIndex ? 'text-white' : 'text-text-muted'"
          >
            {{ t('app.nav.lockedBadge') }}
          </span>
        </li>
      </ul>

      <p v-else class="px-2 py-6 text-center text-sm text-text-muted">
        {{ t('app.shell.paletteNoMatches') }}
      </p>
    </div>

    <!-- The design closes the palette with a hint band. Only the two gestures
         this palette actually implements are listed: a hint for a shortcut that
         does nothing would be worse than no hint at all. -->
    <template #footer>
      <span class="mr-auto flex items-center gap-4 text-sm text-text-muted">
        <span>{{ t('app.shell.paletteHintNavigate') }}</span>
        <span>{{ t('app.shell.paletteHintOpen') }}</span>
      </span>
    </template>
  </UiModal>
</template>
