<script setup lang="ts">
/**
 * Shows an API key's secret for the only time it will ever be shown. The value exists in this
 * component's props and in the store's memory, and in no storage, URL, log or column anywhere in
 * the product; when the dialog closes the page clears the store's copy.
 *
 * Built around that fact: the warning is the first thing in the body, in the attention colour
 * (nothing failed, the reader must act before a value stops existing); the key is selectable text
 * as well as copyable, because a clipboard write can fail in a locked-down browser; the dialog is
 * not dismissible by backdrop or Escape, since either would destroy the secret, and closes only
 * through its own buttons; the copy control keeps its translated accessible name and the secret is
 * never placed in a `title` or an `aria-label`.
 */
import { ref, watch, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import UiAlert from '../ui/UiAlert.vue'
import UiButton from '../ui/UiButton.vue'
import UiDescriptionItem from '../ui/UiDescriptionItem.vue'
import UiDescriptionList from '../ui/UiDescriptionList.vue'
import UiIcon from '../ui/UiIcon.vue'
import UiModal from '../ui/UiModal.vue'
import type { RevealedApiKey } from '../../types/apiKey'

/** Props accepted by {@link ApiKeyCreatedDialog}. */
const props = defineProps<{
  /** The key on show; the page mounts the dialog only while it holds one. */
  revealed: RevealedApiKey
}>()

/** Events emitted by {@link ApiKeyCreatedDialog}. */
const emit = defineEmits<{
  /** Fired when the administrator is finished with the key; the page then forgets it. */
  (e: 'close'): void
}>()

const { t } = useI18n()

/** Whether the key was copied during this showing, so a second click is not made blind. */
const copied: Ref<boolean> = ref(false)

/**
 * Copies the key to the clipboard. A failure is swallowed on purpose and leaves {@link copied}
 * false: the clipboard is blocked in some browsers, and the honest answer is the unchanged button
 * beside a key that can still be selected by hand.
 * @returns Resolves once the clipboard write has settled, successfully or not.
 */
const copyKey = async (): Promise<void> => {
  try {
    await navigator.clipboard.writeText(props.revealed.secret)
    copied.value = true
  } catch {
    copied.value = false
  }
}

/**
 * Asks the page to forget the key and close.
 * @returns Nothing; emits synchronously.
 */
const close = (): void => {
  emit('close')
}

// A second key must not open under the first one's "Copied" state.
watch(
  (): RevealedApiKey => {
    return props.revealed
  },
  (): void => {
    copied.value = false
  },
)
</script>

<template>
  <UiModal
    :open="true"
    :title="t('apiKeys.secret.title')"
    :close-label="t('common.close')"
    :dismissible="false"
    @close="close"
  >
    <UiAlert variant="warning" class="mb-4">{{ t('apiKeys.secret.warning') }}</UiAlert>

    <UiDescriptionList>
      <UiDescriptionItem :term="t('apiKeys.secret.nameLabel')" class="sm:col-span-2">
        {{ revealed.name }}
      </UiDescriptionItem>
      <UiDescriptionItem :term="t('apiKeys.secret.keyLabel')" mono class="sm:col-span-2">
        <!-- Selectable text, not a masked field: the clipboard may be unavailable. -->
        <span
          data-testid="api-key-secret"
          class="mt-1 block rounded-control border border-border-strong bg-surface-2 px-3 py-2.5 text-base text-text-primary select-all wrap-anywhere"
        >
          {{ revealed.secret }}
        </span>
      </UiDescriptionItem>
    </UiDescriptionList>

    <p class="mt-4 text-sm text-text-muted">{{ t('apiKeys.secret.hint') }}</p>

    <!-- A name change on a button is not announced by itself, so the copy result is also spoken
         from a status region that is in the page before it has anything to say. -->
    <span class="sr-only" role="status">{{ copied ? t('common.copied') : '' }}</span>

    <template #footer>
      <UiButton variant="secondary" @click="copyKey">
        <UiIcon name="copy" size="sm" />
        {{ copied ? t('common.copied') : t('apiKeys.secret.copy') }}
      </UiButton>
      <UiButton @click="close">{{ t('common.done') }}</UiButton>
    </template>
  </UiModal>
</template>
