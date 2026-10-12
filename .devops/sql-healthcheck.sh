#!/usr/bin/env bash

# Healthy only once sql-entrypoint.sh has created and seeded the database
test -f /tmp/scripts-complete
