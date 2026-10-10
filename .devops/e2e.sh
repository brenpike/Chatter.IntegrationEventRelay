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

docker compose up -d --build

count_consumed() {
	docker compose logs --no-color consumer1 consumer2 2>/dev/null \
		| grep -o "handled by consumer. Message Id: '[^']*'" \
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
docker compose ps -a >&2
exit 1
