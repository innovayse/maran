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
import { computed, onMounted, ref, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRouter } from 'vue-router'
import UiAlert from '../../components/ui/UiAlert.vue'
import UiButton from '../../components/ui/UiButton.vue'
import UiForm from '../../components/ui/UiForm.vue'
import UiInput from '../../components/ui/UiInput.vue'
import UiSelect, { type SelectOption } from '../../components/ui/UiSelect.vue'
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

/** Client-side validation message for the plan field. */
const planIdError: ComputedRef<string | null> = computed(() => {
  if (!submitted.value) {
    return null
  }
  // Nothing chosen is the only way this field can be wrong now: the options come from
  // the backend, so the value is always one the server issued. The shape checks this
  // replaced existed only because the id used to be typed by hand.
  return planId.value.length === 0 ? t('accounts.form.errors.planIdRequired') : null
})

/**
 * The plans offered by the picker. Labels come from the backend already localized —
 * plans are server-side reference data, and a limit shown beside the name is what
 * makes the choice meaningful rather than a list of words.
 */
const planOptions: ComputedRef<SelectOption[]> = computed(() => {
  return store.plans.map((plan) => {
    return {
      value: plan.id,
      label: t('accounts.form.planOption', {
        name: plan.displayName,
        disk: plan.diskQuotaMb,
        sites: plan.maxSites,
      }),
    }
  })
})

/** Whether every field currently passes client-side validation. */
const isValid: ComputedRef<boolean> = computed(() => {
  return nameError.value === null && primaryDomainError.value === null && planIdError.value === null
})

/**
 * Loads the plans the picker offers, once, when the form opens.
 * @returns Resolves once the request has settled.
 */
const loadPlans = async (): Promise<void> => {
  await store.loadPlans()
}

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
  const created = await store.create({
    name: name.value,
    primaryDomain: primaryDomain.value,
    planId: planId.value,
  })
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
onMounted(loadPlans)
</script>

<template>
  <section class="w-full max-w-2xl">
    <div class="mb-4">
      <h1 class="text-3xl font-semibold tracking-title text-text-primary">
        {{ t('accounts.form.heading') }}
      </h1>
      <p class="mt-1 text-base text-text-secondary">{{ t('accounts.form.subtitle') }}</p>
    </div>

    <UiAlert v-if="store.createErrorMessage !== null" variant="error" class="mb-4">
      {{ store.createErrorMessage }}
    </UiAlert>

    <!-- The design's dialog body: the same panel every table sits in, with the
         footer bar bleeding to its edges, which is why the padding lives on the
         two inner blocks rather than on the panel. -->
    <!-- NOT `overflow-hidden`. The footer bar below bleeds to the card's edges and needs its
         bottom corners rounded, and clipping the card was the way that was done — which also
         clipped the select's option list, because that list is positioned inside this box. The
         third plan on this form was rendered, present in the DOM, and unreachable: a hit test at
         its centre returned the card, so an assertion that the option exists passed while an
         operator could not choose it. The footer rounds its own corners instead. -->
    <div class="rounded-xl border border-border-subtle bg-surface-1">
      <UiForm @submit="submit">
        <div class="flex flex-col gap-3.5 p-4.5">
          <UiInput
            v-model="name"
            :label="t('accounts.form.fields.name')"
            :error="nameError"
            required
            :placeholder="t('accounts.form.placeholders.name')"
          />
          <UiInput
            v-model="primaryDomain"
            :label="t('accounts.form.fields.primaryDomain')"
            :error="primaryDomainError"
            required
            :placeholder="t('accounts.form.placeholders.primaryDomain')"
          />
          <UiSelect
            v-model="planId"
            :label="t('accounts.form.fields.planId')"
            :options="planOptions"
            :error="planIdError"
            required
            :placeholder="t('accounts.form.placeholders.planId')"
          />
        </div>
        <div class="flex justify-end gap-2 rounded-b-xl border-t border-border-subtle bg-surface-2 px-4.5 py-3">
          <UiButton variant="secondary" type="button" @click="cancel">
            {{ t('accounts.form.cancel') }}
          </UiButton>
          <UiButton type="submit" :disabled="store.creating">{{ t('accounts.form.submit') }}</UiButton>
        </div>
      </UiForm>
    </div>
  </section>
</template>
