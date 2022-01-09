FROM mcr.microsoft.com/mssql/server:2019-latest

USER root

COPY .scripts /scripts

ENV ACCEPT_EULA=Y
ENV SA_PASSWORD="itsAbadP@SSW0RD"

COPY .devops/sql-healthcheck.sh sql-healthcheck.sh
RUN chmod +x sql-healthcheck.sh

COPY .devops/sql-entrypoint.sh sql-entrypoint.sh
RUN chmod +x sql-entrypoint.sh

HEALTHCHECK --interval=1s --timeout=10s --start-period=60s --retries=3 CMD ["/bin/bash","sql-healthcheck.sh"]

ENTRYPOINT ["/bin/bash","sql-entrypoint.sh"]