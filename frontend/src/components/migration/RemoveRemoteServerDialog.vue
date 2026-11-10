<script setup lang="ts">
/**
 * Removing a remote server, in two steps. The first is the confirmation, which says what removal
 * does and what it leaves alone: the panel forgets the server, but the panel's key stays in the
 * remote server's `authorized_keys` until the administrator takes it out. The second appears once
 * the panel has removed the server and answers with the public key: a reminder, with the key as
 * selectable text and a copy control, to remove that key from the remote server. The reminder
 * cannot be dismissed by the backdrop or Escape, so the administrator reads it before it goes.
 *
 * Props in, events out: the page owns the store and decides what the two props hold.
 */
import { ref, watch, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import UiButton from '../ui/UiButton.vue'
import UiConfirm from '../ui/UiConfirm.vue'
import UiIcon from '../ui/UiIcon.vue'
import UiModal from '../ui/UiModal.vue'
import type { RemovedRemoteServer } from '../../types/migrationSource'

/** Props accepted by {@link RemoveRemoteServerDialog}. */
const props = defineProps<{
  /** The server awaiting a removal confirmation, or `null` when none is. */
  pending: { id: string; name: string } | null
  /** Whether the removal request is in flight. */
  acting: boolean
  /** What the panel answered after removing a server: the key to take off the source, or `null`. */
  removed: RemovedRemoteServer | null
  /** The removed server's name, for the reminder's title; empty while {@link removed} is `null`. */
  removedName: string
}>()

/** Events emitted by {@link RemoveRemoteServerDialog}. */
const emit = defineEmits<{
  /** The administrator confirmed the removal. */
  (e: 'confirm'): void
  /** The administrator abandoned the removal. */
  (e: 'cancel'): void
  /** The administrator is done with the reminder. */
  (e: 'done'): void
}>()

const { t } = useI18n()

/** Whether the key was copied during this showing, so the control can say so. */
const copied: Ref<boolean> = ref(false)

/**
 * Copies the public key to the clipboard. A failure is swallowed on purpose and leaves the control
 * unchanged: the clipboard is blocked in some browsers, and the key stays selectable beside it.
 * @param key The key to copy.
 * @returns Resolves once the clipboard write has settled, successfully or not.
 */
const copyKey = async (key: string): Promise<void> => {
  try {
    await navigator.clipboard.writeText(key)
    copied.value = true
  } catch {
    copied.value = false
  }
}

// A second removal must not open under the first one's "Copied" state.
watch(
  (): RemovedRemoteServer | null => {
    return props.removed
  },
  (): void => {
    copied.value = false
  },
)
</script>

<template>
  <UiConfirm
    :open="pending !== null && removed === null"
    :title="t('migration.remove.confirmTitle', { name: pending?.name ?? '' })"
    :question="t('migration.remove.confirmQuestion')"
    :confirm-label="t('migration.remove.confirmAction')"
    :cancel-label="t('common.cancel')"
    :close-label="t('common.close')"
    :acting="acting"
    :acting-label="t('common.saving')"
    @close="emit('cancel')"
    @confirm="emit('confirm')"
  />

  <UiModal
    v-if="removed !== null"
    :open="true"
    :title="t('migration.remove.reminderTitle', { name: removedName })"
    :close-label="t('common.close')"
    :dismissible="false"
    @close="emit('done')"
  >
    <div class="flex min-w-0 flex-col gap-3">
      <p class="text-sm text-text-secondary">{{ t('migration.remove.reminderIntro') }}</p>
      <p class="text-sm text-text-secondary">{{ t('migration.remove.reminderStep') }}</p>
      <p class="text-xs font-semibold text-text-muted">{{ t('migration.remove.keyLabel') }}</p>
      <!-- Selectable text, not a masked field: the clipboard may be unavailable. -->
      <code
        data-testid="removed-public-key"
        class="block rounded-control border border-border-strong bg-surface-2 px-3 py-2.5 font-mono text-sm text-text-primary select-all wrap-anywhere"
        >{{ removed.publicKey }}</code
      >
      <p v-if="removed.hostKeyFingerprint" class="text-sm text-text-muted">
        {{ t('migration.remove.fingerprint', { fingerprint: removed.hostKeyFingerprint }) }}
      </p>
      <!-- A change of a button's name is not announced by itself, so the copy result is also spoken
           from a status region that is in the page before it has anything to say. -->
      <span class="sr-only" role="status">{{ copied ? t('migration.remove.copied') : '' }}</span>
    </div>

    <template #footer>
      <UiButton variant="secondary" @click="copyKey(removed.publicKey)">
        <UiIcon :name="copied ? 'check' : 'copy'" size="sm" />
        {{ copied ? t('migration.remove.copied') : t('migration.remove.copy') }}
      </UiButton>
      <UiButton @click="emit('done')">{{ t('migration.remove.done') }}</UiButton>
    </template>
  </UiModal>
</template>
