<script setup lang="ts">
/**
 * The pull grants of one export: the form that issues one, and the way to the grants issued so far.
 * A grant lets one other panel pull the package once, so the package never has to be handed over as
 * a file; its token is shown once by {@link GrantTokenDialog}.
 *
 * **Earlier grants are on their own page.** The grants list is a server page (`/migration/export-grants`),
 * so this panel only issues; a link carries the exported account's name there as the search.
 *
 * **The checks are advice.** The address range and the lifetime mirror the panel's validator to save a
 * round trip; the panel re-validates and its localized refusal is what the administrator reads.
 *
 * Props in, events out: the page owns the store.
 */
import { computed, ref, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import UiButton from '../ui/UiButton.vue'
import UiForm from '../ui/UiForm.vue'
import UiInput from '../ui/UiInput.vue'
import UiLink from '../ui/UiLink.vue'
import UiSectionHeading from '../ui/UiSectionHeading.vue'
import { isIpv4, isIpv6 } from '../../utils/dnsAddress'
import { isUsableCidr } from '../../utils/isUsableCidr'
import { readOptionalWholeNumber } from '../../utils/optionalWholeNumber'
import type { AccountExportAction, IssueExportGrantRequest } from '../../types/accountMove'

/** The longest a grant may work, in minutes: the panel's cap, mirrored so the field can say so. */
const MAX_LIFETIME_MINUTES = 60

/** Props accepted by {@link ExportGrantsPanel}. */
defineProps<{
  /** The exported account's name, which the grants page is searched by. */
  accountName: string
  /** The action in flight, or `null`. */
  busy: AccountExportAction | null
}>()

/** Events emitted by {@link ExportGrantsPanel}. */
const emit = defineEmits<{
  /** The administrator asked to issue a grant with these bounds. */
  (e: 'issue', request: IssueExportGrantRequest): void
}>()

const { t } = useI18n()

/** The address range as typed; empty for any address. */
const allowedFrom: Ref<string> = ref('')

/** The lifetime in minutes as typed; empty for the panel's default. */
const lifetime: Ref<string> = ref('')

/** Whether a submit has been attempted, so nothing turns red before the administrator has tried. */
const submitted: Ref<boolean> = ref(false)

/** The typed range split into its entries. */
const allowedItems: ComputedRef<string[]> = computed(() => {
  return allowedFrom.value.split(/[\s,]+/).filter((item) => {
    return item !== ''
  })
})

/** Validation message for the address range, or `null`. A single address is as good as a range. */
const allowedFromError: ComputedRef<string | null> = computed(() => {
  if (!submitted.value) {
    return null
  }
  const valid = allowedItems.value.every((item) => {
    return isIpv4(item) || isIpv6(item) || isUsableCidr(item)
  })
  return valid ? null : t('accountMove.export.grants.allowedFromInvalid')
})

/** Validation message for the lifetime, or `null`. */
const lifetimeError: ComputedRef<string | null> = computed(() => {
  if (!submitted.value) {
    return null
  }
  const read = readOptionalWholeNumber(lifetime.value)
  if (read.kind === 'empty') {
    return null
  }
  return read.kind === 'figure' && read.value >= 1 && read.value <= MAX_LIFETIME_MINUTES
    ? null
    : t('accountMove.export.grants.lifetimeInvalid', { max: MAX_LIFETIME_MINUTES })
})

/**
 * Validates, then asks the page to issue the grant.
 * @returns Nothing; emits synchronously when the bounds are acceptable.
 */
const submit = (): void => {
  submitted.value = true
  if (allowedFromError.value !== null || lifetimeError.value !== null) {
    return
  }
  const read = readOptionalWholeNumber(lifetime.value)
  emit('issue', {
    allowedFrom: allowedItems.value.length === 0 ? null : allowedItems.value.join(', '),
    lifetimeMinutes: read.kind === 'figure' ? read.value : null,
  })
}
</script>

<template>
  <div class="flex min-w-0 flex-col gap-4">
    <UiSectionHeading :title="t('accountMove.export.grants.title')" :subtitle="t('accountMove.export.grants.intro')" />

    <UiForm class="flex flex-col gap-4" @submit="submit">
      <div class="grid gap-4 sm:grid-cols-2">
        <div class="flex flex-col gap-1.5">
          <UiInput
            v-model="allowedFrom"
            :label="t('accountMove.export.grants.allowedFrom')"
            :error="allowedFromError"
            mono
            autocomplete="off"
          />
          <p class="text-sm text-text-muted">{{ t('accountMove.export.grants.allowedFromHint') }}</p>
        </div>
        <div class="flex flex-col gap-1.5">
          <UiInput
            v-model="lifetime"
            :label="t('accountMove.export.grants.lifetime')"
            :error="lifetimeError"
            inputmode="numeric"
            autocomplete="off"
          />
          <p class="text-sm text-text-muted">{{ t('accountMove.export.grants.lifetimeHint', { max: MAX_LIFETIME_MINUTES }) }}</p>
        </div>
      </div>
      <div class="flex justify-end">
        <UiButton type="submit" :disabled="busy !== null">
          {{ busy === 'grant' ? t('accountMove.export.grants.issuing') : t('accountMove.export.grants.issue') }}
        </UiButton>
      </div>
    </UiForm>

    <div>
      <UiLink :to="{ name: 'export-grants', query: { search: accountName } }" trailing-icon="chevronRight">
        {{ t('accountMove.export.grants.seeGrants', { name: accountName }) }}
      </UiLink>
    </div>
  </div>
</template>
