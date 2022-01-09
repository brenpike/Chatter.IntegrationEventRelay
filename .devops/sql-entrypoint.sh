#!/usr/bin/env bash

run_scripts() {
	echo "Waiting for MS SQL (pid $$) to be available ⏳ ..."
	/opt/mssql-tools/bin/sqlcmd -l 30 -S localhost -h-1 -V1 -U sa -P $SA_PASSWORD -Q "SET NOCOUNT ON SELECT \"Sql Server Ready\" , @@servername"
	is_up=$?
	while [ $is_up -ne 0 ] ; do 
	  echo -e $(date) 
	  /opt/mssql-tools/bin/sqlcmd -l 30 -S localhost -h-1 -V1 -U sa -P $SA_PASSWORD -Q "SET NOCOUNT ON SELECT \"Sql Server Ready\" , @@servername"
	  is_up=$?
	  sleep 1
	done
	
	for foo in /scripts/*.sql
	  do 
	  echo "Found script $foo"
	  /opt/mssql-tools/bin/sqlcmd -U sa -P $SA_PASSWORD -l 30 -e -i $foo
	  echo "Script $foo executed."
	done
	echo "All scripts have been executed. Waiting for MS SQL (pid $pid) to terminate."
}

/opt/mssql/bin/sqlservr &
pid=$$ | run_scripts 

trap "kill -15 $pid" SIGTERM
wait $pid
exit 0


