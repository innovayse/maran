<script setup lang="ts">
/**
 * The glyph in front of an entry's name: a folder, a file or a link. Decorative: the name is
 * printed beside it and the kind has its own column, so the glyph carries no accessible name of its
 * own and adds nothing for a screen reader.
 */
import { computed, type ComputedRef } from 'vue'
import UiIcon, { type UiIconName } from '../ui/UiIcon.vue'
import type { FileEntry } from '../../types/file'

/** Props accepted by {@link FileEntryIcon}. */
const props = defineProps<{
  /** The entry the glyph stands for. */
  entry: Pick<FileEntry, 'kind' | 'isSymlink'>
}>()

/** The glyph for the entry: a link shows as a link whatever it points at. */
const name: ComputedRef<UiIconName> = computed(() => {
  if (props.entry.isSymlink) {
    return 'link'
  }
  return props.entry.kind === 'directory' ? 'folder' : 'file'
})

/** Folders take the accent so they read as the way in; everything else is muted. */
const tone: ComputedRef<string> = computed(() => {
  return props.entry.kind === 'directory' ? 'text-accent-text' : 'text-text-muted'
})
</script>

<template>
  <UiIcon :name="name" size="lg" class="shrink-0" :class="tone" />
</template>
