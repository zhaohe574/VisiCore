<script setup lang="ts">
defineProps<{
  title: string
  count?: number
  description?: string
  subtitle?: string
}>()
</script>

<template>
  <header class="page-heading">
    <div class="heading-title">
      <div>
        <div style="display: flex; align-items: center; gap: 10px;">
          <h1>{{ title }}</h1>
          <span v-if="count !== undefined" class="count-badge">{{ count }}</span>
        </div>
        <p v-if="description || subtitle" class="muted" style="margin: 4px 0 0; font-size: 12.5px;">
          {{ description || subtitle }}
        </p>
      </div>
    </div>
    <div class="heading-actions">
      <slot name="actions" />
      <slot name="extra" />
      <slot />
    </div>
  </header>
</template>
