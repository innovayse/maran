<script setup lang="ts">
/**
 * The move-to-trash question and, when the request fails outright, the failure that replaces it.
 * Dumb by the usual contract: the host runs the request and owns the outcome. Moving to the trash
 * can be undone, so the question is not styled as destructive.
 */
import { useI18n } from 'vue-i18n'
import UiAlert from '../ui/UiAlert.vue'
import UiButton from '../ui/UiButton.vue'
import UiConfirm from '../ui/UiConfirm.vue'
import UiModal from '../ui/UiModal.vue'

/** Props accepted by {@link FileTrashDialog}. */
defineProps<{
  /** How many entries are moved. */
  count: number
  /** The trash retention in days, once the server has said; `null` while it is unknown. */
  retentionDays: number | null
  /** Whether the request is in flight. */
  submitting: boolean
  /** The panel's message for the last failed request, or `null`. */
  errorMessage: string | null
}>()

/** Events emitted by {@link FileTrashDialog}. */
const emit = defineEmits<{
  /** The person confirmed moving the entries to the trash. */
  (e: 'confirm'): void
  /** The person closed the dialog. */
  (e: 'close'): void
}>()

const { t } = useI18n()
</script>

<template>
  <UiConfirm
    v-if="errorMessage === null"
    :open="true"
    :title="t('filesDialogs.trash.title', { count }, count)"
    :question="
      retentionDays === null
        ? t('filesDialogs.trash.question')
        : t('filesDialogs.trash.questionWithDays', { days: retentionDays }, retentionDays)
    "
    :confirm-label="t('filesDialogs.trash.confirm')"
    :cancel-label="t('common.cancel')"
    :close-label="t('common.close')"
    :acting="submitting"
    :acting-label="t('filesDialogs.trash.working')"
    :destructive="false"
    @confirm="emit('confirm')"
    @close="emit('close')"
  />
  <UiModal
    v-else
    :open="true"
    :title="t('filesDialogs.trash.failedTitle')"
    :close-label="t('common.close')"
    @close="emit('close')"
  >
    <UiAlert variant="error">{{ errorMessage }}</UiAlert>
    <template #footer>
      <UiButton @click="emit('close')">{{ t('common.close') }}</UiButton>
    </template>
  </UiModal>
</template>
