<script setup lang="ts">
defineProps<{ title: string; count?: number }>()
</script>
<template><header class="page-heading"><div class="heading-title"><h1>{{ title }}</h1><span v-if="count !== undefined" class="count">{{ count }}</span></div><div class="heading-actions"><slot /></div></header></template>
