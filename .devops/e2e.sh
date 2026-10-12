#!/usr/bin/env bash
# End-to-end check of the docker-compose demo: builds and starts the stack, then waits until the consumers have
# handled exactly the expected number of distinct integration events of each type. Leaves the stack running; tear
# down with `docker compose down -v`.
#
#   EXPECTED_EVENTS        distinct integration events per type that the seed script should produce, as space-separated
#                          Type=count pairs (default: 25 soft deletes and 25 hard deletes of Event1 -> 50
#                          Event1DeletedEvent, 25 restores -> 25 Event1RestoredEvent, 25 Event2 inserts -> 25 Event2CreatedEvent)
#   E2E_TIMEOUT_SECONDS    how long to wait for them once the stack is up (default 600)
#   E2E_SETTLE_SECONDS     how long the count must stay unchanged before it is checked (default 20)

set -euo pipefail

expected_by_type=$(echo "${EXPECTED_EVENTS:-Event1DeletedEvent=50 Event1RestoredEvent=25 Event2CreatedEvent=25}" | tr ' ' '\n' | grep . | sort)
expected=$(echo "$expected_by_type" | awk -F= '{ total += $2 } END { print total }')
timeout=${E2E_TIMEOUT_SECONDS:-600}
settle=${E2E_SETTLE_SECONDS:-20}

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

# Distinct handled events (event type, source id and message id), one per line.
# grep exits 1 when nothing has been consumed yet; that must not trip set -e/pipefail
handled_events() {
	docker compose logs --no-color consumer1 consumer2 2>/dev/null \
		| { grep -oE "[A-Za-z0-9]+ with id '[^']*' handled by consumer. Message Id: '[^']*'" || true; } \
		| sort -u
}

# Type=count per event type, sorted like expected_by_type
consumed_by_type() {
	handled_events | awk '{ count[$1]++ } END { for (t in count) print t "=" count[t] }' | sort
}

count_consumed() {
	handled_events | wc -l
}

deadline=$((SECONDS + timeout))
consumed=0
while [ $SECONDS -lt $deadline ]; do
	consumed=$(count_consumed)
	echo "$(date -u +%H:%M:%S) consumed ${consumed}/${expected} distinct integration events"
	if [ "$consumed" -ge "$expected" ]; then
		# Wait until no more events arrive, so duplicate publications can't stand in for missing events
		sleep "$settle"
		settled=$(count_consumed)
		if [ "$settled" -ne "$consumed" ]; then
			continue
		fi
		# Compare per event type, so events mapped or routed to the wrong type are caught too
		actual_by_type=$(consumed_by_type)
		if [ "$actual_by_type" = "$expected_by_type" ]; then
			echo "E2E passed: ${settled} distinct integration events consumed:" $actual_by_type
			exit 0
		fi
		echo "E2E failed: consumed" $actual_by_type "but expected exactly" $expected_by_type >&2
		dump_logs
		exit 1
	fi
	sleep 5
done

echo "E2E failed: ${consumed}/${expected} distinct integration events consumed after ${timeout}s." >&2
dump_logs
exit 1
