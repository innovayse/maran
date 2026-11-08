<script setup lang="ts">
/**
 * The legend of {@link UiLiveChart}: one toggle per line that hides and shows it. Each swatch is the
 * line's own colour AND dash pattern, so two lines stay apart for a reader who cannot tell colours
 * apart. The toggles are real buttons with `aria-pressed`; the swatch is decorative, the name is
 * printed beside it.
 */
import { useI18n } from 'vue-i18n'

/** One legend entry. */
export interface UiLiveChartLegendItem {
  /** Stable key: the entry's identity, and the suffix of its test id. */
  key: string
  /** The line's name, already translated. */
  label: string
}

/** Props accepted by {@link UiLiveChartLegend}. */
defineProps<{
  /** The entries in line order, at most six; the position picks the swatch style. */
  items: UiLiveChartLegendItem[]
  /** Keys of the entries whose line is hidden. */
  hiddenKeys: string[]
}>()

/** Events emitted by {@link UiLiveChartLegend}. */
const emit = defineEmits<{
  /** The reader pressed an entry; carries its key. */
  (e: 'toggle', key: string): void
}>()

const { t } = useI18n()

/**
 * Reports the pressed entry.
 * @param key The entry's key.
 * @returns Nothing.
 */
const onToggle = (key: string): void => {
  emit('toggle', key)
}
</script>

<template>
  <ul class="m-0 flex list-none flex-wrap gap-x-3 gap-y-1 p-0" :aria-label="t('liveDashboard.chart.legend')">
    <li v-for="(item, index) in items" :key="item.key">
      <button
        type="button"
        class="flex items-center gap-1.5 rounded-inset text-xs text-text-secondary hover:text-text-primary focus-visible:shadow-focus focus-visible:outline-none"
        :class="{ 'opacity-60': hiddenKeys.includes(item.key) }"
        :aria-pressed="!hiddenKeys.includes(item.key)"
        :data-testid="`live-chart-legend-${item.key}`"
        @click="onToggle(item.key)"
      >
        <span class="ui-live-swatch" :class="`ui-live-swatch-${index}`" aria-hidden="true" />
        {{ item.label }}
      </button>
    </li>
  </ul>
</template>

<style scoped>
/* The legend swatch is the line's own colour and dash, drawn as a short top border. */
.ui-live-swatch {
  display: inline-block;
  width: 1.25rem;
  height: 0;
  border-top: 2px solid;
  border-color: currentcolor;
}
.ui-live-swatch-0 { color: var(--color-accent); }
.ui-live-swatch-1 { color: var(--color-violet); border-top-style: dashed; }
.ui-live-swatch-2 { color: var(--color-success); border-top-style: dotted; }
.ui-live-swatch-3 { color: var(--color-warning); border-top-style: dashed; border-top-width: 3px; }
.ui-live-swatch-4 { color: var(--color-danger); border-top-style: dotted; border-top-width: 3px; }
.ui-live-swatch-5 { color: var(--color-text-secondary); border-top-style: double; border-top-width: 4px; }
</style>
