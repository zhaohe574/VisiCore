#!/bin/sh
set -u

agent_pid=""
config_pid=""

shutdown() {
  [ -z "$agent_pid" ] || kill -TERM "$agent_pid" 2>/dev/null || true
  [ -z "$config_pid" ] || kill -TERM "$config_pid" 2>/dev/null || true
  [ -z "$agent_pid" ] || wait "$agent_pid" 2>/dev/null || true
  [ -z "$config_pid" ] || wait "$config_pid" 2>/dev/null || true
  exit 0
}

trap shutdown INT TERM

ASPNETCORE_URLS="http://127.0.0.1:8081" dotnet /app/edge-agent/VisiCore.EdgeAgent.dll &
agent_pid=$!
ASPNETCORE_URLS="http://+:8080" dotnet /app/edge-node-config/VisiCore.EdgeNodeConfig.dll &
config_pid=$!

while :; do
  if ! kill -0 "$agent_pid" 2>/dev/null; then
    wait "$agent_pid"
    exit_code=$?
    kill -TERM "$config_pid" 2>/dev/null || true
    wait "$config_pid" 2>/dev/null || true
    exit "$exit_code"
  fi
  if ! kill -0 "$config_pid" 2>/dev/null; then
    wait "$config_pid"
    exit_code=$?
    kill -TERM "$agent_pid" 2>/dev/null || true
    wait "$agent_pid" 2>/dev/null || true
    exit "$exit_code"
  fi
  sleep 1
done
