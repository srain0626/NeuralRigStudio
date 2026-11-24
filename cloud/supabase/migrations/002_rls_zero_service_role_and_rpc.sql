-- NeuralRig Studio v3.0.1 Zero-Service-Role RLS & RPC Hardening
-- Safe for publishable/anon keys forwarding user Bearer JWTs from Next.js API routes.

create schema if not exists private;
revoke all on schema private from public, anon;
grant usage on schema private to authenticated, service_role;

create table if not exists private.admin_emails (
  email text primary key,
  created_at timestamptz not null default now()
);
revoke all on table private.admin_emails from public, anon, authenticated;

create or replace function private.is_admin()
returns boolean
language sql
stable
security definer
set search_path = ''
as $$
  select
    (select auth.uid()) is not null
    and (
      exists (
        select 1
        from public.profiles p
        where p.id = (select auth.uid())
          and p.is_admin = true
      )
      or exists (
        select 1
        from private.admin_emails a
        where lower(a.email) = lower(coalesce((select auth.jwt() ->> 'email'), ''))
      )
    );
$$;

revoke all on function private.is_admin() from public, anon;
grant execute on function private.is_admin() to authenticated, service_role;

create or replace function public.increment_hub_gear_downloads(p_gear_id uuid)
returns integer
language sql
volatile
security definer
set search_path = ''
as $$
  update public.hub_gears
  set downloads = downloads + 1
  where id = p_gear_id
  returning downloads;
$$;

revoke all on function public.increment_hub_gear_downloads(uuid) from public;
grant execute on function public.increment_hub_gear_downloads(uuid) to anon, authenticated, service_role;

grant usage on schema public to anon, authenticated;
grant select on public.hub_gears, public.app_releases to anon, authenticated;
grant insert on public.bug_reports, public.telemetry_events to anon, authenticated;
grant select, insert, update, delete on public.profiles, public.user_settings_sync, public.hub_gears to authenticated;
grant select, update on public.bug_reports to authenticated;
grant select on public.telemetry_events to authenticated;
grant insert, update on public.app_releases to authenticated;
grant usage, select on all sequences in schema public to anon, authenticated;

-- 1. profiles
alter policy "Users can view own profile" on public.profiles
  to authenticated
  using ((select auth.uid()) = id or private.is_admin());

create policy "profiles_insert_own"
  on public.profiles for insert
  to authenticated
  with check ((select auth.uid()) = id and (is_admin = false or private.is_admin()));

alter policy "Users can update own non-admin profile fields" on public.profiles
  to authenticated
  using ((select auth.uid()) = id or private.is_admin())
  with check (((select auth.uid()) = id and (is_admin = false or private.is_admin())) or private.is_admin());

-- 2. user_settings_sync
alter policy "Users manage own synced settings" on public.user_settings_sync
  to authenticated
  using ((select auth.uid()) = user_id)
  with check ((select auth.uid()) = user_id);

create policy "user_settings_sync_admin_select"
  on public.user_settings_sync for select
  to authenticated
  using (private.is_admin());

-- 3. hub_gears
alter policy "Anyone can browse hub gear" on public.hub_gears
  to anon, authenticated
  using (true);

alter policy "Authenticated creators insert own hub gear" on public.hub_gears
  to authenticated
  with check ((select auth.uid()) = owner_id);

alter policy "Owners update own hub gear" on public.hub_gears
  to authenticated
  using ((select auth.uid()) = owner_id or private.is_admin())
  with check ((select auth.uid()) = owner_id or private.is_admin());

alter policy "Owners delete own hub gear" on public.hub_gears
  to authenticated
  using ((select auth.uid()) = owner_id or private.is_admin());

-- 4. bug_reports
alter policy "Anyone can insert sanitized bug report" on public.bug_reports
  to anon, authenticated
  with check ((user_id is null or user_id = (select auth.uid())) and status = 'open');

create policy "bug_reports_select_admin"
  on public.bug_reports for select
  to authenticated
  using (private.is_admin());

create policy "bug_reports_update_admin"
  on public.bug_reports for update
  to authenticated
  using (private.is_admin())
  with check (private.is_admin());

-- 5. telemetry_events
create policy "telemetry_events_insert_public"
  on public.telemetry_events for insert
  to anon, authenticated
  with check (user_id is null or user_id = (select auth.uid()));

create policy "telemetry_events_select_admin"
  on public.telemetry_events for select
  to authenticated
  using (private.is_admin());

-- 6. app_releases
alter policy "Public can read active releases" on public.app_releases
  to anon, authenticated
  using (is_active = true or private.is_admin());

create policy "app_releases_insert_admin"
  on public.app_releases for insert
  to authenticated
  with check (private.is_admin());

create policy "app_releases_update_admin"
  on public.app_releases for update
  to authenticated
  using (private.is_admin())
  with check (private.is_admin());
