revoke execute on function public.reconcile_native_paystack_retry_tokens() from public;
revoke execute on function public.reconcile_native_paystack_retry_tokens() from anon;
revoke execute on function public.reconcile_native_paystack_retry_tokens() from authenticated;
grant execute on function public.reconcile_native_paystack_retry_tokens() to service_role;
