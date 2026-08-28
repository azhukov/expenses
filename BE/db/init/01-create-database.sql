-- Database provisioning (D13). EF Migrations connect to a database that already exists and cannot
-- set its encoding, locale provider or collation; none of the three can be altered afterwards
-- without a dump and restore. Getting this wrong looks like nothing at all — sorting is simply
-- quietly incorrect — which is why a startup check asserts it and this script is the only place it
-- is decided.
--
-- ICU root collation ('und') sorts mixed-language content sensibly rather than by raw byte order.
-- Per-column collations were rejected because no column here is reliably in one language.
--
-- Run against the maintenance database, before any migration. Local development mounts this file
-- into docker-entrypoint-initdb.d; the integration suite runs it verbatim against a throwaway
-- container, so tests meet the same database production does (D17). Plain SQL with no psql
-- meta-commands, so both can execute it as it stands.

CREATE DATABASE expenses
  ENCODING 'UTF8'
  LOCALE_PROVIDER icu
  ICU_LOCALE 'und'
  TEMPLATE template0;
