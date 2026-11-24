-- Keep anon SELECT policy free of private schema calls; restrict private.is_admin() to authenticated role
alter policy "Public can read active releases" on public.app_releases
  to anon, authenticated
  using (is_active = true);

create policy "app_releases_select_admin"
  on public.app_releases for select
  to authenticated
  using (private.is_admin());
