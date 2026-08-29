<script setup lang="ts">
/**
 * Small colored label rendering a hosting account's lifecycle state. Used by
 * the accounts list so status is scannable without reading a text column
 * closely; the color carries no meaning on its own (text label is always
 * present) so it stays accessible without relying on color alone.
 */
import { computed } from 'vue'
import type { ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import type { AccountStatus } from '../../types/account'

/** Props accepted by {@link AccountStatusBadge}. */
const props = defineProps<{
  /** The account's current lifecycle state. */
  status: AccountStatus
}>()

const { t } = useI18n()

/** Tailwind utility classes for the badge's background/text color, by status. */
const statusClasses: ComputedRef<string> = computed(() =>
  props.status === 'active' ? 'bg-green-100 text-green-800' : 'bg-slate-200 text-slate-700',
)

/** i18n-resolved label for the current status. */
const label: ComputedRef<string> = computed(() => t(`accounts.status.${props.status}`))
</script>

<template>
  <span class="inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium" :class="statusClasses">
    {{ label }}
  </span>
</template>
