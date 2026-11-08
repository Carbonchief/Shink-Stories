"""Exercise the collection migration on an isolated local PostgreSQL cluster."""

from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest


ROOT = Path(__file__).resolve().parents[2]
MIGRATIONS = ROOT / "Shink/Database/migrations"


@unittest.skipUnless(shutil.which("pg_config"), "Local PostgreSQL tools are required")
class StoreCollectionMigrationTests(unittest.TestCase):
    def test_backfill_constraints_idempotence_and_existing_access(self):
        pgbin = Path(subprocess.check_output(["pg_config", "--bindir"], text=True).strip())
        with tempfile.TemporaryDirectory(prefix="schink-collections-pg-") as temp:
            data = Path(temp) / "data"
            subprocess.run([str(pgbin / "initdb"), "-D", str(data), "-A", "trust", "--no-locale", "-E", "UTF8"],
                           check=True, capture_output=True)
            started = False
            try:
                subprocess.run([str(pgbin / "pg_ctl"), "-D", str(data), "-l", str(Path(temp) / "server.log"),
                                "-o", f"-k {temp} -h '' -p 55479", "-w", "start"], check=True, capture_output=True)
                started = True

                def sql(source):
                    result = subprocess.run([str(pgbin / "psql"), "-h", temp, "-p", "55479", "-d", "postgres",
                                             "-v", "ON_ERROR_STOP=1"], input=source, text=True, capture_output=True)
                    self.assertEqual(result.returncode, 0, result.stderr)

                sql("""
create role anon; create role authenticated; create role service_role bypassrls;
create function public.set_updated_at() returns trigger language plpgsql as $$ begin new.updated_at = now(); return new; end $$;
create table public.store_orders(quantity integer, total_price_zar numeric);
""")
                sql((MIGRATIONS / "20260422_store_products_admin_catalog.sql").read_text())
                sql("""
grant select on public.store_products to anon, authenticated;
grant all on public.store_products to service_role;
insert into public.store_products(slug,name,image_path,unit_price_zar,is_enabled)
select slug,slug,'/branding/Panda.webp',180,slug <> 'disabled-product'
from unnest(array['suurlemoentjie-boek','suurlemoentjie-boek-tjommie','suurlemoentjie-boek-subskripsie',
                  'suurlemoentjie-boek-pakket','disabled-product']) slug;
""")
                migration = (MIGRATIONS / "20261108194610_store_product_collections.sql").read_text()
                sql(migration)
                sql("""
do $$ begin
    if (select count(*) from public.store_products where collection_slug='kinderboeke') <> 1 or
       (select count(*) from public.store_products where collection_slug='pakkette') <> 3 or
       (select count(*) from public.store_products where collection_slug='storietjommies') <> 5 then
        raise exception 'Incorrect catalogue backfill';
    end if;
end $$;
update public.store_products set collection_slug='pakkette' where slug='tiekie';
""")
                sql(migration)
                sql("""
insert into public.store_products(slug,name,image_path,unit_price_zar) values ('future-product','Future','/test',100);
do $$ begin
    if (select collection_slug from public.store_products where slug='tiekie') <> 'pakkette' then
        raise exception 'Reapplying migration overwrites admin assignment';
    end if;
    if (select collection_slug from public.store_products where slug='future-product') <> 'storietjommies' then
        raise exception 'Missing compatibility default';
    end if;
    begin
        update public.store_products set collection_slug='invalid' where slug='tiekie';
        raise exception 'Invalid collection accepted';
    exception when check_violation then null; end;
    begin
        update public.store_products set collection_slug=null where slug='tiekie';
        raise exception 'Null collection accepted';
    exception when not_null_violation then null; end;
    if not (select relrowsecurity from pg_class where oid='public.store_products'::regclass) then
        raise exception 'RLS lost';
    end if;
end $$;
set role anon;
do $$ begin
    if exists (select from public.store_products where not is_enabled) then raise exception 'Disabled product exposed'; end if;
    if not exists (select from public.store_products where collection_slug='kinderboeke') then raise exception 'Collection unreadable'; end if;
    if has_table_privilege('anon','public.store_products','UPDATE') then raise exception 'Anonymous write granted'; end if;
end $$;
reset role;
set role service_role;
update public.store_products set collection_slug='kinderboeke' where slug='future-product';
""")
            finally:
                if started:
                    subprocess.run([str(pgbin / "pg_ctl"), "-D", str(data), "-w", "stop", "-m", "fast"],
                                   check=True, capture_output=True)


if __name__ == "__main__":
    unittest.main()
