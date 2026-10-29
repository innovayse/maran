<script setup lang="ts">
/**
 * The plan form screen, one page for both routes: `/plans/new` defines a plan and `/plans/:id`
 * edits one. Renders a `<section>`, not a `<main>` — the single `<main>` landmark lives in the
 * layout this page is nested under. State comes exclusively from the plans store; the page never
 * touches the API layer (rules/vue.md: API composables are called from stores only).
 *
 * **An edit loads the list fresh and finds its plan in it.** The server has no single-plan read
 * and none is needed: a panel holds a handful of plans. It reloads even when the list is already
 * in the store, because the account count the edit notice states is a live fact and a stale one
 * would tell an administrator that nobody is on a plan somebody has just been put on.
 *
 * **The form is not drawn until the plan it edits is known.** `PlanForm` takes its starting
 * values once, when it is created, so mounting it before the plan arrived would show an empty
 * form for a plan that exists and the first keystroke would overwrite nothing it should.
 *
 * A refusal from the server — a name another plan has, a quota an account already exceeds — is
 * rendered verbatim above the form, and the form stays as the administrator left it so they can
 * correct the one figure and send it again.
 */
import { computed, onMounted, ref, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRouter } from 'vue-router'
import UiAlert from '../../components/ui/UiAlert.vue'
import UiButton from '../../components/ui/UiButton.vue'
import UiEmptyState from '../../components/ui/UiEmptyState.vue'
import UiPageHeading from '../../components/ui/UiPageHeading.vue'
import UiSpinner from '../../components/ui/UiSpinner.vue'
import PlanForm from '../../components/plans/PlanForm.vue'
import { usePlansStore } from '../../stores/plans'
import type { ManagedPlan, PlanRequest } from '../../types/plan'

/** Props the router passes (`props: true` on the edit route; absent on the create route). */
const props = defineProps<{
  /** The plan being edited, from the path; undefined when a new plan is being defined. */
  id?: string
}>()

const { t } = useI18n()
const router = useRouter()
const store = usePlansStore()

/** Whether the first load for an edit has settled, so "not found" is not claimed before it. */
const settled: Ref<boolean> = ref(false)

/** Whether this page edits an existing plan. */
const isEditing: ComputedRef<boolean> = computed(() => {
  return props.id !== undefined
})

/** The plan being edited, or `undefined` while it is not in the list (not loaded, or deleted). */
const plan: ComputedRef<ManagedPlan | undefined> = computed(() => {
  return props.id === undefined ? undefined : store.find(props.id)
})

/** The heading's key: it names the job the page is doing. */
const headingKey: ComputedRef<string> = computed(() => {
  return isEditing.value ? 'plans.form.headingEdit' : 'plans.form.headingNew'
})

/**
 * Prepares the screen: forgets the last attempt's refusal and, for an edit, loads the plan.
 * @returns Resolves once the load, if any, has settled.
 */
const prepare = async (): Promise<void> => {
  store.clearErrors()
  if (isEditing.value) {
    await store.load()
  }
  settled.value = true
}

/**
 * Sends the validated request: a create or an update, then back to the list on success.
 * @param request The plan's name and limits, as the form validated them.
 * @returns Resolves once the attempt has settled; a refusal stays in the store for the template.
 */
const save = async (request: PlanRequest): Promise<void> => {
  const saved = props.id === undefined ? await store.create(request) : await store.update(props.id, request)
  if (saved !== null) {
    await router.push({ name: 'plans' })
  }
}

/**
 * Leaves the form without saving.
 * @returns Resolves once navigation has been dispatched.
 */
const cancel = async (): Promise<void> => {
  await router.push({ name: 'plans' })
}

onMounted(prepare)
</script>

<template>
  <section class="flex w-full max-w-3xl flex-col gap-4">
    <UiPageHeading
      :eyebrow="t('plans.form.eyebrow')"
      :title="t(headingKey)"
      :subtitle="isEditing ? t('plans.form.subtitleEdit') : t('plans.form.subtitleNew')"
    />

    <!-- The one thing that needs attention goes first: a refusal from the server, rendered
         verbatim — the message already says what to change. -->
    <UiAlert v-if="store.saveErrorMessage !== null" variant="error">
      {{ store.saveErrorMessage }}
    </UiAlert>

    <template v-if="isEditing">
      <UiSpinner v-if="!settled" :label="t('plans.form.loading')" />

      <!-- A customer who opens the edit URL lands here too: the list endpoint refused them, and
           its message is shown instead of the form. -->
      <UiAlert v-else-if="store.errorMessage !== null" variant="error">{{ store.errorMessage }}</UiAlert>

      <UiEmptyState
        v-else-if="plan === undefined"
        :title="t('plans.form.notFoundTitle')"
        :description="t('plans.form.notFoundDescription')"
      >
        <UiButton variant="secondary" :to="{ name: 'plans' }">{{ t('plans.form.backToList') }}</UiButton>
      </UiEmptyState>

      <PlanForm v-else :plan="plan" :submitting="store.saving" @submit="save" @cancel="cancel" />
    </template>

    <PlanForm v-else :plan="null" :submitting="store.saving" @submit="save" @cancel="cancel" />
  </section>
</template>
