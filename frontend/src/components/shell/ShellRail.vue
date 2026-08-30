<script setup lang="ts">
/**
 * The shell's collapsed navigation rail: the same backend-driven entries as
 * {@link ShellSidebar}, drawn icon-only, with the theme and expand controls at
 * the bottom. The layout renders exactly one of the two at a time, as the
 * design does.
 *
 * Values from the design canvas: 56px wide on `--s1` with a `--b1` right
 * border, `padding:12px 0`, a 4px gap, the 28px accent brand square with a
 * 10px gap under it, and 34×32px 7px-radius icon buttons in `--t2`.
 *
 * Each entry keeps its text as a visually hidden label rather than only an
 * `aria-label`: the accessible name then survives translation, and the link
 * still has real content for assistive technology.
 */
import { useI18n } from 'vue-i18n'
import UiButton from '../ui/UiButton.vue'
import UiNav from '../ui/UiNav.vue'
import UiNavItem from '../ui/UiNavItem.vue'
import UiNavLink from '../ui/UiNavLink.vue'
import UiIcon from '../ui/UiIcon.vue'
import type { NavigationEntry } from '../../types/navigation'

/** Props accepted by {@link ShellRail}. */
defineProps<{
  /** Navigation entries to render, in catalogue order, as built by `useNavigation`. */
  entries: readonly NavigationEntry[]
}>()

/** Events emitted by {@link ShellRail}. */
const emit = defineEmits<{
  /** Requests that the shell expand the rail back into the full sidebar. */
  (e: 'expand'): void
}>()

const { t } = useI18n()

/**
 * Asks the shell to expand the rail. The collapsed/expanded state lives in
 * the layout, which owns both presentations.
 * @returns Nothing; the event is emitted synchronously.
 */
const expand = (): void => {
  emit('expand')
}
</script>

<template>
  <!-- w-14 is exactly the design's 56px rail. -->
  <aside class="shell-rail flex w-14 shrink-0 flex-col items-center gap-1 border-r border-border-subtle bg-surface-1 py-3">
    <!-- Collapsing must not cost the document its heading: the rail draws the
         brand square the design draws, and carries the product name inside it
         for assistive technology, so `Maran` is the h1 in both presentations. -->
    <h1
      class="mb-2.5 grid h-7 w-7 shrink-0 place-items-center rounded-lg bg-accent text-sm font-bold text-white"
    >
      <span aria-hidden="true">{{ t('app.brandInitial') }}</span>
      <span class="sr-only">{{ t('app.title') }}</span>
    </h1>

    <UiNav :label="t('app.nav.ariaLabel')">
      <UiNavItem v-for="entry in entries" :key="entry.key">
        <UiNavLink :to="entry.target" :locked="entry.locked" :title="entry.labelKey === null ? (entry.label ?? '') : t(entry.labelKey)">
          <UiIcon :name="entry.icon" :size="16" />
          <span class="sr-only">
            {{ entry.labelKey === null ? entry.label : t(entry.labelKey) }}
            <template v-if="entry.locked">{{ t('app.nav.lockedBadge') }}</template>
          </span>
        </UiNavLink>
      </UiNavItem>
    </UiNav>

    <div class="flex-1"></div>

    <!-- One control at the foot of the rail, as the design draws it: expand.
         The theme lives in the expanded sidebar's footer and in the header, and
         a third copy here would only add a button to a column that exists to be
         narrow. -->
    <UiButton
      variant="ghost"
      class="shell-rail-button"
      :aria-label="t('app.shell.expandSidebar')"
      :title="t('app.shell.expandSidebar')"
      @click="expand"
    >
      <UiIcon name="chevronRight" :size="15" />
    </UiButton>
  </aside>
</template>

<style scoped>
/*
 * As in the sidebar: the kit primitives are re-dressed here rather than
 * edited, and a scoped selector's data attribute wins over a utility class.
 */
.shell-rail :deep(nav) {
  width: auto;
}

.shell-rail :deep(ul) {
  gap: 4px;
  align-items: center;
}

/* `justify-content` must be restated, not only `place-items`: the kit's nav row
   is a full-width flex line with `justify-content: space-between`, which pushed
   the icon to the left edge of the 34px button — nine pixels off centre — while
   `place-items` quietly governed a different axis and looked like it had worked.
   The wrapper span is told not to grow for the same reason. */
.shell-rail :deep(a),
.shell-rail-button {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 0;
  width: 34px;
  height: 32px;
  padding: 0;
  border-radius: 7px;
  background: none;
  color: var(--t2);
}

.shell-rail :deep(a > span) {
  flex: none;
}

.shell-rail :deep(a:hover),
.shell-rail-button:hover {
  background: var(--s3);
  color: var(--t1);
}

.shell-rail :deep(a[aria-current='page']) {
  background: var(--acs);
  color: var(--ac);
}

.shell-rail-button {
  color: var(--t3);
}
</style>
