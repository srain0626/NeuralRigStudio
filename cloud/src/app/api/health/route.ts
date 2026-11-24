import { NextResponse } from "next/server";
import { isSupabaseConfigured } from "@/lib/supabase";

export async function GET() {
  return NextResponse.json({
    ok: true,
    service: "neuralrig-cloud",
    version: "3.0.0",
    configured: isSupabaseConfigured(),
    timestamp: new Date().toISOString(),
  });
}
