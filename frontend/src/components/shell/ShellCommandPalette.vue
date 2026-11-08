<script setup lang="ts">
/**
 * Jump-to palette: type a few letters, land on a screen. Opened with the keyboard shortcut or
 * the app bar's search field.
 *
 * It searches two things: the navigation the panel already holds (the "Pages" group, matched in the
 * browser), and, from two typed characters on, what the panel's global search endpoint finds
 * (accounts, sites), shown as groups as they arrive. A group the server could not answer is a muted
 * note, never an error, and the pages stay usable. The typed text goes to the server in a request
 * body only, and the store holding the answer is emptied when the palette closes.
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
import UiIcon, { type UiIconName } from '../ui/UiIcon.vue'
import { useDebouncedSearch } from '../../composables/useDebouncedSearch'
import { useSearchStore } from '../../stores/search'
import { navigationLabelText } from '../../utils/navigationLabelText'
import { searchHitTarget } from '../../utils/searchHitTarget'
import type { NavigationEntry, NavigationIcon } from '../../types/navigation'
import type { SearchHit, SearchKind } from '../../types/search'

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

/** The fewest characters worth sending to the server; the endpoint refuses anything shorter. */
const MINIMUM_SERVER_TERM_LENGTH = 2

/** How long typing must pause before the term is sent, in milliseconds. */
const SEARCH_PAUSE_MS = 200

/** One selectable row of the palette, whatever it opens. */
interface PaletteRow {
  /** The DOM id of the row's option. */
  id: string
  /** The icon drawn before the title. */
  icon: UiIconName
  /** The main line. */
  title: string
  /** The second line, or empty. */
  subtitle: string
  /** Text drawn at the end of the row (a locked page's badge), or empty. */
  badge: string
  /** Opens what the row stands for. */
  open: () => Promise<void>
}

/** A headed run of rows. */
interface PaletteSection {
  /** Stable key, also the heading id suffix. */
  key: string
  /** The translated heading. */
  label: string
  /** The rows of the section. */
  rows: PaletteRow[]
  /** A muted note shown instead of rows when the section could not be read, or empty. */
  note: string
}

const { t } = useI18n()
const router = useRouter()
const searchStore = useSearchStore()

/** What the user has typed. */
const query: Ref<string> = ref('')

/**
 * Id of the row the keyboard chose, or null while it has chosen none (a fresh open, a new query).
 * The selection is kept by id, not by position: the server's answer arrives after the pages are
 * already listed and rebuilds {@link rows}, and a position would then point at another row (or be
 * reset to the first, scrolling the list back up under a user who had walked to its end).
 */
const chosenId: Ref<string | null> = ref(null)

/** Id of the listbox, so the search field can name what it controls (`aria-controls`). */
const listId: string = useId()

/** The listbox element, searched for the active row so it can be scrolled into view. */
const listElement: Ref<HTMLElement | null> = ref(null)

/** The search field, focused when the palette opens. */
const searchField: Ref<InstanceType<typeof UiSearchInput> | null> = ref(null)

/**
 * Every destination the palette offers: each entry followed by its submenu children (issue #195),
 * so "Schedule" or "Grant repair" can be jumped to directly. A child whose label repeats its
 * parent's landing page still appears, because its own label is what someone types. A page that is
 * not built yet is left out (issue #311): a search result is something to open, and it has nowhere
 * to go.
 */
const destinations: ComputedRef<readonly NavigationEntry[]> = computed(() => {
  return props.entries
    .flatMap((entry): NavigationEntry[] => {
      return [entry, ...entry.children]
    })
    .filter((entry): boolean => {
      return entry.state.kind !== 'planned'
    })
})

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
    return destinations.value
  }

  return destinations.value.filter((entry) => {
    return entryLabel(entry).toLocaleLowerCase().includes(needle)
  })
})

/**
 * The palette's content: the matching pages, then one section per group the server answered with.
 * An empty ready group is left out; an unavailable one stays as a muted note.
 */
const sections: ComputedRef<PaletteSection[]> = computed(() => {
  const result: PaletteSection[] = []

  if (matches.value.length > 0) {
    result.push({
      key: 'pages',
      label: t('search.pages'),
      note: '',
      rows: matches.value.map((entry): PaletteRow => {
        return {
          id: `palette-option-${entry.key}`,
          icon: entry.icon,
          title: entryLabel(entry),
          subtitle: '',
          badge: entry.state.kind === 'locked' ? t('app.nav.lockedBadge') : '',
          open: (): Promise<void> => {
            return choose(entry)
          },
        }
      }),
    })
  }

  for (const group of searchStore.groups) {
    const label = t(`search.groups.${group.kind}`)
    if (group.state === 'unavailable') {
      result.push({ key: group.kind, label, rows: [], note: t('search.unavailable', { group: label }) })
    } else if (group.hits.length > 0) {
      result.push({
        key: group.kind,
        label,
        note: '',
        rows: group.hits.map((hit): PaletteRow => {
          return {
            id: `palette-option-${hit.kind}-${hit.id}`,
            icon: iconOfHit(hit.kind),
            title: hit.title,
            subtitle: hit.subtitle,
            badge: '',
            open: (): Promise<void> => {
              return chooseHit(hit)
            },
          }
        }),
      })
    }
  }

  return result
})

/** Every selectable row in display order: what the arrow keys walk. */
const rows: ComputedRef<PaletteRow[]> = computed(() => {
  return sections.value.flatMap((section): PaletteRow[] => {
    return section.rows
  })
})

/** Index of the row the keyboard is on, into {@link rows}: the chosen row, else the first. */
const activeIndex: ComputedRef<number> = computed(() => {
  const index = rows.value.findIndex((row): boolean => {
    return row.id === chosenId.value
  })
  return Math.max(index, 0)
})

/** Id of the row the keyboard is on, or undefined when nothing matches. */
const activeOptionId: ComputedRef<string | undefined> = computed(() => {
  return rows.value[activeIndex.value]?.id
})

/** The sentence a screen reader hears when the result count changes. */
const statusText: ComputedRef<string> = computed(() => {
  if (searchStore.loading) {
    return t('search.loading')
  }

  return t('search.count', rows.value.length)
})

/**
 * The text shown for an entry: the panel's own screens carry an i18n key, a
 * module carries the display name the backend localized.
 * @param entry The entry to name.
 * @returns The label to render and to search against.
 */
const entryLabel = (entry: NavigationEntry): string => {
  return navigationLabelText(entry.label, t)
}

/**
 * Navigates to an entry and closes the palette.
 * @param entry The entry the user chose.
 * @returns Resolves once navigation has been dispatched.
 */
const choose = async (entry: NavigationEntry): Promise<void> => {
  emit('close')
  // Planned pages are filtered out of the list, so every row the user can choose has a target.
  if (entry.state.kind !== 'planned') {
    await router.push(entry.state.target)
  }
}

/**
 * The icon a kind of server hit is drawn with.
 * @param kind Which kind of hit it is.
 * @returns The icon name.
 */
const iconOfHit = (kind: SearchKind): NavigationIcon => {
  return kind === 'account' ? 'users' : 'globe'
}

/**
 * Opens a server hit and closes the palette.
 * @param hit The hit the user chose.
 * @returns Resolves once navigation has been dispatched.
 */
const chooseHit = async (hit: SearchHit): Promise<void> => {
  emit('close')
  await router.push(searchHitTarget(hit))
}

/**
 * Sends the typed text to the server once typing pauses, or drops the server answer when the text
 * is too short to search.
 * @param text The text as typed.
 * @returns Nothing.
 */
const sendTerm = (text: string): void => {
  const term = text.trim()
  if (term.length < MINIMUM_SERVER_TERM_LENGTH) {
    searchStore.clear()
    return
  }

  void searchStore.search(term)
}

const pause = useDebouncedSearch(sendTerm, SEARCH_PAUSE_MS)

/**
 * Moves the keyboard selection, clamped to the ends of the list.
 * @param step 1 to move down, -1 to move up.
 * @returns Nothing; the active index updates synchronously.
 */
const moveSelection = (step: number): void => {
  const last = rows.value.length - 1
  if (last < 0) {
    return
  }

  chosenId.value = rows.value[Math.min(Math.max(activeIndex.value + step, 0), last)]?.id ?? null
}

/**
 * Opens the currently selected entry.
 * @returns Resolves once navigation has been dispatched, or immediately when
 * nothing matches.
 */
const chooseActive = async (): Promise<void> => {
  const row = rows.value[activeIndex.value]
  if (row !== undefined) {
    await row.open()
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
      // Nothing typed here is kept: the term and the answer for it go when the palette closes.
      pause.reset()
      searchStore.clear()
      return
    }

    query.value = ''
    chosenId.value = null
    await nextTick()
    // `UiModal`'s focus trap moves focus to the panel's first focusable element (its Close
    // button) on the same tick, and it runs after this watcher, so focusing here alone lost the
    // race and typing went nowhere (issue #208). Waiting one macrotask lets the trap finish first;
    // the field is then focused last, which is what a palette is for.
    await new Promise<void>((resolve): void => {
      window.setTimeout(resolve, 0)
    })
    if (props.open) {
      searchField.value?.focus()
    }
  },
)

// The list scrolls inside `max-h-72`, and the keyboard moves a selection that
// focus does not follow, so without this the active row walks out of view.
// `flush: 'post'` waits for the re-render, and `block: 'nearest'` moves the list
// only as far as needed instead of recentring on every key press.
watch(
  activeOptionId,
  (id: string | undefined): void => {
    if (id !== undefined) {
      listElement.value?.querySelector(`[id="${id}"]`)?.scrollIntoView({ block: 'nearest' })
    }
  },
  { flush: 'post' },
)

// Every keystroke restarts the pause; the term reaches the server only when typing stops. A new
// term also starts the selection over: the list it narrows is not the one the user walked.
watch(query, (text: string): void => {
  chosenId.value = null
  pause.type(text)
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
        :aria-controls="rows.length > 0 ? listId : undefined"
        :aria-expanded="rows.length > 0"
        :aria-activedescendant="activeOptionId"
      />

      <!-- Announces the result count when it changes, so a screen reader hears data arrive. -->
      <p class="sr-only" aria-live="polite">{{ statusText }}</p>

      <!-- role=listbox with an active descendant, not a list of buttons: the
           focus stays in the search field while the arrow keys move the
           selection, which is the pattern a palette needs. The field is the
           combobox that owns it (`aria-controls` / `aria-activedescendant`).
           Rows are grouped under headings; a group is a `role=group` named by its heading. -->
      <ul
        v-if="rows.length > 0"
        :id="listId"
        ref="listElement"
        class="flex max-h-72 flex-col gap-1 overflow-y-auto"
        role="listbox"
        :aria-label="t('app.shell.paletteResultsLabel')"
      >
        <template v-for="section in sections" :key="section.key">
          <li v-if="section.rows.length > 0" role="presentation">
            <ul role="group" :aria-labelledby="`${listId}-${section.key}`" class="flex flex-col gap-1">
              <li :id="`${listId}-${section.key}`" role="presentation" class="px-3 pt-2 text-xs font-medium text-text-muted">
                {{ section.label }}
              </li>
              <li
                v-for="row in section.rows"
                :id="row.id"
                :key="row.id"
                role="option"
                :aria-selected="rows.indexOf(row) === activeIndex"
                class="flex cursor-pointer items-center gap-3 rounded-control px-3 py-2.5 text-sm font-medium"
                :class="
                  rows.indexOf(row) === activeIndex ? 'bg-accent-fill text-white' : 'text-text-primary hover:bg-surface-3'
                "
                @click="row.open()"
              >
                <UiIcon :name="row.icon" size="lg" />
                <span class="min-w-0 flex-1">
                  <span class="block truncate">{{ row.title }}</span>
                  <span
                    v-if="row.subtitle !== ''"
                    class="block truncate text-xs font-normal"
                    :class="rows.indexOf(row) === activeIndex ? 'text-white' : 'text-text-muted'"
                  >
                    {{ row.subtitle }}
                  </span>
                </span>
                <span
                  v-if="row.badge !== ''"
                  class="font-display text-xs font-extrabold"
                  :class="rows.indexOf(row) === activeIndex ? 'text-white' : 'text-text-muted'"
                >
                  {{ row.badge }}
                </span>
              </li>
            </ul>
          </li>
        </template>
      </ul>

      <p v-else-if="!searchStore.loading" class="px-2 py-6 text-center text-sm text-text-muted">
        {{ t('app.shell.paletteNoMatches') }}
      </p>

      <!-- A group the server could not answer: a muted note, the other groups stay usable. -->
      <p
        v-for="section in sections.filter((candidate) => candidate.note !== '')"
        :key="`note-${section.key}`"
        class="px-3 text-sm text-text-muted"
      >
        {{ section.note }}
      </p>

      <p v-if="searchStore.failed" class="px-3 text-sm text-text-muted">{{ t('search.failed') }}</p>
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
