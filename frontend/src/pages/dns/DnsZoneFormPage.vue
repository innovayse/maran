<script setup lang="ts">
/**
 * Create-zone screen. `UiForm` renders `<form novalidate>`, so the browser validates nothing and
 * every message the person reads comes either from this page or from the server (rules/vue.md:
 * "Forms: the browser never validates").
 *
 * The client-side rules are advice that saves a round trip: a name must be a host name of two or
 * more labels. Which names a customer may create is the server's decision (a zone is allowed only
 * for a domain one of the account's sites claims), so the form states the rule in a hint and
 * shows the server's already-localized refusal as it was sent.
 *
 * A customer owns exactly one account, so they get no account picker: their account id goes on
 * the request from their token and the administrator-only accounts list is never read
 * (useAccountChoice).
 */
import { computed, onMounted, ref, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRouter } from 'vue-router'
import UiAlert from '../../components/ui/UiAlert.vue'
import UiButton from '../../components/ui/UiButton.vue'
import UiCard from '../../components/ui/UiCard.vue'
import UiCombobox from '../../components/ui/UiCombobox.vue'
import UiFieldHint from '../../components/ui/UiFieldHint.vue'
import UiForm from '../../components/ui/UiForm.vue'
import UiInput from '../../components/ui/UiInput.vue'
import UiPageHeading from '../../components/ui/UiPageHeading.vue'
import { useAccountChoice } from '../../composables/useAccountChoice'
import { useDnsZonesStore } from '../../stores/dnsZones'
import { isHostname } from '../../utils/dnsAddress'
import { flashNoticeState } from '../../utils/flashNotice'

const { t } = useI18n()
const router = useRouter()
const store = useDnsZonesStore()
const { fixedAccountId, picker: accountPicker, search: searchAccounts } = useAccountChoice()

/** The account that will own the zone. */
const accountId: Ref<string> = ref('')

/** The apex name as typed. */
const name: Ref<string> = ref('')

/** Whether a submit has been attempted, so nothing turns red before the person has tried. */
const submitted: Ref<boolean> = ref(false)

/** Validation message for the account picker, or `null`. */
const accountError: ComputedRef<string | null> = computed(() => {
  if (!submitted.value || fixedAccountId.value !== null) {
    return null
  }
  return accountId.value.length === 0 ? t('dns.zoneForm.errors.accountRequired') : null
})

/** Validation message for the name, or `null`. */
const nameError: ComputedRef<string | null> = computed(() => {
  if (!submitted.value) {
    return null
  }
  const value = name.value.trim()
  if (value.length === 0) {
    return t('dns.zoneForm.errors.nameRequired')
  }
  const valid = isHostname(value) && value.includes('.') && !value.endsWith('.')
  return valid ? null : t('dns.zoneForm.errors.nameInvalid')
})

/**
 * Validates, then submits. On success the new zone's page opens, where its records are; on
 * failure the store holds the server's own message and the template renders it verbatim.
 * @returns Resolves once the attempt has settled.
 */
const submit = async (): Promise<void> => {
  submitted.value = true
  if (accountError.value !== null || nameError.value !== null) {
    return
  }

  const created = await store.create({
    accountId: fixedAccountId.value ?? accountId.value,
    name: name.value.trim(),
  })

  if (created !== null) {
    await router.push({
      name: 'dns-zone-detail',
      params: { id: created.id },
      state: flashNoticeState(t('dns.zoneForm.created', { name: created.name })),
    })
  }
}

/**
 * Leaves the form without submitting.
 * @returns Resolves once navigation has been dispatched.
 */
const cancel = async (): Promise<void> => {
  await router.push({ name: 'dns-zones' })
}

onMounted(() => {
  store.clearErrors()
})
</script>

<template>
  <section class="flex w-full max-w-3xl min-w-0 flex-col gap-4">
    <UiPageHeading :title="t('dns.zoneForm.heading')" :subtitle="t('dns.zoneForm.subtitle')" />

    <!-- The one thing that needs attention goes first: the server's own refusal, verbatim. -->
    <UiAlert v-if="store.createErrorMessage !== null" variant="error">{{ store.createErrorMessage }}</UiAlert>

    <!-- `UiCard` does not clip its content, on purpose: the account picker's option list is
         positioned inside this box and a clipped card would hide it. -->
    <UiCard>
      <UiForm @submit="submit">
        <div class="flex flex-col gap-5">
          <UiCombobox
            v-if="fixedAccountId === null"
            v-model="accountId"
            v-bind="accountPicker"
            :label="t('dns.zoneForm.fields.account')"
            :placeholder="t('dns.zoneForm.placeholders.account')"
            :error="accountError"
            required
            @search="searchAccounts"
          />
          <div>
            <UiInput
              v-model="name"
              :label="t('dns.zoneForm.fields.name')"
              :placeholder="t('dns.zoneForm.placeholders.name')"
              :error="nameError"
              mono
              required
            />
            <UiFieldHint class="mt-1.5">{{ t('dns.zoneForm.hints.name') }}</UiFieldHint>
          </div>
          <div class="flex flex-col-reverse gap-2 border-t border-border-subtle pt-5 sm:flex-row sm:justify-end">
            <UiButton variant="secondary" type="button" @click="cancel">{{ t('common.cancel') }}</UiButton>
            <UiButton type="submit" :disabled="store.creating">{{ t('dns.zoneForm.submit') }}</UiButton>
          </div>
        </div>
      </UiForm>
    </UiCard>
  </section>
</template>
