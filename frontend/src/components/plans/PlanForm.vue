<script setup lang="ts">
/**
 * The plan form, for both jobs it has: defining a new plan and editing an existing one. The name,
 * the seven limits, the client-side mirror of the server's validator, and — on an edit — the
 * notice that says what saving will do to the accounts already on the plan.
 *
 * Dumb by the usual contract: props in, emits out. It never touches a store or the API layer
 * (rules/vue.md) — it reports a validated request and the page decides what to do with it. It
 * refuses to emit until its own rules pass, which is what makes the round trip it saves a real
 * saving rather than a message printed beside a request that went anyway.
 *
 * **The rules are the server's, read from `PlanDefinitionValidator`, and they are advice.** Disk,
 * sites, databases, SFTP logins and PHP workers must be at least 1 (a plan that sells none is a
 * broken plan wearing a working plan's name); FTPS logins and cron entries may be 0, because
 * "none" is a product. The upper bounds are the server's typo and overflow guards. Whatever the
 * form lets through, the server re-validates, and its already-localized refusal is what the page
 * shows — so when a rule here is stricter than the server's it is a bug, and the bounds err wide.
 *
 * **What an edit reaches.** The plan's limits are read through the plan by every account on it,
 * so saving changes all of them at once (`UpdatePlanCommandHandler`). Two things follow and the
 * form says both before the button is pressed: a lowered disk quota is written to each account on
 * the host and is REFUSED, whole, if any account already uses more (an account put under a quota
 * smaller than its files cannot write at all), and a lowered count removes nothing — it only stops
 * the next one being created. The number of accounts comes from the server (`accountCount`); the
 * form does not count anything itself.
 *
 * **Numbers are typed as text.** The kit's input is a text field, and `type="number"` would bring
 * the browser's own spinner and its own validation bubble (rules/vue.md: "the browser never
 * validates"). The `numeric` keyboard is requested instead, and the text is parsed here.
 */
import { computed, ref, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import UiAlert from '../ui/UiAlert.vue'
import UiButton from '../ui/UiButton.vue'
import UiCard from '../ui/UiCard.vue'
import UiForm from '../ui/UiForm.vue'
import UiInput from '../ui/UiInput.vue'
import UiSectionHeading from '../ui/UiSectionHeading.vue'
import type { ManagedPlan, PlanRequest } from '../../types/plan'

/** The limits a plan states, by the name the request and the locale file both use. */
type LimitKey = Exclude<keyof PlanRequest, 'name'>

/** One limit's bounds, as the server's validator states them. */
interface LimitField {
  /** Which limit this is; also the suffix of its label's locale key. */
  key: LimitKey
  /** The smallest figure the server accepts. */
  min: number
  /** The largest figure the server accepts. */
  max: number
}

/** The longest name the server accepts (`Plan.NameMaxLength`). */
const MAX_NAME_LENGTH = 64

/**
 * The limits in the order the form draws them, with the bounds `PlanDefinitionValidator` states.
 * The upper bounds are a pebibyte of disk, a million of anything countable and ten thousand
 * workers: far above what a server can honour, there so a stray digit is refused with a sentence.
 */
const LIMIT_FIELDS: readonly LimitField[] = [
  { key: 'diskQuotaMb', min: 1, max: 1_073_741_824 },
  { key: 'maxSites', min: 1, max: 1_000_000 },
  { key: 'maxDatabases', min: 1, max: 1_000_000 },
  { key: 'maxSftpUsers', min: 1, max: 1_000_000 },
  { key: 'maxFtpUsers', min: 0, max: 1_000_000 },
  { key: 'maxCronEntries', min: 0, max: 1_000_000 },
  { key: 'maxPhpWorkersPerPool', min: 1, max: 10_000 },
]

/** A control character anywhere in a name (the server refuses them). */
const CONTROL_CHARACTER = /\p{Cc}/u

/** Props accepted by {@link PlanForm}. */
const props = defineProps<{
  /** The plan being edited, or `null` when a new one is being defined. */
  plan: ManagedPlan | null
  /** Whether a save request is already in flight, which disables the submit control. */
  submitting: boolean
}>()

/** Events emitted by {@link PlanForm}. */
const emit = defineEmits<{
  /** Fired only when every client-side rule passes, carrying the request to send. */
  (e: 'submit', request: PlanRequest): void
  /** Fired when the administrator abandons the form. */
  (e: 'cancel'): void
}>()

const { t } = useI18n()

/** The plan's name as typed; starts as the name the plan shows now. */
const name: Ref<string> = ref(props.plan?.displayName ?? '')

/**
 * Each limit as typed text, starting from the plan's current figure. A new plan starts empty
 * rather than at a made-up default: a default is a domain value, and domain values come from the
 * backend (rules/vue.md).
 */
const values: Ref<Record<LimitKey, string>> = ref({
  diskQuotaMb: props.plan === null ? '' : String(props.plan.diskQuotaMb),
  maxSites: props.plan === null ? '' : String(props.plan.maxSites),
  maxDatabases: props.plan === null ? '' : String(props.plan.maxDatabases),
  maxSftpUsers: props.plan === null ? '' : String(props.plan.maxSftpUsers),
  maxFtpUsers: props.plan === null ? '' : String(props.plan.maxFtpUsers),
  maxCronEntries: props.plan === null ? '' : String(props.plan.maxCronEntries),
  maxPhpWorkersPerPool: props.plan === null ? '' : String(props.plan.maxPhpWorkersPerPool),
})

/** Whether a submit has been attempted, so nothing turns red before the administrator has tried. */
const submitted: Ref<boolean> = ref(false)

/** Whether this form edits an existing plan. */
const isEditing: ComputedRef<boolean> = computed(() => {
  return props.plan !== null
})

/** Validation message for the name, or `null`. */
const nameError: ComputedRef<string | null> = computed(() => {
  if (!submitted.value) {
    return null
  }
  const trimmed = name.value.trim()
  if (trimmed.length === 0) {
    return t('plans.form.errors.nameRequired')
  }
  if (trimmed.length > MAX_NAME_LENGTH) {
    return t('plans.form.errors.nameTooLong', { max: MAX_NAME_LENGTH })
  }
  return CONTROL_CHARACTER.test(trimmed) ? t('plans.form.errors.nameControl') : null
})

/** Whether every field currently passes the client's own mirror of the server's rules. */
const isValid: ComputedRef<boolean> = computed(() => {
  return (
    nameError.value === null &&
    LIMIT_FIELDS.every((field) => {
      return limitError(field) === null
    })
  )
})

/**
 * The disk quota the administrator has typed, when it is a usable figure.
 * Drives the "you are lowering the quota" notice, which must not flicker on half-typed text.
 */
const typedDiskQuota: ComputedRef<number | null> = computed(() => {
  return parseWhole(values.value.diskQuotaMb)
})

/** Whether the typed disk quota is below the one the plan has now. */
const diskIsLowered: ComputedRef<boolean> = computed(() => {
  return (
    props.plan !== null &&
    props.plan.accountCount > 0 &&
    typedDiskQuota.value !== null &&
    typedDiskQuota.value < props.plan.diskQuotaMb
  )
})

/**
 * Reads typed text as a whole number.
 * @param text What the administrator typed.
 * @returns The number, or `null` when the text is not digits only.
 */
const parseWhole = (text: string): number | null => {
  const trimmed = text.trim()
  return /^\d+$/.test(trimmed) ? Number(trimmed) : null
}

/**
 * Reads one limit's typed text for the request. Only called once {@link isValid} has proved every
 * figure parses, so the fallback of zero merely satisfies the type and is never sent.
 * @param key Which limit to read.
 * @returns The figure.
 */
const figureOf = (key: LimitKey): number => {
  return parseWhole(values.value[key]) ?? 0
}

/**
 * Validates one limit against its bounds.
 * @param field The limit to check.
 * @returns The message to show, or `null` when the figure is acceptable (or nothing was tried yet).
 */
const limitError = (field: LimitField): string | null => {
  if (!submitted.value) {
    return null
  }
  const figure = parseWhole(values.value[field.key])
  if (figure === null) {
    return t('plans.form.errors.whole')
  }
  return figure < field.min || figure > field.max
    ? t('plans.form.errors.range', { min: field.min, max: field.max })
    : null
}

/**
 * Validates, and emits only when the request is one the server has a chance of accepting.
 * @returns Nothing; emits synchronously when the form is valid.
 */
const submit = (): void => {
  submitted.value = true
  if (!isValid.value) {
    return
  }
  emit('submit', {
    name: name.value.trim(),
    diskQuotaMb: figureOf('diskQuotaMb'),
    maxSites: figureOf('maxSites'),
    maxDatabases: figureOf('maxDatabases'),
    maxSftpUsers: figureOf('maxSftpUsers'),
    maxFtpUsers: figureOf('maxFtpUsers'),
    maxCronEntries: figureOf('maxCronEntries'),
    maxPhpWorkersPerPool: figureOf('maxPhpWorkersPerPool'),
  })
}

/**
 * Abandons the form.
 * @returns Nothing; emits `cancel`.
 */
const cancel = (): void => {
  emit('cancel')
}
</script>

<template>
  <div class="flex flex-col gap-4">
    <!-- An edit reaches customers who did nothing, so what it will do is said before the form, in
         the notice's own colour: amber when accounts are on the plan (a warning is amber, not
         blue), blue when nobody is. -->
    <UiAlert v-if="plan !== null" :variant="plan.accountCount > 0 ? 'warning' : 'info'">
      <p class="font-semibold">{{ t('plans.form.reach.title') }}</p>
      <template v-if="plan.accountCount > 0">
        <p class="mt-1">{{ t('plans.form.reach.accounts', plan.accountCount) }}</p>
        <p class="mt-1">{{ t('plans.form.reach.disk') }}</p>
        <p class="mt-1">{{ t('plans.form.reach.counts') }}</p>
      </template>
      <p v-else class="mt-1">{{ t('plans.form.reach.none') }}</p>
    </UiAlert>

    <UiCard>
      <UiForm @submit="submit">
        <UiSectionHeading class="mb-4" :title="t('plans.form.nameSection')" />
        <UiInput
          v-model="name"
          :label="t('plans.form.fields.name')"
          :placeholder="t('plans.form.placeholders.name')"
          :error="nameError"
          required
        />

        <UiSectionHeading
          class="mt-6 mb-4"
          :title="t('plans.form.limitsSection')"
          :subtitle="t('plans.form.limitsHint')"
        />
        <!-- Four columns would squeeze the longest Russian and Armenian labels to a word per line;
             two from a tablet, three once the content panel is wide. Each cell is `min-w-0` so a
             long label wraps inside its column instead of widening the grid. -->
        <div class="grid gap-4 sm:grid-cols-2 xl:grid-cols-3">
          <div v-for="field in LIMIT_FIELDS" :key="field.key" class="min-w-0">
            <UiInput
              v-model="values[field.key]"
              :label="t(`plans.form.fields.${field.key}`)"
              :error="limitError(field)"
              inputmode="numeric"
              mono
              required
            />
          </div>
        </div>

        <!-- Under the figure it concerns, not at the top of the page: the sentence is about the
             number the administrator has just changed. -->
        <UiAlert v-if="diskIsLowered && plan !== null" class="mt-4" variant="warning">
          {{ t('plans.form.diskLowered', { from: plan.diskQuotaMb, to: typedDiskQuota }) }}
        </UiAlert>

        <div class="mt-5 flex flex-wrap justify-end gap-2 border-t border-border-subtle pt-4">
          <UiButton variant="secondary" type="button" @click="cancel">{{ t('common.cancel') }}</UiButton>
          <UiButton type="submit" :disabled="submitting">
            {{ isEditing ? t('plans.form.submitEdit') : t('plans.form.submitNew') }}
          </UiButton>
        </div>
      </UiForm>
    </UiCard>
  </div>
</template>
