<script setup lang="ts">
/**
 * Shows a pull grant's token for the only time it will ever be shown. The token exists in this
 * component's props and in the store's memory, and in no storage, URL, log or column anywhere in the
 * product (the panel keeps only its digest); when the dialog closes the page clears the store's copy.
 *
 * Built around that fact: the warning is the first thing in the body; the token is selectable text as
 * well as copyable, because a clipboard write can fail in a locked-down browser; the dialog is not
 * dismissible by backdrop or Escape, since either would destroy the token, and closes only through
 * its own buttons; the token is never placed in a `title` or an `aria-label`.
 */
import { useI18n } from 'vue-i18n'
import UiAlert from '../ui/UiAlert.vue'
import UiButton from '../ui/UiButton.vue'
import UiDescriptionItem from '../ui/UiDescriptionItem.vue'
import UiDescriptionList from '../ui/UiDescriptionList.vue'
import UiModal from '../ui/UiModal.vue'
import CopyValueButton from './CopyValueButton.vue'
import { useLocaleStore } from '../../stores/locale'
import { formatIsoTimestamp } from '../../utils/formatIsoTimestamp'
import type { ExportGrant } from '../../types/accountMove'

/** Props accepted by {@link GrantTokenDialog}. */
defineProps<{
  /** The grant on show; the page mounts the dialog only while it holds one. */
  grant: ExportGrant
}>()

/** Events emitted by {@link GrantTokenDialog}. */
const emit = defineEmits<{
  /** Fired when the administrator is finished with the token; the page then forgets it. */
  (e: 'close'): void
}>()

const { t } = useI18n()
const localeStore = useLocaleStore()

/**
 * Asks the page to forget the token and close.
 * @returns Nothing; emits synchronously.
 */
const close = (): void => {
  emit('close')
}
</script>

<template>
  <UiModal
    :open="true"
    :title="t('accountMove.export.token.title')"
    :close-label="t('common.close')"
    :dismissible="false"
    @close="close"
  >
    <UiAlert variant="warning" class="mb-4">{{ t('accountMove.export.token.warning') }}</UiAlert>

    <UiDescriptionList>
      <UiDescriptionItem :term="t('accountMove.export.token.tokenLabel')" mono class="sm:col-span-2">
        <!-- Selectable text, not a masked field: the clipboard may be unavailable. -->
        <span
          data-testid="grant-token"
          class="mt-1 block rounded-control border border-border-strong bg-surface-2 px-3 py-2.5 text-base text-text-primary select-all wrap-anywhere"
        >
          {{ grant.token }}
        </span>
      </UiDescriptionItem>
      <UiDescriptionItem :term="t('accountMove.export.token.expiresLabel')">
        {{ formatIsoTimestamp(grant.expiresAt, localeStore.current) }}
      </UiDescriptionItem>
      <UiDescriptionItem :term="t('accountMove.export.token.fromLabel')" mono>
        {{ grant.allowedFrom === null ? t('accountMove.export.grants.fromAnyShort') : grant.allowedFrom }}
      </UiDescriptionItem>
    </UiDescriptionList>

    <template #footer>
      <CopyValueButton :value="grant.token" :label="t('accountMove.export.token.copy')" />
      <UiButton @click="close">{{ t('accountMove.export.token.done') }}</UiButton>
    </template>
  </UiModal>
</template>
