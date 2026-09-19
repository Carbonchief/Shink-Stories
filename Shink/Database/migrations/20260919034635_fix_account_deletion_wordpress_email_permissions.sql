-- DELETE predicates also require SELECT on the columns they read. The original
-- account-deletion migration granted DELETE only, so even an empty legacy table
-- made the SECURITY INVOKER cleanup fail with 42501 and roll back.
grant select (normalized_email) on table
    private.wordpress_membership_periods,
    private.wordpress_membership_orders,
    private.wordpress_subscriptions,
    private.wordpress_users
to service_role;
