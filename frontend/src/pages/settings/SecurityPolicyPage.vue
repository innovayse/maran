<script setup lang="ts">
/**
 * The panel's security policy: how short a password may be, whether administrators
 * must hold a second factor, and how many wrong passwords lock an account for how
 * long. Renders a `<section>`, not a `<main>` — the single `<main>` landmark lives
 * in the layout this page is nested under.
 *
 * The numbers are the server's, read on mount and written back whole: the endpoint
 * is a PUT because there is exactly one policy on a panel and the form carries all
 * of it. Nothing here invents a default; a panel that has not answered yet renders
 * no form rather than plausible-looking numbers.
 */
import { computed, ref, watch, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import UiAlert from '../../components/ui/UiAlert.vue'
import UiButton from '../../components/ui/UiButton.vue'
import UiCard from '../../components/ui/UiCard.vue'
import UiForm from '../../components/ui/UiForm.vue'
import UiInput from '../../components/ui/UiInput.vue'
import UiSwitch from '../../components/ui/UiSwitch.vue'
import { useSecurityPolicyStore } from '../../stores/securityPolicy'
import type { SecurityPolicy } from '../../types/securityPolicy'

const { t } = useI18n()
const policyStore = useSecurityPolicyStore()

/** The shortest accepted password, as typed. Text, because the form is `novalidate`. */
const minimumPasswordLength: Ref<string> = ref('')

/** Whether administrators are steered into two-factor enrolment. */
const forceTwoFactorForAdmins: Ref<boolean> = ref(false)

/** Consecutive failures that lock an account, as typed. */
const maxFailedLoginAttempts: Ref<string> = ref('')

/** How long a locked account stays locked, in minutes, as typed. */
const lockoutMinutes: Ref<string> = ref('')

/** True once the panel has answered, which is when the form has something to edit. */
const isLoaded: ComputedRef<boolean> = computed(() => {
  return policyStore.policy !== null
})

/**
 * Copies a policy the panel reported into the form's fields.
 * @param policy The policy to show, or `null` while none has been read.
 * @returns Nothing; the fields are updated synchronously.
 */
const fill = (policy: SecurityPolicy | null): void => {
  if (policy === null) {
    return
  }

  minimumPasswordLength.value = String(policy.minimumPasswordLength)
  forceTwoFactorForAdmins.value = policy.forceTwoFactorForAdmins
  maxFailedLoginAttempts.value = String(policy.maxFailedLoginAttempts)
  lockoutMinutes.value = String(policy.lockoutMinutes)
}

/**
 * Saves the policy as typed.
 *
 * The numbers are sent as numbers, and the bounds are the server's to enforce: it
 * refuses a minimum length under eight, a lock threshold under three and a lockout
 * over a day, each with its own localized message, and duplicating those bounds
 * here would put two authorities on one rule.
 * @returns Resolves once the request has settled.
 */
const submit = async (): Promise<void> => {
  await policyStore.save({
    minimumPasswordLength: Number(minimumPasswordLength.value),
    forceTwoFactorForAdmins: forceTwoFactorForAdmins.value,
    maxFailedLoginAttempts: Number(maxFailedLoginAttempts.value),
    lockoutMinutes: Number(lockoutMinutes.value),
  })
}

// The form is filled from whatever the panel last reported, including after a save.
watch(
  () => {
    return policyStore.policy
  },
  fill,
  { immediate: true },
)

void policyStore.load()
</script>

<template>
  <section class="w-full max-w-2xl">
    <h1 class="text-3xl font-semibold tracking-title text-text-primary">
      {{ t('app.securityPolicy.heading') }}
    </h1>
    <p class="mt-1 mb-4 text-base text-text-secondary">{{ t('app.securityPolicy.subtitle') }}</p>

    <UiAlert v-if="policyStore.errorMessage !== null" variant="error" class="mb-4">
      {{ policyStore.errorMessage }}
    </UiAlert>

    <UiAlert v-if="policyStore.saved" variant="info" class="mb-4">
      {{ t('app.securityPolicy.saved') }}
    </UiAlert>

    <UiCard v-if="isLoaded">
      <UiForm @submit="submit">
        <div class="flex flex-col gap-3">
          <UiInput
            v-model="minimumPasswordLength"
            :label="t('app.securityPolicy.minimumPasswordLengthLabel')"
            required
          />

          <UiSwitch
            v-model="forceTwoFactorForAdmins"
            :label="t('app.securityPolicy.forceTwoFactorLabel')"
          />

          <!-- The warning is the reason this switch is not just another field: an
               administrator who turns it on and then cannot complete enrolment has
               locked themselves out of their own panel, and the only way back is a
               command on the server. -->
          <UiAlert variant="error">{{ t('app.securityPolicy.forceTwoFactorWarning') }}</UiAlert>

          <UiInput
            v-model="maxFailedLoginAttempts"
            :label="t('app.securityPolicy.maxFailedLoginAttemptsLabel')"
            required
          />

          <UiInput
            v-model="lockoutMinutes"
            :label="t('app.securityPolicy.lockoutMinutesLabel')"
            required
          />

          <UiButton class="mt-1" type="submit" :disabled="policyStore.loading">
            {{ t('app.securityPolicy.save') }}
          </UiButton>
        </div>
      </UiForm>
    </UiCard>
  </section>
</template>
