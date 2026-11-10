<script setup lang="ts">
/**
 * Shows the credentials an import generated for the databases and logins it created, for the only time
 * they will ever be shown. They exist in this component's props and in the store's memory, and in no
 * storage, URL, log or column anywhere in the product; when the dialog closes the page clears the
 * store's copy.
 *
 * Built around that fact, like every credential dialog in the panel: the warning leads; each value is
 * selectable text as well as copyable, because a clipboard write can fail in a locked-down browser; the
 * dialog is not dismissible by backdrop or Escape, since either would destroy the values, and closes
 * only through its own buttons; a value is never placed in a `title` or an `aria-label`.
 */
import { useI18n } from 'vue-i18n'
import UiAlert from '../ui/UiAlert.vue'
import UiButton from '../ui/UiButton.vue'
import UiDescriptionItem from '../ui/UiDescriptionItem.vue'
import UiDescriptionList from '../ui/UiDescriptionList.vue'
import UiModal from '../ui/UiModal.vue'
import CopyValueButton from './CopyValueButton.vue'
import type { IssuedSecret } from '../../types/accountMove'

/** Props accepted by {@link IssuedSecretsDialog}. */
defineProps<{
  /** The credentials on show; the page mounts the dialog only while it holds them. */
  secrets: IssuedSecret[]
}>()

/** Events emitted by {@link IssuedSecretsDialog}. */
const emit = defineEmits<{
  /** Fired when the administrator is finished with the credentials; the page then forgets them. */
  (e: 'close'): void
}>()

const { t } = useI18n()

/**
 * Asks the page to forget the credentials and close.
 * @returns Nothing; emits synchronously.
 */
const close = (): void => {
  emit('close')
}
</script>

<template>
  <UiModal
    :open="true"
    :title="t('accountMove.import.secrets.title')"
    :close-label="t('common.close')"
    :dismissible="false"
    @close="close"
  >
    <UiAlert variant="warning" class="mb-4">{{ t('accountMove.import.secrets.warning') }}</UiAlert>

    <p v-if="secrets.length === 0" class="text-sm text-text-muted">{{ t('accountMove.import.secrets.none') }}</p>

    <UiDescriptionList v-else>
      <UiDescriptionItem v-for="(entry, index) in secrets" :key="`${entry.subject}-${index}`" :term="entry.subject" mono class="sm:col-span-2">
        <div class="mt-1 flex flex-wrap items-center gap-2">
          <!-- Selectable text, not a masked field: the clipboard may be unavailable. -->
          <span
            data-testid="issued-secret"
            class="min-w-0 flex-1 rounded-control border border-border-strong bg-surface-2 px-3 py-2.5 text-base text-text-primary select-all wrap-anywhere"
          >
            {{ entry.secret }}
          </span>
          <CopyValueButton :value="entry.secret" :label="t('accountMove.import.secrets.copy', { subject: entry.subject })" />
        </div>
      </UiDescriptionItem>
    </UiDescriptionList>

    <template #footer>
      <UiButton @click="close">{{ t('accountMove.import.secrets.done') }}</UiButton>
    </template>
  </UiModal>
</template>
