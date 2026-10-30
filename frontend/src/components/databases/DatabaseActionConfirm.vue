<script setup lang="ts">
/**
 * Asks the operator to confirm a password reset or a drop of one database before the page sends it,
 * naming the database and the consequence. Dumb: the page holds which database and action are
 * pending and carries the action out on `confirm`.
 *
 * One dialog for the whole table rather than one per row: only one action can be awaiting an answer,
 * and the menu that opened it is already gone by the time it appears. `UiModal` restores focus to
 * the row's menu trigger when the dialog closes, so the keyboard user lands back on the control
 * they used.
 */
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import UiConfirm from '../ui/UiConfirm.vue'
import type { Database, DatabaseAction } from '../../types/database'

/** Props accepted by {@link DatabaseActionConfirm}. */
const props = defineProps<{
  /** The database awaiting confirmation, or `null` while none is (the dialog is closed). */
  database: Database | null
  /** Which action that database is waiting on. */
  action: DatabaseAction
  /** Whether the confirmed request is in flight, which holds the dialog's spinner. */
  acting: boolean
}>()

/** Events emitted by {@link DatabaseActionConfirm}. */
const emit = defineEmits<{
  /** The operator abandoned the action. */
  (e: 'close'): void
  /** The operator confirmed the action. */
  (e: 'confirm'): void
}>()

const { t } = useI18n()

/** The confirmation's title, naming the row and what would be done to it. */
const confirmationTitle: ComputedRef<string> = computed(() => {
  const name = props.database?.fullName ?? ''
  return props.action === 'drop'
    ? t('databases.list.confirmDropTitle', { name })
    : t('databases.list.confirmResetPasswordTitle', { name })
})

/** The consequence the confirmation asks the operator to weigh. */
const confirmationQuestion: ComputedRef<string> = computed(() => {
  return props.action === 'drop' ? t('databases.list.confirmDrop') : t('databases.list.confirmResetPassword')
})

/** The confirm button's label, naming the action rather than a bare "Confirm" (#218). */
const confirmationLabel: ComputedRef<string> = computed(() => {
  return props.action === 'drop'
    ? t('databases.list.confirmDropAction')
    : t('databases.list.confirmResetPasswordAction')
})
</script>

<template>
  <UiConfirm
    :open="database !== null"
    :title="confirmationTitle"
    :question="confirmationQuestion"
    :confirm-label="confirmationLabel"
    :cancel-label="t('common.cancel')"
    :close-label="t('common.close')"
    :acting="acting"
    :acting-label="t('databases.list.working')"
    :destructive="action === 'drop'"
    @close="emit('close')"
    @confirm="emit('confirm')"
  />
</template>
