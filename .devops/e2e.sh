#!/usr/bin/env bash
# End-to-end check of the docker-compose demo: builds and starts the stack, then waits until the consumers have
# handled the expected number of distinct integration events. Leaves the stack running; tear down with
# `docker compose down -v`.
#
#   EXPECTED_EVENTS        number of distinct integration events the seed script should produce (default 100)
#   E2E_TIMEOUT_SECONDS    how long to wait for them once the stack is up (default 600)

set -euo pipefail

expected=${EXPECTED_EVENTS:-100}
timeout=${E2E_TIMEOUT_SECONDS:-600}

cd "$(dirname "$0")/.."

# Recent logs per service, so a failure can be diagnosed from the job log alone
dump_logs() {
	docker compose ps -a >&2
	for service in $(docker compose config --services); do
		echo "::group::logs: ${service}" >&2
		docker compose logs --no-color --tail=150 "$service" >&2 || true
		echo "::endgroup::" >&2
	done
}

# Fails fast when a service the others depend on exits (for example the db container when a seed script fails).
# Recreates every container so a rerun without `docker compose down` starts from a fresh database and only counts
# this run's consumer logs.
if ! docker compose up -d --build --force-recreate --renew-anon-volumes; then
	echo "E2E failed: the stack did not start." >&2
	dump_logs
	exit 1
fi

# grep exits 1 when nothing has been consumed yet; that must not trip set -e/pipefail
count_consumed() {
	docker compose logs --no-color consumer1 consumer2 2>/dev/null \
		| { grep -o "handled by consumer. Message Id: '[^']*'" || true; } \
		| sort -u \
		| wc -l
}

deadline=$((SECONDS + timeout))
consumed=0
while [ $SECONDS -lt $deadline ]; do
	consumed=$(count_consumed)
	echo "$(date -u +%H:%M:%S) consumed ${consumed}/${expected} distinct integration events"
	if [ "$consumed" -ge "$expected" ]; then
		echo "E2E passed: ${consumed} distinct integration events consumed (expected ${expected})."
		exit 0
	fi
	sleep 5
done

echo "E2E failed: ${consumed}/${expected} distinct integration events consumed after ${timeout}s." >&2
dump_logs
exit 1
