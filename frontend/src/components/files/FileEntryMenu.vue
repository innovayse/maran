<script setup lang="ts">
/**
 * The actions menu of one row of the file browser. Which items it offers depends only on what the
 * entry is: a file can be edited, downloaded and extracted, a folder cannot; everything else is
 * offered for both. The menu is advice: the server decides what is allowed and refuses the rest.
 *
 * It only reports the choice; what an action does is the page's business.
 */
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import UiDropdown from '../ui/UiDropdown.vue'
import UiDropdownItem from '../ui/UiDropdownItem.vue'
import UiIcon from '../ui/UiIcon.vue'
import type { FileEntry, FileEntryAction } from '../../types/file'

/** Props accepted by {@link FileEntryMenu}. */
const props = defineProps<{
  /** The entry the menu acts on. */
  entry: FileEntry
}>()

/** Events emitted by {@link FileEntryMenu}. */
const emit = defineEmits<{
  /** An item was chosen. */
  (e: 'action', action: FileEntryAction): void
}>()

const { t } = useI18n()

/** Whether the entry is a regular file, the only kind with content to edit, download or extract. */
const isFile: ComputedRef<boolean> = computed(() => {
  return props.entry.kind === 'file'
})
</script>

<template>
  <UiDropdown
    :label="t('common.actions')"
    :aria-label="t('filesBrowser.menu.label', { name: entry.name })"
    align="end"
    variant="bare"
    :chevron="false"
  >
    <template #trigger>
      <UiIcon name="ellipsis" size="md" />
    </template>
    <UiDropdownItem v-if="isFile" @select="emit('action', 'edit')">{{ t('filesBrowser.menu.edit') }}</UiDropdownItem>
    <UiDropdownItem v-if="isFile" @select="emit('action', 'download')">{{ t('filesBrowser.menu.download') }}</UiDropdownItem>
    <UiDropdownItem @select="emit('action', 'rename')">{{ t('filesBrowser.menu.rename') }}</UiDropdownItem>
    <UiDropdownItem @select="emit('action', 'move')">{{ t('filesBrowser.menu.move') }}</UiDropdownItem>
    <UiDropdownItem @select="emit('action', 'copy')">{{ t('filesBrowser.menu.copy') }}</UiDropdownItem>
    <UiDropdownItem @select="emit('action', 'permissions')">{{ t('filesBrowser.menu.permissions') }}</UiDropdownItem>
    <UiDropdownItem @select="emit('action', 'archive')">{{ t('filesBrowser.menu.archive') }}</UiDropdownItem>
    <UiDropdownItem v-if="isFile" @select="emit('action', 'extract')">{{ t('filesBrowser.menu.extract') }}</UiDropdownItem>
    <UiDropdownItem destructive @select="emit('action', 'trash')">{{ t('filesBrowser.menu.trash') }}</UiDropdownItem>
    <UiDropdownItem destructive @select="emit('action', 'delete')">{{ t('filesBrowser.menu.delete') }}</UiDropdownItem>
  </UiDropdown>
</template>
