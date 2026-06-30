import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

// __SSO_ENABLED__ is the build-time default for the splash login switch:
//   SSO_ENABLED=true npm run build  → splash shows "Sign in with SSO"
//   (default / unset)               → splash shows "Continue" (testing mode)
// At runtime the localStorage key "advisory-sso" overrides this, so you can flip it from the splash
// toggle without a rebuild.
const ssoEnabled = process.env.SSO_ENABLED === "true" || process.env.SSO_ENABLED === "1";

export default defineConfig({
  plugins: [react()],
  server: { port: 5173 },
  define: { __SSO_ENABLED__: JSON.stringify(ssoEnabled) },
});
