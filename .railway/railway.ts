import { defineRailway, github, project, service } from "railway/iac";
// This repository manages only its own resources in the environment.
export const partial = "anime-rating-platform";
export default defineRailway(() => {
  const animeRatingPlatform = service("anime-rating-platform", {
    source: github("trism-prism/anime-rating-platform", { branch: "main", checkSuites: true }),
    build: { buildCommand: "dotnet publish api/api.csproj -c Release -o out", buildEnvironment: "V3", builder: "NIXPACKS" },
    start: "cd out && dotnet api.dll",
    healthcheck: "/health",
    replicas: { "iad": 1 },
    deploy: { ipv6EgressEnabled: true },
  });

  return project("confident-patience", {
    variables: { managed: false },
    resources: [animeRatingPlatform],
  });
});
