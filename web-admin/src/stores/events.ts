import { ref } from 'vue'
import { defineStore } from 'pinia'
import { HubConnectionBuilder, LogLevel, type HubConnection } from '@microsoft/signalr'
import { apiBase, type EventKind, type ResourceEvent } from '../api'

type Listener = (event?: ResourceEvent) => void | Promise<void>
export const useEvents = defineStore('events', () => {
  const state = ref<'connecting' | 'connected' | 'reconnecting' | 'disconnected'>('disconnected')
  let connection: HubConnection | null = null
  let retryTimer: ReturnType<typeof setTimeout> | undefined
  let stopped = true
  const listeners = new Map<string, Set<Listener>>()
  const versions = new Map<string, number>()
  function emit(kind: string, event?: ResourceEvent) { listeners.get(kind)?.forEach(listener => { Promise.resolve(listener(event)).catch(() => { /* 页面负责显示查询错误。 */ }) }) }
  function subscribe(kinds: (EventKind | 'reconnected')[], listener: Listener) {
    for (const kind of kinds) { if (!listeners.has(kind)) listeners.set(kind, new Set()); listeners.get(kind)!.add(listener) }
    return () => kinds.forEach(kind => listeners.get(kind)?.delete(listener))
  }
  async function connect() {
    if (!connection || stopped) return
    state.value = 'connecting'
    try { await connection.start(); if (!stopped) { state.value = 'connected'; emit('reconnected') } }
    catch { if (!stopped) { state.value = 'disconnected'; retryTimer = setTimeout(() => void connect(), 5000) } }
  }
  async function start() {
    if (!stopped) return
    stopped = false
    connection = new HubConnectionBuilder().withUrl(`${apiBase}/hubs/v2/events`, { withCredentials: true }).withAutomaticReconnect([0, 2000, 5000, 10000, 30000]).configureLogging(LogLevel.None).build()
    const kinds: EventKind[] = ['alarm.changed', 'device.changed', 'media.changed', 'export.changed', 'access.changed']
    for (const kind of kinds) connection.on(kind, (event: ResourceEvent) => {
      const key = `${kind}:${event.id}`
      if (versions.has(key) && versions.get(key)! >= event.version) return
      if (versions.size > 5000) versions.clear()
      versions.set(key, event.version)
      emit(kind, event)
    })
    connection.onreconnecting(() => { state.value = 'reconnecting' })
    connection.onreconnected(() => { versions.clear(); state.value = 'connected'; emit('reconnected') })
    connection.onclose(() => { state.value = 'disconnected'; if (!stopped) retryTimer = setTimeout(() => void connect(), 5000) })
    await connect()
  }
  async function stop() { stopped = true; clearTimeout(retryTimer); const old = connection; connection = null; await old?.stop(); versions.clear(); state.value = 'disconnected' }
  return { state, start, stop, subscribe }
})
