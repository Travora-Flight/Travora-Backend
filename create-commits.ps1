git init
dotnet new gitignore

git add .gitignore Travora.sln
git add Travora.Domain/Travora.Domain.csproj
git add Travora.Application/Travora.Application.csproj
git add Travora.Infrastructure/Travora.Infrastructure.csproj
git add Travora.API/Travora.API.csproj
git add Travora.Shared/Travora.Shared.csproj
git commit -m "Setup: Initialize Clean Architecture solution and projects"

git add Travora.Domain/
git commit -m "Domain: Add core entities, enums, and interfaces"

git add Travora.Shared/
git commit -m "Shared: Add cross-cutting concerns and settings"

git add Travora.Application/
git commit -m "Application: Setup application layer structure"

git add Travora.Infrastructure/Data/ApplicationDbContext.cs
git add Travora.Infrastructure/Data/Configurations/
git commit -m "Infrastructure: Implement DbContext and EF Core Configurations"

git add Travora.Infrastructure/Identity/
git commit -m "Infrastructure: Add Identity services and JWT generator"

git add Travora.API/appsettings.json
git add Travora.API/appsettings.Development.json
git add Travora.API/Configurations/
git commit -m "API: Configure application settings and strongly-typed options"

git add Travora.API/Extensions/
git commit -m "API: Implement setup extensions for Auth, Swagger, and Services"

git add Travora.API/Program.cs
git add Travora.API/Properties/
git commit -m "API: Update Program.cs and wire up dependency injection"

git add Travora.Infrastructure/Data/Migrations/
git commit -m "Data: Add Initial EF Core Migration"

git add .
git commit -m "Chore: Finalize project setup and references"
