<script setup lang="ts">
defineProps<{
  title: string
  count?: number
  description?: string
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
        <p v-if="description" class="muted" style="margin: 4px 0 0; font-size: 12.5px;">
          {{ description }}
        </p>
      </div>
    </div>
    <div class="heading-actions">
      <slot />
    </div>
  </header>
</template>
