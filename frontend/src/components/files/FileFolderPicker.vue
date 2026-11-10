<script setup lang="ts">
/**
 * Chooses a destination folder by walking the account's folders: the folder being looked at is the
 * choice, an Up button leaves it and each sub-folder opens it. Dumb: the listing comes in as a prop
 * and a wish to look elsewhere goes out as `browse`; which names exist is the panel's answer, and
 * where a move may land is the agent's decision, not this picker's.
 *
 * **A folder can hold thousands of sub-folders**, so the picker never shows the whole of it: the
 * panel sends a page at a time, the name search is the panel's (sent once typing pauses, as
 * `search`) and `more` asks for the next page, which the host appends to the listing.
 */
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import UiAlert from '../ui/UiAlert.vue'
import UiButton from '../ui/UiButton.vue'
import UiIcon from '../ui/UiIcon.vue'
import UiSearchInput from '../ui/UiSearchInput.vue'
import UiSpinner from '../ui/UiSpinner.vue'
import { useDebouncedSearch } from '../../composables/useDebouncedSearch'
import { childPath, parentPath } from '../../utils/filePath'
import { SEARCH_TERM_MAX_LENGTH } from '../../utils/searchTermLimit'
import type { FileEntry } from '../../types/file'

/** What the picker shows of the folder being looked at: the folders read so far and how the read stands. */
export interface FolderListing {
  /** The folders the panel listed inside the folder being looked at, across the pages read so far. */
  folders: readonly FileEntry[]
  /** Whether the first page of the listing is being read. */
  loading: boolean
  /** Whether a further page is being read while the folders already read stay on screen. */
  loadingMore: boolean
  /** Whether the panel holds further folders than the ones shown. */
  hasMore: boolean
  /** The panel's reason the listing failed, or `null`. */
  errorMessage: string | null
}

/** How long typing must pause before the name search is sent, in milliseconds. */
const SEARCH_DELAY_MS = 300

/** Props accepted by {@link FileFolderPicker}. */
const props = defineProps<{
  /** The folder being looked at, relative to the home; empty for the home. This is the choice. */
  path: string
  /** The listing of {@link path}. */
  listing: FolderListing
}>()

/** Events emitted by {@link FileFolderPicker}. */
const emit = defineEmits<{
  /** The person wants to look inside another folder. */
  (e: 'browse', path: string): void
  /** The name search of the folder being looked at changed (empty clears it). */
  (e: 'search', term: string): void
  /** The person asked for the next page of folders. */
  (e: 'more'): void
}>()

const { t } = useI18n()

/** The name search box: its text is sent once typing pauses, unless it is too long. */
const search = useDebouncedSearch(
  (term: string): void => {
    emit('search', term)
  },
  SEARCH_DELAY_MS,
  true,
)

/** The message under the search box while its text is longer than the list accepts. */
const searchError: ComputedRef<string | null> = computed(() => {
  return search.tooLong.value ? t('common.list.searchTooLong', { max: SEARCH_TERM_MAX_LENGTH }) : null
})

/** How the folder being looked at is named: the home reads as a word, any other as its path. */
const currentLabel: ComputedRef<string> = computed(() => {
  return props.path === '' ? t('filesDialogs.picker.home') : props.path
})

/**
 * Looks inside another folder; the search belongs to the folder it was typed in, so it is cleared.
 * @param next The folder to look inside.
 * @returns Nothing.
 */
const browse = (next: string): void => {
  search.reset()
  emit('browse', next)
}
</script>

<template>
  <div class="flex flex-col gap-2" role="group" :aria-label="t('filesDialogs.picker.label')">
    <div class="flex flex-wrap items-center gap-2">
      <UiButton variant="secondary" :disabled="path === ''" @click="browse(parentPath(path))">
        <UiIcon name="chevronUp" size="sm" />{{ t('filesDialogs.picker.up') }}
      </UiButton>
      <p class="min-w-0 text-sm wrap-anywhere" data-testid="picker-current">
        {{ t('filesDialogs.picker.current', { path: currentLabel }) }}
      </p>
    </div>
    <UiSearchInput
      :model-value="search.query.value"
      :label="t('filesDialogs.picker.searchLabel')"
      :placeholder="t('filesDialogs.picker.searchPlaceholder')"
      :clear-label="t('filesDialogs.picker.searchClear')"
      :error="searchError"
      @update:model-value="search.type"
    />
    <UiAlert v-if="listing.errorMessage !== null" variant="error">{{ listing.errorMessage }}</UiAlert>
    <UiSpinner v-else-if="listing.loading" :label="t('common.loading.busy')" />
    <p v-else-if="listing.folders.length === 0" class="text-sm text-text-muted">
      {{ search.query.value === '' ? t('filesDialogs.picker.empty') : t('filesDialogs.picker.noMatches') }}
    </p>
    <ul v-else class="flex max-h-56 flex-col gap-1 overflow-y-auto">
      <li v-for="folder in listing.folders" :key="folder.path">
        <UiButton variant="ghost" @click="browse(childPath(path, folder.name))">
          <UiIcon name="folder" size="sm" />{{ folder.name }}
        </UiButton>
      </li>
      <li v-if="listing.hasMore">
        <UiButton variant="secondary" :disabled="listing.loadingMore" @click="emit('more')">
          {{ t('filesDialogs.picker.more') }}
        </UiButton>
      </li>
    </ul>
  </div>
</template>
