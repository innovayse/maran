<script setup lang="ts">
/**
 * Create-account screen: a form for name, primary domain and plan id, with
 * client-side validation mirroring the server's
 * `CreateAccountCommandValidator` constraints exactly (same character
 * classes and length limits — read from
 * `backend/src/Maran.Modules/Accounts/Commands/CreateAccount/CreateAccountCommandValidator.cs`)
 * so a user sees the same problem before submitting that the server would
 * otherwise reject. On submit, a server-side failure (e.g. a name already
 * taken — a race the client check cannot catch) is rendered verbatim
 * (rules/vue.md: "the backend owns their text"). Renders a `<section>`, not
 * a `<main>` — the single `<main>` landmark lives in the layout this page
 * is nested under.
 */
import { computed, ref } from 'vue'
import type { ComputedRef, Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRouter } from 'vue-router'
import UiAlert from '../../components/ui/UiAlert.vue'
import UiButton from '../../components/ui/UiButton.vue'
import UiForm from '../../components/ui/UiForm.vue'
import UiInput from '../../components/ui/UiInput.vue'
import { useAccountsStore } from '../../stores/accounts'

/**
 * Matches the server's `Name` rule exactly: lowercase, starts with a letter,
 * 3-32 characters of lowercase letters/digits/hyphen/underscore.
 */
const NAME_PATTERN = /^[a-z][a-z0-9_-]{2,31}$/

/**
 * Matches the server's `PrimaryDomain` rule exactly: dot-separated labels of
 * up to 63 alphanumeric/hyphen characters, not starting or ending with a
 * hyphen, at least two labels.
 */
const DOMAIN_PATTERN = /^(?!-)[A-Za-z0-9-]{1,63}(?<!-)(\.(?!-)[A-Za-z0-9-]{1,63}(?<!-))+$/

/**
 * Matches a well-formed GUID/UUID, the shape `PlanId` must take — the
 * server rejects an empty (all-zero) GUID via `NotEmpty()`.
 */
const GUID_PATTERN = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i

/** All-zero GUID, the one value the server's `NotEmpty()` rule rejects for `PlanId`. */
const EMPTY_GUID = '00000000-0000-0000-0000-000000000000'

const { t } = useI18n()
const router = useRouter()
const store = useAccountsStore()

/** Current value of the name field. */
const name: Ref<string> = ref('')
/** Current value of the primary domain field. */
const primaryDomain: Ref<string> = ref('')
/** Current value of the plan id field. */
const planId: Ref<string> = ref('')
/** Whether a submit has been attempted, so validation messages only show after the first try. */
const submitted: Ref<boolean> = ref(false)

/** Client-side validation message for the name field, mirroring the server's rule. */
const nameError: ComputedRef<string | null> = computed(() => {
  if (!submitted.value) {
    return null
  }
  if (name.value.length === 0) {
    return t('accounts.form.errors.nameRequired')
  }
  if (name.value.length > 32 || !NAME_PATTERN.test(name.value)) {
    return t('accounts.form.errors.nameInvalid')
  }
  return null
})

/** Client-side validation message for the primary domain field, mirroring the server's rule. */
const primaryDomainError: ComputedRef<string | null> = computed(() => {
  if (!submitted.value) {
    return null
  }
  if (primaryDomain.value.length === 0) {
    return t('accounts.form.errors.primaryDomainRequired')
  }
  if (primaryDomain.value.length > 253 || !DOMAIN_PATTERN.test(primaryDomain.value)) {
    return t('accounts.form.errors.primaryDomainInvalid')
  }
  return null
})

/** Client-side validation message for the plan id field, mirroring the server's rule. */
const planIdError: ComputedRef<string | null> = computed(() => {
  if (!submitted.value) {
    return null
  }
  if (planId.value.length === 0 || planId.value === EMPTY_GUID) {
    return t('accounts.form.errors.planIdRequired')
  }
  if (!GUID_PATTERN.test(planId.value)) {
    return t('accounts.form.errors.planIdInvalid')
  }
  return null
})

/** Whether every field currently passes client-side validation. */
const isValid: ComputedRef<boolean> = computed(
  () => nameError.value === null && primaryDomainError.value === null && planIdError.value === null,
)

/**
 * Validates the form and, when valid, submits it to the store. Navigates back to the list on
 * success; a server-side failure is left in the store's `createErrorMessage` for the template to
 * render verbatim.
 * @returns Resolves once the attempt has settled.
 */
const submit = async (): Promise<void> => {
  submitted.value = true
  if (!isValid.value) {
    return
  }
  const created = await store.create({ name: name.value, primaryDomain: primaryDomain.value, planId: planId.value })
  if (created !== null) {
    await router.push({ name: 'accounts' })
  }
}

/**
 * Navigates back to the accounts list without submitting.
 * @returns Resolves once navigation has been dispatched.
 */
const cancel = async (): Promise<void> => {
  await router.push({ name: 'accounts' })
}
</script>

<template>
  <section class="mx-auto max-w-xl">
    <h1 class="mb-4 text-2xl font-semibold">{{ t('accounts.form.heading') }}</h1>

    <UiAlert v-if="store.createErrorMessage !== null" variant="error" class="mb-4">
      {{ store.createErrorMessage }}
    </UiAlert>

    <UiForm class="flex flex-col gap-4" @submit="submit">
      <UiInput
        v-model="name"
        :label="t('accounts.form.fields.name')"
        :error="nameError"
        required
        placeholder="example"
      />
      <UiInput
        v-model="primaryDomain"
        :label="t('accounts.form.fields.primaryDomain')"
        :error="primaryDomainError"
        required
        placeholder="example.com"
      />
      <UiInput
        v-model="planId"
        :label="t('accounts.form.fields.planId')"
        :error="planIdError"
        required
        placeholder="00000000-0000-0000-0000-000000000000"
      />
      <div class="flex gap-2">
        <UiButton type="submit" :disabled="store.creating">{{ t('accounts.form.submit') }}</UiButton>
        <UiButton variant="secondary" type="button" @click="cancel">
          {{ t('accounts.form.cancel') }}
        </UiButton>
      </div>
    </UiForm>
  </section>
</template>
