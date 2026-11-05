<script setup lang="ts">
/**
 * The keys of one SFTP login as a stacked list: label, key type, fingerprint (mono, wrapping) and the
 * day it was added, with a remove control per key. Stacked rather than tabular because it lives in a
 * narrow dialog. Dumb: it renders rows and reports the click; the dialog owns the confirmation.
 */
import { useI18n } from 'vue-i18n'
import UiBadge from '../ui/UiBadge.vue'
import UiButton from '../ui/UiButton.vue'
import { useLocaleStore } from '../../stores/locale'
import { formatDate } from '../../utils/formatDate'
import type { SftpUserKey } from '../../types/sftpUser'

/** Props accepted by {@link SftpKeyList}. */
defineProps<{
  /** The keys of the page the panel answered. */
  keys: SftpUserKey[]
  /** Whether a removal is in flight, which disables every remove control. */
  busy: boolean
}>()

/** Events emitted by {@link SftpKeyList}. */
const emit = defineEmits<{
  /** The person asked to remove one key. */
  (e: 'remove', key: SftpUserKey): void
}>()

const { t } = useI18n()
const localeStore = useLocaleStore()
</script>

<template>
  <ul class="flex flex-col gap-2">
    <li
      v-for="key in keys"
      :key="key.id"
      class="flex flex-col gap-2 rounded-control border border-border-subtle bg-surface-2 p-3"
    >
      <div class="flex flex-wrap items-center justify-between gap-2">
        <p class="min-w-0 font-medium text-text-primary wrap-anywhere">{{ key.label }}</p>
        <UiButton
          variant="destructive"
          size="md"
          :disabled="busy"
          :aria-label="t('sftp.keys.removeLabel', { label: key.label })"
          @click="emit('remove', key)"
        >
          {{ t('sftp.keys.remove') }}
        </UiButton>
      </div>
      <div class="flex flex-wrap items-center gap-2">
        <UiBadge>{{ key.algorithm }}</UiBadge>
        <span class="text-sm text-text-muted">
          {{ t('sftp.keys.added', { date: formatDate(key.createdAt, localeStore.current) }) }}
        </span>
      </div>
      <p class="font-mono text-xs text-text-secondary wrap-anywhere">
        <span class="sr-only">{{ t('sftp.keys.fingerprint') }}: </span>{{ key.fingerprint }}
      </p>
    </li>
  </ul>
</template>
