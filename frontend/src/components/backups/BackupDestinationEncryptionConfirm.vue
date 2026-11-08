<script setup lang="ts">
/**
 * Asks the administrator to confirm switching encryption of new backups on or off for one
 * destination, before the page sends it (issue #160). Dumb: the page holds which change is pending
 * and carries it out on `confirm`.
 *
 * **Switching on is the weighty answer, and the dialog says why.** The key that seals the backups
 * is made and held by the panel, and the one copy of it that matters is inside the panel's own
 * recovery archive; a backup sealed under a key that is lost is lost with it. The question therefore
 * names the custody, and the confirm button is the primary pill rather than a destructive one,
 * because the action removes nothing. **Switching off** is the lighter question: it stops sealing
 * new backups, keeps the key, and leaves every sealed backup restorable.
 */
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import UiConfirm from '../ui/UiConfirm.vue'
import type { BackupDestination } from '../../types/backupDestination'

/** Props accepted by {@link BackupDestinationEncryptionConfirm}; the page mounts it only while a change waits. */
const props = defineProps<{
  /** The destination the change is for. */
  destination: BackupDestination
  /** The setting being asked for: true to seal new backups, false to stop sealing them. */
  enable: boolean
  /** Whether the confirmed request is in flight, which holds the dialog's spinner. */
  acting: boolean
}>()

/** Events emitted by {@link BackupDestinationEncryptionConfirm}. */
const emit = defineEmits<{
  /** The person abandoned the change. */
  (e: 'close'): void
  /** The person confirmed the change. */
  (e: 'confirm'): void
}>()

const { t } = useI18n()

/** The confirmation's title, naming the destination and the direction of the change. */
const confirmationTitle: ComputedRef<string> = computed(() => {
  const key = props.enable ? 'backups.destinations.encryption.enableTitle' : 'backups.destinations.encryption.disableTitle'
  return t(key, { name: props.destination.displayName })
})

/** The consequence the administrator weighs: the key-custody warning when switching on. */
const question: ComputedRef<string> = computed(() => {
  return t(
    props.enable ? 'backups.destinations.encryption.enableQuestion' : 'backups.destinations.encryption.disableQuestion',
  )
})
</script>

<template>
  <UiConfirm
    :open="true"
    :title="confirmationTitle"
    :question="question"
    :confirm-label="
      enable ? t('backups.destinations.encryption.enableAction') : t('backups.destinations.encryption.disableAction')
    "
    :cancel-label="t('common.cancel')"
    :close-label="t('common.close')"
    :acting="acting"
    :acting-label="t('common.saving')"
    :destructive="false"
    @close="emit('close')"
    @confirm="emit('confirm')"
  />
</template>
