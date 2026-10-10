#!/usr/bin/env bash

# Newer SQL Server images ship sqlcmd as mssql-tools18 (which requires -C to trust the self-signed certificate)
if [ -x /opt/mssql-tools18/bin/sqlcmd ]; then
	SQLCMD="/opt/mssql-tools18/bin/sqlcmd -C"
else
	SQLCMD="/opt/mssql-tools/bin/sqlcmd"
fi

# Marker read by sql-healthcheck.sh; the container only reports healthy once all scripts have run
READY_FILE=/tmp/scripts-complete
rm -f $READY_FILE

run_scripts() {
	echo "Waiting for MS SQL to be available ⏳ ..."
	$SQLCMD -l 30 -S localhost -h-1 -V1 -U sa -P "$SA_PASSWORD" -Q "SET NOCOUNT ON SELECT \"Sql Server Ready\" , @@servername"
	is_up=$?
	while [ $is_up -ne 0 ] ; do
	  echo -e $(date)
	  $SQLCMD -l 30 -S localhost -h-1 -V1 -U sa -P "$SA_PASSWORD" -Q "SET NOCOUNT ON SELECT \"Sql Server Ready\" , @@servername"
	  is_up=$?
	  sleep 1
	done

	for foo in /scripts/*.sql
	  do
	  echo "Found script $foo"
	  $SQLCMD -U sa -P "$SA_PASSWORD" -l 30 -e -i $foo
	  echo "Script $foo executed."
	done
	touch $READY_FILE
	echo "All scripts have been executed."
}

/opt/mssql/bin/sqlservr &
pid=$!
run_scripts &

trap "kill -15 $pid" SIGTERM
wait $pid
exit 0
