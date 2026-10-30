<script setup lang="ts">
/**
 * The backup history's one attention item, drawn from the panel's answer
 * (`GET /api/v1/backups/attention`).
 *
 * A warning opens the screen when an account's most recent FINISHED run failed — a failed copy is a
 * copy that does not exist. A failure a later completed run superseded is history and is not
 * raised; a still-running run neither raises nor clears one. The panel decides that across every
 * row, because the screen holds one page. One failure is named — the account and the moment —
 * because that is enough to act on; several are counted. When the panel could not answer, the
 * notice says so rather than implying there is nothing to attend to.
 */
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import UiAlert from '../ui/UiAlert.vue'
import { useLocaleStore } from '../../stores/locale'
import { backupAccountName } from '../../utils/backupAccountName'
import { formatIsoTimestamp } from '../../utils/formatIsoTimestamp'
import type { BackupAttentionLoad } from '../../types/backup'

/** Props accepted by {@link BackupAttentionNotice}. */
const props = defineProps<{
  /** Where the attention question stands, as the backups store holds it. */
  attention: BackupAttentionLoad
}>()

const { t } = useI18n()
const localeStore = useLocaleStore()

/** The sentence the warning says, or `null` when nothing needs attention or the panel could not say. */
const message: ComputedRef<string | null> = computed(() => {
  const current = props.attention
  if (current.state !== 'answered') {
    return null
  }
  const { unrecoveredAccounts, newest } = current.attention
  if (unrecoveredAccounts === 1 && newest !== null) {
    return t('backups.list.attentionOne', {
      account: backupAccountName(newest, t('common.emptyValue')),
      startedAt: formatIsoTimestamp(newest.startedAt, localeStore.current),
    })
  }
  return unrecoveredAccounts > 0
    ? t('backups.list.attentionMany', { count: unrecoveredAccounts }, unrecoveredAccounts)
    : null
})
</script>

<template>
  <!-- Warning, not error: the failure already happened and can be answered by taking another copy. -->
  <UiAlert v-if="message !== null" variant="warning">{{ message }}</UiAlert>
  <UiAlert v-else-if="attention.state === 'unavailable'" variant="info">
    {{ t('backups.list.attentionUnavailable') }}
  </UiAlert>
</template>
