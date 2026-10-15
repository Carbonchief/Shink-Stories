-- Keep the usage rollup in memory instead of repeatedly sorting listening
-- history to temporary disk files. This setting applies only while this
-- admin function runs; the existing API timeout and access grants stay intact.
alter function public.get_admin_analytics_snapshot_base()
    set work_mem = '16MB';
