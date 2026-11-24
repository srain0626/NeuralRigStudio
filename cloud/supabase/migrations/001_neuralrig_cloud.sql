-- NeuralRig Studio v3.0 Public Cloud Schema (Supabase / PostgreSQL)
-- Run inside Supabase SQL Editor or via `supabase db push`

create extension if not exists "pgcrypto";

-- 1. User Profiles (mirrored from Google OAuth via Supabase Auth)
create table if not exists public.profiles (
  id uuid primary key references auth.users(id) on delete cascade,
  email text not null default '',
  display_name text not null default '',
  avatar_url text not null default '',
  is_admin boolean not null default false,
  created_at timestamptz not null default now(),
  updated_at timestamptz not null default now()
);

alter table public.profiles enable row level security;

create policy "Users can view own profile"
  on public.profiles for select
  using (auth.uid() = id);

create policy "Users can update own non-admin profile fields"
  on public.profiles for update
  using (auth.uid() = id);

-- 2. Cloud Settings, Presets, Guitar Profiles & Custom Gear Sync
create table if not exists public.user_settings_sync (
  user_id uuid primary key references auth.users(id) on delete cascade,
  settings_json jsonb not null default '{}'::jsonb,
  presets_json jsonb not null default '[]'::jsonb,
  guitar_profiles_json jsonb not null default '[]'::jsonb,
  custom_devices_json jsonb not null default '[]'::jsonb,
  updated_at timestamptz not null default now()
);

alter table public.user_settings_sync enable row level security;

create policy "Users manage own synced settings"
  on public.user_settings_sync for all
  using (auth.uid() = user_id)
  with check (auth.uid() = user_id);

-- 3. Community Custom Gear Hub (SPICE Pedals, Tube Amps, Acoustic Cabs & Presets)
create table if not exists public.hub_gears (
  id uuid primary key default gen_random_uuid(),
  owner_id uuid not null references auth.users(id) on delete cascade,
  owner_name text not null default 'Creator',
  slug text unique not null,
  name text not null,
  short_name text not null default '',
  original_name text not null default '',
  aliases text[] not null default '{}',
  category text not null check (category in ('drive', 'amp', 'cab', 'preset')),
  summary text not null default '',
  schematic_source_url text not null default '',
  tags text[] not null default '{}',
  spec_json jsonb not null,
  downloads integer not null default 0,
  likes integer not null default 0,
  created_at timestamptz not null default now(),
  updated_at timestamptz not null default now()
);

create index if not exists idx_hub_gears_category on public.hub_gears(category);
create index if not exists idx_hub_gears_downloads on public.hub_gears(downloads desc);

alter table public.hub_gears enable row level security;

create policy "Anyone can browse hub gear"
  on public.hub_gears for select
  using (true);

create policy "Authenticated creators insert own hub gear"
  on public.hub_gears for insert
  with check (auth.uid() = owner_id);

create policy "Owners update own hub gear"
  on public.hub_gears for update
  using (auth.uid() = owner_id);

create policy "Owners delete own hub gear"
  on public.hub_gears for delete
  using (auth.uid() = owner_id);

-- 4. Automated Bug Reports & Scrubbed Log Analytics
create table if not exists public.bug_reports (
  id uuid primary key default gen_random_uuid(),
  user_id uuid references auth.users(id) on delete set null,
  user_email text not null default 'anonymous',
  title text not null,
  category text not null default 'general',
  severity text not null default 'normal',
  status text not null default 'open' check (status in ('open', 'triaged', 'resolved', 'wontfix')),
  app_version text not null default '3.0.0',
  description text not null default '',
  sanitized_logs text not null default '',
  diagnostics_json jsonb not null default '{}'::jsonb,
  created_at timestamptz not null default now()
);

create index if not exists idx_bug_reports_created_at on public.bug_reports(created_at desc);

alter table public.bug_reports enable row level security;

create policy "Anyone can insert sanitized bug report"
  on public.bug_reports for insert
  with check (true);

-- 5. Anonymous Product Telemetry Events
create table if not exists public.telemetry_events (
  id bigint generated always as identity primary key,
  user_id uuid references auth.users(id) on delete set null,
  event_type text not null,
  app_version text not null default '3.0.0',
  platform text not null default 'win-x64',
  metadata jsonb not null default '{}'::jsonb,
  created_at timestamptz not null default now()
);

create index if not exists idx_telemetry_created_at on public.telemetry_events(created_at desc);

alter table public.telemetry_events enable row level security;

-- 6. OTA Release Policies (Choice vs Force Mandatory Updates)
create table if not exists public.app_releases (
  version text primary key,
  min_supported_version text not null default '3.0.0',
  force_update boolean not null default false,
  release_notes_ko text not null default '',
  release_notes_en text not null default '',
  installer_url text not null default '',
  standalone_exe_url text not null default '',
  sha256_installer text not null default '',
  is_active boolean not null default true,
  created_at timestamptz not null default now(),
  updated_at timestamptz not null default now()
);

alter table public.app_releases enable row level security;

create policy "Public can read active releases"
  on public.app_releases for select
  using (is_active = true);

-- Seed initial v3.0.0 baseline release row
insert into public.app_releases (version, min_supported_version, force_update, release_notes_ko, release_notes_en)
values (
  '3.0.0',
  '3.0.0',
  false,
  'NeuralRig Studio v3.0.0 공개 라이브 릴리즈: 범용 ASIO/WASAPI, 벤치마크 스위트, npx MCP, 클라우드 동기화·기어 허브·OTA 탑재.',
  'NeuralRig Studio v3.0.0 public release: Universal ASIO/WASAPI, Benchmark Suite, npx MCP, Cloud Sync, Gear Hub & OTA.'
)
on conflict (version) do nothing;
